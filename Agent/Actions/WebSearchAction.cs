using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using static LetheAISharp.SearchAPI.WebSearchAPI;

namespace LetheAISharp.Agent.Actions
{
    /// <summary>
    /// Represents an action that performs web searches based on the provided search queries and returns a list of
    /// enriched search results.
    /// </summary>
    /// <remarks>This action is designed to execute web searches for all queries specified in the <see
    /// cref="TopicSearch"/> parameter. The results are aggregated into a single list of <see
    /// cref="EnrichedSearchResult"/> objects. If the operation is canceled via the <see cref="CancellationToken"/>, the
    /// method will return the results collected up to that point.</remarks>
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
                var hits = await LLMEngine.WebSearch(query).ConfigureAwait(false);
                if (hits != null && hits.Count > 0)
                {
                    allResults.AddRange(hits);
                }
            }
            return allResults;
        }
    }
}
