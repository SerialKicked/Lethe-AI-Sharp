using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.SearchAPI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

            // A reconnect (or a model swap) may well fix whatever made /apply-template fail, so give the
            // accurate token count another chance.
            _applyTemplateUnavailable = false;
            _inputTokensUnavailable = false;

            if (CompletionType == CompletionType.Chat)
            {
                SupportsVision = res.modalities.vision;
                SupportsToolCalls = res.chat_template_caps.supports_tool_calls;
                SupportParallelToolCall = res.chat_template_caps.supports_parallel_tool_calls;
                var isthink = res.chat_template.Contains("enable_think") || res.chat_template.Contains("<think>", StringComparison.InvariantCultureIgnoreCase) || res.chat_template.Contains("[THINK]", StringComparison.InvariantCultureIgnoreCase);
                AllowPrefill = LLMEngine.Settings.BackendChatAllowPrefill ?? !isthink;

                LLMEngine.Logger?.LogInformation(
                    "[LlamaCpp] Chat template caps: tools={Tools}, tool_calls={ToolCalls}, parallel_tool_calls={Parallel}, system_role={System}, object_arguments={ObjectArgs}, typed_content={Typed}",
                    res.chat_template_caps.supports_tools, res.chat_template_caps.supports_tool_calls,
                    res.chat_template_caps.supports_parallel_tool_calls, res.chat_template_caps.supports_system_role,
                    res.chat_template_caps.supports_object_arguments, res.chat_template_caps.supports_typed_content);

                if (res.chat_template_caps.supports_tool_calls && !res.chat_template_caps.supports_object_arguments)
                {
                    // Tool call arguments travel as a JSON string. When the server reports that the template
                    // never reads them as a mapping, it will not convert them, and any template that iterates
                    // the arguments (Qwen and friends) fails to render. Nothing we send can work around it.
                    LLMEngine.Logger?.LogWarning("[LlamaCpp] The chat template accepts tool calls but does not read their arguments as an object. Tool-call rendering may fail server-side; consider a corrected Jinja template.");
                }

                if (!res.chat_template_caps.supports_system_role)
                {
                    LLMEngine.Logger?.LogWarning("[LlamaCpp] The chat template does not support system messages. LetheAISharp relies on in-conversation system messages, so expect degraded results.");
                }
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

        /// <summary>
        /// True when the server exposes /v1/chat/completions/input_tokens (llama.cpp builds from June
        /// 2026 onward), letting it count the exact prompt tokens of a fully-built chat request -
        /// template, tools, template kwargs and image expansion included. This is the only count that
        /// cannot drift from what generation actually processes.
        /// </summary>
        public bool SupportsRequestTokenCount => CompletionType == CompletionType.Chat && !_inputTokensUnavailable;

        /// <inheritdoc cref="ILLMServiceClient.CountRequestTokensSync"/>
        public int CountRequestTokensSync(object parameters)
        {
            if (parameters is not ChatRequest input)
                throw new ArgumentException("Parameters must be of type ChatRequest");
            try
            {
                return checked((int)_client.ChatInputTokensSync(input).input_tokens);
            }
            catch (ApiException ex) when (ex.StatusCode is 404 or 501)
            {
                // Older llama.cpp server without the endpoint: stop paying for a round-trip we know
                // will fail on every subsequent count. Cleared by GetBackendInfo (i.e. on reconnect).
                _inputTokensUnavailable = true;
                LLMEngine.Logger?.LogWarning(
                    "[LlamaCpp] /v1/chat/completions/input_tokens is not available on this server (HTTP {StatusCode}); falling back to template-based token counting. Update llama.cpp to a June 2026 or newer build for exact token counts.",
                    ex.StatusCode);
                throw;
            }
        }

        /// <summary>
        /// True once /v1/chat/completions/input_tokens has been reported missing, so we stop paying
        /// for a round-trip we know will fail on every subsequent count. Cleared by
        /// <see cref="GetBackendInfo"/> (i.e. on reconnect).
        /// </summary>
        private bool _inputTokensUnavailable;

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
            // Fallback message-list count for servers without /v1/chat/completions/input_tokens
            // (see CountRequestTokensSync, the preferred exact path). Unlike that endpoint, this path
            // reconstructs the prompt from the message list: in chat mode the authoritative count must
            // match what /v1/chat/completions actually builds: names, macro expansion and the model's
            // chat-template scaffolding all affect the real token
            // count. Counting the raw message text (as the legacy /v1/messages/count_tokens path did)
            // systematically under-counts, and the gap grows with the number of messages. To get an exact
            // count we render the messages through the model's chat template via /apply-template (using the
            // same content, tools and template arguments generation uses), then tokenize that string.
            // Note: /tokenize cannot expand image tokens, so callers must add an image estimate on top.
            if (CompletionType == CompletionType.Chat && !_applyTemplateUnavailable)
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

                    var templated = _client.ApplyTemplateSync(new ApplyTemplateQuery
                    {
                        messages = chatMessages,
                        tools = BuildTemplateTools(),
                        chat_template_kwargs = new Dictionary<string, object>
                        {
                            { "enable_thinking", !LLMEngine.Settings.DisableThinking }
                        },
                        add_generation_prompt = LLMEngine.AddGenerationPromptOverride
                    });
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
                    _applyTemplateUnavailable = true;
                }
                catch (ApiException ex)
                {
                    // The interesting part of a template failure is the server's own error text, which
                    // ex.Message ("HTTP status code 500 was not expected.") does not carry.
                    _applyTemplateUnavailable = true;
                    LLMEngine.Logger?.LogWarning(
                        "[LlamaCpp] /apply-template failed (HTTP {StatusCode}); falling back to legacy token count, which under-counts. Server said: {Response}",
                        ex.StatusCode, string.IsNullOrWhiteSpace(ex.Response) ? "(no response body)" : ex.Response);
                }
                catch (Exception ex)
                {
                    _applyTemplateUnavailable = true;
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
        /// True once /apply-template has failed, so we stop paying for a round-trip we know will fail on
        /// every subsequent count. Cleared by <see cref="GetBackendInfo"/> (i.e. on reconnect).
        /// </summary>
        private bool _applyTemplateUnavailable;

        /// <summary>
        /// True when the accurate /apply-template token count is in use. While it is, the server renders
        /// the tool definitions itself, so callers must not add a local estimate for them on top.
        /// </summary>
        public bool CountsToolDefinitions => CompletionType == CompletionType.Chat && !_applyTemplateUnavailable;

        /// <summary>
        /// Converts the currently active tool list into the /apply-template tool schema, or null when no
        /// tools are in play. Templates commonly render a large tool-description block and restructure the
        /// system message when tools are present, so omitting these makes the count structurally wrong -
        /// not merely short by a constant.
        /// </summary>
        private static List<ApplyTemplateTool>? BuildTemplateTools()
        {
            if (!LLMEngine.ToolCallsLoaded)
                return null;

            var tools = LLMEngine.ToolManager.GetToolList();
            if (tools.Count == 0)
                return null;

            var result = new List<ApplyTemplateTool>(tools.Count);
            foreach (var tool in tools)
            {
                if (tool.Function is null)
                    continue;

                // Parameters is a System.Text.Json node; re-parse it through Newtonsoft so it serializes as
                // a nested object rather than an escaped string (the server rejects non-object parameters).
                JToken parameters;
                try
                {
                    parameters = tool.Function.Parameters is null
                        ? new JObject()
                        : JToken.Parse(tool.Function.Parameters.ToJsonString());
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogWarning(ex, "[LlamaCpp] Could not convert the parameter schema of tool {Name}; counting it without parameters.", tool.Function.Name);
                    parameters = new JObject();
                }

                result.Add(new ApplyTemplateTool
                {
                    type = "function",
                    function = new ApplyTemplateToolFunction
                    {
                        name = tool.Function.Name ?? string.Empty,
                        description = tool.Function.Description ?? string.Empty,
                        parameters = parameters
                    }
                });
            }
            return result.Count == 0 ? null : result;
        }

        /// <summary>
        /// Builds a Newtonsoft-serializable /apply-template message from a <see cref="SingleMessage"/>,
        /// mirroring the three cases in <see cref="SingleMessage.ToChatCompletion"/> (tool result,
        /// assistant tool-call-only, and normal text). Tool-call arguments are kept as a single-encoded
        /// JSON *string* (never a System.Text.Json JsonNode) so serialization cannot self-reference.
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
                    // The server rejects non-assistant messages without a content field.
                    content = message.Message ?? string.Empty,
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
                            // Must be single-encoded object text, matching what the chat endpoint sends.
                            arguments = record.GetArgumentsText()
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
                content = message.ToChatContentText() ?? string.Empty,
                name = message.ToChatContentName()
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