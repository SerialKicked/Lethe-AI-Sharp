using LetheAISharp.Agent.Actions;
using LetheAISharp.Agent.Plugins;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.Moods;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Agent
{

    public class AgentConfig
    {
        public Dictionary<string, AgentTaskSetting> PluginSettings { get; set; } = [];
    }

    /// <summary>
    /// AgentLoop is responsible for managing the agent mode of a BasePersona.
    /// </summary>
    /// <param name="owner">persona tied to the agent</param>
    public class AgentRuntime(BasePersona owner)
    {
        public BasePersona Owner { get; private set; } = owner;
        public AgentConfig Config { get; set; } = new();

        private CancellationTokenSource? _cts = new();
        private DateTime _lastuseractivity = DateTime.Now;
        private bool _running;
        private Task? _loop;
        private static readonly List<IAgentTask> _plugins = [];
        private static readonly Dictionary<string, Func<IAgentTask>> _pluginRegistry = [];
        private static readonly Dictionary<string, object> _actions = [];

        public string AbilitiesToString()
        {
            var list = new List<string>();
            foreach (var plugin in _plugins)
            {
                if (!list.Contains(plugin.Ability))
                    list.Add(plugin.Ability);
            }
            if (list.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var id in list)
            {
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                sb.Append(id.ToLowerInvariant());
            }
            return sb.Append('.').ToString();
        }

        /// <summary>
        /// Updates the timestamp of the most recent user activity. 
        /// Must be called by the app to notifiy the library of user activity, so the agent knows when not to interrupt
        /// </summary>
        public void NotifyUserActivity()
        {
            _lastuseractivity = DateTime.Now;
        }

        public void ForceRunLoop()
        {
            _lastuseractivity = DateTime.MinValue;
        }

        public void CancelWork()
        {
            NotifyUserActivity();
            _cts?.Cancel();
        }

        private void BuildNewToken()
        {
            _cts?.Dispose();
            _cts = new();
        }

        /// <summary>
        /// The main loop that runs the agent tasks based on user inactivity and agent mode status.
        /// </summary>
        /// <returns></returns>
        private async Task MainLoop()
        {
            _running = true;
            // Initial delay to allow the system to settle. if the persona was just loaded, it means that the user is active, anyway.
            await Task.Delay(10000, _cts!.Token).ConfigureAwait(false);

            while (_running && !_cts.Token.IsCancellationRequested)
            {
                // don't do anything if not in agent mode, or if user was active recently
                if (!Owner.AgentMode || (DateTime.Now - _lastuseractivity) < LLMEngine.Settings.BackgroundAgentMinInactivityTime || LLMEngine.Status == SystemStatus.NotInit)
                {
                    await Task.Delay(5000, _cts.Token).ConfigureAwait(false);
                    if (_cts.Token.IsCancellationRequested && _running)
                        BuildNewToken();
                    continue;
                }
                // Run through all plugins
                foreach (var plugin in _plugins)
                {
                    if (!Config.PluginSettings.TryGetValue(plugin.Id, out var setting))
                        continue;
                    try
                    {
                        var shouldrun = await plugin.Observe(Owner, setting, _cts.Token).ConfigureAwait(false);
                        if (shouldrun)
                        {
                            await plugin.Execute(Owner, setting, _cts.Token).ConfigureAwait(false);
                            SaveSettings();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // gtfo
                        if (_running)
                            BuildNewToken();
                        break;
                    }
                    catch (Exception ex)
                    {
                        LLMEngine.Logger?.LogError(ex, "Error in plugin {PluginId}: {ex}", plugin.Id, ex.Message);
                    }
                    if (!_running || _cts.Token.IsCancellationRequested)
                    {
                        if (_running)
                            BuildNewToken();
                        break;
                    }
                }
            }
        }


        #region *** Start / Stop ***

        /// <summary>
        /// Intialize and start the agent loop. Should be called by BasePersona.BeginChat()
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public void Init()
        {
            // Make sure it's not already running
            if (_loop != null && !_loop.IsCompleted)
                throw new InvalidOperationException("Agent mode is already running. This is most likely caused by switching to a new active persona without closing the previous one properly.");
            LoadSettings();
            LoadPlugins();
            if (_cts == null || _cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();
            _loop = Task.Run(MainLoop);
        }

        /// <summary>
        /// Stop the agent loop and wait for it to finish. Should be called by BasePersona.EndChat()
        /// </summary>
        public void CloseSync()
        {
            Close().GetAwaiter().GetResult();
        }

        public async Task Close()
        {
            if (_loop == null)
                return;

            // Signal shutdown
            _running = false;
            _cts?.Cancel();

            try
            {
                // Wait for the loop to finish
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation happens
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                _loop = null;
                SaveSettings();
            }
        }

        #endregion


        #region *** Settings ***

        private void SaveSettings()
        {
            if (string.IsNullOrEmpty(Owner.UniqueName))
                return;
            // if path doesn't have a trailing slash, add one
            var selpath = LLMEngine.Settings.DataPath;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;

            var content = JsonConvert.SerializeObject(Config, new JsonSerializerSettings { Formatting = Formatting.Indented });
            // create directory if it doesn't exist
            var dir = Path.GetDirectoryName(selpath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(selpath + Owner.UniqueName + ".agent", content);
        }

        private void LoadSettings()
        {
            if (string.IsNullOrEmpty(Owner.UniqueName))
                return;
            // if path doesn't have a trailing slash, add one
            var selpath = LLMEngine.Settings.DataPath;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var filepath = selpath + Owner.UniqueName + ".agent";
            if (!File.Exists(filepath))
                return;
            try
            {
                var content = File.ReadAllText(filepath);
                var cfg = JsonConvert.DeserializeObject<AgentConfig>(content);
                if (cfg != null)
                    Config = cfg;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Failed to load agent config for {UniqueName}: {ex}", Owner.UniqueName, ex.Message);
            }
        }

        #endregion


        #region *** Plugin Management ***

        /// <summary>
        /// Loads and initializes plugins based on the distinct agent task identifiers.
        /// </summary>
        private void LoadPlugins()
        {
            _plugins.Clear();

            foreach (var id in Owner.AgentTasks.Distinct())
            {
                // First try to load from plugin registry
                if (_pluginRegistry.TryGetValue(id, out var factory))
                {
                    try
                    {
                        var plugin = factory();
                        _plugins.Add(plugin);
                        continue;
                    }
                    catch
                    {
                        // If factory fails, fall through to hardcoded plugins
                    }
                }

                // Fall back to hardcoded plugins for backward compatibility
                switch (id)
                {
                    case "ResearchTask":
                        _plugins.Add(new ResearchTask());
                        break;
                    case "ActiveResearchTask":
                        _plugins.Add(new ActiveResearchTask());
                        break;

                }
            }
            // Now that everything is loaded check the config, and initialize new configs if needed
            foreach (var plugin in _plugins)
            {
                if (!Config.PluginSettings.ContainsKey(plugin.Id))
                {
                    Config.PluginSettings[plugin.Id] = plugin.GetDefaultSettings();
                }
            }
        }

        /// <summary>
        /// Register external plugin with the agent.
        /// </summary>
        /// <param name="id">The unique identifier for the plugin. This value cannot be null, empty, or consist only of whitespace.</param>
        /// <param name="plugin">class / interface of the plugin</param>
        public static void RegisterPlugin(string id, IAgentTask plugin)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Plugin ID cannot be null or empty", nameof(id));
            ArgumentNullException.ThrowIfNull(plugin);

            _pluginRegistry[id] = () => plugin;
        }

        /// <summary>
        /// Registers a plugin with the specified identifier and factory method.
        /// </summary>
        /// <param name="id">The unique identifier for the plugin. This value cannot be null, empty, or consist only of whitespace.</param>
        /// <param name="factory">A factory method that creates an instance of the plugin. This value cannot be null.</param>
        public static void RegisterPlugin(string id, Func<IAgentTask> factory)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Plugin ID cannot be null or empty", nameof(id));
            ArgumentNullException.ThrowIfNull(factory);

            _pluginRegistry[id] = factory;
        }

        /// <summary>
        /// Unregisters a plugin from the system using its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the plugin to unregister. Must not be null, empty, or consist only of whitespace.</param>
        public static void UnregisterPlugin(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            _pluginRegistry.Remove(id);
        }

        /// <summary>
        /// List all registered plugin IDs.
        /// </summary>
        /// <returns></returns>
        public static IReadOnlyList<string> GetRegisteredPluginIds()
        {
            var lst = _pluginRegistry.Keys.ToList();
            lst.Add("ResearchTask");
            lst.Add("ActiveResearchTask");
            return lst.AsReadOnly();
        }

        /// <summary>
        /// Scans an already-loaded assembly and auto-discovers and registers all
        /// <see cref="IAgentTask"/> and <see cref="IAgentAction{TResult,TParam}"/> implementations.
        /// </summary>
        /// <param name="assembly">Assembly to scan for agent task/action plugin types.</param>
        public static void RegisterFromAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var dllName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";

            // 1) Auto-discover IAgentTask implementations
            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IAgentTask).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var instance = (IAgentTask)Activator.CreateInstance(type)!;
                if (_pluginRegistry.ContainsKey(instance.Id))
                    continue;
                var capturedType = type;
                _pluginRegistry[instance.Id] = () => (IAgentTask)Activator.CreateInstance(capturedType)!;
                LLMEngine.Logger?.LogInformation("Auto-registered task plugin: {id} from {dll}", instance.Id, dllName);
            }

            // 2) Auto-discover IAgentAction<,> implementations
            foreach (var type in assembly.GetTypes()
                         .Where(t => !t.IsAbstract && !t.IsInterface
                                     && t.GetInterfaces().Any(i => i.IsGenericType
                                                                    && i.GetGenericTypeDefinition() == typeof(IAgentAction<,>))))
            {
                var instance = Activator.CreateInstance(type)!;
                var id = type.GetProperty("Id")?.GetValue(instance) as string;
                if (string.IsNullOrEmpty(id) || _actions.ContainsKey(id))
                    continue;
                _actions[id] = instance;
                LLMEngine.Logger?.LogInformation("Auto-registered action plugin: {id} from {dll}", id, dllName);
            }
        }

        /// <summary>
        /// Loads a plugin DLL.
        /// </summary>
        /// <param name="dllPath">Absolute path to the plugin DLL.</param>
        /// <exception cref="FileNotFoundException">Thrown when the DLL file does not exist.</exception>
        [Obsolete("Use LLMEngine.RegisterPlugin() instead.")]
        public static void RegisterDll(string dllPath)
        {
            LLMEngine.RegisterPlugin(dllPath);
        }

        /// <summary>
        /// Loads all plugin DLLs from a directory, calling <see cref="RegisterDll"/> for each match.
        /// Returns silently if the directory does not exist. Errors for individual DLLs are logged
        /// and do not prevent other DLLs from loading.
        /// </summary>
        /// <param name="directoryPath">Path to the plugins folder.</param>
        /// <param name="searchPattern">File search pattern; defaults to <c>*.dll</c>.</param>
        [Obsolete("Use LLMEngine.RegisterPluginsFromDirectory() instead.")]
        public static void RegisterPluginsFromDirectory(string directoryPath, string searchPattern = "*.dll")
        {
            LLMEngine.RegisterPluginsFromDirectory(directoryPath, searchPattern);
        }

        #endregion


        #region *** Action Management ***

        internal static void LoadDefaultActions()
        {
            RegisterAction(new CalendarUpdateAction());
            RegisterAction(new DeepSearchAction());
            RegisterAction(new FindResearchTopicsAction());
            RegisterAction(new FindSingleTopicSearchAction());
            RegisterAction(new MergeSearchResultsAction());
            RegisterAction(new SessionAnalysisAction());
            RegisterAction(new WebSearchAction());
        }

        public static void RegisterAction<TResult, TParam>(IAgentAction<TResult, TParam> action)
        {
            _actions[action.Id] = action;
        }

        public static IAgentAction<TResult, TParam>? GetAction<TResult, TParam>(string id)
        {
            if (_actions.TryGetValue(id, out var action))
                return action as IAgentAction<TResult, TParam>;
            return null;
        }

        public static bool IsActionRegistered(string id)
        {
            return _actions.ContainsKey(id);
        }

        public static void UnregisterAction(string id)
        {
            _actions.Remove(id);
        }

        #endregion

    }
}
