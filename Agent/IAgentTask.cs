using AIToolkit.Files;

namespace AIToolkit.Agent
{
    public interface IAgentTask
    {
        string Id { get; }
        Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
        Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct);
    }
}
