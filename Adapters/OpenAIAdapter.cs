using AIToolkit.LLM;
using OpenAI.Chat;

namespace AIToolkit.API
{
    /// <summary>
    /// Adapter for OpenAI-compatible backends (we'll see later)
    /// </summary>
    public class OpenAIAdapter : ILLMServiceClient
    {
        public event EventHandler<LLMTokenStreamingEventArgs>? TokenReceived;

        private readonly OpenAI_APIClient _client;
        private readonly HttpClient _httpClient;

        public OpenAIAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMSystem.BackendUrl);
            _client = new OpenAI_APIClient(_httpClient);

            //Hook into the OpenAI streaming event and adapt it to our interface's event
            _client.StreamingMessageReceived += (sender, e) =>
            {
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(e.Token, e.FinishReason));
            };
}

        public string BaseUrl
        {
            get => LLMSystem.BackendUrl;
            set
            {
                LLMSystem.BackendUrl = value;
                _httpClient.BaseAddress = new Uri(LLMSystem.BackendUrl);
            }
        }

        // Implement the rest of the interface methods...
        // This would follow the same pattern as the KoboldCppAdapter but use OpenAI API methods

        public async Task<int> GetMaxContextLength()
        {
            // OpenAI doesn't have a direct endpoint for this
            // Use model info to determine context length
            // var modelInfo = await _client.GetModelInfo("default");
            // Parse context length from model info or use a default
            return await Task.FromResult(16384);
        }

        public async Task<string> GetModelInfo()
        {
            var info = await _client.GetModelInfo("default");
            return info.Id;
        }

        public async Task<string> GetBackendInfo()
        {
            return await _client.GetBackendInfo();
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            var result = await _client.ChatCompletion(input);
            return result.Message.Content;
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            await _client.StreamChatCompletion(input);
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
            return Task.FromResult(_client.CountTokens(text));
        }

        public int CountTokensSync(string text)
        {
            return _client.CountTokens(text);
        }

        public Task<byte[]> TextToSpeech(string text, string voice)
        {
            // OpenAI does not support TTS directly
            return Task.FromResult(Array.Empty<byte>());
        }

        public Task<string> WebSearch(string query)
        {
            // OpenAI does not support web search directly
            return Task.FromResult(string.Empty);
        }

        public Task<string> ImageCaption(byte[] imageData)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> CheckBackend()
        {
            try
            {
                var res = await _client.GetModelList();
                return res != null;

            }
            catch (Exception)
            {
                // Handle the exception
                return false;
            }
        }

        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;  // Depends on your implementation
        public bool SupportsVision => false;  // Depends on your implementation
        public bool SupportsWebSearch => false;
    }
}