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
    public sealed class ResearchTask : IAgentTask
    {
        public string Id => "ResearchTask";

        public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            // Just a small delay so i don't have to remove async and do Task.ResultFrom everywhere. It's not like we're on a timer anyway.
            await Task.Delay(10, ct).ConfigureAwait(false);
            // can't search the web? that's a bummer.
            if (!LLMSystem.SupportsWebSearch || LLMSystem.Status != SystemStatus.Ready)
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
            // Check if the last session has search requests
            if (lastsession.NewTopics.Unfamiliar_Topics == null || lastsession.NewTopics.Unfamiliar_Topics.Count == 0)
                return false;
            return true;
        }

        public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            var session = owner.History.Sessions[^2];
            // retrieve search requests
            var searchtopics = session.NewTopics.Unfamiliar_Topics;
            // really shouldn't happen, but hey maybe some third party shenanigans
            if (searchtopics == null || searchtopics.Count == 0)
                return;

            // Get the web search action from registry
            var webSearchAction = AgentRuntime.GetAction<List<EnrichedSearchResult>, TopicSearch>("WebSearchAction");
            var mergeAction = AgentRuntime.GetAction<string, MergeSearchParams>("MergeSearchResultsAction");

            if (webSearchAction == null || mergeAction == null)
            {
                LLMSystem.Logger?.LogWarning("Required actions not found in registry. ResearchTask cancelled.");
                return;
            }

            foreach (var topic in searchtopics)
            {
                // Cancellation requested?
                if (ct.IsCancellationRequested)
                    return;
                // Compare to recent searches to avoid duplicate
                var wassearchedbefore = await LLMSystem.Bot.Brain.WasSearchedRecently(topic.Topic).ConfigureAwait(false);
                if (wassearchedbefore)
                    continue;
                LLMSystem.Bot.Brain.RecentSearches.Add(topic);

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