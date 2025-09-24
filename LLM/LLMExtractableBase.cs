using LetheAISharp.LLM;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;

namespace LetheAISharp.LLM
{
    public abstract class LLMExtractableBase<T> : ILLMExtractableBase
    {
        public virtual string GetQuery()
        {
            var requestedTask = "Respond using a JSON format containing the following information:\n";
            var schema = DescriptionHelper.GetAllDescriptionsRecursive<T>();

            foreach (var prop in schema)
            {
                requestedTask += $"- {prop.Key}: {prop.Value}\n";
            }

            requestedTask = LLMEngine.Bot.ReplaceMacros(requestedTask);
            return requestedTask;
        }

        public virtual async Task<string> GetGrammar()
        {
            return await LLMEngine.GetGrammar<T>().ConfigureAwait(false);
        }
    }
}
