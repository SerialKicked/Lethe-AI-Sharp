using AIToolkit.Files;
using Newtonsoft.Json;


namespace AIToolkit.Agent
{
    public sealed class AgentState : BaseFile
    {
        public DateTime LastBudgetResetUtc { get; set; } = DateTime.UtcNow.Date;
        public int TokensUsedToday { get; set; }
        public int SearchesUsedToday { get; set; }
        public DateTime LastReflectionUtc { get; set; } = DateTime.MinValue;
        public DateTime LastPersonaUpdateUtc { get; set; } = DateTime.MinValue;

        public List<StagedMessage> StagedMessages { get; set; } = [];
        public List<AgentTask> Queue { get; set; } = [];

        public static AgentState Load(string path)
        {
            if (!File.Exists(path)) return new AgentState();
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AgentState>(json) ?? new AgentState();
        }

        public void ResetBudgetsIfNeeded()
        {
            var today = DateTime.UtcNow.Date;
            if (today > LastBudgetResetUtc)
            {
                LastBudgetResetUtc = today;
                TokensUsedToday = 0;
                SearchesUsedToday = 0;
            }
        }
    }

    public sealed class StagedMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string TopicKey { get; set; } = string.Empty;
        public string Draft { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime ExpireUtc { get; set; } = DateTime.UtcNow.AddHours(6);
        public bool Delivered { get; set; } = false;
    }
}