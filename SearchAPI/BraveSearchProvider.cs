using System.Text.Json;
using System.Text.Json.Serialization;

namespace LetheAISharp.SearchAPI
{
    // Brave Search Provider
    public class BraveSearchProvider(HttpClient httpClient, string apiKey) : ISearchProvider
    {
        public string ProviderName => "Brave";

        public async Task<List<SearchResult>> SearchAsync(string query, int maxResults)
        {
            try
            {
                var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query.SanitizeSearchQuery())}&count={maxResults}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("X-Subscription-Token", apiKey);
                request.Headers.Add("Accept", "application/json");

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var searchResponse = JsonSerializer.Deserialize<BraveSearchResponse>(content);

                return searchResponse?.Web?.Results?.Select(r => new SearchResult
                {
                    Title = r.Title,
                    Url = r.Url,
                    Description = r.Description,
                    Published = r.Published
                }).ToList() ?? [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Brave Search error: {ex.Message}");
                return [];
            }
        }

        private class BraveSearchResponse
        {
            [JsonPropertyName("web")]
            public WebResults Web { get; set; } = new();
        }

        private class WebResults
        {
            [JsonPropertyName("results")]
            public List<BraveResult> Results { get; set; } = [];
        }

        private class BraveResult
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
    }
}


