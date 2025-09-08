using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Agent
{
    public enum AgentActionRequirements
    {
        LLM, WebSearch, ImageRecognition, Grammar
    }

    public interface IAgentAction<Result, Param>
    {
        string Id { get; }
        HashSet<AgentActionRequirements> Requirements { get; }
        Task<Result> Execute(Param param, CancellationToken ct);
    }
}
