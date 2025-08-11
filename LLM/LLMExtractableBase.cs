using AIToolkit.LLM;

namespace AIToolkit
{
    public class LLMExtractableBase<T>
    {
        public static string GetQuery()
        {
            var requestedTask = "Write a JSON file containing the following information based on the data shown above:\n";
            var schema = DescriptionHelper.GetAllDescriptions<T>();

            foreach (var prop in schema)
            {
                requestedTask += $"- {prop.Key}: {prop.Value}\n";
            }

            requestedTask = LLMSystem.ReplaceMacros(requestedTask);
            return requestedTask;
        }

        public async static Task<string> GetGrammar()
        {
            return await LLMSystem.Client!.SchemaToGrammar(typeof(T));
        }
    }
}
