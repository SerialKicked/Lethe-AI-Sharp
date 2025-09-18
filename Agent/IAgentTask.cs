using LetheAISharp.Files;

namespace LetheAISharp.Agent
{
    public interface IAgentTask
    {
        string Id { get; }
        Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
        Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
        AgentTaskSetting GetDefaultSettings();

    }
}
