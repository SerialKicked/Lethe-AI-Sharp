
namespace LetheAISharp.LLM
{
    public interface ILLMExtractableBase
    {
        Task<string> GetGrammar();
        string GetQuery();
    }
}