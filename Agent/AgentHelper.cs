using AIToolkit.LLM;

namespace AIToolkit.Agent
{
    internal static class AgentLLMHelpers
    {
        public static async Task<string> LightQueryAsync(string systemInstruction, int maxTokens, CancellationToken ct)
        {
            var res = await LLMSystem.QuickInferenceForSystemPrompt(systemInstruction, false, ct);
            return res.CleanupAndTrim();
        }
    }
}