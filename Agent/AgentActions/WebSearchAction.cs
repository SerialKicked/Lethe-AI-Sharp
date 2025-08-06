namespace AIToolkit.Agent
{
    public class WebSearchAction : IAgentAction
    {
        public string Name => "web_search";
        public string Description => "Search the internet for information.";
        public List<string> ValidContexts => ["chat", "background", "proactive" ];

        public async Task<ActionResult> Execute(Dictionary<string, object> parameters)
        {
            var query = parameters["query"].ToString();
            var context = parameters.GetValueOrDefault("context", "general").ToString();

            //var results = await webSearcher.Search(query);
            return new ActionResult(ActionResultType.Success, $"Search completed for query: {query} in context: {context}", null);
        }
    }
}
