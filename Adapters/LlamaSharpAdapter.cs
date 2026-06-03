using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.SearchAPI;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace LetheAISharp.API
{
    public class LlamaSharpAdapter : ILLMServiceClient, IDisposable
    {
        private readonly ModelParams Settings;
        private readonly LLamaWeights Weights;
        private readonly LLamaContext Context;
        private readonly StatelessExecutor Executor;
        private readonly WebSearchAPI webSearchClient;

        private CancellationTokenSource? cts;
        private readonly Lock _ctsLock = new();

        public string BaseUrl
        {
            get => LLMEngine.Settings.BackendUrl;
            set
            {
                LLMEngine.Settings.BackendUrl = value;
            }
        }

        public CompletionType CompletionType { get; set; } = CompletionType.Text;
        public List<CompletionType> AvailCompletionTypes => [ CompletionType.Text ];

        public bool SupportsStreaming => true;
        public bool SupportsTTS => false;
        public bool SupportsVision => false;
        public bool SupportsWebSearch => true;
        public bool SupportsStateSave => false;
        public bool SupportsToolCalls => false;
        public bool SupportsSchema => true;
        public bool SupportParallelToolCall => false;
        public bool AllowPrefill => true;

        public event EventHandler<LLMTokenStreamingEventArgs>? TokenReceived;

        public LlamaSharpAdapter(string filepath)
        {
            if (!File.Exists(filepath))
                throw new FileNotFoundException("Model file not found", filepath);
            BaseUrl = filepath;
            Settings = new ModelParams(filepath)
            {
                GpuLayerCount = LLMEngine.Settings.LlamaSharpGPULayers,
                BatchSize = 512,
                FlashAttention = LLMEngine.Settings.LlamaSharpFlashAttention,
                ContextSize = (uint)LLMEngine.Settings.MaxTotalTokens,
                NoKqvOffload = LLMEngine.Settings.LlamaSharpNoKVoffload
            };
            Weights = LLamaWeights.LoadFromFile(Settings);
            Context = Weights.CreateContext(Settings);
            Executor = new StatelessExecutor(Weights, Settings)
            {
                ApplyTemplate = false
            };
            webSearchClient = new WebSearchAPI();
        }

        public void Dispose()
        {
            Context?.Dispose();
            Weights?.Dispose();
            cts?.Dispose();
            GC.SuppressFinalize(this);
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

        public async Task<bool> CheckBackend()
        {
            // Backend is inside the DLL, it's always "working"
            return await Task.FromResult(true);
        }

        public async Task<int> CountTokens(string text)
        {
            return await Task.FromResult(CountTokensSync(text));
        }

        public int CountTokensSync(string text)
        {
            return Context.Tokenize(text).Length;
        }

        private static InferenceParams MakeParams(GenerationInput input)
        {
            var gram = string.IsNullOrWhiteSpace(input.Grammar) ? null : new Grammar(input.Grammar, "root");
            var pipeline = new CustomSamplingPipeline()
            {
                Temperature = (float)input.Temperature,
                TopP = (float)input.Top_p,
                TopK = input.Top_k,
                TypicalP = (float)input.Typical,
                MinP = (float)input.Min_p,
                RepeatPenalty = (float)input.Rep_pen,
                GrammarOptimization = CustomSamplingPipeline.GrammarOptimizationMode.Extended,
                PenaltyCount = input.Rep_pen_range,
                Grammar = gram,
                PenalizeNewline = false,
                PreventEOS = input.Bypass_eos,
                Seed = input.Sampler_seed <= 0 ? 0 : (uint)input.Sampler_seed
            };
            var sett = new InferenceParams()
            {
                MaxTokens = input.Max_length,
                AntiPrompts = input.Stop_sequence?.ToArray() ?? [],
                SamplingPipeline = pipeline
            };
            return sett;
        }

        public async Task<string> GenerateText(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");

            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose(); // Dispose old token source
                cts = new CancellationTokenSource();
                token = cts.Token;
            }
            string response = string.Empty;
            var x = MakeParams(input);
            try
            {
                await foreach (var text in Executor.InferAsync(input.Prompt, x, token))
                {
                    if (token.IsCancellationRequested)
                        break;
                    response += text;
                }
            }
            catch (Exception e)
            {
                LLMEngine.Logger?.LogError(e, "Error during LlamaSharp inference");
                throw;
            }
            return response;
        }

        public async Task GenerateTextStreaming(object parameters)
        {
            if (parameters is not GenerationInput input)
                throw new ArgumentException("Parameters must be of type GenerationInput");

            CancellationToken token;
            lock (_ctsLock)
            {
                cts?.Dispose(); // Dispose old token source
                cts = new CancellationTokenSource();
                token = cts.Token;
            }
            await foreach (var text in Executor.InferAsync(input.Prompt, MakeParams(input), token))
            {
                if (token.IsCancellationRequested)
                    break;
                TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(text, null));
            }
            var reason = token.IsCancellationRequested ? "cancel" : "stop";
            await Task.Delay(25).ConfigureAwait(false);
            TokenReceived?.Invoke(this, new LLMTokenStreamingEventArgs(string.Empty, reason));
        }

        public async Task<string> GetBackendInfo()
        {
            var backend = $"LlamaSharp {typeof(LLamaContext).Assembly.GetName().Version}";
            return await Task.FromResult(backend);
        }

        public async Task<int> GetMaxContextLength()
        {
            return await Task.FromResult((int)(Settings.ContextSize ?? 0));
        }

        public async Task<string> GetModelInfo()
        {
            var filename = File.Exists(BaseUrl) ? Path.GetFileName(BaseUrl) : "missing model file";
            return await Task.FromResult(filename);
        }

        public IPromptBuilder GetPromptBuilder()
        {
            return new TextPromptBuilder();
        }

        public Task<bool> LoadKVState(int value)
        {
            throw new NotSupportedException("Internal API does not support KV cache manipulation");
        }

        public Task<bool> SaveKVState(int value)
        {
            throw new NotSupportedException("Internal API does not support KV cache manipulation");
        }

        public Task<bool> ClearKVStates()
        {
            throw new NotSupportedException("Internal API does not support KV cache manipulation");
        }

        public async Task<string> SchemaToGrammar(Type jsonclass)
        {
            var gram = GbnfConverter.Convert(jsonclass);
            return await Task.FromResult(gram);
        }

        public Task<byte[]> TextToSpeech(string text, string voice)
        {
            throw new NotSupportedException("Internal API does not support TTS");
        }

        public void UpdateSearchProvider()
        {
            webSearchClient.SwitchProvider(LLMEngine.Settings.WebSearchAPI, LLMEngine.Settings.WebSearchBraveAPIKey);
        }

        public async Task<string> WebSearch(string query)
        {
            if (!SupportsWebSearch)
                return string.Empty;
            var res = await webSearchClient.SearchAndEnrichAsync(query, LLMEngine.Settings.WebSearchResultsPerQuery, LLMEngine.Settings.WebSearchDetailedResults).ConfigureAwait(false);
            // Convert results to a common format
            return JsonConvert.SerializeObject(res);
        }

        public int CountMessageTokens(List<SingleMessage> messages)
        {
            var totalstring = new StringBuilder();
            foreach (var message in messages)
                totalstring.Append(message.ToTextCompletion());
            var full = totalstring.ToString();
            return string.IsNullOrEmpty(full) ? 0 : CountTokensSync(full);
        }

    }
}
