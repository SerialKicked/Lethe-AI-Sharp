using System.Collections.Generic;

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
        System
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

        /// <summary> Raw JSON arguments string. </summary>
        public string ArgumentsJson { get; init; } = string.Empty;

        /// <summary> Raw JSON result string (empty if <see cref="Success"/> is false). </summary>
        public string ResultJson { get; init; } = string.Empty;

        /// <summary> Whether the tool execution succeeded. </summary>
        public bool Success { get; init; }

        /// <summary> Error message if <see cref="Success"/> is false; null otherwise. </summary>
        public string? Error { get; init; }

        /// <summary> How long the tool execution took. </summary>
        public System.TimeSpan Duration { get; init; }
    }
}
