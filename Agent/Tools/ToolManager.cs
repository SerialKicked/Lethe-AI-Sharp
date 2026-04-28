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
    /// <summary>
    /// Manages the registration, discovery, and access of tool sets for the LLM engine. Provides methods to
    /// register, unregister, and query tool lists, as well as to load plugins dynamically from DLLs or directories.
    /// </summary>
    /// <remarks>The ToolManager coordinates tool set availability based on the current engine configuration
    /// and persona. It supports dynamic plugin loading, auto-discovery of tool implementations, and ensures that only
    /// allowed tool sets are accessible.</remarks>
    public class ToolManager
    {
        /// <summary>
        /// Gets the set of toolset names that are currently allowed for use by the engine.
        /// </summary>
        /// <remarks>The returned set reflects either the default allowed toolsets or an overridden set if the currently loaded persona specifies a different set.
        /// </remarks>
        public HashSet<string> AllowedToolSets => LLMEngine.Bot.OverrideDefaultToolset ? LLMEngine.Bot.Tools : LLMEngine.Settings.AllowedToolsets;

        private readonly Dictionary<string, IToolList> _toolLists = [];

        /// <summary>
        /// Returns a combined list of all tools from registered tool lists that are in the allowed toolsets. 
        /// If no tool lists are registered, returns an empty list.
        /// </summary>
        public List<Tool> GetToolList() {
            return _toolLists.Count == 0 ? [] : [.. _toolLists.Values.Where(e => AllowedToolSets.Contains(e.Id)).SelectMany(tl => tl.GetToolList())];
            }

        public List<IToolList> GetToolsets()
        {
            return _toolLists.Count == 0 ? [] : [.. _toolLists.Values.Where(e => AllowedToolSets.Contains(e.Id))];
        }

        /// <summary>
        /// Register a toolset with the manager. The tool list's <see cref="IToolList.LoadTools"/> method is called immediately to allow it to initialize any resources.
        /// </summary>
        /// <param name="toolList"></param>
        /// <exception cref="ArgumentException"></exception>
        public void RegisterToolList(IToolList toolList)
        {
            if (toolList == null || string.IsNullOrWhiteSpace(toolList.Id))
                throw new ArgumentException("Tool list must have a valid ID.");
            _toolLists[toolList.Id] = toolList;
            toolList.LoadTools();
        }

        /// <summary>
        /// Retrieves a list of identifiers for all registered tool lists.
        /// </summary>
        /// <returns>A list of strings containing the unique identifiers of all registered tool lists. The list is empty if no
        /// tool lists are registered.</returns>
        public List<string> GetRegisteredToolListIds() => [.. _toolLists.Keys];

        /// <summary>
        /// Unregisters the tool list associated with the specified identifier and releases its resources.
        /// </summary>
        /// <remarks>If the tool list is found, its resources are released before removal. This method has
        /// no effect if the specified identifier does not exist.</remarks>
        /// <param name="id">The unique identifier of the tool list to unregister. Cannot be null.</param>
        /// <returns>true if the tool list was found and unregistered; otherwise, false.</returns>
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
        
        /// <summary>
        /// Retrieves a list of tools for the specified tool list identifiers.
        /// </summary>
        /// <param name="ids">An array of tool list identifiers.</param>
        /// <returns>A read-only list of tools from the specified tool lists.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if any of the specified tool list identifiers do not exist.</exception>
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

        /// <summary>
        /// Determines whether the specified function requires user confirmation before execution.
        /// </summary>
        /// <param name="functionName">The name of the function to check for confirmation requirements. Cannot be null or empty.</param>
        /// <returns>true if the specified function requires confirmation; otherwise, false.</returns>
        public bool RequiresConfirmation(string functionName)
        {
            return _toolLists.Values.Any(tl => tl.RequiresConfirmation(functionName));
        }
        
        /// <summary>
        /// Determines whether any tools are available in the allowed tool sets.
        /// </summary>
        /// <returns>true if there are tools available; otherwise, false.</returns>
        public bool HasTools()
        {
            return _toolLists.Values.FirstOrDefault(e => AllowedToolSets.Contains(e.Id)) is not null;
        }

        /// <summary>
        /// Calculates the estimated total token cost for all tool lists that are included in the allowed tool sets.
        /// </summary>
        /// <remarks>The returned value is the sum of the estimated token costs for each eligible tool
        /// list. Only tool lists with identifiers contained in the allowed tool sets are considered in the calculation.</remarks>
        /// <returns>The estimated total token cost for all tool lists whose identifiers are present in the allowed tool sets.</returns>
        public int EstimatedTokenCost()
        {
            return _toolLists.Values.Where(e => AllowedToolSets.Contains(e.Id)).Sum(tl => tl.EstimatedTokenCost) * 2;
        }

        /// <summary>
        /// Scans an already-loaded assembly and auto-discovers and registers all
        /// <see cref="IToolList"/> implementations.
        /// </summary>
        /// <param name="assembly">Assembly to scan for toolset plugin types.</param>
        public void RegisterFromAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var dllName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";

            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IToolList).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var instance = (IToolList)Activator.CreateInstance(type)!;
                if (_toolLists.ContainsKey(instance.Id))
                    continue;
                RegisterToolList(instance);
                LLMEngine.Logger?.LogInformation("Auto-registered toolset plugin: {id} from {dll}", instance.Id, dllName);
            }
        }

        /// <summary>
        /// Loads a plugin DLL.
        /// </summary>
        /// <param name="dllPath">Absolute path to the plugin DLL.</param>
        /// <exception cref="FileNotFoundException">Thrown when the DLL file does not exist.</exception>
        [Obsolete("Use LLMEngine.RegisterPlugin() instead.")]
        public void RegisterDll(string dllPath)
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
        public void RegisterPluginsFromDirectory(string directoryPath, string searchPattern = "*.dll")
        {
            LLMEngine.RegisterPluginsFromDirectory(directoryPath, searchPattern);
        }

    }
}
