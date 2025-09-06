using AIToolkit.Agent.Plugins;
using AIToolkit.Files;
using AIToolkit.LLM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Agent
{
    public interface IAgentTask
    {
        string Id { get; }
        Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
        Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
    }


    public class AgentTaskSetting
    {
        public string PluginId { get; set; } = string.Empty;
        public Dictionary<string, JToken> Settings { get; set; } = [];

        public T? GetSetting<T>(string key, T? defaultValue = default)
        {
            return Settings.TryGetValue(key, out var token) ? token.Value<T>() : defaultValue;
        }

        public void SetSetting<T>(string key, T value)
        {
            Settings[key] = value is null ? JToken.FromObject(string.Empty) : JToken.FromObject(value);
        }
    }

    public class AgentLoopConfig
    {
        public Dictionary<string, AgentTaskSetting> PluginSettings { get; set; } = [];
    }

    public class AgentLoop(BasePersona owner)
    {
        public BasePersona Owner { get; private set; } = owner;
        public AgentLoopConfig Config { get; set; } = new();

        private CancellationTokenSource? _cts = new();
        private bool _running;
        private Task? _loop;
        private readonly List<IAgentTask> _plugins = [];
        private readonly Dictionary<string, Func<IAgentTask>> _pluginRegistry = [];


        private async Task MainLoop()
        {
            _running = true;
            // Initial delay to allow the system to settle. if the persona was just loaded, it means that the user is active, anyway.
            await Task.Delay(10000, _cts!.Token);

            while (_running && !_cts.Token.IsCancellationRequested)
            {
                if (!Owner.AgentMode)
                {
                    await Task.Delay(1000, _cts.Token);
                    continue;
                }
                foreach (var plugin in _plugins)
                {
                    if (!Config.PluginSettings.TryGetValue(plugin.Id, out var setting))
                        continue;
                    try
                    {
                        var shouldrun = await plugin.Observe(Owner, setting, _cts.Token);
                        if (shouldrun)
                        {
                            await plugin.Execute(Owner, setting, _cts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LLMSystem.Logger?.LogError(ex, "Error in plugin {PluginId}: {ex}", plugin.Id, ex.Message);
                    }
                    if (!_running || _cts.Token.IsCancellationRequested)
                        break;
                    await Task.Delay(1000, _cts.Token);
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
                throw new InvalidOperationException("Agent mode is already running.");
            LoadSettings();
            LoadPlugins();
            if (_cts == null || _cts.IsCancellationRequested)
                _cts = new CancellationTokenSource();
            _loop = Task.Run(MainLoop);
        }

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
                await _loop;
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
            var selpath = LLMSystem.Settings.DataPath;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;

            var content = JsonConvert.SerializeObject(Config, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
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
            var selpath = LLMSystem.Settings.DataPath;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var filepath = selpath + Owner.UniqueName + ".agent";
            if (!File.Exists(filepath))
                return;
            try
            {
                var content = File.ReadAllText(filepath);
                var cfg = JsonConvert.DeserializeObject<AgentLoopConfig>(content);
                if (cfg != null)
                    Config = cfg;
            }
            catch (Exception ex)
            {
                LLMSystem.Logger?.LogError(ex, "Failed to load agent config for {UniqueName}: {ex}", Owner.UniqueName, ex.Message);
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
                }
            }
            // Now that everything is loaded check the config, and initialize new configs if needed
            foreach (var plugin in _plugins)
            {
                if (!Config.PluginSettings.ContainsKey(plugin.Id))
                {
                    Config.PluginSettings[plugin.Id] = new AgentTaskSetting { PluginId = plugin.Id };
                }
            }
        }

        /// <summary>
        /// Register external plugin with the agent.
        /// </summary>
        /// <param name="id">The unique identifier for the plugin. This value cannot be null, empty, or consist only of whitespace.</param>
        /// <param name="plugin">class / interface of the plugin</param>
        public void RegisterPlugin(string id, IAgentTask plugin)
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
        public void RegisterPlugin(string id, Func<IAgentTask> factory)
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
        public void UnregisterPlugin(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            _pluginRegistry.Remove(id);
        }

        /// <summary>
        /// List all registered plugin IDs.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<string> GetRegisteredPluginIds()
        {
            return _pluginRegistry.Keys.ToList().AsReadOnly();
        }

        #endregion

    }
}
