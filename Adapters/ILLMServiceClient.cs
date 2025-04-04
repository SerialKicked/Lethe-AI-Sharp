using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.API
{

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
        public bool IsComplete => !string.IsNullOrEmpty(FinishReason) && FinishReason != "null" && !string.IsNullOrEmpty(FinishReason);
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

        // Core operations every backend needs to support
        Task<bool> CheckBackend();
        Task<int> GetMaxContextLength();
        Task<string> GetModelInfo();
        Task<string> GetBackendInfo();

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

        // Information about capabilities
        bool SupportsStreaming { get; }
        bool SupportsTTS { get; }
        bool SupportsVision { get; }
        bool SupportsWebSearch { get; }
    }
}