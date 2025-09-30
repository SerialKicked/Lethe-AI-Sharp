using LetheAISharp.Agent.Actions;
using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using static LetheAISharp.SearchAPI.WebSearchAPI;

namespace LetheAISharp.Agent.Plugins
{
    /// <summary>
    /// Provides an active research task that monitors the ongoing chat session and performs web searches on unfamiliar topics.
    /// Results are then handled by the brain class and brought into conversation when relevant.
    /// </summary>
    /// <remarks>This task is designed to operate within the context of an agent's workflow. It observes the
    /// persona's session history to determine if research is required, and if so, it performs web searches on
    /// unfamiliar topics and stores the results in the persona's memory./remarks>
    public sealed class ActiveResearchTask : IAgentTask
    {
        public string Id => "ActiveResearchTask";
        public string Ability => "Do research on the web";

        public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            // Just a small delay so i don't have to remove async and do Task.ResultFrom everywhere. It's not like we're on a timer anyway.
            await Task.Delay(10, ct).ConfigureAwait(false);

            // can't do what we need? that's a bummer.
            if (LLMEngine.SupportsSchema == false || LLMEngine.Status != SystemStatus.Ready || !LLMEngine.SupportsWebSearch)
                return false;

            var delay = cfg.GetSetting<TimeSpan>("Delay");
            var lastsearch = cfg.GetSetting<DateTime>("LastSearch");
            var minmsgs = cfg.GetSetting<int>("MinMessages");
            var lastguid = cfg.GetSetting<Guid>("LastMessageGuid");

            // Get current active session
            var activesession = owner.History.CurrentSession;
            if (activesession.Messages.Count < minmsgs || (DateTime.Now - lastsearch) < delay)
                return false; // Don't search if the session is too short

            // find ID of last message guid, and if not found start at zero
            var lastmsgindex = activesession.Messages.FindIndex(m => m.Guid == lastguid);
            if (lastmsgindex == -1)
                lastmsgindex = 0;
            // Make a list of the messages starting at lastmsgindex to the end
            var newmessages = activesession.Messages.Skip(lastmsgindex).ToList();
            if (newmessages.Count < minmsgs)
                return false; // Not enough new messages to consider searching
            return true;
        }

        public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            var activesession = owner.History.CurrentSession;

            // find ID of last message guid, and if not found start at zero
            var lastguid = cfg.GetSetting<Guid>("LastMessageGuid");
            var lastmsgindex = activesession.Messages.FindIndex(m => m.Guid == lastguid);
            if (lastmsgindex == -1)
                lastmsgindex = 0;
            // Make a list of the messages starting at lastmsgindex to the end
            var newmessages = activesession.Messages.Skip(lastmsgindex).ToList();

            var webSearchAction = AgentRuntime.GetAction<List<EnrichedSearchResult>, TopicSearch>("WebSearchAction");
            var mergeAction = AgentRuntime.GetAction<string, MergeSearchParams>("MergeSearchResultsAction");
            var findTopicsAction = AgentRuntime.GetAction<TopicLookup?, FindResearchTopicsParams>("FindResearchTopicsAction");

            if (findTopicsAction == null || mergeAction == null || webSearchAction == null)
            {
                LLMEngine.Logger?.LogWarning("ActiveResearchTask cancelled due to missing action.");
                return;
            }
            var param = new FindResearchTopicsParams
            {
                Messages = newmessages,
                IncludeBios = cfg.GetSetting<bool>("UseBio")
            };
            var foundtopics = await findTopicsAction.Execute(param, ct).ConfigureAwait(false);
            if (foundtopics == null || foundtopics.Unfamiliar_Topics.Count == 0)
                return; // Nothing found

            // pick the topic with the highest priority, remove all those who were searched recently
            var searchtopics = foundtopics.Unfamiliar_Topics.OrderByDescending(t => t.Urgency).ThenBy(t => Guid.NewGuid()).Take(3).ToList();
            var actuallist = new List<TopicSearch>();
            foreach (var testtopic in searchtopics)
            {
                if (ct.IsCancellationRequested)
                    return;
                var wassearchedbefore = await LLMEngine.Bot.Brain.WasSearchedRecently(testtopic.Topic, 0.085f).ConfigureAwait(false);
                if (!wassearchedbefore)
                    actuallist.Add(testtopic);
            }
            if (actuallist.Count == 0)
                return; // Nothing to search for
            foreach (var topic in actuallist)
            {
                var allResults = await webSearchAction.Execute(topic, ct).ConfigureAwait(false);
                if (allResults.Count == 0)
                    continue; // Skip this topic if no results
                LLMEngine.Bot.Brain.RecentSearches.Add(topic);

                // Merge all results for this topic into a single memory unit
                var mergeparams = new MergeSearchParams("This is a search done regarding the currently active discussion.", topic.Topic, topic.Reason, allResults);
                var merged = await mergeAction.Execute(mergeparams, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(merged))
                    return; // Skip this topic if merge failed

                // Store directly into the persona's Brain
                var mem = new MemoryUnit
                {
                    Category = MemoryType.WebSearch,
                    Insertion = MemoryInsertion.Natural,
                    Name = topic.Topic,
                    Content = merged.CleanupAndTrim(),
                    Reason = topic.Reason,
                    Added = DateTime.Now,
                    EndTime = DateTime.Now.AddDays(7),
                    Priority = topic.Urgency + 1
                };

                await mem.EmbedText().ConfigureAwait(false);
                owner.Brain.Memorize(mem);
            }
            owner.Brain.AddUserReturnInsert("{{char}} found some new information that might be useful regarding the recently discussed topics.");
            cfg.SetSetting<Guid>("LastMessageGuid", activesession.Messages[^1].Guid);
            cfg.SetSetting<DateTime>("LastSearch", DateTime.Now);
        }

        public AgentTaskSetting GetDefaultSettings()
        {
            var cfg = new AgentTaskSetting();
            cfg.SetSetting<Guid>("LastMessageGuid", Guid.Empty);
            cfg.SetSetting<bool>("UseBio", true);
            cfg.SetSetting<int>("MinMessages", 30);
            cfg.SetSetting<TimeSpan>("Delay", new TimeSpan(4,0,0)); // Don't trigger if last search was less than 8 hours ago
            cfg.SetSetting<DateTime>("LastSearch", DateTime.MinValue);
            return cfg;
        }
    }
}