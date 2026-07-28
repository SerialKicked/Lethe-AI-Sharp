using LetheAISharp.Files;
using LetheAISharp.LLM;
using LLama.Sampling;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using OpenAI.Threads;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
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

        /// <summary>
        /// Performs a non-streaming text completion via llama.cpp's native POST /completion endpoint.
        /// </summary>
        public async Task<LlamaCppCompletionResponse> TextCompletionAsync(LlamaCppCompletionRequest body, CancellationToken cancellationToken = default)
        {
            try
            {
                var res = await SendRequestAsync<LlamaCppCompletionResponse>(_httpClient!, HttpMethod.Post, "/completion", body, cancellationToken: cancellationToken).ConfigureAwait(false);
                return res;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[LlamaCpp] Error during text completion: {Message}", ex.Message);
                var res = new LlamaCppCompletionResponse() { Content = $"Error during text completion: {ex.Message}", Stop_type = "error" };
                return res;
            }
        }

        /// <summary>
        /// Performs a streaming text completion via llama.cpp's native POST /completion endpoint with stream:true.
        /// Raises <see cref="OpenAI_APIClient.StreamingMessageReceived"/> for each received token,
        /// which the adapter forwards as its own TokenReceived event.
        /// </summary>
        public async Task TextCompletionStreamAsync(LlamaCppCompletionRequest body, CancellationToken cancellationToken = default)
        {
            body.Stream = true;

            var request = new HttpRequestMessage(HttpMethod.Post, "/completion");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var json = JsonConvert.SerializeObject(body, JsonSerializerSettings);
            var content = new StringContent(json);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;

            try
            {
                using var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(stream);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line == null)
                        break; // end of stream
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("data:"))
                        continue;

                    var data = line[5..].Trim();
                    if (string.IsNullOrEmpty(data))
                        continue;

                    LlamaCppStreamingChunk? chunk;
                    try
                    {
                        chunk = JsonConvert.DeserializeObject<LlamaCppStreamingChunk>(data, JsonSerializerSettings);
                    }
                    catch (JsonException ex)
                    {
                        LLMEngine.Logger?.LogError(ex, "[LlamaCpp] Failed to parse SSE chunk: {Data}", data);
                        continue;
                    }

                    if (chunk == null)
                        continue;

                    string? finishReason = null;
                    if (chunk.Stop)
                        finishReason = string.IsNullOrEmpty(chunk.Stop_type) ? "stop" : chunk.Stop_type;

                    RaiseOnStreamingResponse(new OpenTokenResponse
                    {
                        Token = chunk.Content ?? string.Empty,
                        FinishReason = finishReason
                    });

                    if (chunk.Stop)
                        break;
                }
            }
            catch (OperationCanceledException ex)
            {
                LLMEngine.Logger?.LogInformation(ex, "[LlamaCpp] Text completion stream cancelled: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[LlamaCpp] Error during text completion streaming: {Message}", ex.Message);
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $"Error during text completion streaming: {ex.Message}",
                    FinishReason = "error"
                });
            }

            LLMEngine.Logger?.LogInformation("[LlamaCpp] Text completion stream finished.");
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
            try
            {
                return await SendRequestAsync<TokenCountResponse>(_httpClient!, HttpMethod.Post, "/v1/messages/count_tokens", body, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LLMEngine.Logger?.LogError(e, "[LlamaCpp] Error getting token count: {Message}", e.Message);
                return new TokenCountResponse { input_tokens = 100 };
            }
        }

        public TokenCountResponse GetTokenCountSync(MessageListQuery body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => GetTokenCountAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Applies the model's chat template to a list of chat messages via POST /apply-template, returning
        /// the fully-formatted prompt string exactly as it would be built for /v1/chat/completions (names,
        /// roles, template scaffolding included). This does not run inference.
        /// </summary>
        /// <remarks>
        /// Retries are disabled: the failure modes here (endpoint missing on old servers, or a template
        /// that refuses the message shape) are permanent, and the caller has a fallback. Retrying would
        /// only add several seconds of backoff to every token count.
        /// </remarks>
        public async Task<ApplyTemplateResponse> ApplyTemplateAsync(ApplyTemplateQuery body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<ApplyTemplateResponse>(_httpClient!, HttpMethod.Post, "/apply-template", body, cancellationToken: cancellationToken, maxRetryAttempts: 0).ConfigureAwait(false);
        }

        public ApplyTemplateResponse ApplyTemplateSync(ApplyTemplateQuery body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => ApplyTemplateAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Counts the exact number of prompt tokens llama.cpp would process for a fully-built chat
        /// request via POST /v1/chat/completions/input_tokens (llama.cpp PR #23913, June 2026).
        /// </summary>
        /// <remarks>
        /// The request is serialized with the same System.Text.Json options as /v1/chat/completions,
        /// so the server parses the identical body it would at generation time: chat template, tool
        /// definitions, chat_template_kwargs, add_generation_prompt and - crucially - image expansion
        /// through mtmd placeholder bitmaps all match generation one-to-one. This makes the count
        /// immune to the estimate drift that plagued the /apply-template + /tokenize pipeline (which
        /// cannot count image tokens at all).
        ///
        /// Retries are disabled: a missing endpoint (older server) is a permanent condition and the
        /// caller has a fallback.
        /// </remarks>
        public async Task<ChatInputTokensResponse> ChatInputTokensAsync(ChatRequest body, CancellationToken cancellationToken = default)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(body, OpenAI.OpenAIClient.JsonSerializationOptions);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions/input_tokens");
            var content = new StringContent(json);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content;
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

            using var response = await _httpClient!.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var responseText = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (status != 200)
            {
                throw new ApiException($"HTTP status code {status} was not expected.", status, responseText, new Dictionary<string, IEnumerable<string>>(), null);
            }
            var res = JsonConvert.DeserializeObject<ChatInputTokensResponse>(responseText, JsonSerializerSettings);
            return res ?? new ChatInputTokensResponse();
        }

        public ChatInputTokensResponse ChatInputTokensSync(ChatRequest body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => ChatInputTokensAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
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
    }

    public class TokenCountResponse
    {
        public int input_tokens { get; set; } = 0;
    }

    /// <summary>
    /// Response body for POST /v1/chat/completions/input_tokens. Contains the exact number of
    /// prompt tokens the paired /v1/chat/completions request would process (image tokens included).
    /// </summary>
    public class ChatInputTokensResponse
    {
        [JsonProperty("input_tokens")]
        public long input_tokens { get; set; } = 0;
    }

    public class MessageListQuery
    {
        public string model { get; set; } = "gpt-4";
        public List<MessageQuery> messages { get; set; } = [];
    }

    /// <summary>
    /// Request body for POST /apply-template. The messages must be in the same shape used by
    /// /v1/chat/completions so the server applies the identical chat template used at generation time.
    /// This is a plain, Newtonsoft-serializable structure on purpose: the OpenAI.Chat.* types use
    /// System.Text.Json (JsonNode) internals that Newtonsoft cannot serialize.
    /// </summary>
    public class ApplyTemplateQuery
    {
        [JsonProperty("messages")]
        public List<ApplyTemplateMessage> messages { get; set; } = [];

        /// <summary>
        /// Tool definitions, in the same shape as /v1/chat/completions. These matter for token counting:
        /// templates typically render a whole tool-description block, and many (Qwen's among them) also
        /// restructure the system message when tools are present. Omitted when null.
        /// </summary>
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public List<ApplyTemplateTool>? tools { get; set; }

        /// <summary>
        /// Template keyword arguments (for example <c>enable_thinking</c>). Omitted when null.
        /// </summary>
        [JsonProperty("chat_template_kwargs", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object>? chat_template_kwargs { get; set; }

        /// <summary>
        /// Whether the server appends the assistant generation prefix. Defaults to true server-side, so
        /// only send it when we need it off (structured output). Omitted when null.
        /// </summary>
        [JsonProperty("add_generation_prompt", NullValueHandling = NullValueHandling.Ignore)]
        public bool? add_generation_prompt { get; set; }
    }

    /// <summary>
    /// A tool definition for /apply-template, mirroring the /v1/chat/completions tool schema.
    /// </summary>
    public class ApplyTemplateTool
    {
        [JsonProperty("type")]
        public string type { get; set; } = "function";

        [JsonProperty("function")]
        public ApplyTemplateToolFunction function { get; set; } = new();
    }

    public class ApplyTemplateToolFunction
    {
        [JsonProperty("name")]
        public string name { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string description { get; set; } = string.Empty;

        // JSON schema of the parameters. Held as a JToken so Newtonsoft emits it as a nested object
        // rather than an escaped string (the server rejects tools whose parameters are not an object).
        [JsonProperty("parameters")]
        public JToken parameters { get; set; } = new JObject();
    }

    /// <summary>
    /// A single chat message for /apply-template, mirroring the /v1/chat/completions message schema.
    /// Null members are dropped so the payload matches what the OpenAI-compatible endpoint expects.
    /// </summary>
    public class ApplyTemplateMessage
    {
        [JsonProperty("role")]
        public string role { get; set; } = "user";

        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
        public string? content { get; set; }

        /// <summary>
        /// Optional author name, matching the <c>name</c> field the chat endpoint sends.
        /// </summary>
        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string? name { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<ApplyTemplateToolCall>? tool_calls { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? tool_call_id { get; set; }
    }

    public class ApplyTemplateToolCall
    {
        [JsonProperty("id")]
        public string id { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string type { get; set; } = "function";

        [JsonProperty("function")]
        public ApplyTemplateFunction function { get; set; } = new();
    }

    public class ApplyTemplateFunction
    {
        [JsonProperty("name")]
        public string name { get; set; } = string.Empty;

        // Per the OpenAI spec, function arguments are a JSON *string*, not a nested object. It must be
        // single-encoded (`{"a":1}`), not a quoted literal (`"{\"a\":1}"`): the server parses this string
        // back into an object before rendering, and a double-encoded value parses back into a string,
        // which then breaks templates that iterate the arguments as a mapping.
        [JsonProperty("arguments")]
        public string arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response body for POST /apply-template. Contains the fully-formatted prompt string.
    /// </summary>
    public class ApplyTemplateResponse
    {
        [JsonProperty("prompt")]
        public string prompt { get; set; } = string.Empty;
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

        public List<string> dry_sequence_breakers { get; set; } = [];

        public int mirostat { get; set; }

        public float mirostat_tau { get; set; }

        public float mirostat_eta { get; set; }

        public List<string> stop { get; set; } = [];

        public int max_tokens { get; set; }

        public int n_keep { get; set; }

        public int n_discard { get; set; }

        public bool ignore_eos { get; set; }

        public bool stream { get; set; }

        public int n_probs { get; set; }

        public int min_keep { get; set; }

        public string grammar { get; set; } = string.Empty;

        public List<string> samplers { get; set; } = [];

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

        public List<string> args { get; set; } = [];
    }

    public class LlamaSetServerStateResponse
    {
        public bool success { get; set; } = false;

    }

    /// <summary>
    /// llama.cpp-specific sampler parameters shared by the native /completion request and by
    /// <see cref="OpenAI.Chat.ChatRequest"/>.
    /// </summary>
    /// <remarks>
    /// Both attribute families are required and neither is redundant: the native /completion body is
    /// serialized with Newtonsoft (<c>JsonProperty</c>), while <c>ChatRequest</c> is serialized with
    /// System.Text.Json (<c>JsonPropertyName</c>), which ignores Newtonsoft attributes entirely. With only
    /// the Newtonsoft names present these fields went out to the chat endpoint as "Top_k", "Min_p" and so
    /// on, and llama.cpp silently discarded every one of them.
    /// </remarks>
    public class LlamaCppAdvancedSampler
    {
        [JsonProperty("top_k")]
        [JsonPropertyName("top_k")]
        public int? Top_k { get; set; }

        [JsonProperty("min_p")]
        [JsonPropertyName("min_p")]
        public float? Min_p { get; set; }

        [JsonProperty("typical_p")]
        [JsonPropertyName("typical_p")]
        public float? Typical_p { get; set; }

        [JsonProperty("repeat_last_n")]
        [JsonPropertyName("repeat_last_n")]
        public int? Repeat_last_n { get; set; }

        [JsonProperty("mirostat")]
        [JsonPropertyName("mirostat")]
        public int? Mirostat { get; set; }

        [JsonProperty("mirostat_tau")]
        [JsonPropertyName("mirostat_tau")]
        public float? Mirostat_tau { get; set; }

        [JsonProperty("mirostat_eta")]
        [JsonPropertyName("mirostat_eta")]
        public float? Mirostat_eta { get; set; }

        [JsonProperty("ignore_eos")]
        [JsonPropertyName("ignore_eos")]
        public bool Ignore_eos { get; set; }

        [JsonProperty("dynatemp_range")]
        [JsonPropertyName("dynatemp_range")]
        public float? Dynatemp_range { get; set; }

        [JsonProperty("dynatemp_exponent")]
        [JsonPropertyName("dynatemp_exponent")]
        public float? Dynatemp_exponent { get; set; }

        [JsonProperty("xtc_probability")]
        [JsonPropertyName("xtc_probability")]
        public float? Xtc_probability { get; set; }

        [JsonProperty("xtc_threshold")]
        [JsonPropertyName("xtc_threshold")]
        public float? Xtc_threshold { get; set; }

        [JsonProperty("dry_multiplier")]
        [JsonPropertyName("dry_multiplier")]
        public float? Dry_multiplier { get; set; }

        [JsonProperty("dry_base")]
        [JsonPropertyName("dry_base")]
        public float? Dry_base { get; set; }

        [JsonProperty("dry_allowed_length")]
        [JsonPropertyName("dry_allowed_length")]
        public int? Dry_allowed_length { get; set; }

        [JsonProperty("dry_penalty_last_n")]
        [JsonPropertyName("dry_penalty_last_n")]
        public int? Dry_penalty_last_n { get; set; }

        [JsonProperty("dry_sequence_breakers")]
        [JsonPropertyName("dry_sequence_breakers")]
        public List<string>? Dry_sequence_breakers { get; set; }

        public virtual void ImportFromGenerationInput(GenerationInput input)
        {
            Top_k = input.Top_k;
            Min_p = (float)input.Min_p;
            Typical_p = (float)input.Typical;
            Repeat_last_n = input.Rep_pen_range;
            Mirostat = (int)input.Mirostat;
            Mirostat_tau = (float)input.Mirostat_tau;
            Mirostat_eta = (float)input.Mirostat_eta;
            Ignore_eos = input.Bypass_eos;
            Dynatemp_range = (float)input.Dynatemp_range;
            Dynatemp_exponent = (float)input.Dynatemp_exponent;
            Xtc_probability = (float)input.Xtc_probability;
            Xtc_threshold = (float)input.Xtc_threshold;
            Dry_multiplier = (float)input.Dry_multiplier;
            Dry_base = (float)input.Dry_base;
            Dry_allowed_length = input.Dry_allowed_length;
            Dry_sequence_breakers = input.Dry_sequence_breakers is not null ? [.. input.Dry_sequence_breakers] : null;
            Dry_penalty_last_n = input.Rep_pen_range;
        }
    }

    /// <summary>
    /// Request body for llama.cpp's native POST /completion endpoint.
    /// Call <see cref="ImportFromGenerationInput"/> to populate from a <see cref="GenerationInput"/>.
    /// Add any additional llama.cpp-specific fields directly to this class.
    /// </summary>
    public class LlamaCppCompletionRequest : LlamaCppAdvancedSampler
    {
        [JsonProperty("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonProperty("n_predict")]
        public int? N_predict { get; set; }

        [JsonProperty("temperature")]
        public float? Temperature { get; set; }

        [JsonProperty("top_p")]
        public float? Top_p { get; set; }

        [JsonProperty("repeat_penalty")]
        public float? Repeat_penalty { get; set; }

        [JsonProperty("seed")]
        public long? Seed { get; set; }

        [JsonProperty("stop")]
        public ICollection<string>? Stop { get; set; }

        [JsonProperty("grammar")]
        public string? grammar { get; set; }

        [JsonProperty("stream")]
        public bool Stream { get; set; } = false;

        [JsonProperty("cache_prompt")]
        public bool Cache_prompt { get; set; } = true;

        /// <summary>
        /// Maps all fields common to both <see cref="GenerationInput"/> and the llama.cpp /completion API.
        /// To add more llama.cpp-specific fields, extend this method or set them directly after calling it.
        /// </summary>
        public override void ImportFromGenerationInput(GenerationInput input)
        {
            base.ImportFromGenerationInput(input);
            Prompt = input.Prompt;
            N_predict = input.Max_length;
            Temperature = (float)input.Temperature;
            Top_p = (float)input.Top_p;
            Repeat_penalty = (float)input.Rep_pen;
            Seed = input.Sampler_seed == -1 ? LLMEngine.RNG.Next(int.MaxValue) : input.Sampler_seed;
            Stop = input.Stop_sequence;
            grammar = LLMEngine.CompletionAPIType == CompletionType.Text ? input.Grammar : null;
        }
    }

    /// <summary>
    /// Response from llama.cpp's native POST /completion endpoint (non-streaming).
    /// </summary>
    public class LlamaCppCompletionResponse
    {
        [JsonProperty("content")]
        public string Content { get; set; } = string.Empty;

        [JsonProperty("stop_type")]
        public string? Stop_type { get; set; }

        [JsonProperty("stopping_word")]
        public string? Stopping_word { get; set; }

        [JsonProperty("tokens_cached")]
        public int Tokens_cached { get; set; }

        [JsonProperty("tokens_evaluated")]
        public int Tokens_evaluated { get; set; }

        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        [JsonProperty("generation_settings")]
        public object? Generation_settings { get; set; }

        [JsonProperty("timings")]
        public object? Timings { get; set; }
    }

    /// <summary>
    /// A single SSE chunk from llama.cpp's streaming /completion endpoint.
    /// </summary>
    internal class LlamaCppStreamingChunk
    {
        [JsonProperty("content")]
        public string? Content { get; set; }

        [JsonProperty("stop")]
        public bool Stop { get; set; }

        [JsonProperty("stop_type")]
        public string? Stop_type { get; set; }
    }
}