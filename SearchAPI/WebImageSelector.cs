using System.Net;
using System.Text.RegularExpressions;

namespace LetheAISharp.SearchAPI
{
    /// <summary>
    /// An image found on a web page that might be relevant to its content.
    /// </summary>
    public class WebImageCandidate
    {
        /// <summary> Absolute http/https URL of the image. </summary>
        public string Url { get; set; } = string.Empty;
        /// <summary> Alternative text of the image, if any. </summary>
        public string Alt { get; set; } = string.Empty;
        /// <summary> Width declared in the HTML, or 0 if unknown. </summary>
        public int Width { get; set; }
        /// <summary> Height declared in the HTML, or 0 if unknown. </summary>
        public int Height { get; set; }
        /// <summary> True if the image sits inside a figure element. </summary>
        public bool InFigure { get; set; }
        /// <summary> True if this is the page's declared featured image (og:image and similar). </summary>
        public bool IsFeatured { get; set; }
    }

    /// <summary>
    /// Heuristics used to pick the images most likely to be relevant to the content of a web page.
    /// No network access and no LLM involved: the selection is purely based on the page's HTML.
    /// </summary>
    public static class WebImageSelector
    {
        /// <summary> Images with a side shorter than this (in pixels) are considered decorative. </summary>
        public const int MinImageSize = 150;

        private static readonly Regex ImgTagRegex = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new(@"([a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Compiled);
        private static readonly Regex FigureRegex = new(@"<figure\b.*?</figure>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex DigitsRegex = new(@"^\s*(\d+)\s*(px)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UnwantedRegex = new(
            @"(?<![a-z])(logo|logos|icon|icons|avatar|avatars|sprite|sprites|tracking|spacer|blank|banner|badge|emoji|placeholder|advert|ad|ads|button)(?![a-z])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] UnwantedExtensions = [".svg", ".ico", ".cur"];
        private static readonly string[] SourceAttributes = ["src", "data-src", "data-original", "data-lazy-src"];

        /// <summary>
        /// Picks the images of a page that are most likely to be relevant, best first.
        /// </summary>
        /// <param name="html"> Cleaned article HTML (as produced by SmartReader). Can be null or empty. </param>
        /// <param name="baseUri"> URL of the page, used to resolve relative image URLs. </param>
        /// <param name="featuredImage"> The page's declared featured image (og:image and similar), if any. </param>
        /// <param name="maxCandidates"> Maximum number of candidates to return. </param>
        /// <returns>
        /// The featured image first, then images in a figure or with alt text, then the remaining images,
        /// each group in document order. Decorative images (logos, icons, tiny images, trackers...) are excluded.
        /// </returns>
        public static List<WebImageCandidate> PickCandidates(string? html, Uri? baseUri, string? featuredImage, int maxCandidates)
        {
            var result = new List<WebImageCandidate>();
            if (maxCandidates <= 0)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var all = new List<WebImageCandidate>();

            var featuredUrl = ResolveUrl(featuredImage, baseUri);
            if (featuredUrl != null && IsAcceptableUrl(featuredUrl, string.Empty) && seen.Add(featuredUrl))
            {
                all.Add(new WebImageCandidate { Url = featuredUrl, IsFeatured = true });
            }

            if (!string.IsNullOrWhiteSpace(html))
            {
                var figureRanges = FigureRegex.Matches(html).Select(m => (Start: m.Index, End: m.Index + m.Length)).ToList();

                foreach (Match tag in ImgTagRegex.Matches(html))
                {
                    var attrs = ParseAttributes(tag.Value);

                    var raw = PickSource(attrs);
                    var url = ResolveUrl(raw, baseUri);
                    if (url == null)
                        continue;

                    attrs.TryGetValue("class", out var cssClass);
                    if (!IsAcceptableUrl(url, cssClass ?? string.Empty))
                        continue;

                    var width = ParseSize(attrs.GetValueOrDefault("width"));
                    var height = ParseSize(attrs.GetValueOrDefault("height"));
                    if ((width > 0 && width < MinImageSize) || (height > 0 && height < MinImageSize))
                        continue;

                    if (!seen.Add(url))
                        continue;

                    all.Add(new WebImageCandidate
                    {
                        Url = url,
                        Alt = WebUtility.HtmlDecode(attrs.GetValueOrDefault("alt") ?? string.Empty).Trim(),
                        Width = width,
                        Height = height,
                        InFigure = figureRanges.Any(r => tag.Index >= r.Start && tag.Index < r.End)
                    });
                }
            }

            // OrderBy is stable: inside a tier, the document order is preserved.
            return [.. all
                .OrderBy(c => c.IsFeatured ? 0 : (c.InFigure || c.Alt.Length > 0) ? 1 : 2)
                .Take(maxCandidates)];
        }

        /// <summary>
        /// Resolves a (possibly relative) image URL to an absolute http/https URL, without fragment.
        /// Returns null for empty values, data URIs and non-http schemes.
        /// </summary>
        public static string? ResolveUrl(string? raw, Uri? baseUri)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            raw = WebUtility.HtmlDecode(raw.Trim());
            if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;

            Uri? abs;
            if (baseUri != null)
            {
                if (!Uri.TryCreate(baseUri, raw, out abs))
                    return null;
            }
            else if (!Uri.TryCreate(raw, UriKind.Absolute, out abs))
            {
                return null;
            }

            if (abs.Scheme != Uri.UriSchemeHttp && abs.Scheme != Uri.UriSchemeHttps)
                return null;
            return abs.GetLeftPart(UriPartial.Path) + abs.Query;
        }

        private static bool IsAcceptableUrl(string url, string cssClass)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            var path = uri.AbsolutePath;
            if (UnwantedExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;
            // Only the path and class are checked: the host and the alt text produce too many false positives.
            return !UnwantedRegex.IsMatch(Uri.UnescapeDataString(path)) && !UnwantedRegex.IsMatch(cssClass);
        }

        private static string? PickSource(Dictionary<string, string> attrs)
        {
            foreach (var name in SourceAttributes)
            {
                if (attrs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                    && !value.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }
            return null;
        }

        private static Dictionary<string, string> ParseAttributes(string tag)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in AttrRegex.Matches(tag))
            {
                var value = m.Groups[2].Success ? m.Groups[2].Value
                          : m.Groups[3].Success ? m.Groups[3].Value
                          : m.Groups[4].Value;
                attrs.TryAdd(m.Groups[1].Value, value);
            }
            return attrs;
        }

        private static int ParseSize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;
            var m = DigitsRegex.Match(value);
            return m.Success && int.TryParse(m.Groups[1].Value, out var size) ? size : 0;
        }
    }
}
