using LetheAISharp.LLM;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    public class ToolManager
    {

        private readonly Dictionary<string, IToolList> _toolLists = [];

        public HashSet<string> AllowedToolSets => LLMEngine.Settings.AllowedToolsets;

        public List<Tool> GetToolList() => _toolLists.Count == 0 ? [] : [.. _toolLists.Values.SelectMany(tl => tl.GetToolList()).Where(e => AllowedToolSets.Contains(e.Id))];

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
    }
}
