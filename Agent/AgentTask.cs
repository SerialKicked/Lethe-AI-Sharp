namespace AIToolkit.Agent
{
    public enum AgentTaskType
    {
        Observe,
        Reflect,
        PlanSearch,
        ExecuteSearch,
        PersonaUpdate,
        StageMessage,
        EmbedRefresh,
        PluginSpecific
    }

    public enum AgentTaskStatus { Queued, Running, Done, Failed, Deferred }

    public sealed class AgentTask
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public AgentTaskType Type { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Queued;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime NotBeforeUtc { get; set; } = DateTime.UtcNow;
        public int Attempts { get; set; } = 0;
        public int Priority { get; set; } = 5; // lower = higher priority
        public string? CorrelationKey { get; set; } // e.g. topic, goal id
        public bool RequiresLLM { get; set; } = false;

        public override string ToString() => $"{Type}({Id}) P={Priority} S={Status}";
    }
}