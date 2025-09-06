using AIToolkit.Files;
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
            await Task.Delay(10, ct);
            // can't search the web? that's a bummer.
            if (!LLMSystem.SupportsWebSearch)
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

            foreach (var topic in searchtopics)
            {
                // Cancellation requested?
                if (ct.IsCancellationRequested)
                    return;
                // Compare to recent searches to avoid duplicate
                var wassearchedbefore = await LLMSystem.Bot.Brain.WasSearchedRecently(topic.Topic);
                if (wassearchedbefore)
                    continue;

                LLMSystem.Bot.Brain.RecentSearches.Add(topic);

                var allResults = new List<EnrichedSearchResult>();

                // Execute searches for all the queries the LLM / Agent left for us to do
                foreach (var query in topic.SearchQueries)
                {
                    if (ct.IsCancellationRequested)
                        return;
                    var hits = await LLMSystem.WebSearch(query);
                    if (hits != null && hits.Count > 0)
                    {
                        allResults.AddRange(hits);
                    }
                }

                if (allResults.Count == 0)
                    continue; // Skip this topic if no results

                // Merge all results for this topic into a single memory unit
                var merged = await MergeResults(session, topic.Topic, topic.Reason, allResults);
                if (string.IsNullOrWhiteSpace(merged))
                    continue; // Skip this topic if merge failed

                // Store directly into the persona's Brain
                var mem = new MemoryUnit
                {
                    Category = MemoryType.WebSearch,
                    Insertion = MemoryInsertion.Natural,
                    Name = topic.Topic,
                    Content = merged.CleanupAndTrim(),
                    Reason = topic.Reason,
                    Added = DateTime.Now,
                    EndTime = DateTime.Now.AddDays(10),
                    Priority = topic.Urgency
                };

                await mem.EmbedText();
                owner.Brain.Memories.Add(mem);
            }
            cfg.SetSetting("LastSessionGuid", session.Guid);
        }

        private static async Task<string> MergeResults(ChatSession session, string topic, string reason, List<EnrichedSearchResult> webresults)
        {
            LLMSystem.NamesInPromptOverride = false;
            var fullprompt = BuildMergerPrompt(session, topic, reason, webresults);
            var llmparams = LLMSystem.Sampler.GetCopy();
            if (llmparams.Temperature > 0.75)
                llmparams.Temperature = 0.75;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Max_length = 1024;
            llmparams.Prompt = fullprompt;
            var response = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
            {
                response = response.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            }
            response = response.RemoveUnfinishedSentence();
            LLMSystem.Logger?.LogInformation("WebSearch Plugin Result: {output}", response);
            LLMSystem.NamesInPromptOverride = null;
            return response;
        }

        private static string BuildMergerPrompt(ChatSession session, string topic, string reason, List<EnrichedSearchResult> webresults)
        {
            var prompt = new StringBuilder();
            prompt.AppendLinuxLine($"You are {LLMSystem.Bot.Name} and your goal is to analyze and merge information from the following documents regarding the subject of '{topic}'. This topic was made relevant during the previous chat session.");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"# Previous Chat Session");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"{session.Content.RemoveNewLines()}");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"# Documents Found:");
            prompt.AppendLinuxLine();
            var cnt = 0;
            foreach (var item in webresults)
            {
                prompt.AppendLinuxLine($"## {item.Title}").AppendLinuxLine();
                prompt.AppendLinuxLine($"{item.Description}").AppendLinuxLine();
                if (item.ContentExtracted && LLMSystem.GetTokenCount(item.FullContent) <= 3000)
                    cnt++;
            }
            prompt.AppendLinuxLine();
            if (cnt > 0)
            {
                prompt.AppendLinuxLine($"You can also use the following content to improve your response (this is extracted directly from the web pages, meaning there might be clutter in there).").AppendLinuxLine();
                for (var i = 0; i < webresults.Count; i++)
                {
                    var item = webresults[i];
                    var tks = LLMSystem.GetTokenCount(item.FullContent);
                    if (item.ContentExtracted && tks > 100 && tks <= 2500)
                    {
                        prompt.AppendLinuxLine($"## {item.Title} (Full Content)");
                        prompt.AppendLinuxLine($"{item.FullContent.CleanupAndTrim()}").AppendLinuxLine().AppendLinuxLine();
                    }
                }
            }
            var sysprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.SysPrompt, LLMSystem.User, LLMSystem.Bot, prompt.ToString().CleanupAndTrim());

            var txt = new StringBuilder($"You are researching '{topic}' for the following reason: {reason}").AppendLinuxLine().Append($"Merge the information available in the system prompt to offer an explanation on this topic. Don't use markdown formatting, favor natural language. The explanation should be 1 to 3 paragraphs long.");
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, LLMSystem.Bot, txt.ToString());
            LLMSystem.NamesInPromptOverride = false;
            msg += LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            LLMSystem.NamesInPromptOverride = null;
            return sysprompt + msg;
        }


    }
}