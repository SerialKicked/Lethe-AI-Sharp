using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    public interface IToolList
    {
        string Id { get; }
        string Description { get; }
        string SystemPromptInstruction { get; }

        IReadOnlyList<Tool> GetToolList();
        bool RequiresConfirmation(string functionName) => false;
        void LoadTools(bool clearExisting = false);
        void UnloadTools();

        /// <summary>
        /// Whether this toolset is currently available. A toolset listed in the allowed toolsets is ignored by the <see cref="ToolManager"/> when this returns false.
        /// Lets a toolset be switched off at runtime without editing the global or per-persona allowed lists.
        /// </summary>
        bool IsAvailable => true;

        /// <summary>
        /// Estimated token cost of including this toolset in the prompt.
        /// Used to make budget-aware decisions when composing toolsets.
        /// </summary>
        int EstimatedTokenCost => GetToolList().Sum(t => 
            TokenTools.CountTokensApprox(t.Function?.Name ?? "unknown") + 
            TokenTools.CountTokensApprox(t.Function?.Description ?? string.Empty) +
            TokenTools.CountTokensApprox(t.Function?.Parameters?.ToJsonString() ?? string.Empty));
    }
}
