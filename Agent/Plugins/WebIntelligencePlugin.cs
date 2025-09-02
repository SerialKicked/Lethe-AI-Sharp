using AIToolkit.Files;
using AIToolkit.LLM;
using AIToolkit.Memory;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using static AIToolkit.SearchAPI.WebSearchAPI;

namespace AIToolkit.Agent.Plugins
{
    public sealed class WebIntelligencePlugin : IAgentPlugin
    {
        public string Id => "WebIntelligence"; 
        public IEnumerable<AgentTaskType> Supported => [AgentTaskType.Observe, AgentTaskType.ExecuteSearch];

        public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
        {
            // Respect daily search budget
            if (ctx.State.SearchesUsedToday >= ctx.Config.DailySearchBudget)
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            // Need at least one archived session (the one before current)
            var sessions = LLMSystem.History.Sessions;
            if (sessions.Count < 2)
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            var archived = sessions[^2];
            if (archived.Messages.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            // No topics, nothing to do
            if (archived.NewTopics?.Unfamiliar_Topics == null || archived.NewTopics.Unfamiliar_Topics.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>([]);
            
            // Already researched or in progress? Skip
            var key = $"web-{archived.Guid}";
            var hasPendingForSession = ctx.State.Queue.Any(t =>
                (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Running || t.Status == AgentTaskStatus.Deferred) &&
                t.Type == AgentTaskType.ExecuteSearch &&
                t.CorrelationKey == key);

            if (hasPendingForSession || LLMSystem.Bot.Brain.Has(MemoryType.WebSearch, archived.Guid))
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            // Only when idle
            if (ctx.IdleTime.TotalMinutes < ctx.Config.MinIdleMinutesBeforeBackgroundWork)
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            // Create one ExecuteSearch task for the entire session
            var task = new AgentTask
            {
                Type = AgentTaskType.ExecuteSearch,
                Priority = 2,
                PayloadJson = JsonSerializer.Serialize(new { SessionId = archived.Guid }),
                CorrelationKey = key,
                RequiresLLM = true // We'll need LLM for merging results
            };

            return Task.FromResult<IEnumerable<AgentTask>>([task]);
        }

        public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            if (task.Type != AgentTaskType.ExecuteSearch)
                return AgentTaskResult.Fail();

            return await ExecuteSearchAsync(task, ctx, ct);
        }

        private static async Task<AgentTaskResult> ExecuteSearchAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            if (ctx.State.SearchesUsedToday >= ctx.Config.DailySearchBudget)
                return AgentTaskResult.Fail();

            var payload = JsonSerializer.Deserialize<JsonElement>(task.PayloadJson);
            var sessionId = Guid.Parse(payload.GetProperty("SessionId").GetString()!);
            
            // Get the session and its topics directly
            var session = LLMSystem.History.GetSessionByID(sessionId);
            if (session?.NewTopics?.Unfamiliar_Topics == null)
                return AgentTaskResult.Fail();

            var totalSearchCount = 0;
            var allStagedMessages = new List<StagedMessage>();

            // Process each topic from the session
            foreach (var topic in session.NewTopics.Unfamiliar_Topics)
            {
                // Drop trivial ones if budget nearly exhausted
                if (topic.Urgency <= 1 && ctx.Config.DailySearchBudget - ctx.State.SearchesUsedToday - totalSearchCount < 5)
                    continue;

                var allResults = new List<EnrichedSearchResult>();
                var searchCount = 0;

                // Execute searches for all queries for this topic
                foreach (var query in topic.SearchQueries.Take(3))
                {
                    if (ctx.State.SearchesUsedToday + totalSearchCount + searchCount >= ctx.Config.DailySearchBudget)
                        break;

                    var hits = await LLMSystem.WebSearch(query);
                    if (hits != null && hits.Count > 0)
                    {
                        allResults.AddRange(hits);
                        searchCount++;
                    }
                }

                if (allResults.Count == 0)
                    continue; // Skip this topic if no results

                // Merge all results for this topic into a single memory
                var merged = await MergeResults(session, topic.Topic, topic.Reason, allResults);
                if (string.IsNullOrWhiteSpace(merged))
                    continue; // Skip this topic if merge failed

                // Store directly in Bot Brain
                var mem = new MemoryUnit
                {
                    Category = MemoryType.WebSearch,
                    Insertion = MemoryInsertion.Natural,
                    Name = topic.Topic,
                    Content = merged.CleanupAndTrim(),
                    Reason = topic.Reason,
                    Added = DateTime.Now,
                    EndTime = DateTime.Now.AddDays(10),
                    Priority = topic.Urgency,
                    Guid = session.Guid
                };

                await mem.EmbedText();
                LLMSystem.Bot.Brain.Memories.Add(mem);

                // Stage a short note for the user
                var staged = BuildStagedMessage(sessionId, topic.Topic, mem);
                allStagedMessages.Add(staged);
                
                totalSearchCount += searchCount;
            }

            return AgentTaskResult.Ok(staged: allStagedMessages, searches: totalSearchCount);
        }

        private static StagedMessage BuildStagedMessage(Guid sessionId, string topic, MemoryUnit items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Topic: {topic}");

            return new StagedMessage
            {
                TopicKey = $"web-{sessionId}-{San(topic)}",
                Draft = $"(Background) I researched '{topic}'. I've saved links and notes. Ready to use them next time.",
                Rationale = sb.ToString(),
                ExpireUtc = DateTime.UtcNow.AddHours(6),
            };
        }

        private static string San(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string([.. s.Select(c => bad.Contains(c) ? '_' : c)]).ToLowerInvariant();
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
            prompt.AppendLinuxLine($"{session.Summary.RemoveNewLines()}");
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
            var sysprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.SysPrompt, LLMSystem.User, LLMSystem.Bot, prompt.ToString());

            var txt = new StringBuilder($"You are researching '{topic}' for the following reason: {reason}").AppendLinuxLine().Append($"Merge the information available in the system prompt to offer an explanation on this topic. Don't use markdown formatting, favor natural language. The explanation should be 1 to 3 paragraphs long."); 
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, LLMSystem.Bot, txt.ToString());
            LLMSystem.NamesInPromptOverride = false;
            msg += LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            LLMSystem.NamesInPromptOverride = null;
            return sysprompt + msg;
        }

    }
}