namespace AIToolkit.Agent.Plugins
{
    public sealed class CoreReflectionPlugin : IAgentPlugin
    {
        public string Id => "CoreReflection";
        public IEnumerable<AgentTaskType> Supported => new[] { AgentTaskType.Observe, AgentTaskType.Reflect };

        public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
        {
            var list = new List<AgentTask>();
            if ((ctx.UtcNow - ctx.State.LastReflectionUtc).TotalMinutes > 30 &&
                ctx.SessionCount > 0 &&
                ctx.IdleTime.TotalMinutes >= ctx.Config.MinIdleMinutesBeforeBackgroundWork)
            {
                list.Add(new AgentTask
                {
                    Type = AgentTaskType.Reflect,
                    Priority = 1,
                    RequiresLLM = true
                });
            }
            return Task.FromResult<IEnumerable<AgentTask>>(list);
        }

        public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            if (task.Type != AgentTaskType.Reflect)
                return AgentTaskResult.Fail();

            var prompt = "Provide a concise (<=60 words) internal summary of recent conversation progress and pending goals. No formatting.";
            var summary = await AgentLLMHelpers.LightQueryAsync(prompt, 256, ct);
            if (string.IsNullOrWhiteSpace(summary))
                return AgentTaskResult.Fail();

            ctx.State.LastReflectionUtc = ctx.UtcNow;
            var staged = new StagedMessage
            {
                TopicKey = "reflection",
                Draft = "(Background) I reviewed recent chats; I'm ready to continue whenever you are.",
                Rationale = summary,
                ExpireUtc = DateTime.UtcNow.AddMinutes(ctx.Config.StageMessageTTLMinutes)
            };
            return AgentTaskResult.Ok(staged: [staged], tokens: 256);
        }
    }
}