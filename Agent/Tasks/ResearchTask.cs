using AIToolkit.Agent.Actions;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using AIToolkit.Memory;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using static AIToolkit.SearchAPI.WebSearchAPI;

namespace AIToolkit.Agent.Plugins
{
    /// <summary>
    /// Represents a task that performs research by analyzing unfamiliar topics from a persona's session history and
    /// executing web searches to gather and merge relevant information. It works on archived sessions only and works
    /// well in tandem with <seealso cref="ActiveResearchTask"/>.
    /// </summary>
    /// <remarks>This task is designed to operate within the context of an agent's workflow. It observes the
    /// persona's session history to determine if research is required, and if so, it performs web searches on
    /// unfamiliar topics and stores the results in the persona's memory./remarks>
    public sealed class ResearchTask : IAgentTask
    {
        public string Id => "ResearchTask";

        public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            // Just a small delay so i don't have to remove async and do Task.ResultFrom everywhere. It's not like we're on a timer anyway.
            await Task.Delay(10, ct).ConfigureAwait(false);
            // can't search the web? that's a bummer.
            if (!LLMEngine.SupportsWebSearch || LLMEngine.Status != SystemStatus.Ready)
                return false;
            // Check if there's at least 2 sessions, otherwise we're in the first session and we don't have searches to make, yet.
            var sessions = owner.History.Sessions;
            if (sessions.Count < 2)
                return false;
            var lastsession = sessions[^2];
            // Check if the last session already had a search, if so, don't do it again.
            var lastguidtest = cfg.GetSetting<Guid>("LastSessionGuid");
            if (lastsession.Guid == lastguidtest)
                return false;
            return true;
        }

        public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            var session = owner.History.Sessions[^2];

            // Get the web search action from registry
            var findTopicAction = AgentRuntime.GetAction<TopicLookup?, FindResearchTopicsParams>("FindResearchTopicsAction");
            var webSearchAction = AgentRuntime.GetAction<List<EnrichedSearchResult>, TopicSearch>("WebSearchAction");
            var mergeAction = AgentRuntime.GetAction<string, MergeSearchParams>("MergeSearchResultsAction");

            if (webSearchAction == null || mergeAction == null || findTopicAction == null)
            {
                LLMEngine.Logger?.LogWarning("Required actions not found in registry. ResearchTask cancelled.");
                return;
            }

            var searchparams = new FindResearchTopicsParams { Messages = session.Messages, IncludeBios = true };
            var lookup = await findTopicAction.Execute(searchparams, ct).ConfigureAwait(false);
            if (lookup == null || lookup.Unfamiliar_Topics.Count == 0)
                return;

            foreach (var topic in lookup.Unfamiliar_Topics)
            {
                // Cancellation requested?
                if (ct.IsCancellationRequested)
                    return;
                // Compare to recent searches to avoid duplicate
                var wassearchedbefore = await LLMEngine.Bot.Brain.WasSearchedRecently(topic.Topic).ConfigureAwait(false);
                if (wassearchedbefore)
                    continue;
                LLMEngine.Bot.Brain.RecentSearches.Add(topic);

                var allResults = await webSearchAction.Execute(topic, ct).ConfigureAwait(false);
                if (allResults.Count == 0)
                    continue; // Skip this topic if no results

                var mergeparams = new MergeSearchParams(session.Content, topic.Topic, topic.Reason, allResults);

                // Merge all results for this topic into a single memory unit
                var merged = await mergeAction.Execute(mergeparams, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(merged))
                    continue; // Skip this topic if merge failed

                // Store directly into the persona's Brain
                var mem = new MemoryUnit
                {
                    Category = MemoryType.WebSearch,
                    Insertion = MemoryInsertion.NaturalForced,
                    Name = topic.Topic,
                    Content = merged.CleanupAndTrim(),
                    Reason = topic.Reason,
                    Added = DateTime.Now,
                    EndTime = DateTime.Now.AddDays(10),
                    Priority = topic.Urgency
                };

                await mem.EmbedText().ConfigureAwait(false);
                owner.Brain.Memories.Add(mem);
            }
            cfg.SetSetting<Guid>("LastSessionGuid", session.Guid);
        }

        public AgentTaskSetting GetDefaultSettings()
        {
            return new AgentTaskSetting();
        }
    }
}