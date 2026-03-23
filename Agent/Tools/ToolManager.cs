using CommunityToolkit.HighPerformance.Helpers;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    public class ToolManager
    {

        private readonly Dictionary<string, IToolList> _toolLists = [];

        public HashSet<string> AllowedToolSets => LLMEngine.Settings.AllowedToolsets;

        public List<Tool> GetToolList() {
            return _toolLists.Count == 0 ? [] : [.. _toolLists.Values.Where(e => AllowedToolSets.Contains(e.Id)).SelectMany(tl => tl.GetToolList())];
            }

        public void RegisterToolList(IToolList toolList)
        {
            if (toolList == null || string.IsNullOrWhiteSpace(toolList.Id))
                throw new ArgumentException("Tool list must have a valid ID.");
            _toolLists[toolList.Id] = toolList;
            toolList.LoadTools();
        }

        public List<string> GetRegisteredToolListIds() => [.. _toolLists.Keys];

        public bool UnregisterToolList(string id)
        {
            if (_toolLists.ContainsKey(id))
            {
                _toolLists[id].UnloadTools();
                _toolLists.Remove(id);
                return true;
            }
            else
            {
                return false;
            }
        }

        public IReadOnlyList<Tool> GetToolsForIds(params string[] ids)
        {
            var tools = new List<Tool>();
            foreach (var id in ids)
            {
                if (_toolLists.TryGetValue(id, out var toolList))
                {
                    tools.AddRange(toolList.GetToolList());
                }
                else
                {
                    throw new KeyNotFoundException($"No tool list found with ID: {id}");
                }
            }
            return tools;
        }

        public bool RequiresConfirmation(string functionName)
        {
            return _toolLists.Values.Any(tl => tl.RequiresConfirmation(functionName));
        }

        public bool HasTools()
        {
            return _toolLists.Values.FirstOrDefault(e => AllowedToolSets.Contains(e.Id)) is not null;
        }

        public int EstimatedTokenCost()
        {
            return _toolLists.Values.Where(e => AllowedToolSets.Contains(e.Id)).Sum(tl => tl.EstimatedTokenCost) * 2;
        }

        /// <summary>
        /// Loads a plugin DLL and auto-discovers and registers all
        /// <see cref="IAgentTask"/> and <see cref="IAgentAction{TResult,TParam}"/> implementations.
        /// Any class implementing <see cref="IPluginEntry"/> is invoked first so the plugin
        /// can perform its own registration logic before automatic discovery runs.
        /// </summary>
        /// <param name="dllPath">Absolute path to the plugin DLL.</param>
        /// <exception cref="FileNotFoundException">Thrown when the DLL file does not exist.</exception>
        public void RegisterDll(string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"Plugin DLL not found: {dllPath}", dllPath);

            var assembly = Assembly.LoadFrom(dllPath);
            var dllName = Path.GetFileName(dllPath);

            // 1) Invoke any explicit entry points first (gives plugin authors full control)
            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IToolPluginEntry).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                try
                {
                    var entry = (IToolPluginEntry)Activator.CreateInstance(type)!;
                    entry.Register();
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogError(ex, "Failed to invoke IPluginEntry on type {type} from {dll}", type.FullName, dllName);
                }
            }

            // 2) Auto-discover IToolList implementations
            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IToolList).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var instance = (IToolList)Activator.CreateInstance(type)!;
                if (_toolLists.ContainsKey(instance.Id))
                    continue;
                var capturedType = type;
                RegisterToolList((IToolList)Activator.CreateInstance(capturedType)!);
                LLMEngine.Logger?.LogInformation("Auto-registered toolset plugin: {id} from {dll}", instance.Id, dllName);
            }
        }

        /// <summary>
        /// Loads all plugin DLLs from a directory, calling <see cref="RegisterDll"/> for each match.
        /// Returns silently if the directory does not exist. Errors for individual DLLs are logged
        /// and do not prevent other DLLs from loading.
        /// </summary>
        /// <param name="directoryPath">Path to the plugins folder.</param>
        /// <param name="searchPattern">File search pattern; defaults to <c>*.dll</c>.</param>
        public void RegisterPluginsFromDirectory(string directoryPath, string searchPattern = "*.dll")
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var dll in Directory.GetFiles(directoryPath, searchPattern))
            {
                try
                {
                    RegisterDll(dll);
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogError(ex, "Failed to load plugin DLL: {dll}", Path.GetFileName(dll));
                }
            }
        }

    }
}
