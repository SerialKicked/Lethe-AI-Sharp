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

    /// <summary>
    /// The result of extracting a web page: its main content, and optionally its most relevant images.
    /// </summary>
    public class ExtractedPage
    {
        /// <summary> Main content of the page as cleaned-up markdown text. Empty if nothing could be extracted. </summary>
        public string Content { get; set; } = string.Empty;
        /// <summary> Title of the page, if known. </summary>
        public string Title { get; set; } = string.Empty;
        /// <summary> Local file paths of the relevant images downloaded for this page, best first. </summary>
        public List<string> Images { get; set; } = [];
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

        /// <summary>
        /// Extracts the main content of a web page as cleaned-up markdown text.
        /// Uses a local readability extraction, and falls back to Jina Reader when it fails.
        /// </summary>
        /// <param name="url"> The URL of the page. </param>
        /// <returns> The extracted text, or an empty string if nothing could be extracted. </returns>
        public async Task<string> ExtractContentLocallyAsync(string url)
        {
            var page = await ExtractPageAsync(url).ConfigureAwait(false);
            return page.Content;
        }

        /// <summary>
        /// Extracts the main content of a web page, and optionally the images that are the most relevant to it.
        /// </summary>
        /// <param name="url"> The URL of the page. </param>
        /// <param name="maxImages">
        /// Maximum number of relevant images to download. 0 (default) disables image extraction.
        /// Images are only available when the local extraction succeeded (not with the Jina fallback).
        /// </param>
        /// <param name="ct"> Cancellation token. </param>
        /// <returns> The extracted page. <see cref="ExtractedPage.Content"/> is empty if nothing could be extracted. </returns>
        public async Task<ExtractedPage> ExtractPageAsync(string url, int maxImages = 0, CancellationToken ct = default)
        {
            SmartReader.Article article;
            try
            {
                article = await SmartReader.Reader.ParseArticleAsync(url, null, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogWarning(ex, "[WebSearch API] Local extraction failed for {Url}: {Message}", url, ex.Message);
                return new ExtractedPage { Content = await ExtractContentWithJinaAsync(url).ConfigureAwait(false) };
            }

            if (!article.IsReadable)
                return new ExtractedPage { Content = await ExtractContentWithJinaAsync(url).ConfigureAwait(false) };

            string markdown;
            try
            {
                // article.Content is already stripped of nav/ads by SmartReader
                var converter = new ReverseMarkdown.Converter(new ReverseMarkdown.Config()
                {
                    Formatting = { RemoveComments = true, CleanupSpaces = true, OutputLineEnding = LLMEngine.NewLine },
                    Images = { Base64Handling = ReverseMarkdown.Config.Base64ImageHandling.Skip },
                    Tables = { CellListHandling = ReverseMarkdown.Config.TableCellListHandlingOption.InlineText },
                    Tags = { Unknown = ReverseMarkdown.Config.UnknownTagsOption.Drop  },
                    Links = { SmartHref = true }
                    

                });
                markdown = converter.Convert(article.Content);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogWarning(ex, "[WebSearch API] Markdown conversion failed for {Url}: {Message}", url, ex.Message);
                return new ExtractedPage { Content = await ExtractContentWithJinaAsync(url).ConfigureAwait(false) };
            }
            // content is in markdown, remove all links completely (including the text or image or whatever is linked)
            // markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"\[.*?\]\(.*?\)", string.Empty); // Remove links
            // remove lines that contains no text, no letter a-Z
            markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"^\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
            // remove lines with only a * (and leading/trailing spaces)
            markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"^\s*\*\s*$\n|\r", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
            // remove special characters like : < > | \ / that can cause issues in file names or markdown rendering
            // markdown = System.Text.RegularExpressions.Regex.Replace(markdown, @"[:<>|\\\/]", " ");

            markdown = markdown.CleanupAndTrim();

            if (LLMEngine.Settings.WebSearchDetailedMaxLength > 0 && markdown.Length > LLMEngine.Settings.WebSearchDetailedMaxLength)
            {
                var originalLength = markdown.Length;
                markdown = markdown[..LLMEngine.Settings.WebSearchDetailedMaxLength] + "... (cut content)";
                LLMEngine.Logger?.LogInformation("[WebSearch API] page content extraction for {Url} was too long and got truncated from {CurLength} to {MaxLength} characters", url, originalLength, LLMEngine.Settings.WebSearchDetailedMaxLength);
            }

            var page = new ExtractedPage { Content = markdown, Title = article.Title ?? string.Empty };
            if (maxImages > 0 && !string.IsNullOrEmpty(markdown))
            {
                try
                {
                    page.Images = await DownloadRelevantImagesAsync(article.Content, article.Uri, article.FeaturedImage, url, maxImages, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LLMEngine.Logger?.LogWarning(ex, "[WebSearch API] Image extraction failed for {Url}: {Message}", url, ex.Message);
                }
            }
            return page;
        }

        private const long MaxImageBytes = 5 * 1024 * 1024;
        private static readonly TimeSpan ImageCacheLifetime = TimeSpan.FromHours(24);

        /// <summary> Folder where the images downloaded for web page extraction are stored. </summary>
        public static string ImageCacheDirectory => Path.Combine(Path.GetTempPath(), "LetheAISharp", "webcache");

        /// <summary>
        /// Picks the most relevant images of a page (see <see cref="WebImageSelector"/>) and downloads them to
        /// <see cref="ImageCacheDirectory"/>. Candidates that cannot be downloaded or validated are skipped.
        /// </summary>
        /// <returns> Local file paths of the downloaded images, best first. </returns>
        private async Task<List<string>> DownloadRelevantImagesAsync(string? html, Uri? pageUri, string? featuredImage, string pageUrl, int maxImages, CancellationToken ct)
        {
            var images = new List<string>();
            // More candidates than needed: some will fail the download or the size check.
            var candidates = WebImageSelector.PickCandidates(html, pageUri, featuredImage, maxImages * 3);
            if (candidates.Count == 0)
                return images;

            Directory.CreateDirectory(ImageCacheDirectory);
            CleanupImageCache();

            foreach (var candidate in candidates)
            {
                if (images.Count >= maxImages || ct.IsCancellationRequested)
                    break;
                var path = await DownloadImageAsync(candidate.Url, pageUrl, ct).ConfigureAwait(false);
                if (path != null)
                    images.Add(path);
            }
            return images;
        }

        private async Task<string?> DownloadImageAsync(string imageUrl, string referer, CancellationToken ct)
        {
            try
            {
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(imageUrl)))[..16];
                // Files in the cache were validated before being saved, so they can be reused as-is.
                var existing = Directory.GetFiles(ImageCacheDirectory, hash + ".*").FirstOrDefault();
                if (existing != null && new FileInfo(existing).Length > 0)
                    return existing;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));

                using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
                if (Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
                    request.Headers.Referrer = refUri;
                request.Headers.Accept.ParseAdd("image/*");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    LLMEngine.Logger?.LogInformation("[WebSearch API] Image download failed for {Url}: {StatusCode}", imageUrl, response.StatusCode);
                    return null;
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                var acceptable = mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase);
                if (!acceptable || mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (response.Content.Headers.ContentLength > MaxImageBytes)
                    return null;

                // Read with a hard limit, the Content-Length header can be missing or wrong.
                using var ms = new MemoryStream();
                await using (var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
                {
                    var buffer = new byte[16 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        ms.Write(buffer, 0, read);
                        if (ms.Length > MaxImageBytes)
                            return null;
                    }
                }
                var data = ms.ToArray();

                // Validate that it is a real image, and big enough not to be decorative.
                var info = new ImageMagick.MagickImageInfo(data);
                if (Math.Min(info.Width, info.Height) < WebImageSelector.MinImageSize)
                    return null;

                var path = Path.Combine(ImageCacheDirectory, hash + ExtensionFromMediaType(mediaType));
                await File.WriteAllBytesAsync(path, data, ct).ConfigureAwait(false);
                return path;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                LLMEngine.Logger?.LogInformation("[WebSearch API] Image download skipped for {Url}: {Message}", imageUrl, ex.Message);
                return null;
            }
        }

        private static string ExtensionFromMediaType(string mediaType) => mediaType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tiff",
            _ => ".img"
        };

        private static void CleanupImageCache()
        {
            try
            {
                var limit = DateTime.UtcNow - ImageCacheLifetime;
                foreach (var file in Directory.GetFiles(ImageCacheDirectory))
                {
                    if (File.GetLastWriteTimeUtc(file) < limit)
                        File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogDebug("[WebSearch API] Image cache cleanup failed: {Message}", ex.Message);
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

                    return content.CleanupAndTrim();
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