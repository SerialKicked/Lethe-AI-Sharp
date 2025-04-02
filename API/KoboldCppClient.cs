#pragma warning disable 8603 // Null returns
#pragma warning disable 8604 // Disable "CS8604 Possible null reference argument for parameter"
#pragma warning disable 8618 // Disable "CS8618 Non-nullable field is uninitialized"
#pragma warning disable 8625 // Disable "CS8625 Cannot convert null literal to non-nullable reference type"
#pragma warning disable 8765 // Disable "CS8765 Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes)."

using Newtonsoft.Json;
using System;
using System.Net.Http.Headers;
using System.Text;


namespace AIToolkit.API
{
    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KoboldCppClient 
    {
        public bool ReadResponseAsString { get; set; }

        /// <summary>
        /// Maximum number of retry attempts for failed requests (0 = no retries)
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Base delay between retry attempts in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 500;

        /// <summary>
        /// Whether to use exponential backoff for retries
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// HTTP status codes that should trigger a retry
        /// </summary>
        public HashSet<int> RetryStatusCodes { get; } = new HashSet<int> { 408, 429, 500, 502, 503, 504 };

        public string BaseUrl
        {
            get { return _baseUrl; }
            set
            {
                _baseUrl = value;
                if (!string.IsNullOrEmpty(_baseUrl) && !_baseUrl.EndsWith("/"))
                    _baseUrl += '/';
            }
        }

        public JsonSerializerSettings JsonSerializerSettings => _settings.Value;

        public event EventHandler<TextStreamingEvenArg> StreamingMessageReceived;

        protected struct ObjectResponseResult<T>
        {
            public ObjectResponseResult(T responseObject, string responseText)
            {
                this.Object = responseObject;
                this.Text = responseText;
            }

            public T Object { get; }

            public string Text { get; }
        }

        private string _baseUrl = string.Empty;

        // Task-specific clients
        private HttpClient _httpClient;

        private static Lazy<JsonSerializerSettings> _settings = new Lazy<JsonSerializerSettings>(CreateSerializerSettings, true);

        protected virtual void OnStreamingMessageReceived(TextStreamingEvenArg e) => StreamingMessageReceived?.Invoke(this, e);

        public KoboldCppClient(HttpClient httpClient, int maxRetryAttempts = 3, int retryDelayMs = 500)
        {
            BaseUrl = "/";
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.Timeout = TimeSpan.FromMinutes(2);

            MaxRetryAttempts = maxRetryAttempts;
            RetryDelayMs = retryDelayMs;
        }

        private static JsonSerializerSettings CreateSerializerSettings()
        {
            var settings = new JsonSerializerSettings();
            return settings;
        }

        #region Core Internal Functions

        private async Task<T> SendRequestAsync<T>(HttpClient selectclient, HttpMethod method, string endpoint,
            object body = null, CancellationToken cancellationToken = default)
        {
            var client = selectclient;
            int attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    using var request = new HttpRequestMessage(method, new Uri(_baseUrl + endpoint, UriKind.RelativeOrAbsolute));

                    if (body != null)
                    {
                        var json = JsonConvert.SerializeObject(body, JsonSerializerSettings);
                        var content = new StringContent(json);
                        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                        request.Content = content;
                    }

                    request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    var status = (int)response.StatusCode;
                    if (status == 200)
                    {
                        var objectResponse = await ReadObjectResponseAsync<T>(response, new Dictionary<string, IEnumerable<string>>(), cancellationToken);
                        return objectResponse.Object;
                    }
                    else if (status == 503)
                    {
                        var objectResponse = await ReadObjectResponseAsync<ServerBusyError>(response, new Dictionary<string, IEnumerable<string>>(), cancellationToken);
                        if (objectResponse.Object == null)
                        {
                            throw new ApiException("Response was null which was not expected.", status, null, new Dictionary<string, IEnumerable<string>>(), null);
                        }

                        // Handle 503 with retry if configured
                        if (attempt <= MaxRetryAttempts && RetryStatusCodes.Contains(status))
                        {
                            await DelayForRetryAsync(attempt);
                            continue;
                        }

                        throw new ApiException<ServerBusyError>("Server is busy", status, objectResponse.Text,
                            new Dictionary<string, IEnumerable<string>>(), objectResponse.Object, null);
                    }
                    // For other status codes that should be retried
                    else if (attempt <= MaxRetryAttempts && RetryStatusCodes.Contains(status))
                    {
                        await DelayForRetryAsync(attempt);
                        continue;
                    }

                    var responseData = response.Content == null ? null : await response.Content.ReadAsStringAsync();
                    throw new ApiException($"HTTP status code {status} was not expected.", status, responseData, new Dictionary<string, IEnumerable<string>>(), null);
                }
                catch (HttpRequestException ex) when (attempt <= MaxRetryAttempts)
                {
                    // Network-level exceptions (connection refused, etc.)
                    await DelayForRetryAsync(attempt);

                    // If this was the last attempt, rethrow
                    if (attempt == MaxRetryAttempts)
                        throw new ApiException("Request failed after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt <= MaxRetryAttempts)
                {
                    // Timeout exceptions
                    await DelayForRetryAsync(attempt);

                    // If this was the last attempt, rethrow
                    if (attempt == MaxRetryAttempts)
                        throw new ApiException("Request timed out after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
            }
        }

        /// <summary>
        /// Calculates and waits for the appropriate delay between retry attempts
        /// </summary>
        private async Task DelayForRetryAsync(int attempt)
        {
            int delayMs = RetryDelayMs;

            if (UseExponentialBackoff)
            {
                // Simple exponential backoff: delay * 2^(attempt-1)
                delayMs = (int)(RetryDelayMs * Math.Pow(2, attempt - 1));

                // Add some jitter (±20% randomization) to avoid thundering herd
                var random = new Random();
                double jitter = 0.8 + (random.NextDouble() * 0.4); // 0.8 to 1.2
                delayMs = (int)(delayMs * jitter);

                // Cap at 30 seconds max delay
                delayMs = Math.Min(delayMs, 30000);
            }

            await Task.Delay(delayMs);
        }

        #endregion

        #region KoboldCpp API - Text Model Operations

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieve the current max context length setting value that horde sees
        /// </summary>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<MaxContextLengthSetting> ContextLengthAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<MaxContextLengthSetting>(_httpClient, HttpMethod.Get, "api/v1/config/max_context_length", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieve the actual max context length setting value set from the launcher
        /// </summary>
        /// <remarks>
        /// Retrieve the actual max context length setting value set from the launcher
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<MaxContextLengthSetting> TrueMaxContextLengthAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<MaxContextLengthSetting>(_httpClient, HttpMethod.Get, "api/extra/true_max_context_length", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Generate text with a specified prompt
        /// </summary>
        /// <remarks>
        /// Generates text given a prompt and generation settings.
        /// <br/>
        /// <br/>Unspecified values are set to defaults.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<GenerationOutput> GenerateAsync(GenerationInput body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<GenerationOutput>(_httpClient, HttpMethod.Post, "api/v1/generate", cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Generate text with a specified prompt. SSE streamed results. Use StreamingMessageReceived event to intercept
        /// </summary>
        public virtual async Task GenerateTextStreamAsync(GenerationInput body, CancellationToken cancellationToken = default)
        {
            var client_ = _httpClient;
            var urlBuilder_ = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
            urlBuilder_.Append("api/extra/generate/stream");
            var url_ = urlBuilder_.ToString();

            var request = new HttpRequestMessage(HttpMethod.Get, url_);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            var json_ = JsonConvert.SerializeObject(body, JsonSerializerSettings);
            var content_ = new StringContent(json_);
            content_.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
            request.Content = content_;
            request.Method = new HttpMethod("POST");
            //request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("application/json"));

            using (var response = await client_.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new System.IO.StreamReader(stream))
                {
                    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync().ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(line) && line.StartsWith("data:"))
                        {
                            var data = line.Substring(5).Trim();
                            OnStreamingMessageReceived(new TextStreamingEvenArg(data));
                        }
                    }
                }
            }
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Current KoboldAI United API version
        /// </summary>
        /// <remarks>
        /// Returns the matching *KoboldAI* (United) version of the API that you are currently using. This is not the same as the KoboldCpp API version - this is used to feature match against KoboldAI United.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KCBasicResult> VersionAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KCBasicResult>(_httpClient, HttpMethod.Get, "api/v1/info/version", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieve the current model string.
        /// </summary>
        /// <remarks>
        /// Gets the current model display name.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KCBasicResult> ModelAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KCBasicResult>(_httpClient, HttpMethod.Get, "api/v1/model", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieve the KoboldCpp backend version
        /// </summary>
        /// <remarks>
        /// Retrieve the KoboldCpp backend version
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KCExtraResult> ExtraVersionAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KCExtraResult>(_httpClient, HttpMethod.Get, "api/extra/version", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieve the KoboldCpp recent performance information
        /// </summary>
        /// <remarks>
        /// Retrieve the KoboldCpp recent performance information
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KcppPerf> PerfAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KcppPerf>(_httpClient, HttpMethod.Get, "api/extra/perf", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Poll the incomplete results of the currently ongoing text generation.
        /// </summary>
        /// <remarks>
        /// Poll the incomplete results of the currently ongoing text generation. Will not work when multiple requests are in queue.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<GenerationOutput> CheckGETAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<GenerationOutput>(_httpClient, HttpMethod.Get, "api/extra/generate/check", cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Poll the incomplete results of the currently ongoing text generation. Supports multiuser mode.
        /// </summary>
        /// <remarks>
        /// Poll the incomplete results of the currently ongoing text generation. A unique genkey previously submitted allows polling even in multiuser mode.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<GenerationOutput> CheckPOSTAsync(GenkeyData body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<GenerationOutput>(_httpClient, HttpMethod.Post, "api/extra/generate/check", body, cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Counts the number of tokens in a string.
        /// </summary>
        /// <remarks>
        /// Counts the number of tokens in a string.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KcppValueResult> TokencountAsync(KcppPrompt body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KcppValueResult>(_httpClient, HttpMethod.Post, "api/extra/tokencount", body, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Count the number of tokens in a given string (synchronous version)
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public KcppValueResult TokencountSync(KcppPrompt body)
        {
            // Using a new task and ConfigureAwait(false) to avoid deadlocks
            return Task.Run(() => TokencountAsync(body)).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Aborts the currently ongoing text generation.
        /// </summary>
        /// <remarks>
        /// Aborts the currently ongoing text generation. Does not work when multiple requests are in queue.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KcppResponse> AbortAsync(GenkeyData body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<KcppResponse>(_httpClient, HttpMethod.Post, "api/extra/abort", body, cancellationToken: cancellationToken);
        }

        #endregion

        #region KoboldCpp API - Extra API

        /// <summary>
        /// Send a DuckDuckGo API web search query to the server
        /// </summary>
        /// <param name="body"></param>
        /// <returns></returns>
        public virtual async Task<WebQueryFullResponse> WebQueryAsync(WebQuery body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<WebQueryFullResponse>(_httpClient, HttpMethod.Post, "api/extra/websearch", body, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Creates text-to-speech audio from input text.
        /// </summary>
        /// <param name="body">The input text and voice for TTS.</param>
        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <returns>The generated audio as a byte array.</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<byte[]> TextToSpeechAsync(TextToSpeechInput body, CancellationToken cancellationToken = default)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUrl + "api/extra/tts", UriKind.RelativeOrAbsolute));

                    var json = JsonConvert.SerializeObject(body, JsonSerializerSettings);
                    var content = new StringContent(json);
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
                    request.Content = content;

                    request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse("audio/wav"));

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    var status = (int)response.StatusCode;
                    if (status == 200)
                    {
                        var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                        using var memoryStream = new MemoryStream();
                        await responseStream.CopyToAsync(memoryStream).ConfigureAwait(false);
                        return memoryStream.ToArray();
                    }
                    // For other status codes that should be retried
                    else if (attempt <= MaxRetryAttempts && RetryStatusCodes.Contains(status))
                    {
                        await DelayForRetryAsync(attempt);
                        continue;
                    }

                    var responseData = response.Content == null ? null : await response.Content.ReadAsStringAsync();
                    throw new ApiException($"HTTP status code {status} was not expected.", status, responseData, new Dictionary<string, IEnumerable<string>>(), null);
                }
                catch (HttpRequestException ex) when (attempt <= MaxRetryAttempts)
                {
                    // Network-level exceptions (connection refused, etc.)
                    await DelayForRetryAsync(attempt);

                    // If this was the last attempt, rethrow
                    if (attempt == MaxRetryAttempts)
                        throw new ApiException("Request failed after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt <= MaxRetryAttempts)
                {
                    // Timeout exceptions
                    await DelayForRetryAsync(attempt);

                    // If this was the last attempt, rethrow
                    if (attempt == MaxRetryAttempts)
                        throw new ApiException("Request timed out after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
            }
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Uses Whisper to perform a Speech-To-Text transcription.
        /// </summary>
        /// <remarks>
        /// Uses Whisper to perform a Speech-To-Text transcription.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<KcppValueResult> TranscribeAsync(KcppAudioData body, CancellationToken cancellationToken = default)
        {
            if (body == null)
                throw new System.ArgumentNullException("body");

            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    var json_ = JsonConvert.SerializeObject(body, JsonSerializerSettings);
                    var content_ = new System.Net.Http.StringContent(json_);
                    content_.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json");
                    request_.Content = content_;
                    request_.Method = new System.Net.Http.HttpMethod("POST");
                    request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                    var urlBuilder_ = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                    // Operation Path: "api/extra/transcribe"
                    urlBuilder_.Append("api/extra/transcribe");

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
                        foreach (var item_ in response_.Headers)
                            headers_[item_.Key] = item_.Value;
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }

                        var status_ = (int)response_.StatusCode;
                        if (status_ == 200)
                        {
                            var objectResponse_ = await ReadObjectResponseAsync<KcppValueResult>(response_, headers_, cancellationToken).ConfigureAwait(false);
                            if (objectResponse_.Object == null)
                            {
                                throw new ApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
                            }
                            return objectResponse_.Object;
                        }
                        else
                        {
                            var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                            throw new ApiException("The HTTP status code of the response was not expected (" + status_ + ").", status_, responseData_, headers_, null);
                        }
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }

        #endregion

        #region KoboldCpp API - Image Generation API

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Generates an image from a text prompt
        /// </summary>
        /// <remarks>
        /// Generates an image from a text prompt, and returns a base64 encoded png.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<ImageResponse> Txt2imgAsync(KcppImagePrompt body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<ImageResponse>(_httpClient, HttpMethod.Post, "sdapi/v1/txt2img", body, cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Transforms an existing image into a new image
        /// </summary>
        /// <remarks>
        /// Transforms an existing image into a new image, guided by a text prompt, and returns a base64 encoded png.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<Img2ImgResponse> Img2imgAsync(KcppImg2ImgQuery body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<Img2ImgResponse>(_httpClient, HttpMethod.Post, "sdapi/v1/img2img", body, cancellationToken: cancellationToken);
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Generates a short text caption describing an image
        /// </summary>
        /// <remarks>
        /// Generates a short text caption describing an image.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<CaptionedImageResponse> InterrogateAsync(KcppCaptionQuery body, CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<CaptionedImageResponse>(_httpClient, HttpMethod.Post, "sdapi/v1/interrogate", body, cancellationToken: cancellationToken);
        }

        #endregion

        #region KoboldCpp API - Basic OpenAI Clone API

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Generates text continuations given a prompt. Please refer to OpenAI documentation
        /// </summary>
        /// <remarks>
        /// Generates text continuations given a prompt.
        /// <br/>
        /// <br/>This is an OpenAI compatibility endpoint.
        /// <br/>
        /// <br/> Please refer to OpenAI documentation at [https://platform.openai.com/docs/api-reference/completions](https://platform.openai.com/docs/api-reference/completions)
        /// </remarks>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task CompletionsAsync(CancellationToken cancellationToken = default)
        {
            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    request_.Content = new System.Net.Http.StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
                    request_.Method = new System.Net.Http.HttpMethod("POST");

                    var urlBuilder_ = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                    // Operation Path: "v1/completions"
                    urlBuilder_.Append("v1/completions");

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
                        foreach (var item_ in response_.Headers)
                            headers_[item_.Key] = item_.Value;
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }

                        var status_ = (int)response_.StatusCode;
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Generates a response from a list of messages. Please refer to OpenAI documentation
        /// </summary>
        /// <remarks>
        /// Given a list of messages comprising a conversation, the model will return a response.
        /// <br/>
        /// <br/> This is an OpenAI compatibility endpoint.
        /// <br/>
        /// <br/> Please refer to OpenAI documentation at [https://platform.openai.com/docs/api-reference/chat](https://platform.openai.com/docs/api-reference/chat)
        /// </remarks>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task Completions2Async(CancellationToken cancellationToken = default)
        {
            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    request_.Content = new System.Net.Http.StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
                    request_.Method = new System.Net.Http.HttpMethod("POST");

                    var urlBuilder_ = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                    // Operation Path: "v1/chat/completions"
                    urlBuilder_.Append("v1/chat/completions");

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
                        foreach (var item_ in response_.Headers)
                            headers_[item_.Key] = item_.Value;
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }

                        var status_ = (int)response_.StatusCode;
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// List and describe the various models available in the API. Please refer to OpenAI documentation
        /// </summary>
        /// <remarks>
        /// List and describe the various models available in the API.
        /// <br/>
        /// <br/> This is an OpenAI compatibility endpoint.
        /// <br/>
        /// <br/> Please refer to OpenAI documentation at [https://platform.openai.com/docs/api-reference/models](https://platform.openai.com/docs/api-reference/models)
        /// </remarks>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task ModelsAsync(CancellationToken cancellationToken = default)
        {
            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    request_.Method = new System.Net.Http.HttpMethod("GET");

                    var urlBuilder_ = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                    // Operation Path: "v1/models"
                    urlBuilder_.Append("v1/models");

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
                        foreach (var item_ in response_.Headers)
                            headers_[item_.Key] = item_.Value;
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }


                        var status_ = (int)response_.StatusCode;
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Transcribes a wav file with speech to text using loaded Whisper model. Please refer to OpenAI documentation
        /// </summary>
        /// <remarks>
        /// Transcribes a wav file with speech to text using loaded Whisper model.
        /// <br/>
        /// <br/> This is an OpenAI compatibility endpoint.
        /// <br/>
        /// <br/> Please refer to OpenAI documentation at [https://platform.openai.com/docs/api-reference/audio/createTranscription](https://platform.openai.com/docs/api-reference/audio/createTranscription)
        /// </remarks>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task TranscriptionsAsync(CancellationToken cancellationToken = default)
        {
            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {
                    request_.Content = new System.Net.Http.StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
                    request_.Method = new System.Net.Http.HttpMethod("POST");

                    var urlBuilder_ = new System.Text.StringBuilder();
                    if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                    // Operation Path: "v1/audio/transcriptions"
                    urlBuilder_.Append("v1/audio/transcriptions");

                    var url_ = urlBuilder_.ToString();
                    request_.RequestUri = new System.Uri(url_, System.UriKind.RelativeOrAbsolute);

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {
                        var headers_ = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IEnumerable<string>>();
                        foreach (var item_ in response_.Headers)
                            headers_[item_.Key] = item_.Value;
                        if (response_.Content != null && response_.Content.Headers != null)
                        {
                            foreach (var item_ in response_.Content.Headers)
                                headers_[item_.Key] = item_.Value;
                        }

                        var status_ = (int)response_.StatusCode;
                    }
                    finally
                    {
                        if (disposeResponse_)
                            response_.Dispose();
                    }
                }
            }
            finally
            {
                if (disposeClient_)
                    client_.Dispose();
            }
        }

        #endregion

        /// <param name="cancellationToken">A cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
        /// <summary>
        /// Retrieves the KoboldCpp preloaded story
        /// </summary>
        /// <remarks>
        /// Retrieves the KoboldCpp preloaded story, --preloadstory configures a prepared story json save file to be hosted on the server, which frontends (such as KoboldAI Lite) can access over the API.
        /// </remarks>
        /// <returns>Successful request</returns>
        /// <exception cref="ApiException">A server side error occurred.</exception>
        public virtual async Task<PreloadedStoryResponse> PreloadstoryAsync(CancellationToken cancellationToken = default)
        {
            return await SendRequestAsync<PreloadedStoryResponse>(_httpClient, HttpMethod.Get, "api/extra/preloadstory", cancellationToken: cancellationToken);
        }

        protected virtual async Task<ObjectResponseResult<T>> ReadObjectResponseAsync<T>(HttpResponseMessage response, IReadOnlyDictionary<string, IEnumerable<string>> headers, CancellationToken cancellationToken)
        {
            if (response == null || response.Content == null)
            {
                return new ObjectResponseResult<T>(default(T), string.Empty);
            }

            if (ReadResponseAsString)
            {
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                try
                {
                    var test = JsonConvert.DeserializeObject<T>(responseText, JsonSerializerSettings);
                    // var typedBody = JsonConvert.DeserializeObject<T>(responseText, JsonSerializerSettings);
                    return new ObjectResponseResult<T>(test, responseText);
                }
                catch (JsonException exception)
                {
                    var message = "Could not deserialize the response body string as " + typeof(T).FullName + ".";
                    throw new ApiException(message, (int)response.StatusCode, responseText, headers, exception);
                }
            }
            else
            {
                try
                {
                    using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var streamReader = new System.IO.StreamReader(responseStream))
                    using (var jsonTextReader = new JsonTextReader(streamReader))
                    {
                        var serializer = JsonSerializer.Create(JsonSerializerSettings);
                        var typedBody = serializer.Deserialize<T>(jsonTextReader);
                        return new ObjectResponseResult<T>(typedBody, string.Empty);
                    }
                }
                catch (JsonException exception)
                {
                    var message = "Could not deserialize the response body stream as " + typeof(T).FullName + ".";
                    throw new ApiException(message, (int)response.StatusCode, string.Empty, headers, exception);
                }
            }
        }
    }


    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class BasicError
    {
        [JsonProperty("msg", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Msg { get; set; }

        [JsonProperty("type", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Type { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static BasicError FromJson(string data)
        {

            return JsonConvert.DeserializeObject<BasicError>(data, new JsonSerializerSettings());

        }

    }

    public class KCExtraResult
    {
        public string result { get; set; }
        public string version { get; set; }
        public bool txt2img { get; set; }
        public bool vision { get; set; }
        public bool transcribe { get; set; }
}

    public class KCBasicResult
    {
        public string Result { get; set; }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class BasicResult
    {
        [JsonProperty("result", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required]
        public BasicResultInner Result { get; set; } = new BasicResultInner();

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static BasicResult FromJson(string data)
        {

            return JsonConvert.DeserializeObject<BasicResult>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class BasicResultInner
    {
        [JsonProperty("result", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Result { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static BasicResultInner FromJson(string data)
        {

            return JsonConvert.DeserializeObject<BasicResultInner>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class GenerationInput
    {
        /// <summary>
        /// Maximum number of tokens to send to the model.
        /// </summary>
        [JsonProperty("max_context_length", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Max_context_length { get; set; }

        /// <summary>
        /// Number of tokens to generate.
        /// </summary>
        [JsonProperty("max_length", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Max_length { get; set; }

        /// <summary>
        /// This is the submission.
        /// </summary>
        [JsonProperty("prompt", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Prompt { get; set; }

        /// <summary>
        /// Base repetition penalty value.
        /// </summary>
        [JsonProperty("rep_pen", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(1D, double.MaxValue)]
        public double Rep_pen { get; set; }

        /// <summary>
        /// Repetition penalty range.
        /// </summary>
        [JsonProperty("rep_pen_range", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
        public int Rep_pen_range { get; set; }

        /// <summary>
        /// Sampler order to be used. If N is the length of this array, then N must be greater than or equal to 6 and the array must be a permutation of the first N non-negative integers.
        /// </summary>
        [JsonProperty("sampler_order", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.MinLength(6)]
        public ICollection<int> Sampler_order { get; set; }

        /// <summary>
        /// RNG seed to use for sampling. If not specified, the global RNG will be used.
        /// </summary>
        [JsonProperty("sampler_seed", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(1, 999999)]
        public int Sampler_seed { get; set; }

        /// <summary>
        /// An array of string sequences where the API will stop generating further tokens. The returned text WILL contain the stop sequence.
        /// </summary>
        [JsonProperty("stop_sequence", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<string> Stop_sequence { get; set; }

        /// <summary>
        /// Temperature value.
        /// </summary>
        [JsonProperty("temperature", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Temperature { get; set; }

        /// <summary>
        /// Tail free sampling value.
        /// </summary>
        [JsonProperty("tfs", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, 1D)]
        public double Tfs { get; set; }

        /// <summary>
        /// Top-a sampling value.
        /// </summary>
        [JsonProperty("top_a", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Top_a { get; set; }

        /// <summary>
        /// Top-k sampling value.
        /// </summary>
        [JsonProperty("top_k", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
        public int Top_k { get; set; }

        /// <summary>
        /// Top-p sampling value.
        /// </summary>
        [JsonProperty("top_p", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, 1D)]
        public double Top_p { get; set; }

        /// <summary>
        /// Min-p sampling value.
        /// </summary>
        [JsonProperty("min_p", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, 1D)]
        public double Min_p { get; set; }

        /// <summary>
        /// Typical sampling value.
        /// </summary>
        [JsonProperty("typical", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, 1D)]
        public double Typical { get; set; }

        /// <summary>
        /// If true, prevents the EOS token from being generated (Ban EOS).
        /// </summary>
        [JsonProperty("use_default_badwordsids", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Use_default_badwordsids { get; set; } = false;

        /// <summary>
        /// If greater than 0, uses dynamic temperature. Dynamic temperature range will be between Temp+Range and Temp-Range. If less or equal to 0 , uses static temperature.
        /// </summary>
        [JsonProperty("dynatemp_range", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Dynatemp_range { get; set; } = 0D;

        /// <summary>
        /// Modifies temperature behavior. If greater than 0 uses smoothing factor.
        /// </summary>
        [JsonProperty("smoothing_factor", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Smoothing_factor { get; set; } = 0D;

        /// <summary>
        /// Exponent used in dynatemp.
        /// </summary>
        [JsonProperty("dynatemp_exponent", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Dynatemp_exponent { get; set; } = 1D;

        /// <summary>
        /// KoboldCpp ONLY. Sets the mirostat mode, 0=disabled, 1=mirostat_v1, 2=mirostat_v2
        /// </summary>
        [JsonProperty("mirostat", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, 2D)]
        public double Mirostat { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. Mirostat tau value.
        /// </summary>
        [JsonProperty("mirostat_tau", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Mirostat_tau { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. Mirostat eta value.
        /// </summary>
        [JsonProperty("mirostat_eta", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Mirostat_eta { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. A unique genkey set by the user. When checking a polled-streaming request, use this key to be able to fetch pending text even if multiuser is enabled.
        /// </summary>
        [JsonProperty("genkey", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Genkey { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. A string containing the GBNF grammar to use.
        /// </summary>
        [JsonProperty("grammar", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Grammar { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. If true, retains the previous generation's grammar state, otherwise it is reset on new generation.
        /// </summary>
        [JsonProperty("grammar_retain_state", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Grammar_retain_state { get; set; } = false;

        /// <summary>
        /// KoboldCpp ONLY. If set, forcefully appends this string to the beginning of any submitted prompt text. If resulting context exceeds the limit, forcefully overwrites text from the beginning of the main prompt until it can fit. Useful to guarantee full memory insertion even when you cannot determine exact token count.
        /// </summary>
        [JsonProperty("memory", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Memory { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. If set, takes an array of base64 encoded strings, each one representing an image to be processed.
        /// </summary>
        [JsonProperty("images", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Images { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. If true, also removes detected stop_sequences from the output and truncates all text after them.
        /// </summary>
        [JsonProperty("trim_stop", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Trim_stop { get; set; } = false;

        /// <summary>
        /// KoboldCpp ONLY. If true, prints special tokens as text for GGUF models
        /// </summary>
        [JsonProperty("render_special", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Render_special { get; set; } = false;

        /// <summary>
        /// KoboldCpp ONLY. If true, allows EOS token to be generated, but does not stop generation. Not recommended unless you know what you are doing.
        /// </summary>
        [JsonProperty("bypass_eos", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Bypass_eos { get; set; } = false;

        /// <summary>
        /// An array of string sequences to remove from model vocab. All matching tokens with matching substrings are removed.
        /// </summary>
        [JsonProperty("banned_tokens", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<string> Banned_tokens { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. An dictionary of key-value pairs, which indicate the token IDs (int) and logit bias (float) to apply for that token. Up to 16 value can be provided.
        /// </summary>
        [JsonProperty("logit_bias", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public object Logit_bias { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. DRY multiplier value, 0 to disable.
        /// </summary>
        [JsonProperty("dry_multiplier", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Dry_multiplier { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. DRY base value.
        /// </summary>
        [JsonProperty("dry_base", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Dry_base { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. DRY allowed length value.
        /// </summary>
        [JsonProperty("dry_allowed_length", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0, int.MaxValue)]
        public int Dry_allowed_length { get; set; }

        /// <summary>
        /// An array of string sequence breakers for DRY.
        /// </summary>
        [JsonProperty("dry_sequence_breakers", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<string> Dry_sequence_breakers { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. XTC threshold.
        /// </summary>
        [JsonProperty("xtc_threshold", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Xtc_threshold { get; set; }

        /// <summary>
        /// KoboldCpp ONLY. XTC probability. Set to above 0 to enable XTC.
        /// </summary>
        [JsonProperty("xtc_probability", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [System.ComponentModel.DataAnnotations.Range(0D, double.MaxValue)]
        public double Xtc_probability { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static GenerationInput FromJson(string data)
        {

            return JsonConvert.DeserializeObject<GenerationInput>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class GenerationOutput
    {
        /// <summary>
        /// Array of generated outputs.
        /// </summary>
        [JsonProperty("results", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required]
        public ICollection<GenerationResult> Results { get; set; } = new System.Collections.ObjectModel.Collection<GenerationResult>();

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static GenerationOutput FromJson(string data)
        {

            return JsonConvert.DeserializeObject<GenerationOutput>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class GenerationResult
    {
        /// <summary>
        /// Generated output as plain text.
        /// </summary>
        [JsonProperty("text", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Text { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static GenerationResult FromJson(string data)
        {

            return JsonConvert.DeserializeObject<GenerationResult>(data, new JsonSerializerSettings());

        }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class MaxContextLengthSetting
    {
        [JsonProperty("value", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Range(8, int.MaxValue)]
        public int Value { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static MaxContextLengthSetting FromJson(string data)
        {

            return JsonConvert.DeserializeObject<MaxContextLengthSetting>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class MaxLengthSetting
    {
        [JsonProperty("value", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
        public int Value { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static MaxLengthSetting FromJson(string data)
        {

            return JsonConvert.DeserializeObject<MaxLengthSetting>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class ServerBusyError
    {
        [JsonProperty("detail", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required]
        public BasicError Detail { get; set; } = new BasicError();

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static ServerBusyError FromJson(string data)
        {

            return JsonConvert.DeserializeObject<ServerBusyError>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppValueResult
    {
        [JsonProperty("value", Required = Required.Always)]
        public int Value { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppValueResult FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppValueResult>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppVersion
    {
        [JsonProperty("result", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Result { get; set; }

        [JsonProperty("version", Required = Required.Always)]
        [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = true)]
        public string Version { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppVersion FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppVersion>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppPerf
    {
        /// <summary>
        /// Last processing time in seconds.
        /// </summary>
        [JsonProperty("last_process", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Last_process { get; set; }

        /// <summary>
        /// Last evaluation time in seconds.
        /// </summary>
        [JsonProperty("last_eval", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Last_eval { get; set; }

        /// <summary>
        /// Last token count.
        /// </summary>
        [JsonProperty("last_token_count", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Last_token_count { get; set; }

        /// <summary>
        /// Last generation seed used.
        /// </summary>
        [JsonProperty("last_seed", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Last_seed { get; set; }

        /// <summary>
        /// Total requests generated since startup.
        /// </summary>
        [JsonProperty("total_gens", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Total_gens { get; set; }

        /// <summary>
        /// Reason the generation stopped. INVALID=-1, OUT_OF_TOKENS=0, EOS_TOKEN_HIT=1, CUSTOM_STOPPER=2
        /// </summary>
        [JsonProperty("stop_reason", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Stop_reason { get; set; }

        /// <summary>
        /// Length of generation queue.
        /// </summary>
        [JsonProperty("queue", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Queue { get; set; }

        /// <summary>
        /// Status of backend, busy or idle.
        /// </summary>
        [JsonProperty("idle", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Idle { get; set; }

        /// <summary>
        /// Status of embedded horde worker. If it's too high, may have crashed.
        /// </summary>
        [JsonProperty("hordeexitcounter", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public int Hordeexitcounter { get; set; }

        /// <summary>
        /// Seconds that the server has been running for.
        /// </summary>
        [JsonProperty("uptime", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Uptime { get; set; }

        /// <summary>
        /// Seconds that the server has been running for.
        /// </summary>
        [JsonProperty("idletime", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Idletime { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppPerf FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppPerf>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class GenkeyData
    {
        /// <summary>
        /// A unique key used to identify this generation while it is in progress.
        /// </summary>
        [JsonProperty("genkey", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Genkey { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static GenkeyData FromJson(string data)
        {

            return JsonConvert.DeserializeObject<GenkeyData>(data, new JsonSerializerSettings());

        }
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppPrompt
    {
        /// <summary>
        /// The string to be tokenized.
        /// </summary>
        [JsonProperty("prompt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Prompt { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppPrompt FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppPrompt>(data, new JsonSerializerSettings());

        }

    }


    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppAudioData
    {
        /// <summary>
        /// Base64 respresentation of a 16-bit 16kHz wave file to be transcribed to text.
        /// </summary>
        [JsonProperty("audio_data", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Audio_data { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppAudioData FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppAudioData>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppImagePrompt
    {
        [JsonProperty("prompt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Prompt { get; set; }

        [JsonProperty("negative_prompt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Negative_prompt { get; set; }

        [JsonProperty("cfg_scale", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Cfg_scale { get; set; }

        [JsonProperty("steps", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Steps { get; set; }

        [JsonProperty("width", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Width { get; set; }

        [JsonProperty("height", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Height { get; set; }

        [JsonProperty("seed", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Seed { get; set; }

        [JsonProperty("sampler_name", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Sampler_name { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppImagePrompt FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppImagePrompt>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppImg2ImgQuery
    {
        [JsonProperty("prompt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Prompt { get; set; }

        [JsonProperty("negative_prompt", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Negative_prompt { get; set; }

        [JsonProperty("cfg_scale", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Cfg_scale { get; set; }

        [JsonProperty("steps", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Steps { get; set; }

        [JsonProperty("width", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Width { get; set; }

        [JsonProperty("height", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Height { get; set; }

        [JsonProperty("seed", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Seed { get; set; }

        [JsonProperty("sampler_name", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Sampler_name { get; set; }

        [JsonProperty("denoising_strength", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double Denoising_strength { get; set; }

        [JsonProperty("init_images", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public ICollection<object> Init_images { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppImg2ImgQuery FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppImg2ImgQuery>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppCaptionQuery
    {
        /// <summary>
        /// A base64 string containing the encoded PNG of the image.
        /// </summary>
        [JsonProperty("image", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Image { get; set; }

        /// <summary>
        /// Not used.
        /// </summary>
        [JsonProperty("model", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Model { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppCaptionQuery FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppCaptionQuery>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class KcppResponse
    {
        /// <summary>
        /// Whether the abort was successful.
        /// </summary>
        [JsonProperty("success", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool Success { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static KcppResponse FromJson(string data)
        {

            return JsonConvert.DeserializeObject<KcppResponse>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class ImageResponse
    {
        /// <summary>
        /// A base64 string containing the encoded PNG of the generated image.
        /// </summary>
        [JsonProperty("images", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Images { get; set; }

        /// <summary>
        /// Not used. Will be empty.
        /// </summary>
        [JsonProperty("parameters", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public object Parameters { get; set; }

        /// <summary>
        /// Not used. Will be empty.
        /// </summary>
        [JsonProperty("info", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Info { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static ImageResponse FromJson(string data)
        {

            return JsonConvert.DeserializeObject<ImageResponse>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class Img2ImgResponse
    {
        /// <summary>
        /// A base64 string containing the encoded PNG of the generated image.
        /// </summary>
        [JsonProperty("images", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Images { get; set; }

        /// <summary>
        /// Not used. Will be empty.
        /// </summary>
        [JsonProperty("parameters", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public object Parameters { get; set; }

        /// <summary>
        /// Not used. Will be empty.
        /// </summary>
        [JsonProperty("info", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Info { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static Img2ImgResponse FromJson(string data)
        {

            return JsonConvert.DeserializeObject<Img2ImgResponse>(data, new JsonSerializerSettings());

        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class CaptionedImageResponse
    {
        /// <summary>
        /// A short text description of the image.
        /// </summary>
        [JsonProperty("caption", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string Caption { get; set; }

        private IDictionary<string, object> _additionalProperties;

        [JsonExtensionData]
        public IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

        public string ToJson()
        {

            return JsonConvert.SerializeObject(this, new JsonSerializerSettings());

        }
        public static CaptionedImageResponse FromJson(string data)
        {
            return JsonConvert.DeserializeObject<CaptionedImageResponse>(data, new JsonSerializerSettings());
        }

    }

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class ApiException : System.Exception
    {
        public int StatusCode { get; private set; }

        public string Response { get; private set; }

        public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> Headers { get; private set; }

        public ApiException(string message, int statusCode, string response, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> headers, System.Exception innerException)
            : base(message + "\n\nStatus: " + statusCode + "\nResponse: \n" + ((response == null) ? "(null)" : response.Substring(0, response.Length >= 512 ? 512 : response.Length)), innerException)
        {
            StatusCode = statusCode;
            Response = response ?? string.Empty;
            Headers = headers;
        }

        public override string ToString()
        {
            return string.Format("HTTP Response: \n\n{0}\n\n{1}", Response, base.ToString());
        }
    }

    [System.CodeDom.Compiler.GeneratedCode("NSwag", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class ApiException<TResult> : ApiException
    {
        public TResult Result { get; private set; }

        public ApiException(string message, int statusCode, string response, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> headers, TResult result, System.Exception innerException)
            : base(message, statusCode, response, headers, innerException)
        {
            Result = result;
        }
    }

    public class StreamingTokenResponse
    {
        public string token { get; set; }
        public string finish_reason { get; set; }

        public static StreamingTokenResponse FromJson(string data)
        {

            return JsonConvert.DeserializeObject<StreamingTokenResponse>(data, new JsonSerializerSettings());

        }
    }

    public class TextStreamingEvenArg : EventArgs
    {
        public StreamingTokenResponse Data { get; }

        public TextStreamingEvenArg(string data)
        {
            Data = StreamingTokenResponse.FromJson(data);
        }
    }

    public class WebQuery
    {
        public string q { get; set; }
    }

    public class WebQuerySingleResponse
    {
        public string title { get; set; }
        public string url { get; set; }
        public string desc { get; set; }
        public string content { get; set; }
    }

    public class WebQueryFullResponse : List<WebQuerySingleResponse>;

    public class TextToSpeechInput
    {
        [JsonProperty("input", Required = Required.Always)]
        public string Input { get; set; }

        [JsonProperty("voice", Required = Required.Always)]
        public string Voice { get; set; }
    }

    public class PreloadedStoryResponse
    {
        public string prompt { get; set; }
        public string memory { get; set; }
        public string authorsnote { get; set; }
        public ICollection<object> actions { get; set; }
    }
}

#pragma warning restore 8603 // Null returns
#pragma warning restore 8604 // Disable "CS8604 Possible null reference argument for parameter"
#pragma warning restore 8618 // Disable "CS8618 Non-nullable field is uninitialized"
#pragma warning restore 8625 // Disable "CS8625 Cannot convert null literal to non-nullable reference type"
#pragma warning restore 8765 // Disable "CS8765 Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes)."