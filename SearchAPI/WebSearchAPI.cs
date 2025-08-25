using AIToolkit.Agent;

namespace AIToolkit.SearchAPI
{

    public enum BackendSearchAPI { DuckDuckGo, Brave }

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
        // Search API Settings
        public static string BraveAPIKey { get; set; } = string.Empty;
        public static BackendSearchAPI SearchAPI { get; set; } = BackendSearchAPI.DuckDuckGo;
        public static bool SearchDetailedResults { get; set; } = true;


        private readonly HttpClient _httpClient;
        private ISearchProvider _currentProvider;


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public WebSearchAPI(HttpClient httpClient)
        {
            _httpClient = httpClient;
            SwitchProvider(SearchAPI, BraveAPIKey);
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.


        public void SwitchProvider(BackendSearchAPI provider, string apiKey = "")
        {
            BraveAPIKey = apiKey;
            SearchAPI = provider;
            _currentProvider = provider switch
            {
                BackendSearchAPI.DuckDuckGo => new DuckDuckGoSearchProvider(_httpClient),
                BackendSearchAPI.Brave => new BraveSearchProvider(_httpClient, apiKey),
                _ => throw new ArgumentException("Unsupported search provider"),
            };
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

                    if (extractContent && !string.IsNullOrEmpty(result.Url) && WebSearchAPI.SearchAPI != BackendSearchAPI.DuckDuckGo)
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

            // Start with DuckDuckGo (since you prefer their summaries)
            WebSearchAPI.SearchAPI = BackendSearchAPI.DuckDuckGo;
            var searchService = new WebSearchAPI(httpClient);

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
            searchService.SwitchProvider(BackendSearchAPI.Brave, WebSearchAPI.BraveAPIKey);
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