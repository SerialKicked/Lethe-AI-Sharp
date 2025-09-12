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

// Ignore IDE1006 for this file to allow deserialization of JSON with lowercase property names
#pragma warning disable IDE1006

namespace AIToolkit.LLM
{

    internal sealed class ThresholdConfig
    {
        public List<string> emotion_labels { get; set; } = [];
        public List<double> thresholds { get; set; } = [];
    }

    internal sealed class LinearLayer
    {
        public List<List<float>> weight { get; set; } = [];
        public List<float> bias { get; set; } = [];
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
            B = [.. bias];
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
        private static string[] _labelOrder = [];
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
                _labelOrder = [.. _head.id2label.OrderBy(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture)).Select(kv => kv.Value)];
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

        public enum AggregationStrategy
        {
            Max,
            Mean,
            NoisyOr,
            NoisyOrDamp,
            LogOddsMean
        }

        private static ModelParams? modelSettings;
        private static LLamaWeights? modelWeights;
        private static LLamaEmbedder? embedder;
        private static bool headLoaded;
        private static float[]? PerLabelThresholds;
        private static float TargetNorm = 18.0f; // rough BERT pooled magnitude
        private static bool ScaleLlamaEmbedding = true; // diagnostic toggle

        internal static string ModelPath =>LLMEngine.Settings.SentimentModelPath;
        internal static string HeadPath => LLMEngine.Settings.SentimentGoEmotionHeadPath;
        internal static string ThresholdsPath => LLMEngine.Settings.SentimentThresholdsPath;

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
                for (int i = 0; i < labels.Length; i++)
                {
                    if (byName.TryGetValue(labels[i], out var thr))
                        mapped[i] = thr;
                    else
                    {
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

        // Normalize a raw probability p relative to its threshold thr so that:
        // p==thr -> 0.5, p==0 -> 0, p==1 -> 1, piecewise-linear on each side.
        private static float NormalizeByThreshold(float prob, float thr)
        {
            const float eps = 1e-6f;
            thr = Math.Clamp(thr, eps, 1 - eps);
            prob = Math.Clamp(prob, 0f, 1f);

            if (prob >= thr)
            {
                float denom = 1f - thr;
                if (denom <= eps) return 1f; // thr ~ 1
                return 0.5f + 0.5f * (prob - thr) / denom;
            }
            else
            {
                float denom = thr;
                if (denom <= eps) return 0f; // thr ~ 0
                return 0.5f - 0.5f * (thr - prob) / denom;
            }
        }

        public static async Task<List<(string Label, float Probability)>> Analyze(string text, float threshold = 0.5f, int topK = 3)
        {
            if (!Enabled) return [];
            Ensure();

            // TODO: Replace this with real BERT CLS. Current llama embedding is sub-optimal for this head.
            var process = text;
            if (process.Length > 512)
                process = process[..512];
            var emb = await embedder!.GetEmbeddings(process).ConfigureAwait(false);
            var fakeCls = AdjustEmbedding(emb[0]);
            if (fakeCls.Length != GoEmotionRuntime.HiddenIn)
                throw new InvalidDataException($"Embedding size {fakeCls.Length} != expected {GoEmotionRuntime.HiddenIn}");

            var logits = GoEmotionRuntime.Forward(fakeCls);
            var probs = Sigmoid(logits);

            var perLabelThr = PerLabelThresholds;
            List<(string Label, float Probability)> selected;
            if (perLabelThr is not null && perLabelThr.Length == probs.Length)
            {
                // Normalize per-label so threshold maps to 0.5
                var normalized = new float[probs.Length];
                for (int i = 0; i < probs.Length; i++)
                    normalized[i] = NormalizeByThreshold(probs[i], perLabelThr[i]);

                // Use normalized >= 0.5 (equivalent to raw >= threshold), and return normalized values
                selected = [.. GoEmotionRuntime.Labels
                    .Select((lbl, i) => (lbl, nprob: normalized[i]))
                    .Where(t => t.nprob >= 0.5f)
                    .OrderByDescending(t => t.nprob)
                    .Select(t => (t.lbl, t.nprob))];
            }
            else
            {
                // fallback to global threshold when no file (no normalization)
                selected = [.. GoEmotionRuntime.Labels
                    .Select((lbl, i) => (lbl, probs[i]))
                    .Where(t => t.Item2 >= threshold)
                    .OrderByDescending(t => t.Item2)];
            }

            if (selected.Count == 0)
            {
                if (perLabelThr is not null && perLabelThr.Length == probs.Length)
                {
                    // TopK by normalized values
                    selected = [.. GoEmotionRuntime.Labels
                        .Select((lbl, i) => (lbl, NormalizeByThreshold(probs[i], perLabelThr[i])))
                        .OrderByDescending(t => t.Item2)
                        .Take(topK)];
                }
                else
                {
                    selected = [.. GoEmotionRuntime.Labels
                        .Select((lbl, i) => (lbl, probs[i]))
                        .OrderByDescending(t => t.Item2)
                        .Take(topK)];
                }
            }

            return selected;
        }

        // Returns the normalized per-label probability vector for a single text
        private static async Task<float[]> AnalyzeNormalizedVector(string text)
        {
            Ensure();

            var process = text;
            if (process.Length > 512)
                process = process[..512];

            var emb = await embedder!.GetEmbeddings(process).ConfigureAwait(false);
            var fakeCls = AdjustEmbedding(emb[0]);
            if (fakeCls.Length != GoEmotionRuntime.HiddenIn)
                throw new InvalidDataException($"Embedding size {fakeCls.Length} != expected {GoEmotionRuntime.HiddenIn}");

            var logits = GoEmotionRuntime.Forward(fakeCls);
            var probs = Sigmoid(logits);

            var perLabelThr = PerLabelThresholds;
            if (perLabelThr is not null && perLabelThr.Length == probs.Length)
            {
                // Normalize so threshold maps to 0.5
                var normalized = new float[probs.Length];
                for (int i = 0; i < probs.Length; i++)
                    normalized[i] = NormalizeByThreshold(probs[i], perLabelThr[i]);
                return normalized;
            }

            // No thresholds file: return raw probabilities
            return probs;
        }

        public static async Task<List<(string Label, float Probability)>> MultiParagraphAnalysis(
            string text,
            AggregationStrategy strategy = AggregationStrategy.LogOddsMean,
            int topK = 3,
            bool lengthWeighted = true)
        {
            if (!Enabled) return [];
            Ensure();

            // Prefer splitting on blank lines for actual paragraphs
            var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries);

            int labelCount = GoEmotionRuntime.Labels.Length;
            var agg = new double[labelCount];
            var weightSum = 0.0;

            // Initialize for strategies
            // Max: start at 0
            // Mean / LogOddsMean: sum accumulators; LogOddsMean is in logit domain
            // NoisyOr: we accumulate product of (1 - p), start at 1
            var noisyOrProd = Enumerable.Repeat(1.0, labelCount).ToArray();

            foreach (var rawPara in paragraphs)
            {
                var para = rawPara.RemoveNewLines();
                if (string.IsNullOrWhiteSpace(para))
                    continue;

                var p = await AnalyzeNormalizedVector(para).ConfigureAwait(false);
                double w = lengthWeighted ? Math.Max(1, para.Length) : 1.0;
                weightSum += w;

                for (int i = 0; i < labelCount; i++)
                {
                    double val = p[i];

                    switch (strategy)
                    {
                        case AggregationStrategy.Max:
                            agg[i] = Math.Max(agg[i], val);
                            break;

                        case AggregationStrategy.Mean:
                            agg[i] += w * val;
                            break;
                        case AggregationStrategy.NoisyOrDamp:
                            {
                                // val is normalized: 0.5 = threshold. Keep only positive evidence.
                                double pos = Math.Clamp((val - 0.5) * 2.0, 0.0, 1.0); // FIX: use val, not i

                                // Expand midrange so 0.6→~0.53, 0.7→~0.66, 0.8→~0.80
                                const double gamma = 0.7; // try 0.5–0.8
                                pos = Math.Pow(pos, gamma);

                                // Sharpen union so multiple positives accumulate faster
                                const double lambda = 1.3; // try 1.2–2.0
                                double q = Math.Pow(1.0 - pos, lambda);

                                noisyOrProd[i] *= q;
                                break;
                            }
                        case AggregationStrategy.NoisyOr:
                            {
                                //normal version : 1 - Π(1 - p_i); accumulate product here
                                noisyOrProd[i] *= (1.0 - val);
                                break;
                            }
                        case AggregationStrategy.LogOddsMean:
                            // mean of log-odds, then sigmoid at the end
                            const double eps = 1e-6;
                            val = Math.Clamp(val, eps, 1 - eps);
                            double logit = Math.Log(val / (1 - val));
                            agg[i] += w * logit;
                            break;
                    }
                }
            }

            var result = new List<(string Label, float Probability)>(labelCount);

            for (int i = 0; i < labelCount; i++)
            {
                double score = 0.0;
                switch (strategy)
                {
                    case AggregationStrategy.Max:
                        score = agg[i]; // already [0,1]
                        break;

                    case AggregationStrategy.Mean:
                        score = weightSum > 0 ? agg[i] / weightSum : 0.0;
                        break;

                    case AggregationStrategy.NoisyOrDamp:
                    case AggregationStrategy.NoisyOr:
                        score = 1.0 - noisyOrProd[i];
                        if (strategy == AggregationStrategy.NoisyOrDamp) score = 1.0 - Math.Pow(1.0 - score, 0.85);
                        break;

                    case AggregationStrategy.LogOddsMean:
                        if (weightSum > 0)
                        {
                            double avgLogit = agg[i] / weightSum;
                            score = 1.0 / (1.0 + Math.Exp(-avgLogit));
                        }
                        else
                        {
                            score = 0.0;
                        }
                        break;
                }

                result.Add((GoEmotionRuntime.Labels[i], (float)score));
            }

            return [.. result.OrderByDescending(t => t.Probability).Take(topK)];
        }

        public static async Task<List<(string Label, float Probability)>> MergedAnalyze(string text, int topK = 3)
        {
            static float Calibrate(float p, float T = 0.85f, float b = 0f)
            {
                const float eps = 1e-6f;
                p = Math.Clamp(p, eps, 1 - eps);
                var logit = MathF.Log(p / (1 - p));
                var z = (logit - b) / T;
                return 1f / (1f + MathF.Exp(-z));
            }

            var maxres = await MultiParagraphAnalysis(text, AggregationStrategy.Max, 10).ConfigureAwait(false);
            var lomres = await MultiParagraphAnalysis(text, AggregationStrategy.LogOddsMean, 10).ConfigureAwait(false);
            var maxByLabel = maxres.ToDictionary(x => x.Label, x => x.Probability, StringComparer.OrdinalIgnoreCase);
            var lomByLabel = lomres.ToDictionary(x => x.Label, x => x.Probability, StringComparer.OrdinalIgnoreCase);

            var merged = GoEmotionRuntime.Labels
                .Select(lbl =>
                {
                    var max = maxByLabel.TryGetValue(lbl, out var m) ? m : 0f;
                    var lom = lomByLabel.TryGetValue(lbl, out var l) ? l : 0f;
                    var lomCal = Calibrate(lom, 0.85f, 0f);
                    var score = 0.65f * max + 0.35f * lomCal;
                    return (Label: lbl, Score: score, Max: max, LogOddsMean: lom);
                })
                .Where(t => !string.Equals(t.Label, "neutral", StringComparison.OrdinalIgnoreCase))
                .Where(t => t.Max >= 0.5f || t.LogOddsMean >= 0.35f) // gate by either
                .OrderByDescending(t => t.Score)
                .Take(topK)
                .Select(t => (t.Label, t.Score))
                .ToList();
            return merged;
        }
    }
}

#pragma warning restore IDE1006