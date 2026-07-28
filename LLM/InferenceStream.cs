using OpenAI;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace LetheAISharp.LLM
{
    /// <summary>
    /// Identifies the content channel of an inference segment.
    /// </summary>
    public enum InferenceChannel
    {
        /// <summary> Normal visible text response. </summary>
        Text,
        /// <summary> Chain-of-thought / thinking block content. </summary>
        Thinking,
        /// <summary> The LLM is requesting a tool/function call. </summary>
        ToolCall,
        /// <summary> Result being fed back after tool execution. </summary>
        ToolResult,
        /// <summary> Error or system-level message. </summary>
        System,
        Unknown
    }

    /// <summary>
    /// A single typed chunk emitted during streaming inference.
    /// </summary>
    public class InferenceSegment
    {
        /// <summary> What kind of content this segment carries. </summary>
        public InferenceChannel Channel { get; init; }

        /// <summary> The text delta (populated for Text and Thinking channels). </summary>
        public string? Text { get; init; }

        /// <summary> Tool call data (populated for ToolCall channel). </summary>
        public ToolCallInfo? ToolCall { get; init; }

        /// <summary> Tool result data (populated for ToolResult channel). </summary>
        public ToolResultInfo? ToolResult { get; init; }

        /// <summary> Whether this is the final chunk in its channel. </summary>
        public bool IsComplete { get; init; }
    }

    /// <summary>
    /// Data for an LLM-requested tool/function call.
    /// </summary>
    public class ToolCallInfo
    {
        /// <summary> Unique identifier for this call. </summary>
        public string CallId { get; init; } = string.Empty;

        /// <summary> Name of the function the LLM wants to invoke. </summary>
        public string FunctionName { get; init; } = string.Empty;

        /// <summary> Raw JSON arguments string. </summary>
        public string ArgumentsJson { get; init; } = string.Empty;
    }

    /// <summary>
    /// Data for the result returned after a tool call.
    /// </summary>
    public class ToolResultInfo
    {
        /// <summary> Identifier matching the originating <see cref="ToolCallInfo.CallId"/>. </summary>
        public string CallId { get; init; } = string.Empty;

        /// <summary> Name of the function that was invoked. </summary>
        public string FunctionName { get; init; } = string.Empty;

        /// <summary> Whether the tool execution succeeded. </summary>
        public bool Success { get; init; }

        /// <summary> Raw JSON result string. </summary>
        public string ResultJson { get; init; } = string.Empty;

        /// <summary> Error description if <see cref="Success"/> is false. </summary>
        public string? Error { get; init; }
    }

    /// <summary>
    /// The final structured result of a complete inference cycle.
    /// </summary>
    public class InferenceResult
    {
        /// <summary> The final visible text response (thinking blocks removed). </summary>
        public string Response { get; init; } = string.Empty;

        /// <summary> The thinking/CoT block content, if any. </summary>
        public string? ThinkingContent { get; init; }

        /// <summary> All tool calls made during this inference cycle. </summary>
        public List<ToolCallRecord> ToolCalls { get; init; } = [];

        /// <summary> The finish reason reported by the backend (e.g. "stop", "length", "tool_calls"). </summary>
        public string? FinishReason { get; init; }
    }

    /// <summary>
    /// A complete record of a single tool call and its result.
    /// </summary>
    public class ToolCallRecord
    {
        /// <summary> Unique identifier for this call. </summary>
        public string CallId { get; init; } = string.Empty;

        /// <summary> Name of the function that was invoked. </summary>
        public string FunctionName { get; init; } = string.Empty;

        /// <summary>
        /// Raw JSON arguments string. Prefer <see cref="GetArgumentsText"/> when building a backend
        /// payload: historical chat logs may store this value double-encoded (see that method).
        /// </summary>
        public string ArgumentsJson { get; init; } = string.Empty;

        /// <summary> Raw JSON result string (empty if <see cref="Success"/> is false). </summary>
        public string ResultJson { get; init; } = string.Empty;

        /// <summary> Whether the tool execution succeeded. </summary>
        public bool Success { get; init; }

        /// <summary> Error message if <see cref="Success"/> is false; null otherwise. </summary>
        public string? Error { get; init; }

        /// <summary> How long the tool execution took. </summary>
        public System.TimeSpan Duration { get; init; }

        public override string ToString()
        {
            return $"[Function: {FunctionName} (CallId: {CallId})]\nArguments: {ArgumentsJson}";
        }

        public ToolCall ToToolcall()
        {
            // Keep the OpenAI-spec shape: arguments travel as a JSON *string*, so the node has to be a
            // string value rather than the parsed object. Going through GetArgumentsText also guards
            // against arguments-less calls, which used to throw here on JsonNode.Parse("").
            return new ToolCall(CallId, FunctionName, JsonValue.Create(GetArgumentsText()));
        }

        /// <summary>
        /// Returns the arguments as single-encoded JSON object text (for example <c>{"query":"cats"}</c>),
        /// or <c>{}</c> when they are missing or unusable.
        /// </summary>
        /// <remarks>
        /// <see cref="ArgumentsJson"/> cannot be trusted to already be in that form. While streaming, the
        /// OpenAI layer accumulates arguments into a string-valued <see cref="JsonNode"/>; calling
        /// <c>ToJsonString()</c> on such a node returns a quoted, escaped JSON string *literal* instead of
        /// the object text, so records created that way - including every one already persisted in a .log
        /// file - are double-encoded. Passing those on verbatim makes a chat template receive a string
        /// where it expects a mapping, which aborts template rendering server-side. Unwrap one level of
        /// string encoding when present, and fall back to an empty object rather than forwarding something
        /// the backend cannot parse.
        /// </remarks>
        public string GetArgumentsText() => NormalizeArguments(ArgumentsJson);

        private const string EmptyArguments = "{}";

        internal static string NormalizeArguments(string? argumentsJson)
        {
            var text = argumentsJson?.Trim();
            if (string.IsNullOrEmpty(text))
                return EmptyArguments;

            // Two passes at most: the value as stored, then one level of string-unwrapping.
            for (var pass = 0; pass < 2; pass++)
            {
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(text);
                }
                catch (System.Text.Json.JsonException)
                {
                    return EmptyArguments;
                }

                if (node is JsonObject)
                    return text;

                if (node is JsonValue value && value.TryGetValue<string>(out var inner) && !string.IsNullOrWhiteSpace(inner))
                {
                    text = inner.Trim();
                    continue;
                }

                return EmptyArguments;
            }

            return EmptyArguments;
        }
    }
}
