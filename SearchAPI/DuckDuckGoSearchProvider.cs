using System.Text.Json;
using System.Text.Json.Serialization;

namespace LetheAISharp.SearchAPI
{
    // DuckDuckGo Search Provider
    public class DuckDuckGoSearchProvider(HttpClient httpClient) : ISearchProvider
    {
        public string ProviderName => "DuckDuckGo";

        public async Task<List<SearchResult>> SearchAsync(string query, int maxResults)
        {
            try
            {
                // DuckDuckGo Instant Answer API
                var url = $"https://api.duckduckgo.com/?q={Uri.EscapeDataString(query)}&format=json&no_html=1&skip_disambig=1";

                var response = await httpClient.GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var searchResponse = JsonSerializer.Deserialize<DuckDuckGoResponse>(content);

                var results = new List<SearchResult>();

                // Add instant answer if available
                if (!string.IsNullOrEmpty(searchResponse?.AbstractText))
                {
                    results.Add(new SearchResult
                    {
                        Title = searchResponse.Heading ?? "DuckDuckGo Instant Answer",
                        Description = searchResponse.AbstractText,
                        Url = searchResponse.AbstractURL ?? "",
                        Published = ""
                    });
                }

                // Add related topics
                if (searchResponse?.RelatedTopics != null)
                {
                    foreach (var topic in searchResponse.RelatedTopics.Take(maxResults - results.Count))
                    {
                        if (!string.IsNullOrEmpty(topic.Text))
                        {
                            results.Add(new SearchResult
                            {
                                Title = ExtractTitleFromText(topic.Text),
                                Description = topic.Text,
                                Url = topic.FirstURL ?? "",
                                Published = ""
                            });
                        }
                    }
                }

                return [.. results.Take(maxResults)];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DuckDuckGo Search error: {ex.Message}");
                return [];
            }
        }

        private static string ExtractTitleFromText(string text)
        {
            // Extract title from DuckDuckGo's text format
            var parts = text.Split(" - ", 2);
            return parts.Length > 1 ? parts[0] : text[..Math.Min(50, text.Length)];
        }

        private class DuckDuckGoResponse
        {
            [JsonPropertyName("Heading")]
            public string Heading { get; set; } = string.Empty;

            [JsonPropertyName("AbstractText")]
            public string AbstractText { get; set; } = string.Empty;

            [JsonPropertyName("AbstractURL")]
            public string AbstractURL { get; set; } = string.Empty;

            [JsonPropertyName("RelatedTopics")]
            public List<RelatedTopic> RelatedTopics { get; set; } = [];
        }

        private class RelatedTopic
        {
            [JsonPropertyName("Text")]
            public string Text { get; set; } = string.Empty;

            [JsonPropertyName("FirstURL")]
            public string FirstURL { get; set; } = string.Empty;
        }
    }
}


