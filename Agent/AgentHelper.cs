using AIToolkit.LLM;

namespace AIToolkit.Agent
{
    internal static class AgentLLMHelpers
    {
        public static async Task<string> LightQueryAsync(string systemInstruction, int maxTokens, CancellationToken ct)
        {
            using var _ = await LLMSystem.AcquireModelSlotAsync(ct);
            var res = await LLMSystem.QuickInferenceForSystemPrompt(systemInstruction, false);
            return res.CleanupAndTrim();
        }
    }
}