using AIToolkit.LLM;
using Newtonsoft.Json;

namespace AIToolkit.API
{
    /// <summary>
    /// Adapter for KoboldCpp backend
    /// </summary>
    public class KoboldCppAdapter : ILLMServiceClient
    {
        private readonly KoboldCppClient _client;
        private readonly HttpClient _httpClient;

        public event EventHandler<LLMTokenStreamingEventArgs>? TokenReceived;

        public KoboldCppAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMSystem.BackendUrl);
            _client = new KoboldCppClient(httpClient);

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
            set 
            {
                LLMSystem.BackendUrl = value;
                _httpClient.BaseAddress = new Uri(LLMSystem.BackendUrl);
            }
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

        public async Task<bool> CheckBackend()
        {
            try
            {
                var res = await _client.ExtraVersionAsync();
                return res != null;

            }
            catch (Exception)
            {
                // Handle the exception
                return false;
            }
        }

        public bool SupportsStreaming => true;
        public bool SupportsTTS { get; private set; } = false;
        public bool SupportsVision { get; private set; } = false;
        public bool SupportsWebSearch { get; private set; } = false;
    }
}