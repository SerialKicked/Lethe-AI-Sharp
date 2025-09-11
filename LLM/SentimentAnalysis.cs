using AIToolkit.Files;
using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AIToolkit.LLM
{
    internal sealed class ThresholdConfig
    {
        public List<string> emotion_labels { get; set; } = new();
        public List<double> thresholds { get; set; } = new();
    }

    internal sealed class LinearLayer
    {
        public List<List<float>> weight { get; set; } = new();
        public List<float> bias { get; set; } = new();
        private float[,] W = null!;
        private float[] B = null!;
        public int Out => W.GetLength(0);
        public int In => W.GetLength(1);

        public void Build()
        {
            // HF stores (out_features, in_features)
            int rows = weight.Count;
            int cols = weight[0].Count;
            W = new float[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    W[r, c] = weight[r][c];
            B = bias.ToArray();
        }

        public void Apply(ReadOnlySpan<float> input, Span<float> output)
        {
            int rows = W.GetLength(0);
            int cols = W.GetLength(1);
            for (int r = 0; r < rows; r++)
            {
                double sum = B[r];
                for (int c = 0; c < cols; c++)
                    sum += input[c] * W[r, c];
                output[r] = (float)sum;
            }
        }
    }

    internal sealed class GoEmotionHead
    {
        public Dictionary<string, string>? id2label { get; set; }
        public bool has_pooler { get; set; }
        public LinearLayer? pooler { get; set; }
        public LinearLayer? classifier { get; set; }
    }

    internal static class GoEmotionRuntime
    {
        private static GoEmotionHead? _head;
        private static string[] _labelOrder = Array.Empty<string>();
        public static int HiddenIn { get; private set; }
        public static int LabelCount { get; private set; }

        public static void Load(string path)
        {
            var json = File.ReadAllText(path);
            _head = JsonConvert.DeserializeObject<GoEmotionHead>(json) ?? throw new InvalidDataException("Invalid head JSON");

            if (_head.classifier == null)
                throw new InvalidDataException("Classifier layer missing.");

            _head.classifier.Build();
            if (_head.has_pooler && _head.pooler != null)
            {
                _head.pooler.Build();
                HiddenIn = _head.pooler.In;
            }
            else
            {
                HiddenIn = _head.classifier.In;
            }
            LabelCount = _head.classifier.Out;

            if (_head.id2label != null && _head.id2label.Count == LabelCount)
            {
                _labelOrder = _head.id2label
                    .OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
                    .Select(kv => kv.Value)
                    .ToArray();
            }
            else
            {
                _labelOrder = SentimentAnalysis.FallbackLabels;
            }

            Console.WriteLine($"[GoEmotionHead] Loaded. has_pooler={_head.has_pooler} hidden={HiddenIn} labels={LabelCount}");
        }

        public static float[] Forward(float[] clsRaw)
        {
            if (_head == null) throw new InvalidOperationException("Head not loaded.");
            if (clsRaw.Length != HiddenIn)
                throw new ArgumentException($"CLS size {clsRaw.Length} != expected {HiddenIn}");

            float[] work = clsRaw;
            float[] pooledBuf;
            if (_head.has_pooler && _head.pooler != null)
            {
                pooledBuf = new float[_head.pooler.Out];
                _head.pooler.Apply(work, pooledBuf);
                for (int i = 0; i < pooledBuf.Length; i++)
                    pooledBuf[i] = MathF.Tanh(pooledBuf[i]);
                work = pooledBuf;
            }

            var logits = new float[_head.classifier!.Out];
            _head.classifier.Apply(work, logits);
            return logits;
        }

        public static string[] Labels => _labelOrder;
    }

    public static class SentimentAnalysis
    {

        internal static readonly string[] FallbackLabels = [
            "admiration","amusement","anger","annoyance","approval","caring","confusion","curiosity",
            "desire","disappointment","disapproval","disgust","embarrassment","excitement","fear","gratitude",
            "grief","joy","love","nervousness","optimism","pride","realization","relief",
            "remorse","sadness","surprise","neutral"
        ];

        private static ModelParams? modelSettings;
        private static LLamaWeights? modelWeights;
        private static LLamaEmbedder? embedder;
        private static bool headLoaded;

        internal static string ModelPath =>LLMEngine.Settings.SentimentModelPath;
        internal static string HeadPath => LLMEngine.Settings.SentimentGoEmotionHeadPath;
        internal static string ThresholdsPath => LLMEngine.Settings.SentimentThresholdsPath;

        private static float[]? PerLabelThresholds;

        private static float TargetNorm = 18.0f; // rough BERT pooled magnitude
        private static bool ScaleLlamaEmbedding = true; // diagnostic toggle

        private static float[] AdjustEmbedding(float[] v)
        {
            if (!ScaleLlamaEmbedding) return v;
            double n2 = 0;
            for (int i = 0; i < v.Length; i++) n2 += v[i] * v[i];
            var n = Math.Sqrt(n2) + 1e-6;
            var factor = TargetNorm / n;
            var outv = new float[v.Length];
            for (int i = 0; i < v.Length; i++) outv[i] = (float)(v[i] * factor);
            return outv;
        }

        public static bool Enabled
        {
            get => LLMEngine.Settings.SentimentEnabled;
            set
            {
                LLMEngine.Settings.SentimentEnabled = value;
                if (!value) UnloadEmbedder();
            }
        }

        private static void Ensure()
        {
            if (!headLoaded)
            {
                GoEmotionRuntime.Load(HeadPath);
                headLoaded = true;
                LoadPerLabelThresholds();
            }
            // NOTE: This embedder is NOT a BERT encoder. Kept only so code compiles.
            if (embedder == null)
            {
                modelSettings = new ModelParams(ModelPath)
                {
                    GpuLayerCount = 255,
                    Embeddings = true,
                    

                };
                modelWeights = LLamaWeights.LoadFromFile(modelSettings);
                embedder = new LLamaEmbedder(modelWeights, modelSettings);
            }
        }

        private static float[] Sigmoid(float[] logits)
        {
            var r = new float[logits.Length];
            for (int i = 0; i < logits.Length; i++)
            {
                double v = logits[i];
                if (v >= 0)
                {
                    double z = Math.Exp(-v);
                    r[i] = (float)(1 / (1 + z));
                }
                else
                {
                    double z = Math.Exp(v);
                    r[i] = (float)(z / (1 + z));
                }
            }
            return r;
        }

        private static void UnloadEmbedder()
        {
            modelWeights?.Dispose();
            embedder?.Dispose();
            modelWeights = null;
            embedder = null;
        }

        // Call once in Ensure() after GoEmotionRuntime.Load(...)
        private static void LoadPerLabelThresholds()
        {
            try
            {
                if (!File.Exists(ThresholdsPath))
                    return;

                var json = File.ReadAllText(ThresholdsPath);
                var cfg = JsonConvert.DeserializeObject<ThresholdConfig>(json);
                if (cfg == null || cfg.emotion_labels.Count == 0 || cfg.emotion_labels.Count != cfg.thresholds.Count)
                {
                    LLMEngine.Logger?.LogWarning("[Sentiment] thresholds file invalid; ignoring.");
                    return;
                }

                // Map by name to the runtime label order
                var byName = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cfg.emotion_labels.Count; i++)
                    byName[cfg.emotion_labels[i]] = (float)cfg.thresholds[i];

                var labels = GoEmotionRuntime.Labels;
                var mapped = new float[labels.Length];
                bool ok = true;
                for (int i = 0; i < labels.Length; i++)
                {
                    if (byName.TryGetValue(labels[i], out var thr))
                        mapped[i] = thr;
                    else
                    {
                        ok = false;
                        mapped[i] = 0.5f; // fallback default for any missing label
                    }
                }
                PerLabelThresholds = mapped;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[Sentiment] Failed to load thresholds");
                PerLabelThresholds = null;
            }
        }

        public static async Task<List<(string Label, float Probability)>> Analyze(string text, float threshold = 0.5f, int topK = 3)
        {
            Ensure();

            // TODO: Replace this with real BERT CLS. Current llama embedding is sub-optimal for this head.
            var emb = await embedder!.GetEmbeddings(text).ConfigureAwait(false);
            var fakeCls = AdjustEmbedding(emb[0]);
            if (fakeCls.Length != GoEmotionRuntime.HiddenIn)
                throw new InvalidDataException($"Embedding size {fakeCls.Length} != expected {GoEmotionRuntime.HiddenIn}");
            var logits = GoEmotionRuntime.Forward(fakeCls);
            var probs = Sigmoid(logits);

            var perLabelThr = PerLabelThresholds;
            List<(string Label, float Probability)> selected;

            if (perLabelThr is not null && perLabelThr.Length == probs.Length)
            {
                selected = GoEmotionRuntime.Labels
                    .Select((lbl, i) => (lbl, probs: probs[i], thr: perLabelThr[i]))
                    .Where(x => x.probs >= x.thr)
                    .Select(x => (x.lbl, x.probs))
                    .OrderByDescending(t => t.probs)
                    .ToList();

                if (selected.Count == 0)
                    selected = [.. GoEmotionRuntime.Labels.Select((lbl, i) => (lbl, probs[i]))
                                                      .OrderByDescending(t => t.Item2)
                                                      .Take(topK)];
            }
            else
            {
                // fallback to global threshold
                selected = GoEmotionRuntime.Labels
                    .Select((lbl, i) => (lbl, probs[i]))
                    .Where(t => t.Item2 >= threshold)
                    .OrderByDescending(t => t.Item2)
                    .ToList();

                if (selected.Count == 0)
                    selected = [.. GoEmotionRuntime.Labels.Select((lbl, i) => (lbl, probs[i]))
                                                      .OrderByDescending(t => t.Item2)
                                                      .Take(topK)];
            }
            return selected;
        }
    }
}