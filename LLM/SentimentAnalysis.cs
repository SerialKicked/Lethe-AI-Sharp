using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using Newtonsoft.Json;

namespace AIToolkit.LLM
{
    internal class ClassifierHead
    {
        public List<List<float>> weight { get; set; } = new();
        public List<float> bias { get; set; } = new();

        private float[,] weightMatrix = null!;
        private float[] biasVector = null!;

        public static ClassifierHead Load(string path)
        {
            var json = File.ReadAllText(path);
            var obj = JsonConvert.DeserializeObject<ClassifierHead>(json)!;

            int hidden = obj.weight.Count;        // 768
            int labels = obj.weight[0].Count;     // 28

            obj.weightMatrix = new float[hidden, labels];
            for (int i = 0; i < hidden; i++)
                for (int j = 0; j < labels; j++)
                    obj.weightMatrix[i, j] = obj.weight[i][j];

            obj.biasVector = obj.bias.ToArray();

            Console.WriteLine($"Loaded classifier: weight {hidden}x{labels}, bias {obj.biasVector.Length}");
            return obj;
        }

        public float[] Apply(float[] embedding)
        {
            int hidden = weightMatrix.GetLength(0); // 768
            int labels = weightMatrix.GetLength(1); // 28

            if (embedding.Length != hidden)
                throw new ArgumentException($"Embedding length {embedding.Length} does not match expected {hidden}");

            var logits = new float[labels];
            for (int j = 0; j < labels; j++)
            {
                float sum = biasVector[j];
                for (int i = 0; i < hidden; i++)
                    sum += embedding[i] * weightMatrix[i, j];
                logits[j] = sum;
            }
            return logits;
        }
    }

    public static class SentimentAnalysis
    {
        private static string[] labels = [ "admiration","amusement","anger","annoyance","approval","caring","confusion","curiosity", "desire","disappointment","disapproval","disgust","embarrassment","excitement","fear","gratitude", "grief","joy","love","nervousness","optimism","pride","realization","relief", "remorse","sadness","surprise","neutral" ];

        private static ModelParams? modelSettings = null;
        private static LLamaWeights? modelWeights = null;
        private static LLamaEmbedder? embedder = null;
        private static ClassifierHead? classifier = null;

        public static bool Enabled
        {
            get => LLMEngine.Settings.SentimentAnalysis;
            set
            {
                LLMEngine.Settings.SentimentAnalysis = value;
                if (!LLMEngine.Settings.SentimentAnalysis)
                    UnloadEmbedder();
            }
        }

        private static void UnloadEmbedder()
        {
            if (embedder != null)
            {
                modelWeights?.Dispose();
                embedder?.Dispose();
                embedder = null;
                modelWeights = null;
                modelSettings = null;
            }
        }

        // paths — adjust to your environment
        public static string ModelPath => @"data\models\emotion-bert-classifier.gguf";
        public static string ClassifierPath => @"data\models\emotion-bert-classifier.json";

        private static LLamaEmbedder LoadEmbedder()
        {
            modelSettings = new ModelParams(ModelPath)
            {
                GpuLayerCount = 255,
                
                Embeddings = true
            };
            modelWeights = LLamaWeights.LoadFromFile(modelSettings);
            embedder = new LLamaEmbedder(modelWeights, modelSettings);
            return embedder;
        }

        private static float[] Softmax(float[] logits)
        {
            var max = logits.Max();
            var exp = logits.Select(v => Math.Exp(v - max)).ToArray();
            var sum = exp.Sum();
            return exp.Select(v => (float)(v / sum)).ToArray();
        }

        public static async Task<List<(string Label, float Probability)>> Analyze(string text)
        {
            if (embedder == null) 
                LoadEmbedder();
            if (classifier == null) 
                classifier = ClassifierHead.Load(ClassifierPath);

            // run encoder → get CLS embedding
            var emb = await embedder!.GetEmbeddings(text).ConfigureAwait(false);
            float[] cls = emb[0]; // 768 floats

            // run classifier
            float[] logits = classifier.Apply(cls);
            float[] probs = Softmax(logits);

            // pair with labels
            var results = labels
                .Select((label, i) => (label, probs[i]))
                .OrderByDescending(x => x.Item2)
                .ToList();

            return results;
        }
    }
}
