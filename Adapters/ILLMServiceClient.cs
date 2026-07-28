using LetheAISharp.Files;
using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.API
{

    public enum CompletionType { Text, Chat }

    /// <summary>
    /// Arguments for text token streaming events
    /// </summary>
    public class LLMTokenStreamingEventArgs(string token, string? finishReason, List<ToolCallRecord>? toolCallRecords = null, string? reasoningToken = null) : EventArgs
    {
        /// <summary>
        /// The visible token text that was generated.
        /// </summary>
        public string Token { get; } = token;

        /// <summary>
        /// Chain-of-thought / reasoning content delivered through a separate streaming field
        /// (e.g. the <c>reasoning_content</c> property emitted by llama.cpp / vLLM and other
        /// OpenAI-compatible backends). When populated, the engine routes it directly into
        /// the thinking channel without running the inline-tag parser.
        /// </summary>
        public string? ReasoningToken { get; } = reasoningToken;

        /// <summary>
        /// Reason why generation finished (null/empty during streaming, "stop"/"length" when complete)
        /// </summary>
        public string? FinishReason { get; } = finishReason;

        /// <summary>
        /// Tool call records accumulated during any tool-calling rounds that preceded this completion.
        /// Populated only on the final completion event (when <see cref="IsComplete"/> is true).
        /// </summary>
        public List<ToolCallRecord>? ToolCallRecords { get; } = toolCallRecords;

        /// <summary>
        /// Whether this is the final streaming event
        /// </summary>
        public bool IsComplete => !string.IsNullOrEmpty(FinishReason) && FinishReason != "null";
    }

    /// <summary>
    /// Abstract interface for LLM services that unifies different backend APIs
    /// </summary>
    public interface ILLMServiceClient
    {
        // Event for streaming tokens
        event EventHandler<LLMTokenStreamingEventArgs> TokenReceived;

        // Connection properties
        string BaseUrl { get; set; }

        // The type of completion this backend supports (text or chat)
        CompletionType CompletionType { get; protected set;  } // Text or Chat completion
        List<CompletionType> AvailCompletionTypes { get; }

        // Information about capabilities
        bool SupportsStreaming { get; }
        bool SupportsTTS { get; }
        bool SupportsVision { get; }
        bool SupportsWebSearch { get; }
        bool SupportsStateSave { get; }
        bool SupportsSchema { get; }
        bool SupportsToolCalls { get; }
        bool SupportParallelToolCall { get; }
        bool AllowPrefill { get; }

        bool SelectCompletionType(CompletionType type)
        {
            if (!AvailCompletionTypes.Contains(type))
                return false;
            CompletionType = type;
            return true;
        }

        // Core operations every backend needs to support
        Task<bool> CheckBackend();
        Task<int> GetMaxContextLength();
        Task<string> GetModelInfo();
        Task<string> GetBackendInfo();
        IPromptBuilder GetPromptBuilder();

        // Text generation (common to all LLMs)
        Task<string> GenerateText(object parameters);
        Task GenerateTextStreaming(object parameters);
        Task<bool> AbortGeneration();
        bool AbortGenerationSync();

        int CountMessageTokens(List<SingleMessage> messages);

        /// <summary>
        /// True when the backend can return the exact prompt token count of a fully-built generation
        /// request (chat template, tool definitions and images included), rather than an estimate
        /// derived from raw message text. See <see cref="CountRequestTokensSync"/>.
        /// </summary>
        bool SupportsRequestTokenCount => false;

        /// <summary>
        /// Counts the exact number of prompt tokens the backend would process for a fully-built
        /// generation request (the same request object that <see cref="GenerateTextStreaming"/>
        /// would receive). Only called when <see cref="SupportsRequestTokenCount"/> is true;
        /// the default implementation throws.
        /// </summary>
        /// <param name="parameters">The built generation request (e.g. a ChatRequest for chat backends).</param>
        /// <returns>Exact prompt token count as computed by the backend itself.</returns>
        int CountRequestTokensSync(object parameters) => throw new NotSupportedException();

        // Token counting
        Task<int> CountTokens(string text);
        int CountTokensSync(string text);

        // Optional capabilities (may not be supported by all backends)
        Task<byte[]> TextToSpeech(string text, string voice);
        Task<string> WebSearch(string query);
        Task<string> SchemaToGrammar(Type jsonclass);

        void UpdateSearchProvider();

        // KV State management (if supported)
        Task<bool> SaveKVState(int value);
        Task<bool> LoadKVState(int value);
        Task<bool> ClearKVStates();
    }
}