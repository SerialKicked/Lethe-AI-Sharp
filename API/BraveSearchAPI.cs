using AIToolkit.Agent;
using System.Text.Json;
using System.Text.Json.Serialization;

public class SearchAndScrapingService
{
    private readonly HttpClient _httpClient;
    private readonly string _braveApiKey;

    public SearchAndScrapingService(HttpClient httpClient, string braveApiKey)
    {
        _httpClient = httpClient;
        _braveApiKey = braveApiKey;
    }

    // Brave Search API models
    public class BraveSearchResponse
    {
        [JsonPropertyName("web")]
        public WebResults Web { get; set; } = new();
    }

    public class WebResults
    {
        [JsonPropertyName("results")]
        public List<SearchResult> Results { get; set; } = [];
    }

    public class SearchResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("published")]
        public string Published { get; set; } = string.Empty;
    }

    // Your enriched result model
    public class EnrichedSearchResult
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Published { get; set; } = string.Empty;
        public string FullContent { get; set; } = string.Empty;
        public bool ContentExtracted { get; set; }
    }

    public async Task<List<EnrichedSearchResult>> SearchAndEnrichAsync(string query, int maxResults = 5, bool extractContent = true)
    {
        try
        {
            // Step 1: Search with Brave
            var searchResults = await SearchWithBraveAsync(query, maxResults);

            // Step 2: Optionally extract full content
            var enrichedResults = new List<EnrichedSearchResult>();

            foreach (var result in searchResults)
            {
                var enriched = new EnrichedSearchResult
                {
                    Title = result.Title,
                    Url = result.Url,
                    Description = result.Description,
                    Published = result.Published
                };

                if (extractContent)
                {
                    enriched.FullContent = await ExtractContentWithJinaAsync(result.Url);
                    enriched.ContentExtracted = !string.IsNullOrEmpty(enriched.FullContent);
                }

                enrichedResults.Add(enriched);
            }

            return enrichedResults;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in SearchAndEnrichAsync: {ex.Message}");
            return [];
        }
    }

    private async Task<List<SearchResult>> SearchWithBraveAsync(string query, int count)
    {
        try
        {
            var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={count}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Subscription-Token", _braveApiKey);
            request.Headers.Add("Accept", "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<BraveSearchResponse>(content);

            return searchResponse?.Web?.Results ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Brave Search API error: {ex.Message}");
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

    // Helper method to process your unfamiliar topics
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

                // Be nice to the APIs
                await Task.Delay(500);
            }

            allResults[topic.Topic] = topicResults;
        }

        return allResults;
    }
}

// Usage example
public class SearchUsageExample
{
    public static async Task ExampleUsage()
    {
        using var httpClient = new HttpClient();
        var searchService = new SearchAndScrapingService(httpClient, "YOUR_BRAVE_API_KEY");

        // Single search
        var results = await searchService.SearchAndEnrichAsync("C# async programming", 5);

        foreach (var result in results)
        {
            Console.WriteLine($"Title: {result.Title}");
            Console.WriteLine($"URL: {result.Url}");
            Console.WriteLine($"Description: {result.Description}");

            if (result.ContentExtracted)
            {
                Console.WriteLine($"Content Preview: {result.FullContent[..Math.Min(200, result.FullContent.Length)]}...");
            }

            Console.WriteLine("---");
        }

        // Process unfamiliar topics from your LLM extraction
        var topics = new List<TopicSearch>
        {
            new()
            {
                Topic = "Quantum Computing",
                Reason = "Discussion about future of computing",
                Urgency = 3,
                SearchQueries = ["quantum computing basics", "quantum vs classical computing"]
            }
        };

        var topicResults = await searchService.ProcessUnfamiliarTopicsAsync(topics);

        foreach (var (topic, searchResults) in topicResults)
        {
            Console.WriteLine($"Results for topic: {topic}");
            foreach (var result in searchResults)
            {
                Console.WriteLine($"  - {result.Title}: {result.Url}");
            }
        }
    }
}