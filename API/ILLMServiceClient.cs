using AIToolkit.LLM;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.API
{

    /// <summary>
    /// Arguments for text token streaming events
    /// </summary>
    public class LLMTokenStreamingEventArgs(string token, string finishReason) : EventArgs
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

    /// <summary>
    /// Adapter for KoboldCpp backend
    /// </summary>
    public class KoboldCppAdapter : ILLMServiceClient
    {
        private readonly KoboldCppClient _client;

        public event EventHandler<LLMTokenStreamingEventArgs> TokenReceived;

        public KoboldCppAdapter(HttpClient httpClient)
        {
            _client = new KoboldCppClient(httpClient);
            httpClient.BaseAddress = new Uri(LLMSystem.BackendUrl);

            // Hook into the KoboldCpp streaming event and adapt it to our interface's event
            _client.StreamingMessageReceived += (sender, e) =>
            {
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(
                    e.Data.token,
                    e.Data.finish_reason
                ));
            };
        }

        public string BaseUrl
        {
            get => LLMSystem.BackendUrl;
            set => LLMSystem.BackendUrl = value;
        }

        public async Task<int> GetMaxContextLength()
        {
            var result = await _client.TrueMaxContextLengthAsync();
            return result.Value;
        }

        public async Task<string> GetModelInfo()
        {
            var info = await _client.ModelAsync();
            var index = info.Result.IndexOf('/');
            return index > 0 ? info.Result[(index + 1)..] : info.Result;
        }

        public async Task<string> GetBackendInfo()
        {
            var engine = await _client.ExtraVersionAsync();
            if (engine?.result == null)
                return "Engine Not Supported";
            SupportsTTS = engine.tts;
            SupportsVision = engine.vision;
            SupportsWebSearch = engine.websearch;
            return $"{engine.result} {engine.version}";
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            var result = await _client.GenerateAsync(input);
            return string.Join("", result.Results.Select(r => r.Text));
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            await _client.GenerateTextStreamAsync(input);
        }

        public async Task<bool> AbortGeneration()
        {
            var result = await _client.AbortAsync(new GenkeyData());
            return result.Success;
        }

        public async Task<int> CountTokens(string text)
        {
            var result = await _client.TokencountAsync(new KcppPrompt { Prompt = text });
            return result.Value;
        }

        public int CountTokensSync(string text)
        {
            return _client.TokencountSync(new KcppPrompt { Prompt = text }).Value;
        }

        public async Task<byte[]> TextToSpeech(string text, string voice)
        {
            if (!SupportsTTS)
                return [];
            return await _client.TextToSpeechAsync(new TextToSpeechInput { Input = text, Voice = voice });
        }

        public async Task<string> WebSearch(string query)
        {
            if (!SupportsWebSearch)
                return string.Empty;
            var results = await _client.WebQueryAsync(new WebQuery { q = query });
            // Convert results to a common format
            return JsonConvert.SerializeObject(results);
        }

        public async Task<string> ImageCaption(byte[] imageData)
        {
            var base64Image = Convert.ToBase64String(imageData);
            var result = await _client.InterrogateAsync(new KcppCaptionQuery { Image = base64Image });
            return result.Caption;
        }

        public bool AbortGenerationSync()
        {
            return  _client.AbortSync(new GenkeyData()).Success;
        }

        public bool SupportsStreaming => true;
        public bool SupportsTTS { get; private set; } = false;
        public bool SupportsVision { get; private set; } = false;
        public bool SupportsWebSearch { get; private set; } = false;
    }

    /// <summary>
    /// Adapter for OpenAI-compatible backends (we'll see later)
    /// </summary>
    public class OpenAIAdapter : ILLMServiceClient
    {
        public event EventHandler<LLMTokenStreamingEventArgs> TokenReceived;

        private readonly OpenAI_APIClient _client;

        public OpenAIAdapter(HttpClient httpClient)
        {
            _client = new OpenAI_APIClient();

            // Hook into the OpenAI streaming event and adapt it to our interface's event
            //_client.StreamingMessageReceived += (sender, e) =>
            //{
            //    TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(
            //        e.Data.token,
            //        e.Data.finish_reason
            //    ));
            //};
        }

        public string BaseUrl
        {
            get => LLMSystem.BackendUrl; 
            set => LLMSystem.BackendUrl = value;
        }

        // Implement the rest of the interface methods...
        // This would follow the same pattern as the KoboldCppAdapter but use OpenAI API methods

        public async Task<int> GetMaxContextLength()
        {
            // OpenAI doesn't have a direct endpoint for this
            // Use model info to determine context length
            var modelInfo = await _client.GetModelInfo("default");
            // Parse context length from model info or use a default
            return 4096; // Default for many OpenAI models
        }

        public Task<string> GetModelInfo()
        {
            throw new NotImplementedException();
        }

        public Task<string> GetBackendInfo()
        {
            throw new NotImplementedException();
        }

        public Task<string> GenerateText(object parameters)
        {
            throw new NotImplementedException();
        }

        public Task GenerateTextStreaming(object parameters)
        {
            throw new NotImplementedException();
        }

        public Task<bool> AbortGeneration()
        {
            throw new NotImplementedException();
        }

        public bool AbortGenerationSync()
        {
            throw new NotImplementedException();
        }

        public Task<int> CountTokens(string text)
        {
            throw new NotImplementedException();
        }

        public int CountTokensSync(string text)
        {
            throw new NotImplementedException();
        }

        public Task<byte[]> TextToSpeech(string text, string voice)
        {
            throw new NotImplementedException();
        }

        public Task<string> WebSearch(string query)
        {
            throw new NotImplementedException();
        }

        public Task<string> ImageCaption(byte[] imageData)
        {
            throw new NotImplementedException();
        }

        // Implement other methods...

        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;  // Depends on your implementation
        public bool SupportsVision => false;  // Depends on your implementation
        public bool SupportsWebSearch => false;
    }
}