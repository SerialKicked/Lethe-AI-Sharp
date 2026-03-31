using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    public class CompositeToolList(params IToolList[] toolLists) : IToolList
    {
        public string Id => string.Join("+", toolLists.Select(t => t.Id));

        public string Description => string.Join("\n\n", toolLists.Select(t => $"{t.Id}: {t.Description}"));

        public string SystemPromptInstruction => string.Join("\n\n", toolLists.Select(t => t.SystemPromptInstruction));

        public IReadOnlyList<Tool> GetToolList() => [.. toolLists.SelectMany(t => t.GetToolList())];

        public void LoadTools(bool clearExisting = false)
        {
            foreach (var toolList in toolLists)
            {
                toolList.LoadTools(clearExisting);
                clearExisting = false; // Only clear for the first tool list (otherwise it makes no sense to have multiple tool lists)
            }
        }

        public void UnloadTools()
        {
            foreach (var toolList in toolLists)
            {
                toolList.UnloadTools();
            }
        }

        public bool RequiresConfirmation(string functionName) => toolLists.Any(t => t.RequiresConfirmation(functionName));
    }
}
