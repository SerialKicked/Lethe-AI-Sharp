using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.SearchAPI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI.Chat;
using System.Text;

namespace LetheAISharp.API
{
    /// <summary>
    /// Adapter for the LlamaCpp backend (using OpenAI-compatible API + additional features)
    /// </summary>
    public class LlamaCppAdapter : ILLMServiceClient, IDisposable
    {
        public event EventHandler<LLMTokenStreamingEventArgs>? TokenReceived;

        private readonly LlamaCpp_APIClient _client;
        private readonly HttpClient _httpClient;
        private readonly WebSearchAPI webSearchClient;
        private CancellationTokenSource? cts;
        private readonly Lock _ctsLock = new();

        public CompletionType CompletionType => CompletionType.Chat;

        public LlamaCppAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMEngine.Settings.BackendUrl);
            _client = new LlamaCpp_APIClient(_httpClient);
            webSearchClient = new WebSearchAPI();

            //Hook into the OpenAI streaming event and adapt it to our interface's event
            _client.StreamingMessageReceived += (sender, e) =>
            {
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(e.Token, e.FinishReason, e.ToolCallRecords));
            };
}

        public string BaseUrl
        {
            get => LLMEngine.Settings.BackendUrl;
            set
            {
                LLMEngine.Settings.BackendUrl = value;
                _httpClient.BaseAddress = new Uri(LLMEngine.Settings.BackendUrl);
            }
        }

        public void UpdateSearchProvider()
        {
            webSearchClient.SwitchProvider(LLMEngine.Settings.WebSearchAPI, LLMEngine.Settings.WebSearchBraveAPIKey);
        }

        public async Task<int> GetMaxContextLength()
        {
            // OpenAI doesn't have a direct endpoint for this
            // Use model info to determine context length
            // var modelInfo = await _client.GetModelInfo("default").ConfigureAwait(false);
            // Parse context length from model info or use a default
            var res = await _client.GetServerStateAsync().ConfigureAwait(false);
            return await Task.FromResult(res.default_generation_settings.n_ctx).ConfigureAwait(false);
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
            var res = await _client.GetServerStateAsync().ConfigureAwait(false);

            SupportsVision = res.modalities.vision;
            SupportsToolCalls = res.chat_template_caps.supports_tool_calls;
            SupportParallelToolCall = res.chat_template_caps.supports_parallel_tool_calls;

            var isthink = res.chat_template.Contains("enable_think") || res.chat_template.Contains("<think>", StringComparison.InvariantCultureIgnoreCase) || res.chat_template.Contains("[THINK]", StringComparison.InvariantCultureIgnoreCase);
            AllowPrefill = LLMEngine.Settings.BackendChatAllowPrefill ?? !isthink;

            return $"Llama.cpp [{res.build_info}]";
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose(); // Dispose old token source
                cts = new CancellationTokenSource();
                token = cts.Token;
            }
            var param = input;
            try
            {
                if (LLMEngine.Settings.BackendLLamaCppAllowAllSamplers)
                {
                    var serverState = await _client.GetServerStateAsync(token).ConfigureAwait(false);
                    if (serverState?.default_generation_settings != null)
                    {
                        serverState.default_generation_settings.Params.ImportSamplers(LLMEngine.Sampler);
                        await _client.SetServerStateAsync(serverState, token).ConfigureAwait(false);
                    }
                }
                var result = await _client.ChatCompletion(param, token).ConfigureAwait(false);
                var res = result?.Message.Content.ToString();
                return res ?? string.Empty;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[OpenAI API] Error during GenerateText: {Message}", ex.Message);
                return string.Empty;
            }
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose(); // Dispose old token source
                cts = new CancellationTokenSource();
                token = cts.Token;
            }

            if (LLMEngine.Settings.BackendLLamaCppAllowAllSamplers)
            {
                var serverState = await _client.GetServerStateAsync(token).ConfigureAwait(false);
                if (serverState?.default_generation_settings != null)
                {
                    serverState.default_generation_settings.Params.ImportSamplers(LLMEngine.Sampler);
                    await _client.SetServerStateAsync(serverState, token).ConfigureAwait(false);
                }
            }

            await _client.StreamChatCompletion(input, token).ConfigureAwait(false);
        }

        public IPromptBuilder GetPromptBuilder()
        {
            return new ChatPromptBuilder();
        }

        public async Task<bool> AbortGeneration()
        {
            return await Task.FromResult(AbortGenerationSync());
        }

        public bool AbortGenerationSync()
        {
            lock (_ctsLock)
            {
                if (cts != null && !cts.IsCancellationRequested)
                {
                    cts.Cancel();
                    return true;
                }
                return false;
            }
        }

        public async Task<int> CountTokens(string text)
        {
            var request = new TokenRequest { content = text };
            var token = await _client.TokenizeAsync(request).ConfigureAwait(false);
            return token.GetTokenCount();
        }

        public int CountTokensSync(string text)
        {
            var request = new TokenRequest { content = text };
            var token = _client.TokenizeSync(request);
            return token.GetTokenCount();
        }

        public async Task<byte[]> TextToSpeech(string text, string voice)
        {
            // OpenAI does not support TTS directly
            return await Task.FromResult(Array.Empty<byte>());
        }

        public async Task<string> WebSearch(string query)
        {
            if (!SupportsWebSearch)
                return string.Empty;
            var res = await webSearchClient.SearchAndEnrichAsync(query, 3, LLMEngine.Settings.WebSearchDetailedResults).ConfigureAwait(false);
            // Convert results to a common format
            return JsonConvert.SerializeObject(res);
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

        public async Task<string> SchemaToGrammar(Type jsonclass)
        {
            OpenAI.JsonSchema jsonSchema = jsonclass.GetType();
            var res = jsonSchema.Schema.ToJsonString();
            return await Task.FromResult(res!).ConfigureAwait(false);
        }

        public void Dispose()
        {
            cts?.Dispose();
            GC.SuppressFinalize(this);
        }

        public int CountMessageTokens(List<SingleMessage> messages)
        {
            var request = new MessageListQuery();
            foreach (var message in messages)
            {
                var role = message.Role switch
                {
                    AuthorRole.User => "user",
                    AuthorRole.Assistant => "assistant",
                    AuthorRole.System => "system",
                    AuthorRole.SysPrompt => "system",
                    AuthorRole.Tool => "tool",
                    _ => "delete"
                };
                if (role == "delete")
                    continue;
                var msg = message.Message;
                if (message.ToolCalls?.Count > 0 && message.Role == AuthorRole.Assistant)
                {
                    msg = message.ToolCallToString();   
                }
                request.messages.Add(new MessageQuery(role, msg));
            }
            if (request.messages.Count == 0)
                return 0;
            var token = _client.GetTokenCountSync(request);
            return token.input_tokens;
        }


        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;
        public bool SupportsVision { get; private set; } = false;
        public bool SupportsWebSearch => true;
        public bool SupportsStateSave { get; private set; } = false;
        public bool SupportsSchema { get; private set; } = true;
        public bool SupportsToolCalls { get; private set; } = true;
        public bool SupportParallelToolCall { get; private set; } = false;
        public bool AllowPrefill { get; private set; } = LLMEngine.Settings.BackendChatAllowPrefill ?? false;
        public BackendChatCompletionThinkTagBehavior ThinkTagBehavior => LLMEngine.Settings.BackendStartThinkTagBehavior ?? BackendChatCompletionThinkTagBehavior.Silent;
    }
}