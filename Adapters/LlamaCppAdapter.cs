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

        public CompletionType CompletionType { get; set; } = CompletionType.Chat;
        public List<CompletionType> AvailCompletionTypes => [ CompletionType.Chat, CompletionType.Text ];

        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;
        public bool SupportsVision { get; private set; } = false;
        public bool SupportsWebSearch => true;
        public bool SupportsStateSave { get; private set; } = false;
        public bool SupportsSchema { get; private set; } = true;
        public bool SupportsToolCalls { get; private set; } = true;
        public bool SupportParallelToolCall { get; private set; } = false;
        public bool AllowPrefill { get; private set; } = LLMEngine.Settings.BackendChatAllowPrefill ?? false;

        public LlamaCppAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMEngine.Settings.BackendUrl);
            _client = new LlamaCpp_APIClient(_httpClient);
            webSearchClient = new WebSearchAPI();

            //Hook into the OpenAI streaming event and adapt it to our interface's event
            _client.StreamingMessageReceived += (sender, e) =>
            {
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(e.Token, e.FinishReason, e.ToolCallRecords, e.ReasoningToken));
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

            if (CompletionType == CompletionType.Chat)
            {
                SupportsVision = res.modalities.vision;
                SupportsToolCalls = res.chat_template_caps.supports_tool_calls;
                SupportParallelToolCall = res.chat_template_caps.supports_parallel_tool_calls;
                var isthink = res.chat_template.Contains("enable_think") || res.chat_template.Contains("<think>", StringComparison.InvariantCultureIgnoreCase) || res.chat_template.Contains("[THINK]", StringComparison.InvariantCultureIgnoreCase);
                AllowPrefill = LLMEngine.Settings.BackendChatAllowPrefill ?? !isthink;
            }
            else
            {
                SupportsVision = false;
                SupportParallelToolCall = false;
                SupportsToolCalls = false;
                AllowPrefill = true;
            }

            return $"Llama.cpp [{res.build_info}]";
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (CompletionType == CompletionType.Chat)
            {
                return await GenerateChatCompletion(parameters).ConfigureAwait(false);
            }
            if (CompletionType == CompletionType.Text)
            {
                return await GenerateTextCompletion(parameters).ConfigureAwait(false);
            }
            throw new NotImplementedException($"Completion type {CompletionType} is not supported.");
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (CompletionType == CompletionType.Chat)
            {
                await GenerateChatCompletionStreaming(parameters).ConfigureAwait(false);
            }
            else if (CompletionType == CompletionType.Text)
            {
                await GenerateTextCompletionStreaming(parameters).ConfigureAwait(false);
            }
            else
            {
                throw new NotImplementedException($"Completion type {CompletionType} is not supported.");
            }
        }

        public IPromptBuilder GetPromptBuilder()
        {
            if (CompletionType == CompletionType.Text)
                return new TextPromptBuilder();
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
            var res = await webSearchClient.SearchAndEnrichAsync(query, LLMEngine.Settings.WebSearchResultsPerQuery, LLMEngine.Settings.WebSearchDetailedResults).ConfigureAwait(false);
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
            if (CompletionType == CompletionType.Chat)
            {
                OpenAI.JsonSchema jsonSchema = jsonclass;
                var res = jsonSchema.Schema.ToJsonString();
                return await Task.FromResult(res!).ConfigureAwait(false);
            }
            else
            {
                var gram = GbnfConverter.Convert(jsonclass);
                return await Task.FromResult(gram);
            }
        }

        public void Dispose()
        {
            cts?.Dispose();
            GC.SuppressFinalize(this);
        }

        public int CountMessageTokens(List<SingleMessage> messages)
        {
            // In chat mode the authoritative count must match what /v1/chat/completions actually builds:
            // names, macro expansion and the model's chat-template scaffolding all affect the real token
            // count. Counting the raw message text (as the legacy /v1/messages/count_tokens path did)
            // systematically under-counts, and the gap grows with the number of messages. To get an exact
            // count we render the messages through the model's chat template via /apply-template (using the
            // same content generation produces: name prefixes, macros and tool-call structure), then
            // tokenize that string with BOS.
            if (CompletionType == CompletionType.Chat)
            {
                try
                {
                    var chatMessages = new List<ApplyTemplateMessage>();
                    foreach (var message in messages)
                    {
                        var built = BuildTemplateMessage(message);
                        if (built is not null)
                            chatMessages.Add(built);
                    }
                    if (chatMessages.Count == 0)
                        return 0;

                    var templated = _client.ApplyTemplateSync(new ApplyTemplateQuery { messages = chatMessages });
                    if (!string.IsNullOrEmpty(templated?.prompt))
                    {
                        var tokens = _client.TokenizeSync(new TokenRequest
                        {
                            content = templated.prompt,
                            add_special = true,
                            parse_special = true
                        });
                        return tokens.GetTokenCount();
                    }
                    LLMEngine.Logger?.LogWarning("[LlamaCpp] /apply-template returned an empty prompt; falling back to legacy token count.");
                }
                catch (Exception ex)
                {
                    // Older servers may not expose /apply-template. Fall back to the legacy path below.
                    LLMEngine.Logger?.LogWarning(ex, "[LlamaCpp] /apply-template unavailable; falling back to legacy token count.");
                }
            }

            var request = new MessageListQuery();
            foreach (var message in messages)
            {
                var role = message.Role switch
                {
                    AuthorRole.User => "user",
                    AuthorRole.Assistant => "assistant",
                    AuthorRole.System => "system",
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

        /// <summary>
        /// Builds a Newtonsoft-serializable /apply-template message from a <see cref="SingleMessage"/>,
        /// mirroring the three cases in <see cref="SingleMessage.ToChatCompletion"/> (tool result,
        /// assistant tool-call-only, and normal text). Tool-call arguments are kept as the raw JSON
        /// *string* (never a System.Text.Json JsonNode) so serialization cannot self-reference.
        /// Returns null for roles that are not sent to the template.
        /// </summary>
        private static ApplyTemplateMessage? BuildTemplateMessage(SingleMessage message)
        {
            // Tool result messages
            if (message.Role == AuthorRole.Tool && message.ToolCalls.Count > 0)
            {
                return new ApplyTemplateMessage
                {
                    role = "tool",
                    content = message.Message,
                    tool_call_id = message.ToolCalls[0].CallId
                };
            }

            // Assistant tool-call-only messages (no text content)
            if (message.Role == AuthorRole.Assistant && message.ToolCalls.Count > 0 && string.IsNullOrEmpty(message.Message))
            {
                var calls = new List<ApplyTemplateToolCall>();
                foreach (var record in message.ToolCalls)
                {
                    calls.Add(new ApplyTemplateToolCall
                    {
                        id = record.CallId,
                        type = "function",
                        function = new ApplyTemplateFunction
                        {
                            name = record.FunctionName,
                            arguments = record.ArgumentsJson
                        }
                    });
                }
                return new ApplyTemplateMessage
                {
                    role = "assistant",
                    // OpenAI schema requires content (may be null) alongside tool_calls.
                    content = string.IsNullOrEmpty(message.Message) ? null : message.Message,
                    tool_calls = calls
                };
            }

            // Normal messages (System / User / Assistant text)
            var role = message.Role switch
            {
                AuthorRole.User => "user",
                AuthorRole.Assistant => "assistant",
                AuthorRole.System => "system",
                AuthorRole.Tool => "tool",
                _ => null
            };
            if (role is null)
                return null;

            return new ApplyTemplateMessage
            {
                role = role,
                content = message.ToChatContentText()
            };
        }

        private async Task GenerateChatCompletionStreaming(object parameters)
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
            // Samplers handled at the request level now. Kept in case it's needed for something else later
            //if (LLMEngine.Settings.BackendLLamaCppUseProps)
            //{
            //    var serverState = await _client.GetServerStateAsync(token).ConfigureAwait(false);
            //    if (serverState?.default_generation_settings != null)
            //    {
            //        serverState.default_generation_settings.Params.ImportSamplers(LLMEngine.Sampler);
            //        await _client.SetServerStateAsync(serverState, token).ConfigureAwait(false);
            //    }
            //}

            await _client.StreamChatCompletion(input, token).ConfigureAwait(false);
        }

        private async Task GenerateTextCompletionStreaming(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose();
                cts = new CancellationTokenSource();
                token = cts.Token;
            }
            var request = new LlamaCppCompletionRequest();
            request.ImportFromGenerationInput(input);
            await _client.TextCompletionStreamAsync(request, token).ConfigureAwait(false);
        }

        private async Task<string> GenerateChatCompletion(object parameters)
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
                // Samplers handled at the request level now. Kept in case it's needed for something else later
                //if (LLMEngine.Settings.BackendLLamaCppUseProps)
                //{
                //    var serverState = await _client.GetServerStateAsync(token).ConfigureAwait(false);
                //    if (serverState?.default_generation_settings != null)
                //    {
                //        serverState.default_generation_settings.Params.ImportSamplers(LLMEngine.Sampler);
                //        await _client.SetServerStateAsync(serverState, token).ConfigureAwait(false);
                //    }
                //}
                var result = await _client.ChatCompletion(param, token).ConfigureAwait(false);
                var res = result?.Message.Content.ToString();
                if (!string.IsNullOrEmpty(result?.Message?.ReasoningContent))
                {
                    LLMEngine.Logger?.LogInformation("[OpenAI API] Reasoning content: {Reasoning}", result.Message.ReasoningContent.RemoveNewLines());
                }
                return res ?? string.Empty;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[OpenAI API] Error during GenerateText: {Message}", ex.Message);
                return string.Empty;
            }
        }

        private async Task<string> GenerateTextCompletion(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose();
                cts = new CancellationTokenSource();
                token = cts.Token;
            }
            try
            {
                var request = new LlamaCppCompletionRequest();
                request.ImportFromGenerationInput(input);
                var result = await _client.TextCompletionAsync(request, token).ConfigureAwait(false);
                return result?.Content ?? string.Empty;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[LlamaCpp] Error during text completion: {Message}", ex.Message);
                return string.Empty;
            }
        }
    }
}