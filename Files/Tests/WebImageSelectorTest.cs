using LetheAISharp.SearchAPI;

namespace LetheAISharp.Files.Tests
{
    /// <summary>
    /// Manual tests for the web page image selection heuristics and image extraction.
    /// Call <see cref="RunAllTests"/> from a throwaway console project.
    /// The offline tests need no network; <see cref="TestLivePage"/> downloads a real page.
    /// </summary>
    public static class WebImageSelectorTest
    {
        private static int _failures;

        private static void Check(bool condition, string message)
        {
            if (condition)
                return;
            _failures++;
            Console.WriteLine($"  FAIL: {message}");
        }

        /// <summary>
        /// Runs all tests (the live test only if <paramref name="includeLive"/> is true).
        /// </summary>
        /// <returns> True if every test passed. </returns>
        public static async Task<bool> RunAllTests(bool includeLive = false)
        {
            _failures = 0;
            TestResolveUrl();
            TestFiltering();
            TestOrdering();
            TestMaxCandidates();
            if (includeLive)
                await TestLivePage().ConfigureAwait(false);

            Console.WriteLine(_failures == 0 ? "WebImageSelectorTest: all tests passed." : $"WebImageSelectorTest: {_failures} failure(s).");
            return _failures == 0;
        }

        public static void TestResolveUrl()
        {
            Console.WriteLine("TestResolveUrl");
            var page = new Uri("https://example.com/blog/post.html");
            Check(WebImageSelector.ResolveUrl("/img/a.jpg", page) == "https://example.com/img/a.jpg", "root-relative url");
            Check(WebImageSelector.ResolveUrl("b.jpg", page) == "https://example.com/blog/b.jpg", "relative url");
            Check(WebImageSelector.ResolveUrl("//cdn.example.com/c.png", page) == "https://cdn.example.com/c.png", "protocol-relative url");
            Check(WebImageSelector.ResolveUrl("https://x.com/d.png?w=800&amp;h=600#frag", page) == "https://x.com/d.png?w=800&h=600", "query kept, entity decoded, fragment dropped");
            Check(WebImageSelector.ResolveUrl("data:image/png;base64,AAAA", page) == null, "data uri rejected");
            Check(WebImageSelector.ResolveUrl("ftp://x.com/a.png", page) == null, "non http scheme rejected");
            Check(WebImageSelector.ResolveUrl("", page) == null, "empty rejected");
            Check(WebImageSelector.ResolveUrl("/a.png", null) == null, "relative url without base rejected");
        }

        public static void TestFiltering()
        {
            Console.WriteLine("TestFiltering");
            var page = new Uri("https://example.com/post");
            var html = """
                <p>text</p>
                <img src="/img/site-logo.png" alt="Logo">
                <img src="/img/icon.svg">
                <img src="/img/photo-small.jpg" width="48" height="48" alt="tiny">
                <img src="/img/ads/promo.jpg" alt="promo">
                <img class="avatar" src="/img/me.jpg">
                <img src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
                <img src="/img/upload/real-photo.jpg" alt="A real photo">
                <img src="/img/real-photo.jpg">
                <img src="/img/real-photo.jpg">
                <img data-src="/img/lazy.jpg" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=" width="800">
                """;
            var result = WebImageSelector.PickCandidates(html, page, null, 20);
            var urls = result.Select(r => r.Url).ToList();

            Check(!urls.Any(u => u.Contains("logo")), "logo excluded");
            Check(!urls.Any(u => u.EndsWith(".svg")), "svg excluded");
            Check(!urls.Any(u => u.Contains("photo-small")), "tiny image excluded");
            Check(!urls.Any(u => u.Contains("/ads/")), "ad excluded");
            Check(!urls.Any(u => u.Contains("me.jpg")), "avatar class excluded");
            Check(urls.Contains("https://example.com/img/upload/real-photo.jpg"), "'upload' must not be mistaken for 'ad'");
            Check(urls.Contains("https://example.com/img/lazy.jpg"), "lazy loaded image resolved from data-src");
            Check(urls.Count(u => u == "https://example.com/img/real-photo.jpg") == 1, "duplicates removed");
            Check(urls.Count == 3, $"expected 3 candidates, got {urls.Count}");
        }

        public static void TestOrdering()
        {
            Console.WriteLine("TestOrdering");
            var page = new Uri("https://example.com/post");
            var html = """
                <img src="/plain1.jpg">
                <img src="/alt.jpg" alt="Described">
                <figure><img src="/fig.jpg"><figcaption>Caption</figcaption></figure>
                <img src="/plain2.jpg">
                """;
            var result = WebImageSelector.PickCandidates(html, page, "/og.jpg", 10).Select(r => r.Url[(r.Url.LastIndexOf('/') + 1)..]).ToList();
            var expected = new[] { "og.jpg", "alt.jpg", "fig.jpg", "plain1.jpg", "plain2.jpg" };
            Check(result.SequenceEqual(expected), $"order was [{string.Join(", ", result)}]");

            // The featured image also found in the content must not appear twice.
            var dup = WebImageSelector.PickCandidates("<img src=\"/og.jpg\" alt=\"x\">", page, "https://example.com/og.jpg", 10);
            Check(dup.Count == 1 && dup[0].IsFeatured, "featured image deduplicated against content");
        }

        public static void TestMaxCandidates()
        {
            Console.WriteLine("TestMaxCandidates");
            var page = new Uri("https://example.com/post");
            var html = string.Concat(Enumerable.Range(0, 10).Select(i => $"<img src=\"/p{i}.jpg\">"));
            Check(WebImageSelector.PickCandidates(html, page, null, 3).Count == 3, "max respected");
            Check(WebImageSelector.PickCandidates(html, page, null, 0).Count == 0, "zero returns nothing");
            Check(WebImageSelector.PickCandidates(null, page, null, 3).Count == 0, "null html is safe");
        }

        /// <summary>
        /// Live test: extracts a real page with images and checks that the files were downloaded and are valid.
        /// Requires network access.
        /// </summary>
        public static async Task TestLivePage(string url = "https://www.anarkisgaming.com/after-the-collapse-info/")
        {
            Console.WriteLine("TestLivePage");
            var client = new WebSearchAPI();

            var noImages = await client.ExtractPageAsync(url).ConfigureAwait(false);
            Check(!string.IsNullOrWhiteSpace(noImages.Content), "content extracted");
            Check(noImages.Images.Count == 0, "no images unless requested");
            Console.WriteLine($"  Title: {noImages.Title}, content length: {noImages.Content.Length}");

            var withImages = await client.ExtractPageAsync(url, 2).ConfigureAwait(false);
            Check(withImages.Images.Count <= 2, "image cap respected");
            foreach (var path in withImages.Images)
            {
                Console.WriteLine($"  Image: {path} ({new FileInfo(path).Length} bytes)");
                Check(File.Exists(path), $"file exists: {path}");
                Check(ImageUtils.ImageToBase64(path, 256) != null, $"file decodes: {path}");
            }
            if (withImages.Images.Count == 0)
                Console.WriteLine("  (no relevant image was found or downloadable on this page)");
        }
    }
}
