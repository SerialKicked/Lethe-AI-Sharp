namespace AIToolkit.Agent
{
    public interface IAgentContext
    {
        int SessionCount { get; }
        string LastUserMessage { get; }
        TimeSpan IdleTime { get; }
        DateTime UtcNow { get; }
        AgentConfig Config { get; }
        AgentState State { get; }
    }

    public sealed class AgentContext : IAgentContext
    {
        public int SessionCount { get; init; }
        public string LastUserMessage { get; init; } = string.Empty;
        public TimeSpan IdleTime { get; init; }
        public DateTime UtcNow { get; init; } = DateTime.UtcNow;
        public AgentConfig Config { get; init; } = null!;
        public AgentState State { get; init; } = null!;
    }

    public interface IAgentPlugin
    {
        string Id { get; }
        IEnumerable<AgentTaskType> Supported { get; }
        Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct);
        Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct);
        bool CanHandle(AgentTask t) => Supported.Contains(t.Type);
    }

    public sealed class AgentTaskResult
    {
        public bool Success { get; init; }
        public IEnumerable<AgentTask> NewTasks { get; init; } = [];
        public IEnumerable<StagedMessage> Staged { get; init; } = [];
        public int TokensUsed { get; init; }
        public int SearchesUsed { get; init; }
        public static AgentTaskResult Ok(IEnumerable<AgentTask>? add = null, IEnumerable<StagedMessage>? staged = null, int tokens = 0, int searches = 0)
            => new() { Success = true, NewTasks = add ?? [], Staged = staged ?? [], TokensUsed = tokens, SearchesUsed = searches };
        public static AgentTaskResult Fail() => new() { Success = false };
    }
}