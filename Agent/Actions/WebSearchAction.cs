using AIToolkit.GBNF;
using AIToolkit.LLM;
using static AIToolkit.SearchAPI.WebSearchAPI;

namespace AIToolkit.Agent.Actions
{
    public class WebSearchAction : IAgentAction<List<EnrichedSearchResult>, TopicSearch>
    {
        public string Id => "WebSearchAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.WebSearch ];
        public async Task<List<EnrichedSearchResult>> Execute(TopicSearch param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return [];

            var allResults = new List<EnrichedSearchResult>();

            // Execute searches for all the queries the LLM / Agent left for us to do
            foreach (var query in param.SearchQueries)
            {
                if (ct.IsCancellationRequested)
                    return allResults;
                var hits = await LLMSystem.WebSearch(query).ConfigureAwait(false);
                if (hits != null && hits.Count > 0)
                {
                    allResults.AddRange(hits);
                }
            }
            return allResults;
        }
    }
}
