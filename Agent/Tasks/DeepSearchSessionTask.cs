using LetheAISharp.Agent.Research;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using Microsoft.Extensions.Logging;

namespace LetheAISharp.Agent.Plugins
{
    /// <summary>
    /// Optional task wrapper around the iterative deep research engine.
    /// This can coexist with the current ResearchTask.
    /// </summary>
    public sealed class DeepSearchSessionTask : IAgentTask
    {
        public string Id => "DeepSearchSessionTask";
        public string Ability => "Perform iterative deep web research about previous chat session.";

        public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            await Task.Delay(10, ct).ConfigureAwait(false);

            if (!LLMEngine.SupportsWebSearch || LLMEngine.Status != SystemStatus.Ready)
                return false;

            // TODO:
            // Decide what should trigger this task.
            // For now: same rough logic as ResearchTask, or a more selective one.
            return owner.History.Sessions.Count >= 2;
        }

        public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            // TODO:
            // Decide how to derive the research question.
            // Possibilities:
            // - summarize previous session into a research objective
            // - use a specific user request
            // - run FindResearchTopicsAction first and use top topic as seed

            var question = "Research the most important unfamiliar topic from the last archived session.";

            var engine = new DeepResearchEngine(
                new DeepResearchOptions
                {
                    MaxRounds = 4,
                    MinRounds = 2,
                    MaxQueriesPerRound = 3,
                    MaxResultsPerQuery = 3
                },
                LLMEngine.Logger,
                progress: p =>
                {
                    LLMEngine.Logger?.LogInformation(
                        "DeepResearch [{Phase}] round={Round} msg={Message}",
                        p.Phase, p.Round, p.Message);
                });

            var result = await engine.ResearchAsync(question, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.FinalReport))
                return;

            var mem = new MemoryUnit
            {
                Category = MemoryType.WebSearch,
                Insertion = MemoryInsertion.Natural,
                Name = "Deep Research: " + result.Question.RemoveNewLines(),
                Content = result.FinalReport.CleanupAndTrim(),
                Reason = "The user asked the following question: "+ question,
                Added = DateTime.Now,
                EndTime = DateTime.Now.AddDays(14),
                Priority = 100
            };

            await mem.BuildEmbedding().ConfigureAwait(false);
            owner.Brain.Memorize(mem);

            owner.Brain.AddUserReturnInsert("{{mchar}} completed a deep research pass about a previous topic.", this.Id);
        }

        public AgentTaskSetting GetDefaultSettings() => new();
    }
}