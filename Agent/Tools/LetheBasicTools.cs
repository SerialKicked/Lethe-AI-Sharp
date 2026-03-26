using LetheAISharp.Agent.Actions;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    /// <summary>
    /// Basic toolset for demonstration purposes. 
    /// This toolset includes simple tools like performing a web search, and getting the current date and time.
    /// </summary>
    public class LetheBasicTools : IToolList
    {
        public string Id => "Web Search";
        private List<Tool> toolList = [];

        public IReadOnlyList<Tool> GetToolList() => toolList;

        public void LoadTools(bool clearExisting = false)
        {
            toolList.Clear();
            if (clearExisting) 
            {
                Tool.ClearRegisteredTools();
            }
            toolList.Add(Tool.GetOrCreateTool(this, nameof(WebSearch), "Performs a web search for the given query and returns a summary of the results."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(GetCurrentDate), "Gets the current date and time."));
        }

        public void UnloadTools()
        {
            foreach (var tool in toolList)
            {
                Tool.TryUnregisterTool(tool);
            }
            toolList.Clear();
        }

        public async Task<string> GetCurrentDate()
        {
            await Task.Delay(5).ConfigureAwait(false);
            // This is a placeholder implementation. In a real implementation, you would call a date API to get the actual date.
            return $"The current date is {DateTime.Now:MMMM dd, yyyy}. The time is {DateTime.Now:hh:mm tt}.";
        }

        public async Task<string> WebSearch(string query)
        {
            var searchaction = new WebSearchAction();
            var param = new TopicSearch()
            {
                Topic = query,
                Reason = "To find the latest news on the topic.",
                Urgency = 5,
                SearchQueries = [query]
            };
            var serchresults = await searchaction.Execute(param, CancellationToken.None).ConfigureAwait(false);
            // This is a placeholder implementation. In a real implementation, you would call a news API to get the actual news data.
            var result = new StringBuilder();
            result.AppendLinuxLine($"Search results for query: '{query}'");
            foreach (var item in serchresults)
            {
                if (string.IsNullOrWhiteSpace(item.Description) && string.IsNullOrWhiteSpace(item.FullContent))
                    continue;
                result.AppendLinuxLine($"## [{item.Title}]({item.Url})").AppendLinuxLine();
                result.AppendLinuxLine($"{item.Description}").AppendLinuxLine();
                if (item.ContentExtracted)
                {
                    result.AppendLinuxLine($"Full Content: {item.FullContent}").AppendLinuxLine();
                }
                result.AppendLinuxLine();
            }
            return result.ToString();
        }

        public bool RequiresConfirmation(string functionName)
        {
            // StartWith is used here because many backends append random strings to the function name to avoid name collisions,
            // so we want to check if the functionName starts with the base name of the function. 
            // You should take this into account when naming functions so yours don't accidentally collide with each other for confirmation purposes.
            if (functionName.StartsWith(nameof(GetCurrentDate)))
            {
                return false;
            }
            else if (functionName.StartsWith(nameof(WebSearch)))
            {
                // Web search might be a more impactful action, so we require confirmation before allowing the agent to call it.
                return true;
            }
            // Tools not in the toolset should return false for RequiresConfirmation.
            // They either aren't called, or are handled by another toolset that may or may not require confirmation.
            return false;
        }

    }
}
