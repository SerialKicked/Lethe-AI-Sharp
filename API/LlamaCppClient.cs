using LetheAISharp.Files;
using LetheAISharp.LLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using OpenAI.Threads;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LetheAISharp.API
{

    public class LlamaCpp_APIClient : OpenAI_APIClient
    {
        public LlamaCpp_APIClient(HttpClient httpclient) : base(httpclient)
        {
            _httpClient = httpclient;
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            var settings = new OpenAISettings(LLMEngine.Settings.BackendUrl);
            API = new OpenAIClient(new OpenAIAuthentication("123"), settings, _httpClient);
        }

        public override async Task<string> GetBackendInfo()
        {
            return await Task.FromResult("Llama.cpp Backend").ConfigureAwait(false);
        }

        public async Task<TokenList> TokenizeAsync(TokenRequest body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<TokenList>(_httpClient!, HttpMethod.Post, "/tokenize", body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public TokenList TokenizeSync(TokenRequest body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => TokenizeAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task<TokenCountResponse> GetTokenCountAsync(MessageListQuery body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<TokenCountResponse>(_httpClient!, HttpMethod.Post, "/v1/messages/count_tokens", body, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public TokenCountResponse GetTokenCountSync(MessageListQuery body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => GetTokenCountAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public async Task<LlamaServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<LlamaServerState>(_httpClient!, HttpMethod.Get, "/props", cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> SetServerStateAsync(LlamaServerState state, CancellationToken cancellationToken = default)
        {
            var response = await SendRequestAsync<LlamaSetServerStateResponse>(_httpClient!, HttpMethod.Post, "/props", state, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.success;
        }

        public async Task<LlamaCppModelListResponse> GetLlamaModelListAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<LlamaCppModelListResponse>(_httpClient!, HttpMethod.Get, "/models", cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public override async Task StreamChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var cumulativeDelta = string.Empty;
            var toolCallRecords = new List<ToolCallRecord>();
            var toolRound = 0;
            int maxToolRounds = LLMEngine.Settings.ToolCallLimit == 0 ? int.MaxValue : LLMEngine.Settings.ToolCallLimit;
            var currentRequest = request;
            var ChatMessageLog = new List<OpenAI.Chat.Message>();

            if (LLMEngine.Settings.BackendLLamaCppAllowAllSamplers)
            {
                var serverState = await GetServerStateAsync(cancellationToken).ConfigureAwait(false);
                if (serverState?.default_generation_settings != null)
                {
                    serverState.default_generation_settings.Params.ImportSamplers(LLMEngine.Sampler);
                    await SetServerStateAsync(serverState, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                bool continueLoop = true;
                while (continueLoop)
                {
                    continueLoop = false;
                    await foreach (var partialResponse in API.ChatEndpoint.StreamCompletionEnumerableAsync(currentRequest, cancellationToken: cancellationToken))
                    {
                        // Handle tool_calls
                        if (partialResponse.FirstChoice.FinishReason == "tool_calls" && partialResponse.FirstChoice.Message?.ToolCalls != null)
                        {
                            ChatMessageLog.Add(partialResponse.FirstChoice.Message);
                            var toolmsgs = new List<OpenAI.Chat.Message>();
                            foreach (var toolcall in partialResponse.FirstChoice.Message.ToolCalls)
                            {
                                string functionResult;
                                bool success;
                                var sw = Stopwatch.StartNew();
                                try
                                {
                                    var allowed = true;
                                    if (LLMEngine.ToolCallConfirmation != null)
                                    {
                                        // that's an UI call, so no ConfigureAwait(false) here, otherwise we might crash things at the UI level
                                        allowed = await LLMEngine.ToolCallConfirmation(toolcall.Function?.Name ?? string.Empty, toolcall.Function?.Arguments?.ToJsonString() ?? string.Empty);
                                    }
                                    if (toolRound >= maxToolRounds)
                                    {
                                        functionResult = "Max amount of tool calls reached. You must finish your thoughts and produce the response now.";
                                        success = true;
                                    }
                                    else if (!allowed)
                                    {
                                        functionResult = "This tool call was denied by the user.";
                                        success = false;
                                    }
                                    else
                                    {
                                        functionResult = (await toolcall.InvokeFunctionAsync<string>(cancellationToken)) ?? string.Empty;
                                        success = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    functionResult = $"Error: {ex.Message}";
                                    success = false;
                                }
                                sw.Stop();
                                LLMEngine.Logger?.LogInformation("[OpenAI API] Tool Call: {Name}", toolcall.Function?.Name);
                                toolCallRecords?.Add(new ToolCallRecord
                                {
                                    CallId = toolcall.Id ?? string.Empty,
                                    FunctionName = toolcall.Function?.Name ?? string.Empty,
                                    ArgumentsJson = toolcall.Function?.Arguments?.ToJsonString() ?? string.Empty,
                                    ResultJson = success ? functionResult : string.Empty,
                                    Error = success ? null : functionResult,
                                    Success = success,
                                    Duration = sw.Elapsed
                                });
                                var tc = new OpenAI.Chat.Message(toolcall, functionResult);
                                toolmsgs.Add(tc);
                                ChatMessageLog.Add(tc);

                            }

                            // Build updated message list: original messages + assistant tool-call message + tool results.
                            // The assistant message is included as-is (thinking/CoT content, if any, is preserved
                            // since Message properties are not publicly mutable in this library version).
                            var updatedMessages = new List<OpenAI.Chat.Message>(currentRequest.Messages)
                            {
                                partialResponse.FirstChoice.Message
                            };
                            updatedMessages.AddRange(toolmsgs);

                            if (toolRound < maxToolRounds)
                            {
                                // Create a new request preserving all original parameters
                                currentRequest = new ChatRequest(
                                    messages: updatedMessages,
                                    tools: currentRequest.Tools ?? [],
                                    toolChoice: toolRound < maxToolRounds ? "auto" : "none",
                                    model: currentRequest.Model,
                                    maxTokens: currentRequest.MaxCompletionTokens,
                                    responseFormat: currentRequest.ResponseFormat,
                                    seed: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.Seed,
                                    stops: currentRequest.Stops,
                                    frequencyPenalty: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.FrequencyPenalty,
                                    presencePenalty: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.PresencePenalty,
                                    temperature: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.Temperature,
                                    topP: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.TopP,
                                    jsonSchema: currentRequest.ResponseFormatObject?.JsonSchema,
                                    user: currentRequest.User
                                );
                            }
                            else
                            {
                                currentRequest = new ChatRequest(
                                    messages: updatedMessages,
                                    model: currentRequest.Model,
                                    maxTokens: currentRequest.MaxCompletionTokens,
                                    responseFormat: currentRequest.ResponseFormat,
                                    seed: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.Seed,
                                    stops: currentRequest.Stops,
                                    frequencyPenalty: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.FrequencyPenalty,
                                    presencePenalty: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.PresencePenalty,
                                    temperature: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.Temperature,
                                    topP: LLMEngine.Settings.BackendLLamaCppAllowAllSamplers ? null : currentRequest.TopP,
                                    jsonSchema: currentRequest.ResponseFormatObject?.JsonSchema,
                                    user: currentRequest.User
                                );
                            }
                            // keep this here, otherwise the model doesn't receive a warning that tool calls have ended, and it'll imagine them instead.
                            toolRound++;
                            // --
                            continueLoop = true;
                            break; // exit foreach — re-enter while loop (or stop if limit reached)
                        }
                        else if (partialResponse.FirstChoice.Delta?.Content != null)
                        {
                            // handle message stuff
                            cumulativeDelta += partialResponse.FirstChoice.Delta.Content;
                            var hasFinishReason = !string.IsNullOrEmpty(partialResponse.FirstChoice.FinishReason);
                            RaiseOnStreamingResponse(new OpenTokenResponse
                            {
                                Token = partialResponse.FirstChoice.Delta.Content,
                                FinishReason = partialResponse.FirstChoice.FinishReason,
                                ToolCallRecords = hasFinishReason && toolCallRecords?.Count > 0 ? toolCallRecords : null
                            });
                            if (hasFinishReason && (partialResponse.FirstChoice.FinishReason == "stop" || partialResponse.FirstChoice.FinishReason == "length"))
                            {
                                if (partialResponse.FirstChoice.Message != null)
                                    ChatMessageLog.Add(partialResponse.FirstChoice.Message);
                                break;
                            }
                        }
                        else if (!string.IsNullOrEmpty(partialResponse.FirstChoice.FinishReason) && partialResponse.FirstChoice.FinishReason != "null")
                        {
                            if (partialResponse.FirstChoice.FinishReason != "tool_calls" || toolCallRecords?.Count > 0)
                            {
                                if (partialResponse.FirstChoice.Message != null)
                                    ChatMessageLog.Add(partialResponse.FirstChoice.Message);
                                RaiseOnStreamingResponse(new OpenTokenResponse
                                {
                                    Token = "",
                                    FinishReason = partialResponse.FirstChoice.FinishReason,
                                    ToolCallRecords = toolCallRecords?.Count > 0 ? toolCallRecords : null
                                });
                                if (partialResponse.FirstChoice.FinishReason == "tool_calls" && toolCallRecords?.Count > 0)
                                {
                                    toolCallRecords.Clear();
                                }
                            }
                            if (partialResponse.FirstChoice.FinishReason == "stop" || partialResponse.FirstChoice.FinishReason == "length")
                                break;
                        }
                    }
                }
                var x = ChatMessageLog.Count;
            }
            catch (System.Text.Json.JsonException ex)
            {
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $" [Error Streaming Message: {ex.Message}] " + LLMEngine.NewLine + LLMEngine.NewLine + "This is likely an issue with the Jinja chat template used by this model. It might not support some of Lethe AI features (namely system messages in the prompt) or it can just be incorrect." + LLMEngine.NewLine + LLMEngine.NewLine + "You can either:" + LLMEngine.NewLine + "- Edit the Jinja chat template in your backend." + LLMEngine.NewLine + "- Use a different model." + LLMEngine.NewLine + "- Use a text completion backend like KoboldCpp.",
                    FinishReason = "error"
                });
            }
            catch (OperationCanceledException ex)
            {
                LLMEngine.Logger?.LogInformation(ex, "[OpenAI API] Message stream stopped by user: {Message}", ex.Message);
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $"The response was manually interrupted or cancelled. ({ex.Message})",
                    FinishReason = "error"
                });
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[OpenAI API] Error during streaming chat completion: {Message}", ex.Message);
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $"Error during streaming chat completion: {ex.Message}",
                    FinishReason = "error"
                });
            }

            // CA2254 fix: Use a constant message template and pass cumulativeDelta as an argument
            LLMEngine.Logger?.LogInformation("[OpenAI API] Final response: {CumulativeDelta}", cumulativeDelta);
        }
    }

    public class TokenCountResponse
    {
        public int input_tokens { get; set; } = 0;
    }

    public class MessageListQuery
    {
        public string model { get; set; } = "gpt-4";
        public List<MessageQuery> messages { get; set; } = [];
    }

    public class MessageQuery(string role, string content)
    {
        public string role { get; set; } = role;
        public string content { get; set; } = content;

        public MessageQuery() : this("user", string.Empty) { }
    }

    public class TokenRequest
    {
        public string content { get; set; } = string.Empty;
        public bool add_special { get; set; } = false;
        public bool parse_special { get; set; } = true;
        public bool with_pieces { get; set; } = false;
    }

    public class TokenList
    {
        public List<int> tokens { get; set; } = [];

        public int GetTokenCount() => tokens.Count;
    }


    public class LlamaServerState
    {
        public LlamaCppGenerationSlot default_generation_settings { get; set; } = new();

        public int total_slots { get; set; }

        public string model_path { get; set; } = string.Empty;

        public string chat_template { get; set; } = string.Empty;

        public LlamaCppChatTemplateCaps chat_template_caps { get; set; } = new();

        public LlamaCppModalities modalities { get; set; } = new();

        public string build_info { get; set; } = string.Empty;

        public bool is_sleeping { get; set; }
    }

    public class LlamaCppGenerationSlot
    {
        public int id { get; set; }

        public int id_task { get; set; }

        public int n_ctx { get; set; }

        public bool speculative { get; set; }

        public bool is_processing { get; set; }

        [JsonPropertyName("params")]
        public LlamaCppGenerationParams Params { get; set; } = new();

        public string prompt { get; set; } = string.Empty;

        public LlamaCppNextToken next_token { get; set; } = new();
    }

    public class LlamaCppGenerationParams
    {
        public int n_predict { get; set; }

        public long seed { get; set; }

        public float temperature { get; set; }

        public float dynatemp_range { get; set; }

        public float dynatemp_exponent { get; set; }

        public int top_k { get; set; }

        public float top_p { get; set; }

        public float min_p { get; set; }

        public float xtc_probability { get; set; }

        public float xtc_threshold { get; set; }

        public float typical_p { get; set; }

        public int repeat_last_n { get; set; }

        public float repeat_penalty { get; set; }

        public float presence_penalty { get; set; }

        public float frequency_penalty { get; set; }

        public float dry_multiplier { get; set; }

        public float dry_base { get; set; }

        public int dry_allowed_length { get; set; }

        public int dry_penalty_last_n { get; set; }

        public List<string> dry_sequence_breakers { get; set; } = new();

        public int mirostat { get; set; }

        public float mirostat_tau { get; set; }

        public float mirostat_eta { get; set; }

        public List<string> stop { get; set; } = new();

        public int max_tokens { get; set; }

        public int n_keep { get; set; }

        public int n_discard { get; set; }

        public bool ignore_eos { get; set; }

        public bool stream { get; set; }

        public int n_probs { get; set; }

        public int min_keep { get; set; }

        public string grammar { get; set; } = string.Empty;

        public List<string> samplers { get; set; } = new();

        [JsonPropertyName("speculative.n_max")]
        public int SpeculativeNMax { get; set; }

        [JsonPropertyName("speculative.n_min")]
        public int SpeculativeNMin { get; set; }

        [JsonPropertyName("speculative.p_min")]
        public float SpeculativePMin { get; set; }

        public bool timings_per_token { get; set; }

        public void ImportSamplers(SamplerSettings samplers)
        {
            seed = samplers.Sampler_seed == -1 ? LLMEngine.RNG.Next(int.MaxValue) : samplers.Sampler_seed;
            temperature = (float)samplers.Temperature;
            dynatemp_exponent = (float)samplers.Dynatemp_exponent;
            dynatemp_range = (float)samplers.Dynatemp_range;
            top_k = samplers.Top_k;
            top_p = (float)samplers.Top_p;
            min_p = (float)samplers.Min_p;
            xtc_probability = (float)samplers.Xtc_probability;
            xtc_threshold = (float)samplers.Xtc_threshold;
            typical_p = (float)samplers.Typical;
            repeat_penalty = (float)samplers.Rep_pen;
            repeat_last_n = samplers.Rep_pen_range;
            dry_allowed_length = samplers.Dry_allowed_length;
            dry_base = (float)samplers.Dry_base;
            dry_multiplier = (float)samplers.Dry_multiplier;
            dry_sequence_breakers = [.. samplers.Dry_sequence_breakers];
            mirostat = (int)samplers.Mirostat;
            mirostat_eta = (float)samplers.Mirostat_eta;
            mirostat_tau = (float)samplers.Mirostat_tau;
            ignore_eos = samplers.Bypass_eos;
        }
    }

    public class LlamaCppNextToken
    {
        public bool has_next_token { get; set; }

        public bool has_new_line { get; set; }

        public int n_remain { get; set; }

        public int n_decoded { get; set; }

        public string stopping_word { get; set; } = string.Empty;
    }

    public class LlamaCppModalities
    {
        public bool vision { get; set; }
        public bool audio { get; set; }
    }

    public class LlamaCppChatTemplateCaps
    {
        public bool supports_tools { get; set; }

        public bool supports_tool_calls { get; set; }

        public bool supports_system_role { get; set; }

        public bool supports_parallel_tool_calls { get; set; }

        public bool supports_preserve_reasoning { get; set; }

        public bool supports_string_content { get; set; }

        public bool supports_typed_content { get; set; }

        public bool supports_object_arguments { get; set; }
    }

    public class LlamaCppModelListResponse
    {
        public List<LlamaCppModelInfo> data { get; set; } = [];
    }

    public class LlamaCppModelInfo
    {
        public string id { get; set; } = string.Empty;
        public bool in_cache { get; set; }

        public string path { get; set; } = string.Empty;

        public LlamaCppModelStatus status { get; set; } = new();
    }

    public class LlamaCppModelStatus
    {
        public string value { get; set; } = string.Empty;

        public List<string> args { get; set; } = new();
    }

    public class LlamaSetServerStateResponse
    {
        public bool success { get; set; } = false;

    }
}