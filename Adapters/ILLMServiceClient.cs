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
    public class LLMTokenStreamingEventArgs(string token, string? finishReason) : EventArgs
    {
        /// <summary>
        /// The token text that was generated
        /// </summary>
        public string Token { get; } = token;

        /// <summary>
        /// Reason why generation finished (null/empty during streaming, "stop"/"length" when complete)
        /// </summary>
        public string? FinishReason { get; } = finishReason;

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

        CompletionType CompletionType { get; } // Text or Chat completion

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

        // Token counting
        Task<int> CountTokens(string text);
        int CountTokensSync(string text);

        // Optional capabilities (may not be supported by all backends)
        Task<byte[]> TextToSpeech(string text, string voice);
        Task<string> WebSearch(string query);
        Task<string> ImageCaption(byte[] imageData);
        Task<string> SchemaToGrammar(Type jsonclass);

        void UpdateSearchProvider();

        // KV State management (if supported)
        Task<bool> SaveKVState(int value);
        Task<bool> LoadKVState(int value);
        Task<bool> ClearKVStates();

        // Information about capabilities
        bool SupportsStreaming { get; }
        bool SupportsTTS { get; }
        bool SupportsVision { get; }
        bool SupportsWebSearch { get; }
        bool SupportsStateSave { get; }
        bool SupportsSchema { get; }

    }
}