using AIToolkit.LLM;
using AIToolkit.SearchAPI;
using Newtonsoft.Json;
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
        private readonly WebSearchAPI webSearchClient;

        public CompletionType CompletionType => CompletionType.Chat;

        public OpenAIAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMSystem.Settings.BackendUrl);
            _client = new OpenAI_APIClient(_httpClient);
            webSearchClient = new WebSearchAPI(httpClient);

            //Hook into the OpenAI streaming event and adapt it to our interface's event
            _client.StreamingMessageReceived += (sender, e) =>
            {
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(e.Token, e.FinishReason));
            };
}

        public string BaseUrl
        {
            get => LLMSystem.Settings.BackendUrl;
            set
            {
                LLMSystem.Settings.BackendUrl = value;
                _httpClient.BaseAddress = new Uri(LLMSystem.Settings.BackendUrl);
            }
        }

        // Implement the rest of the interface methods...
        // This would follow the same pattern as the KoboldCppAdapter but use OpenAI API methods

        public async Task<int> GetMaxContextLength()
        {
            // OpenAI doesn't have a direct endpoint for this
            // Use model info to determine context length
            // var modelInfo = await _client.GetModelInfo("default").ConfigureAwait(false);
            // Parse context length from model info or use a default
            return await Task.FromResult(32768).ConfigureAwait(false);
        }

        /// <summary>
        /// Get the default model info (first one if multiple loaded)
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetModelInfo()
        {
            var info = await _client.GetModelInfo().ConfigureAwait(false);
            return info.Id;
        }

        public async Task<string> GetBackendInfo()
        {
            return await _client.GetBackendInfo().ConfigureAwait(false);
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            var result = await _client.ChatCompletion(input).ConfigureAwait(false);
            var res = result.Message.Content.ToString();
            return res;
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            await _client.StreamChatCompletion(input).ConfigureAwait(false);
        }

        public IPromptBuilder GetPromptBuilder()
        {
            return new ChatPromptBuilder();
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

        public async Task<string> WebSearch(string query)
        {
            if (!SupportsWebSearch)
                return string.Empty;
            var res = await webSearchClient.SearchAndEnrichAsync(query, 3, LLMSystem.Settings.WebSearchDetailedResults).ConfigureAwait(false);
            // Convert results to a common format
            return JsonConvert.SerializeObject(res);
        }

        public Task<string> ImageCaption(byte[] imageData)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> CheckBackend()
        {
            try
            {
                var res = await _client.GetModelList().ConfigureAwait(false);
                return res != null;

            }
            catch (Exception)
            {
                // Handle the exception
                return false;
            }
        }

        public Task<bool> SaveKVState(int value)
        {
            throw new NotSupportedException("OpenAI API does not support KV cache manipulation");
        }

        public Task<bool> LoadKVState(int value)
        {
            throw new NotSupportedException("OpenAI API does not support KV cache manipulation");
        }

        public Task<bool> ClearKVStates()
        {
            throw new NotSupportedException("OpenAI API does not support KV cache manipulation");
        }

        public Task<string> SchemaToGrammar(Type jsonclass)
        {
            throw new NotImplementedException("Schema to grammar conversion not supported yet.");
        }


        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;  // TODO
        public bool SupportsVision => false;  // TODO
        public bool SupportsWebSearch => true;
        public bool SupportsStateSave => false; // Not Available
        public bool SupportsSchema => false; // TODO
    }
}