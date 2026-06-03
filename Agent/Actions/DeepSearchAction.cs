using LetheAISharp.Agent.Research;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using static LetheAISharp.SearchAPI.WebSearchAPI;

namespace LetheAISharp.Agent.Actions
{
    /// <summary>
    /// Represents an action that performs a deep web search regarding a specific topic or query.
    /// </summary>
    public class DeepSearchAction : IAgentAction<DeepResearchResult, string>
    {
        private readonly Action<DeepResearchProgress>? _progress;

        public DeepSearchAction(Action<DeepResearchProgress>? progress = null)
        {
            _progress = progress;
        }

        public string Id => "DeepSearchAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.WebSearch, AgentActionRequirements.LLM, AgentActionRequirements.Grammar ];

        public async Task<DeepResearchResult> Execute(string param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return new DeepResearchResult() { Success = false, Error = "Operation cancelled." };

            var engine = new DeepResearchEngine(LLMEngine.Settings.DeepResearch,
                LLMEngine.Logger,
                progress: p =>
                {
                    LLMEngine.Logger?.LogInformation("DeepResearch [{Phase}] round={Round} msg={Message}", p.Phase, p.Round, p.Message);
                    _progress?.Invoke(p);
                });

            var result = await engine.ResearchAsync(param, ct).ConfigureAwait(false);
            return result;
        }
    }
}
