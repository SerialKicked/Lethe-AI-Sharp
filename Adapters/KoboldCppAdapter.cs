using AIToolkit.LLM;
using AIToolkit.SearchAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema.Generation;
using System;

namespace AIToolkit.API
{
    
    /// <summary>
    /// Adapter for KoboldCpp backend
    /// </summary>
    public class KoboldCppAdapter : ILLMServiceClient
    {
        private readonly KoboldCppClient _client;
        private readonly HttpClient _httpClient;
        private readonly WebSearchAPI webSearchClient;
        private readonly JSchemaGenerator schemaGenerator = new();
        private bool koboldDDGAvailable = false;

        public event EventHandler<LLMTokenStreamingEventArgs>? TokenReceived;

        public KoboldCppAdapter(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(LLMSystem.Settings.BackendUrl);
            _client = new KoboldCppClient(httpClient);
            webSearchClient = new WebSearchAPI(httpClient);

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
            get => LLMSystem.Settings.BackendUrl;
            set 
            {
                LLMSystem.Settings.BackendUrl = value;
                _httpClient.BaseAddress = new Uri(LLMSystem.Settings.BackendUrl);
            }
        }

        public CompletionType CompletionType => CompletionType.Text;

        public async Task<int> GetMaxContextLength()
        {
            var result = await _client.TrueMaxContextLengthAsync().ConfigureAwait(false);
            return result.Value;
        }

        public async Task<string> GetModelInfo()
        {
            var info = await _client.ModelAsync().ConfigureAwait(false);
            var index = info.Result.IndexOf('/');
            return index > 0 ? info.Result[(index + 1)..] : info.Result;
        }

        public async Task<string> GetBackendInfo()
        {
            var engine = await _client.ExtraVersionAsync().ConfigureAwait(false);
            if (engine?.result == null)
                return "Engine Not Supported";
            SupportsTTS = engine.tts;
            SupportsVision = engine.vision;
            koboldDDGAvailable = engine.websearch;
            return $"{engine.result} {engine.version}";
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            var result = await _client.GenerateAsync(input).ConfigureAwait(false);
            return string.Join("", result.Results.Select(r => r.Text));
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");
            await _client.GenerateTextStreamAsync(input).ConfigureAwait(false);
        }

        public async Task<bool> AbortGeneration()
        {
            var result = await _client.AbortAsync(new GenkeyData()).ConfigureAwait(false);
            return result.Success;
        }

        public async Task<int> CountTokens(string text)
        {
            var result = await _client.TokencountAsync(new KcppPrompt { Prompt = text }).ConfigureAwait(false);
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
            return await _client.TextToSpeechAsync(new TextToSpeechInput { Input = text, Voice = voice }).ConfigureAwait(false);
        }

        public async Task<string> WebSearch(string query)
        {
            if (!SupportsWebSearch)
                return string.Empty;
            if (LLMSystem.Settings.WebSearchAPI == BackendSearchAPI.Brave || (LLMSystem.Settings.WebSearchAPI == BackendSearchAPI.DuckDuckGo && !koboldDDGAvailable))
            {
                var res = await webSearchClient.SearchAndEnrichAsync(query, 3, LLMSystem.Settings.WebSearchDetailedResults).ConfigureAwait(false);
                return JsonConvert.SerializeObject(res);
            }
            else
            {
                var res = await _client.WebQueryAsync(new WebQuery { q = query }).ConfigureAwait(false);
                return JsonConvert.SerializeObject(res);
            }
        }

        public async Task<string> ImageCaption(byte[] imageData)
        {
            var base64Image = Convert.ToBase64String(imageData);
            var result = await _client.InterrogateAsync(new KcppCaptionQuery { Image = base64Image }).ConfigureAwait(false);
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
                var res = await _client.ExtraVersionAsync().ConfigureAwait(false);
                return res != null;

            }
            catch (Exception)
            {
                // Handle the exception
                return false;
            }
        }

        public IPromptBuilder GetPromptBuilder()
        {
            return new TextPromptBuilder();
        }

        public async Task<bool> SaveKVState(int value)
        {
            var res = await _client.SaveKVState(value).ConfigureAwait(false);
            return res.success;
        }

        public async Task<bool> LoadKVState(int value)
        {
            var res = await _client.LoadKVState(value).ConfigureAwait(false);
            return res.success;
        }

        public async Task<bool> ClearKVStates()
        {
            var res = await _client.ClearKVState().ConfigureAwait(false);
            return res.success;
        }

        public async Task<string> SchemaToGrammar(Type jsonclass)
        {
            // Using NewtonSoft
            var schemanewton = schemaGenerator.Generate(jsonclass);
            var jsonnewton = schemanewton.ToString(); // <- note it returns a json, it's not the default tostring.
            var apiPayloadNewton = new GrammarQuery
            {
                schema = JObject.Parse(jsonnewton!)
            };
            // works
            var res = await _client.SchemaToGrammar(apiPayloadNewton).ConfigureAwait(false);

            // I want to use NJsonSchema but it doesn't work because I'm fucking it up, fix.

            if (res.success)
            {
                return res.result;
            }
            else
            {
                throw new Exception($"Failed to convert schema to grammar");
            }
        }

        public bool SupportsStreaming => true;
        public bool SupportsTTS { get; private set; } = false;
        public bool SupportsVision { get; private set; } = false;
        public bool SupportsWebSearch { get; private set; } = true;
        public bool SupportsStateSave { get; private set; } = true;
        public bool SupportsSchema { get; private set; } = true;
    }
}