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
        public IEnumerable<AgentTaskType> Supported => [AgentTaskType.Observe, AgentTaskType.PlanSearch, AgentTaskType.ExecuteSearch];

        private sealed record PlanPayload(Guid SessionId);
        private sealed record ExecPayload(Guid SessionId, string Topic, string Query);

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
            
            // Already researched or already planned/in-progress? Skip
            var key = $"web-{archived.Guid}";
            var hasPendingForSession = ctx.State.Queue.Any(t =>
                (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Running || t.Status == AgentTaskStatus.Deferred) &&
                (t.Type == AgentTaskType.PlanSearch || t.Type == AgentTaskType.ExecuteSearch) &&
                t.CorrelationKey == key);

            if (hasPendingForSession || LLMSystem.Bot.Brain.Has(MemoryType.WebSearch, archived.Guid))
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            // Only when idle
            if (ctx.IdleTime.TotalMinutes < ctx.Config.MinIdleMinutesBeforeBackgroundWork)
                return Task.FromResult<IEnumerable<AgentTask>>([]);

            var payload = JsonSerializer.Serialize(new PlanPayload(archived.Guid));
            var tasks = new[]
            {
                new AgentTask
                {
                    Type = AgentTaskType.PlanSearch,
                    Priority = 3,                 // plan later than execution
                    PayloadJson = payload,
                    CorrelationKey = key,
                }
            };
            return Task.FromResult<IEnumerable<AgentTask>>(tasks);
        }

        public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            switch (task.Type)
            {
                case AgentTaskType.PlanSearch:
                    return await ExecutePlanAsync(task, ctx, ct);
                case AgentTaskType.ExecuteSearch:
                    return await ExecuteSearchAsync(task, ctx, ct);
                default:
                    return AgentTaskResult.Fail();
            }
        }

        private static Task<AgentTaskResult> ExecutePlanAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            var plan = JsonSerializer.Deserialize<PlanPayload>(task.PayloadJson);
            if (plan == null)
                return Task.FromResult(AgentTaskResult.Fail());

            var session = LLMSystem.History.GetSessionByID(plan.SessionId);
            if (session == null || session.NewTopics?.Unfamiliar_Topics == null || session.NewTopics.Unfamiliar_Topics.Count == 0)
                return Task.FromResult(AgentTaskResult.Fail());

            // Mark this session as "planned" right away to block re-planning on next Observe tick
            ResearchStore.Ensure(plan.SessionId);

            var key = $"web-{plan.SessionId}";
            var newTasks = new List<AgentTask>();
            foreach (var topic in session.NewTopics.Unfamiliar_Topics)
            {
                // Drop trivial ones if budget nearly exhausted
                if (topic.Urgency <= 1 && ctx.Config.DailySearchBudget - ctx.State.SearchesUsedToday < 5)
                    continue;

                foreach (var q in topic.SearchQueries.Take(3))
                {
                    var exec = new ExecPayload(session.Guid, topic.Topic, q);
                    newTasks.Add(new AgentTask
                    {
                        Type = AgentTaskType.ExecuteSearch,
                        Priority = 2, // execute before more planning
                        PayloadJson = JsonSerializer.Serialize(exec),
                        CorrelationKey = key,
                        RequiresLLM = false
                    });
                }
            }

            return Task.FromResult(AgentTaskResult.Ok(add: newTasks));
        }

        private static async Task<AgentTaskResult> ExecuteSearchAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            if (ctx.State.SearchesUsedToday >= ctx.Config.DailySearchBudget)
                return AgentTaskResult.Fail();

            var exec = JsonSerializer.Deserialize<ExecPayload>(task.PayloadJson);
            if (exec == null)
                return AgentTaskResult.Fail();

            // Run web search
            var hits = await LLMSystem.WebSearch(exec.Query);
            if (hits == null || hits.Count == 0)
                return AgentTaskResult.Fail();
            // Merge into single memory
            var merged = await MergeResults(exec.Topic, hits);
            if (string.IsNullOrWhiteSpace(merged))
                return AgentTaskResult.Fail();
            // Add as a new memory item

            var mem = new MemoryUnit
            {
                Category = MemoryType.WebSearch,
                Insertion = MemoryInsertion.Natural,
                Name = exec.Topic,
                Content = merged.CleanupAndTrim(),
                Added = DateTime.Now,
                EndTime = DateTime.Now.AddDays(10)
            };

            await mem.EmbedText();
            LLMSystem.Bot.Brain.Memories.Add(mem);

            // Stage a short note for the user
            var staged = BuildStagedMessage(exec.SessionId, exec.Topic, mem);
            return AgentTaskResult.Ok(staged: [staged], searches: 1);
        }

        private static StagedMessage BuildStagedMessage(Guid sessionId, string topic, MemoryUnit items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Topic: {topic}");

            return new StagedMessage
            {
                TopicKey = $"web-{sessionId}-{San(topic)}",
                Draft = $"(Background) I researched “{topic}”. I’ve saved links and notes. Ready to use them next time.",
                Rationale = sb.ToString(),
                ExpireUtc = DateTime.UtcNow.AddHours(6),
            };
        }

        private static string San(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string([.. s.Select(c => bad.Contains(c) ? '_' : c)]).ToLowerInvariant();
        }

        private static async Task<string> MergeResults(string topic, List<EnrichedSearchResult> webresults)
        {
            LLMSystem.NamesInPromptOverride = false;
            var fullprompt = BuildMergerPrompt(topic, webresults);
            var llmparams = LLMSystem.Sampler.GetCopy();
            if (llmparams.Temperature > 0.75)
                llmparams.Temperature = 0.75;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Max_length = LLMSystem.Settings.MaxReplyLength;
            llmparams.Prompt = fullprompt;
            var response = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
            {
                response = response.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            }
            LLMSystem.Logger?.LogInformation("WebSearch Plugin Result: {output}", response);
            LLMSystem.NamesInPromptOverride = null;
            return response;
        }

        private static string BuildMergerPrompt(string userinput, List<EnrichedSearchResult> webresults)
        {
            var prompt = new StringBuilder();
            prompt.AppendLinuxLine("Your goal is analyze and merge information from the follow documents regarding the subject of '" + userinput + "'.");
            prompt.AppendLinuxLine();
            var cnt = 0;
            foreach (var item in webresults)
            {
                prompt.AppendLinuxLine($"# {item.Title}").AppendLinuxLine();
                prompt.AppendLinuxLine($"{item.Description}").AppendLinuxLine();
                if (item.ContentExtracted && LLMSystem.GetTokenCount(item.FullContent) <= 3000)
                    cnt++;
            }
            prompt.AppendLinuxLine();
            if (cnt > 0)
            {
                prompt.AppendLinuxLine($"You can also use the following content to improve your response.").AppendLinuxLine();
                for (var i = 0; i < webresults.Count; i++)
                {
                    var item = webresults[i];
                    if (item.ContentExtracted && LLMSystem.GetTokenCount(item.FullContent) <= 3000)
                    {
                        prompt.AppendLinuxLine($"# {item.Title} (Full Content)");
                        prompt.AppendLinuxLine($"{item.FullContent.CleanupAndTrim()}").AppendLinuxLine().AppendLinuxLine();
                    }
                }
            }
            var sysprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.SysPrompt, LLMSystem.User, LLMSystem.Bot, prompt.ToString());
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, LLMSystem.Bot, $"Merge the information available in the system prompt regarding '{userinput}' to offer a detailed explanation on this topic.");
            LLMSystem.NamesInPromptOverride = false;
            msg += LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            LLMSystem.NamesInPromptOverride = null;
            return sysprompt + msg;
        }

    }
}