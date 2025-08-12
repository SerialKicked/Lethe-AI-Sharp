using AIToolkit.Agent;

namespace AIToolkit.SearchAPI
{

    // Common search result model
    public class SearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Published { get; set; } = string.Empty;
    }

    // Main service that can switch between providers
    public class WebSearchAPI
    {
        private readonly HttpClient _httpClient;
        private ISearchProvider _currentProvider;

        public WebSearchAPI(HttpClient httpClient, ISearchProvider defaultProvider)
        {
            _httpClient = httpClient;
            _currentProvider = defaultProvider;
        }

        public void SwitchProvider(ISearchProvider provider)
        {
            _currentProvider = provider;
            Console.WriteLine($"Switched to {provider.ProviderName} search provider");
        }

        public string CurrentProviderName => _currentProvider.ProviderName;

        // Your enriched result model
        public class EnrichedSearchResult
        {
            public string Title { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public string Published { get; set; } = string.Empty;
            public string FullContent { get; set; } = string.Empty;
            public bool ContentExtracted { get; set; }
            public string SearchProvider { get; set; } = string.Empty;
        }

        public async Task<List<EnrichedSearchResult>> SearchAndEnrichAsync(string query, int maxResults = 5, bool extractContent = true)
        {
            try
            {
                // Step 1: Search with current provider
                var searchResults = await _currentProvider.SearchAsync(query, maxResults);

                // Step 2: Optionally extract full content
                var enrichedResults = new List<EnrichedSearchResult>();

                foreach (var result in searchResults)
                {
                    var enriched = new EnrichedSearchResult
                    {
                        Title = result.Title,
                        Url = result.Url,
                        Description = result.Description,
                        Published = result.Published,
                        SearchProvider = _currentProvider.ProviderName
                    };

                    if (extractContent && !string.IsNullOrEmpty(result.Url))
                    {
                        enriched.FullContent = await ExtractContentWithJinaAsync(result.Url);
                        enriched.ContentExtracted = !string.IsNullOrEmpty(enriched.FullContent);
                    }

                    enrichedResults.Add(enriched);

                    // More generous delay - 1 second between requests
                    if (extractContent) await Task.Delay(1000);
                }

                return enrichedResults;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in SearchAndEnrichAsync: {ex.Message}");
                return [];
            }
        }

        private async Task<string> ExtractContentWithJinaAsync(string url)
        {
            try
            {
                // Jina Reader - just prepend their URL
                var jinaUrl = $"https://r.jina.ai/{url}";

                var response = await _httpClient.GetAsync(jinaUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return content.Trim();
                }
                else
                {
                    Console.WriteLine($"Jina extraction failed for {url}: {response.StatusCode}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Content extraction error for {url}: {ex.Message}");
                return string.Empty;
            }
        }

        // Helper method to process agent generated TopicSearch json stuff
        public async Task<Dictionary<string, List<EnrichedSearchResult>>> ProcessUnfamiliarTopicsAsync(List<TopicSearch> topics, int resultsPerQuery = 3)
        {
            var allResults = new Dictionary<string, List<EnrichedSearchResult>>();

            foreach (var topic in topics)
            {
                var topicResults = new List<EnrichedSearchResult>();

                foreach (var query in topic.SearchQueries)
                {
                    if (string.IsNullOrWhiteSpace(query)) continue;

                    var results = await SearchAndEnrichAsync(query, resultsPerQuery);
                    topicResults.AddRange(results);

                    // Be nice to the APIs - 1 second delay
                    await Task.Delay(1000);
                }

                allResults[topic.Topic] = topicResults;
            }

            return allResults;
        }
    }


    // Usage example showing how to switch between providers
    public static class SearchUsageExample
    {
        public static async Task ExampleUsage()
        {
            using var httpClient = new HttpClient();

            // Set up both providers
            var braveProvider = new BraveSearchProvider(httpClient, "YOUR_BRAVE_API_KEY");
            var duckDuckGoProvider = new DuckDuckGoSearchProvider(httpClient);

            // Start with DuckDuckGo (since you prefer their summaries)
            var searchService = new WebSearchAPI(httpClient, duckDuckGoProvider);

            Console.WriteLine($"Using {searchService.CurrentProviderName} provider");

            // Search with DuckDuckGo
            var results = await searchService.SearchAndEnrichAsync("C# async programming", 3);

            foreach (var result in results)
            {
                Console.WriteLine($"[{result.SearchProvider}] {result.Title}");
                Console.WriteLine($"Description: {result.Description}");
                Console.WriteLine("---");
            }

            // Switch to Brave if needed
            searchService.SwitchProvider(braveProvider);
            Console.WriteLine($"Switched to {searchService.CurrentProviderName} provider");

            // Same search with Brave
            var braveResults = await searchService.SearchAndEnrichAsync("C# async programming", 3);

            foreach (var result in braveResults)
            {
                Console.WriteLine($"[{result.SearchProvider}] {result.Title}");
                Console.WriteLine($"Description: {result.Description}");
                Console.WriteLine("---");
            }
        }
    }
}