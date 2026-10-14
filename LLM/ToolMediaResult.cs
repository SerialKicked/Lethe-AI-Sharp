using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;

namespace LetheAISharp.LLM
{
    /// <summary>
    /// Images a tool wants the LLM (when a vision-capable backend is loaded) and the UI to see,
    /// attached to the tool's result alongside its textual output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tools keep their plain <c>string</c>/<c>Task&lt;string&gt;</c> signatures — no interface changes
    /// and no rewrite of existing toolsets are required. A tool that wants to attach images returns a
    /// JSON result with the media payload embedded under <see cref="JsonKey"/> (use
    /// <see cref="BuildResult"/> or <see cref="EmbedInJson"/> to produce it):
    /// <code>
    /// { "result": "Found 2 matches.", "__media": { "urls": ["/data/pic.png", "https://example.com/cat.jpg"] } }
    /// </code>
    /// The engine's tool-call loop unpacks the payload, attaches the images to the tool result message
    /// sent to the backend (as <c>image_url</c> content parts), records them on the chat-log entry for
    /// the UI, and strips the envelope from the text the model sees as the tool's output.
    /// </para>
    /// <para>
    /// <see cref="Urls"/> accepts local file paths (any format <see cref="ImageUtils"/> can re-encode;
    /// they are converted to resized PNG base64 data URLs exactly like user-message images) or http(s)
    /// URLs, which are forwarded untouched — OpenAI-compatible endpoints and llama.cpp server fetch
    /// remote URLs themselves, so nothing is downloaded here.
    /// </para>
    /// </remarks>
    public class ToolMediaResult
    {
        /// <summary>
        /// Key under which the media payload is embedded in the tool's JSON return value.
        /// </summary>
        public const string JsonKey = "__media";

        /// <summary>
        /// Local file paths or http(s) URLs of the images the tool attached to its result.
        /// </summary>
        [JsonProperty("urls")]
        public List<string> Urls { get; set; } = [];

        /// <summary>
        /// True when <paramref name="pathOrUrl"/> is a remote URL. Remote URLs are forwarded untouched;
        /// local files are resized and re-encoded to PNG base64 exactly like user message images.
        /// </summary>
        public static bool IsRemote(string pathOrUrl) =>
            pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Converts an image path or URL into the backend's <c>image_url</c> content part.
        /// Local files are resized and re-encoded to PNG base64; remote URLs are forwarded as-is.
        /// Returns null when the item is empty, points to a nonexistent local file, or cannot be
        /// decoded as an image.
        /// </summary>
        /// <param name="pathOrUrl"> Local file path or http(s) URL of the image. </param>
        /// <param name="detail"> Optional vision detail level hint for the backend. </param>
        public static ImageUrl? ToImageUrl(string pathOrUrl, ImageDetail detail = ImageDetail.Auto)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return null;

            if (IsRemote(pathOrUrl))
                return new ImageUrl(pathOrUrl, detail);

            if (!File.Exists(pathOrUrl))
            {
                LLMEngine.Logger?.LogWarning("ToolMediaResult: image not found at {path}", pathOrUrl);
                return null;
            }

            // ImageToBase64 always re-encodes to PNG whatever the source format was, so the mime
            // type must always be image/png (same rule as user-message images in SingleMessage).
            var data = ImageUtils.ImageToBase64(pathOrUrl, LLMEngine.Settings.ImageResolution);
            if (data is null)
            {
                LLMEngine.Logger?.LogWarning("ToolMediaResult: failed to encode image at {path}", pathOrUrl);
                return null;
            }

            return new ImageUrl($"data:image/png;base64,{data}", detail);
        }

        /// <summary>
        /// Builds the JSON string a tool returns for a result that carries images.
        /// Signature-compatible with tools that return text only, so existing toolsets need no changes.
        /// </summary>
        /// <param name="result"> The textual result for the LLM. </param>
        /// <param name="source"> Optional source attribution (e.g. toolset id). </param>
        /// <param name="type"> Optional tool type hint (e.g. "media", "web", "memory"). </param>
        /// <param name="urls">
        /// Local file paths or http(s) URLs of the images to attach. Null/empty entries are ignored;
        /// a payload with no valid image degrades to a plain text result.
        /// </param>
        public static string BuildResult(string result, string source = "", string type = "", params string?[] urls)
        {
            var valid = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Cast<string>().ToList();
            if (valid.Count == 0)
                return BuildPlainResult(result, source, type);

            var payload = new JObject
            {
                ["result"] = result,
                ["source"] = source,
                ["type"] = type,
                [JsonKey] = new JObject { ["urls"] = new JArray(valid) }
            };
            return JsonConvert.SerializeObject(payload);
        }

        /// <summary>
        /// Embeds a media payload into an existing tool JSON result string under
        /// <see cref="JsonKey"/>, or replaces an existing payload if one is already present.
        /// Use this to add images to a JSON structure the tool built itself.
        /// </summary>
        /// <param name="json"> The tool's JSON result (arbitrary shape). </param>
        /// <param name="urls"> Local file paths or http(s) URLs of the images to attach. </param>
        /// <returns> The JSON string with the media payload embedded. </returns>
        public static string EmbedInJson(string json, params string?[] urls)
        {
            var valid = urls.Where(u => !string.IsNullOrWhiteSpace(u)).Cast<string>().ToList();
            if (valid.Count == 0)
                return json;

            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(json);
                if (obj == null)
                    return BuildWrappedResult(json, valid);

                obj[JsonKey] = new JObject { ["urls"] = new JArray(valid) };
                return JsonConvert.SerializeObject(obj);
            }
            catch (JsonException)
            {
                // Not valid JSON: wrap the raw text as the result field.
                return BuildWrappedResult(json, valid);
            }
        }

        /// <summary>
        /// Scans a raw tool result string for an embedded media payload.
        /// </summary>
        /// <param name="json"> The raw JSON string the tool returned. </param>
        /// <returns>
        /// The payload with its <see cref="Urls"/> populated, or null when the input is not valid JSON
        /// or carries no media payload (i.e. the tool returned plain text without images).
        /// </returns>
        public static ToolMediaResult? TryParse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(json);
                var media = obj?[JsonKey];
                if (media is not JObject mediaObj)
                    return null;
                return mediaObj.ToObject<ToolMediaResult>();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Removes the media payload from a tool result string, leaving the tool's own JSON
        /// (result/source/type and any other fields) for the model to read as plain text.
        /// Non-JSON input or input without a media payload is returned unchanged.
        /// </summary>
        /// <param name="json"> The raw JSON string the tool returned. </param>
        public static string StripPayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;
            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(json);
                if (obj?[JsonKey] == null)
                    return json;
                obj.Remove(JsonKey);
                return JsonConvert.SerializeObject(obj);
            }
            catch (JsonException)
            {
                return json;
            }
        }

        private static string BuildPlainResult(string result, string source, string type)
        {
            var payload = new JObject
            {
                ["result"] = result,
                ["source"] = source,
                ["type"] = type
            };
            return JsonConvert.SerializeObject(payload);
        }

        private static string BuildWrappedResult(string json, List<string> valid)
        {
            var payload = new JObject
            {
                ["result"] = json,
                [JsonKey] = new JObject { ["urls"] = new JArray(valid) }
            };
            return JsonConvert.SerializeObject(payload);
        }
    }
}
