using LetheAISharp.Agent;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Responses;
using System.Text;

namespace LetheAISharp.SearchAPI
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
        public static string BraveAPIKey
        {
            get => LLMEngine.Settings.WebSearchBraveAPIKey;
            set => LLMEngine.Settings.WebSearchBraveAPIKey = value;
        }
        public static BackendSearchAPI SearchAPI
        {
            get => LLMEngine.Settings.WebSearchAPI;
            set => LLMEngine.Settings.WebSearchAPI = value;
        }
        public static bool SearchDetailedResults
        {
            get => LLMEngine.Settings.WebSearchDetailedResults;
            set => LLMEngine.Settings.WebSearchDetailedResults = value;
        }


        private readonly HttpClient _httpClient;
        private ISearchProvider _currentProvider;


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public WebSearchAPI()
        {
            _httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(120) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; MyApp/1.0)");
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

            public string ToMarkDown(bool includeTitle = true, bool includeLink = true)
            {
                var str = new StringBuilder();
                if (includeTitle)
                {
                    str.AppendLinuxLine($"# {Title}");
                    str.AppendLinuxLine();
                }
                if (includeLink)
                {
                    str.AppendLinuxLine($"[Source]({Url})");
                    str.AppendLinuxLine();
                }
                str.AppendLinuxLine(Description);
                if (ContentExtracted)
                {
                    str.AppendLinuxLine();
                    str.AppendLinuxLine(FullContent);
                }
                return str.ToString();
            }
        }

        public async Task<List<EnrichedSearchResult>> SearchAndEnrichAsync(string query, int maxResults = 5, bool extractContent = true)
        {
            try
            {
                // Step 1: Search with current provider
                var searchResults = await _currentProvider.SearchAsync(query, maxResults).ConfigureAwait(false);

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
                        enriched.FullContent = await ExtractContentLocallyAsync(result.Url).ConfigureAwait(false);
                        enriched.ContentExtracted = !string.IsNullOrEmpty(enriched.FullContent);
                    }

                    enrichedResults.Add(enriched);

                    // More generous delay - 1 second between requests
                    if (extractContent) await Task.Delay(1000).ConfigureAwait(false);
                }

                return enrichedResults;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[WebSearch API] Error in SearchAndEnrichAsync: {Message}", ex.Message);
                return [];
            }
        }

        public async Task<string> ExtractContentLocallyAsync(string url)
        {
            var article = await SmartReader.Reader.ParseArticleAsync(url);
            if (article.IsReadable)
            {
                var cleanHtml = article.Content; // nav/ads stripped
                var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config()
                {
                    RemoveComments = true,
                    CleanupUnnecessarySpaces = true,
                    Base64Images = ReverseMarkdown.Config.Base64ImageHandling.Skip,
                });
                var markdown = converter.Convert(article.Content);
                // content is in markdown, remove all links completely (including the text or image or whatever is linked)
                markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"\[.*?\]\(.*?\)", string.Empty); // Remove links
                // remove lines that contains no text, no letter a-Z
                markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"^\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                // remove lines with only a * (and leading/trailing spaces)
                markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"^\s*\*\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                // remove special characters like : < > | \ / that can cause issues in file names or markdown rendering
                markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"[:<>|\\\/]", " ");


                if (LLMEngine.Settings.WebSearchDetailedMaxLength > 0 && markdown.Length > LLMEngine.Settings.WebSearchDetailedMaxLength)
                {
                    markdown = markdown[..LLMEngine.Settings.WebSearchDetailedMaxLength] + "... (cut content)";
                    LLMEngine.Logger?.LogInformation("[WebSearch API] page content extraction for {Url} was too long and got truncated from {CurLength} to {MaxLength} characters", url, markdown.Length, LLMEngine.Settings.WebSearchDetailedMaxLength);
                }

                return markdown;
            }
            else
            {
                return await ExtractContentWithJinaAsync(url).ConfigureAwait(false);
            }
        }

        public async Task<string> ExtractContentWithJinaAsync(string url)
        {
            try
            {
                // Jina Reader - just prepend their URL
                var jinaUrl = $"https://r.jina.ai/{url}";

                var response = await _httpClient.GetAsync(jinaUrl).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    LLMEngine.Logger?.LogInformation("[WebSearch API] Jina extraction success for {Url}: {StatusCode}", url, response.StatusCode);

                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    // content is in markdown, remove all links completely (including the text or image or whatever is linked)
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"\[.*?\]\(.*?\)", string.Empty); // Remove links
                    // remove lines that contains no text, no letter a-Z
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                    // remove lines with only a * (and leading/trailing spaces)
                    content = System.Text.RegularExpressions.Regex.Replace(content, @"^\s*\*\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);

                    if (LLMEngine.Settings.WebSearchDetailedMaxLength > 0 && content.Length > LLMEngine.Settings.WebSearchDetailedMaxLength) 
                    { 
                        content = content[..LLMEngine.Settings.WebSearchDetailedMaxLength];
                        LLMEngine.Logger?.LogInformation("[WebSearch API] Jina extraction for {Url} was too long and got truncated to {MaxLength} characters", url, LLMEngine.Settings.WebSearchDetailedMaxLength);
                    }

                    return content.CleanupAndTrim();;
                }
                else
                {
                    LLMEngine.Logger?.LogWarning("[WebSearch API] Jina extraction failed for {Url}: {StatusCode}", url, response.StatusCode);
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[WebSearch API] Content extraction error: {Message}", ex.Message);
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

                    var results = await SearchAndEnrichAsync(query, resultsPerQuery).ConfigureAwait(false);
                    topicResults.AddRange(results);

                    // Be nice to the APIs - 1 second delay
                    await Task.Delay(1000).ConfigureAwait(false);
                }

                allResults[topic.Topic] = topicResults;
            }

            return allResults;
        }
    }
}