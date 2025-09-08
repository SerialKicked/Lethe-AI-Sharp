using AIToolkit.GBNF;
using AIToolkit.LLM;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Text;
using static AIToolkit.SearchAPI.WebSearchAPI;

namespace AIToolkit.Agent.Actions
{
    public class MergeSearchParams(string context , string topic, string reason, List<SearchAPI.WebSearchAPI.EnrichedSearchResult> results)
    {
        public string Context { get; set; } = context;
        public string Topic { get; set; } = topic;
        public string Reason { get; set; } = reason;
        public List<EnrichedSearchResult> Results { get; set; } = results;
    }


    public class MergeSearchResultsAction : IAgentAction<string, MergeSearchParams>
    {
        public string Id => "MergeSearchResultsAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.LLM ];

        public async Task<string> Execute(MergeSearchParams param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return string.Empty;

            LLMSystem.NamesInPromptOverride = false;
            var fullprompt = BuildMergerPrompt(param.Context, param.Topic, param.Reason, param.Results).PromptToQuery(AuthorRole.Assistant, (LLMSystem.Sampler.Temperature > 0.75) ? 0.75 : LLMSystem.Sampler.Temperature, 1024);
            var response = await LLMSystem.SimpleQuery(fullprompt).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
            {
                response = response.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            }
            response = response.RemoveUnfinishedSentence();
            LLMSystem.Logger?.LogInformation("WebSearch Plugin Result: {output}", response);
            LLMSystem.NamesInPromptOverride = null;
            return response;
        }

        private static IPromptBuilder BuildMergerPrompt(string summary, string topic, string reason, List<EnrichedSearchResult> webresults)
        {
            var builder = LLMSystem.Client!.GetPromptBuilder();
            var prompt = new StringBuilder();
            prompt.AppendLinuxLine($"You are {LLMSystem.Bot.Name} and your goal is to analyze and merge information from the following documents regarding the subject of '{topic}'. This topic was made relevant during this chat session.");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"# Chat Session");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"{summary.RemoveNewLines()}");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"# Documents Found:");
            prompt.AppendLinuxLine();
            var cnt = 0;
            foreach (var item in webresults)
            {
                prompt.AppendLinuxLine($"## {item.Title}").AppendLinuxLine();
                prompt.AppendLinuxLine($"{item.Description}").AppendLinuxLine();
                if (item.ContentExtracted && LLMSystem.GetTokenCount(item.FullContent) <= 3000)
                    cnt++;
            }
            prompt.AppendLinuxLine();
            if (cnt > 0)
            {
                prompt.AppendLinuxLine($"You can also use the following content to improve your response (this is extracted directly from the web pages, meaning there might be clutter in there).").AppendLinuxLine();
                for (var i = 0; i < webresults.Count; i++)
                {
                    var item = webresults[i];
                    var tks = LLMSystem.GetTokenCount(item.FullContent);
                    if (item.ContentExtracted && tks > 100 && tks <= 2500)
                    {
                        prompt.AppendLinuxLine($"## {item.Title} (Full Content)");
                        prompt.AppendLinuxLine($"{item.FullContent.CleanupAndTrim()}").AppendLinuxLine().AppendLinuxLine();
                    }
                }
            }
            builder.AddMessage(AuthorRole.SysPrompt, prompt.ToString().CleanupAndTrim());
            var txt = new StringBuilder($"You are researching '{topic}' for the following reason: {reason}").AppendLinuxLine().Append($"Merge the information available in the system prompt to offer an explanation on this topic. Don't use markdown formatting, favor natural language. The explanation should be 1 to 3 paragraphs long.");
            builder.AddMessage(AuthorRole.User, txt.ToString());
            return builder;
        }
    }
}
