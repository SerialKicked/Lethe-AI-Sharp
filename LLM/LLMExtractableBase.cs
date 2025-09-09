using AIToolkit.LLM;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;

namespace AIToolkit.LLM
{
    public abstract class LLMExtractableBase<T>
    {
        public virtual string GetQuery()
        {
            var requestedTask = "Write a JSON file containing the following information based on the data shown above:\n";
            var schema = DescriptionHelper.GetAllDescriptionsRecursive<T>();

            foreach (var prop in schema)
            {
                requestedTask += $"- {prop.Key}: {prop.Value}\n";
            }

            requestedTask = LLMEngine.ReplaceMacros(requestedTask);
            return requestedTask;
        }

        public virtual async Task<string> GetGrammar()
        {
            return await LLMEngine.Client!.SchemaToGrammar(typeof(T)).ConfigureAwait(false);
        }
    }
}
