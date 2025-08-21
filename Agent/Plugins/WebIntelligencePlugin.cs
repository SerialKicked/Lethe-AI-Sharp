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

            // Need at least one archived session (the one before current)
            var sessions = LLMSystem.History.Sessions;
            if (sessions.Count < 2)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            var archived = sessions[^2];
            if (archived.Messages.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // Already researched or already planned/in-progress? Skip
            var key = $"web-{archived.Guid}";
            var hasPendingForSession = ctx.State.Queue.Any(t =>
                (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Running || t.Status == AgentTaskStatus.Deferred) &&
                (t.Type == AgentTaskType.PlanSearch || t.Type == AgentTaskType.ExecuteSearch) &&
                t.CorrelationKey == key);

            if (hasPendingForSession || ResearchStore.HasSession(archived.Guid))
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // No topics, nothing to do
            if (archived.NewTopics?.Unfamiliar_Topics == null || archived.NewTopics.Unfamiliar_Topics.Count == 0)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

            // Only when idle
            if (ctx.IdleTime.TotalMinutes < ctx.Config.MinIdleMinutesBeforeBackgroundWork)
                return Task.FromResult<IEnumerable<AgentTask>>(Array.Empty<AgentTask>());

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
            var items = hits.Select(h => new SearchItem
            {
                Title = h.Title,
                Url = h.Url,
                Snippet = h.Description // Ensure property matches your EnrichedSearchResult
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
                sb.AppendLine($"- {it.Title} – {it.Url}");

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
            return new string(s.Select(c => bad.Contains(c) ? '_' : c).ToArray()).ToLowerInvariant();
        }
    }
}