using System.Text;
using System.Text.Json;
using AIToolkit.LLM;
using AIToolkit.Files;

namespace AIToolkit.Agent.Plugins
{
    public sealed class WebIntelligencePlugin : IAgentPlugin
    {
        public string Id => "WebIntelligence";
        public IEnumerable<AgentTaskType> Supported => new[] { AgentTaskType.Observe, AgentTaskType.PlanSearch, AgentTaskType.ExecuteSearch };

        private sealed record PlanPayload(Guid SessionId);
        private sealed record ExecPayload(Guid SessionId, string Topic, string Query);

        public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
        {
            // Respect daily search budget
            if (ctx.State.SearchesUsedToday >= ctx.Config.DailySearchBudget)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // Look for the most recent archived session (the one before current)
            var sessions = LLMSystem.History.Sessions;
            if (sessions.Count < 2)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            var archived = sessions[^2]; // the session just archived
            if (archived.Messages.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // If we already have research stored for this session, skip
            if (ResearchStore.HasSession(archived.Guid))
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // If TopicLookup has no topics, skip
            if (archived.NewTopics?.Unfamiliar_Topics == null || archived.NewTopics.Unfamiliar_Topics.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // If user is idle enough, plan searches
            if (ctx.IdleTime.TotalMinutes < ctx.Config.MinIdleMinutesBeforeBackgroundWork)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            var payload = JsonSerializer.Serialize(new PlanPayload(archived.Guid));
            var tasks = new[]
            {
                new AgentTask
                {
                    Type = AgentTaskType.PlanSearch,
                    Priority = 2,
                    PayloadJson = payload,
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

            // Create one ExecuteSearch task per query (respect a soft cap)
            var newTasks = new List<AgentTask>();
            foreach (var topic in session.NewTopics.Unfamiliar_Topics)
            {
                // Prioritize higher urgency; drop trivial ones if budget is small
                if (topic.Urgency <= 1 && ctx.Config.DailySearchBudget - ctx.State.SearchesUsedToday < 5)
                    continue;

                foreach (var q in topic.SearchQueries.Take(3))
                {
                    var exec = new ExecPayload(session.Guid, topic.Topic, q);
                    newTasks.Add(new AgentTask
                    {
                        Type = AgentTaskType.ExecuteSearch,
                        Priority = 3,
                        PayloadJson = JsonSerializer.Serialize(exec),
                        RequiresLLM = false, // Uses backend web search API, not text generation
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
            var items = hits.Select(h => new SearchItem
            {
                Title = h.Title,
                Url = h.Url,
                Snippet = h.Description // Ensure this matches your EnrichedSearchResult shape (Description vs Snippet)
            }).ToList();

            // Persist
            ResearchStore.AppendResults(exec.SessionId, exec.Topic, exec.Query, items);

            // Stage a short note for the user
            var staged = BuildStagedMessage(exec.SessionId, exec.Topic, items);
            return AgentTaskResult.Ok(staged: [staged], searches: 1);
        }

        private static StagedMessage BuildStagedMessage(Guid sessionId, string topic, List<SearchItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Topic: {topic}");
            foreach (var it in items.Take(3))
            {
                sb.AppendLine($"- {it.Title} – {it.Url}");
            }
            var topicKey = $"web-{sessionId}-{San(topic)}";

            return new StagedMessage
            {
                TopicKey = topicKey,
                Draft = $"(Background) I researched “{topic}”. I’ve saved links and notes. Ready to use them next time.",
                Rationale = sb.ToString(),
                ExpireUtc = DateTime.UtcNow.AddHours(6),
            };
        }

        private static string San(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).ToLowerInvariant();
        }
    }
}