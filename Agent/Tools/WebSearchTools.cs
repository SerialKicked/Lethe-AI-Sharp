using LetheAISharp.Agent.Actions;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.SearchAPI;
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
    public class WebSearchTools : IToolList
    {
        public string Id => "Web Search";

        public string Description => "A set of tools for performing web searches, reading web pages, and retrieving current date and time information.";

        public string SystemPromptInstruction => "You have access to [Web] tools to perform web searches, read the content of a web page from its URL, and retrieve current date and time information. Use these tools proactively whenever relevant information is needed or when the user asks for it.";

        private List<Tool> toolList = [];
        private WebSearchAPI? extractionClient;
        private const int MaxPageImages = 2;

        public IReadOnlyList<Tool> GetToolList() => toolList;

        public void LoadTools(bool clearExisting = false)
        {
            toolList.Clear();
            if (clearExisting) 
            {
                Tool.ClearRegisteredTools();
            }
            toolList.Add(Tool.GetOrCreateTool(this, nameof(WebSearch), "[Web] Performs a web search for the given query and returns a summary of the results."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(WebPageExtract), "[Web] Reads a web page and returns its main content as text. Provide the full http/https URL of the page. Optionally, set includeImages to also see the most relevant images of the page (requires vision)."));
            // Disabled because right now, the LLM can't call itself recursively when in a tool loop.
            //toolList.Add(Tool.GetOrCreateTool(this, nameof(DeepSearch), "Performs a deep search for the given query and returns a detailed report of the findings. Only use this if the user explicitly mentions that you need to do a deep search."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(GetCurrentDate), "[Web] Gets the current date and time."));
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

        /// <summary>
        /// Reads a web page and returns its main content as markdown-like text, optionally with its most relevant images.
        /// </summary>
        /// <param name="url"> Full http/https URL of the page to read. </param>
        /// <param name="includeImages"> If true, and a vision-capable model is loaded, the most relevant images of the page are attached to the result. </param>
        /// <returns> The extracted page content, or an error message if the page could not be read. </returns>
        public async Task<string> WebPageExtract(
            [FunctionParameter("Full http/https URL of the page to read.")] string url,
            [FunctionParameter("Set to true to also look at the most relevant images of the page. Only useful when the visuals matter (charts, products, photos, diagrams).")] bool includeImages = false)
        {
            if (string.IsNullOrWhiteSpace(url))
                return "Error: no URL was provided.";
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return $"Error: '{url}' is not a valid http/https URL.";

            // Images are pointless (and the downloads wasteful) if the model can't see them.
            var wantImages = includeImages && LLMEngine.SupportsVision;

            extractionClient ??= new WebSearchAPI();
            var page = await extractionClient.ExtractPageAsync(uri.AbsoluteUri, wantImages ? MaxPageImages : 0).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(page.Content))
                return $"Error: could not extract any content from '{uri.AbsoluteUri}'.";

            var result = new StringBuilder();
            result.AppendLinuxLine($"Content of {uri.AbsoluteUri}:").AppendLinuxLine();
            result.AppendLinuxLine(page.Content);
            if (includeImages && !LLMEngine.SupportsVision)
            {
                result.AppendLinuxLine().AppendLinuxLine("Note: no vision-capable model is currently loaded, so no images were attached.");
            }
            else if (wantImages && page.Images.Count > 0)
            {
                result.AppendLinuxLine().AppendLinuxLine($"{page.Images.Count} relevant image(s) from the page are attached.");
                return ToolMediaResult.BuildResult(result.ToString(), "Web", "web", [.. page.Images]);
            }
            return result.ToString();
        }

        // ---
        // Disabled because right now, the LLM can't call itself recursively when in a tool loop.
        // ---
        //public async Task<string> DeepSearch(string query)
        //{
        //    var searchaction = new DeepSearchAction();
        //    var searchresults = await searchaction.Execute(query, CancellationToken.None).ConfigureAwait(false);

        //    var response = new StringBuilder();
        //    response.AppendLinuxLine($"**Results regarding the user's query.");
        //    response.AppendLinuxLine();
        //    if (searchresults.Success)
        //        response.AppendLinuxLine(searchresults.FinalReport);
        //    else
        //        response.AppendLinuxLine($"Error: Failed to retrieve deep search results. {searchresults.Error}");
        //    return response.ToString();
        //}

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
            else if (functionName.StartsWith(nameof(WebPageExtract)))
            {
                // Fetching an arbitrary URL chosen by the model also requires confirmation.
                return true;
            }
            //else if (functionName.StartsWith(nameof(DeepSearch)))
            //{
            //    // Deep search might be a more impactful action, so we require confirmation before allowing the agent to call it.
            //    return true;
            //}
            // Tools not in the toolset should return false for RequiresConfirmation.
            // They either aren't called, or are handled by another toolset that may or may not require confirmation.
            return false;
        }

    }
}
