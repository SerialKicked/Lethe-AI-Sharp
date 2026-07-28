using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.CodeDom.Compiler;
using System.Net.Http.Headers;

namespace LetheAISharp.API
{
    [GeneratedCode("NSwag", "14.1.0.0 (NJsonSchema v11.0.2.0 (Newtonsoft.Json v13.0.0.0))")]
    public class HttpClientBase
    {
        protected struct ObjectResponseResult<T>
        {
            public ObjectResponseResult(T? responseObject, string responseText)
            {
                this.Object = responseObject;
                this.Text = responseText;
            }

            public T? Object { get; }

            public string Text { get; }
        }

        protected static Lazy<JsonSerializerSettings> _settings = new Lazy<JsonSerializerSettings>(CreateSerializerSettings, true);

        protected string _baseUrl = string.Empty;

        // Task-specific clients
        protected HttpClient? _httpClient;

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

        /// <summary>
        /// Maximum number of retry attempts for failed requests (0 = no retries)
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;
        public bool ReadResponseAsString { get; set; } = true;

        /// <summary>
        /// Base delay between retry attempts in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 500;

        /// <summary>
        /// HTTP status codes that should trigger a retry
        /// </summary>
        public HashSet<int> RetryStatusCodes { get; } = new HashSet<int> { 404, 408, 429, 500, 502, 503, 504 };

        /// <summary>
        /// Whether to use exponential backoff for retries
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        protected static JsonSerializerSettings CreateSerializerSettings()
        {
            var settings = new JsonSerializerSettings();
            return settings;
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
                    LLMEngine.Logger?.LogError(exception, "Could not deserialize the response body string as {TypeName}. Response: {ResponseText}", typeof(T).FullName, responseText);
                    return new ObjectResponseResult<T>(default(T), string.Empty);
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
                    LLMEngine.Logger?.LogError(exception, "Could not deserialize the response body stream as {TypeName}.", typeof(T).FullName);
                    return new ObjectResponseResult<T>(default(T), string.Empty);
                }
            }
        }

        /// <summary>
        /// Calculates and waits for the appropriate delay between retry attempts
        /// </summary>
        protected async Task DelayForRetryAsync(int attempt)
        {
            int delayMs = RetryDelayMs;

            if (UseExponentialBackoff)
            {
                // Simple exponential backoff: delay * 2^(attempt-1)
                delayMs = (int)(RetryDelayMs * Math.Pow(2, attempt - 1));

                // Add some jitter (±20% randomization) to avoid thundering herd
                double jitter = 0.8 + (LLMEngine.RNG.NextDouble() * 0.4); // 0.8 to 1.2
                delayMs = (int)(delayMs * jitter);

                // Cap at 30 seconds max delay
                delayMs = Math.Min(delayMs, 30000);
            }

            await Task.Delay(delayMs).ConfigureAwait(false);
        }


        /// <summary>
        /// Sends a Post/Get request and returns the response
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="selectclient">HTTP CLient to use</param>
        /// <param name="method">POST or GET method</param>
        /// <param name="endpoint">API Endpoint to hit</param>
        /// <param name="body">message to send for POST requests</param>
        /// <param name="cancellationToken">cancel token thing</param>
        /// <param name="maxRetryAttempts">
        /// Overrides <see cref="MaxRetryAttempts"/> for this call. Pass 0 for endpoints where a failure is
        /// a permanent condition (an unsupported endpoint, or a payload the server rejects) rather than a
        /// transient one, so callers with a fallback path are not billed the full backoff every time.
        /// </param>
        /// <returns></returns>
        /// <exception cref="ApiException"></exception>
        /// <exception cref="ApiException{ServerBusyError}"></exception>
        protected async Task<T> SendRequestAsync<T>(HttpClient selectclient, HttpMethod method, string endpoint, object? body = null, CancellationToken cancellationToken = default, int? maxRetryAttempts = null)
        {
            var client = selectclient;
            var retryLimit = maxRetryAttempts ?? MaxRetryAttempts;
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

                    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                    var status = (int)response.StatusCode;
                    if (status == 200)
                    {
                        var objectResponse = await ReadObjectResponseAsync<T>(response, new Dictionary<string, IEnumerable<string>>(), cancellationToken).ConfigureAwait(false);
                        return objectResponse.Object!;
                    }
                    else if (status == 503)
                    {
                        var objectResponse = await ReadObjectResponseAsync<ServerBusyError>(response, new Dictionary<string, IEnumerable<string>>(), cancellationToken).ConfigureAwait(false);
                        if (objectResponse.Object == null)
                        {
                            throw new ApiException("Response was null which was not expected.", status, string.Empty, new Dictionary<string, IEnumerable<string>>(), null);
                        }

                        // Handle 503 with retry if configured
                        if (attempt <= retryLimit && RetryStatusCodes.Contains(status))
                        {
                            await DelayForRetryAsync(attempt).ConfigureAwait(false);
                            continue;
                        }

                        throw new ApiException<ServerBusyError>("Server is busy", status, objectResponse.Text,
                            new Dictionary<string, IEnumerable<string>>(), objectResponse.Object, null);
                    }
                    // For other status codes that should be retried
                    else if (attempt <= retryLimit && RetryStatusCodes.Contains(status))
                    {
                        await DelayForRetryAsync(attempt).ConfigureAwait(false);
                        continue;
                    }

                    var responseData = response.Content == null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw new ApiException($"HTTP status code {status} was not expected.", status, responseData, new Dictionary<string, IEnumerable<string>>(), null);
                }
                catch (HttpRequestException ex) when (attempt <= retryLimit)
                {
                    // Network-level exceptions (connection refused, etc.)
                    await DelayForRetryAsync(attempt).ConfigureAwait(false);

                    // If this was the last attempt, rethrow
                    if (attempt == retryLimit)
                        throw new ApiException("Request failed after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && attempt <= retryLimit)
                {
                    // Timeout exceptions
                    await DelayForRetryAsync(attempt).ConfigureAwait(false);

                    // If this was the last attempt, rethrow
                    if (attempt == retryLimit)
                        throw new ApiException("Request timed out after maximum retry attempts", 0, ex.Message, new Dictionary<string, IEnumerable<string>>(), ex);
                }
            }
        }
    }

    public class ApiException : System.Exception
    {
        public int StatusCode { get; private set; }

        public string Response { get; private set; }

        public System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> Headers { get; private set; }

        public ApiException(string message, int statusCode, string? response, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> headers, System.Exception? innerException)
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

    public class ApiException<TResult> : ApiException
    {
        public TResult Result { get; private set; }

        public ApiException(string message, int statusCode, string? response, System.Collections.Generic.IReadOnlyDictionary<string, System.Collections.Generic.IEnumerable<string>> headers, TResult result, System.Exception? innerException)
            : base(message, statusCode, response, headers, innerException)
        {
            Result = result;
        }
    }
}