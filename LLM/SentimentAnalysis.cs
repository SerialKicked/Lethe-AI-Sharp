using HNSW.Net;
using LLama;
using LLama.Common;
using LLama.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AIToolkit.LLM
{
    public static class SentimentAnalysis
    {
        private static ModelParams? ModelSettings = null;
        private static LLamaWeights? ModelWeights = null;
        private static LLamaContext? ModelContext = null;

        private static void Load()
        {
            string modelPath = @"data\models\bert.gguf";

            if (ModelSettings != null)
                Unload();

            ModelSettings = new ModelParams(modelPath)
            {
                ContextSize = 512,
                GpuLayerCount = 255,
                PoolingType = LLamaPoolingType.Rank,
                Embeddings = false
                
            };
            // Load weights
            ModelWeights = LLamaWeights.LoadFromFile(ModelSettings);
            // Create context
            ModelContext = new LLamaContext(ModelWeights, ModelSettings);
        }

        /// <summary>
        /// Unload the Embedding model from memory (if any model loaded)
        /// </summary>
        private static void Unload()
        {
            if (ModelWeights != null)
            {
                ModelContext?.Dispose();
                ModelWeights?.Dispose();
                ModelWeights = null;
                ModelContext = null;
                ModelSettings = null;
            }
        }

        private static float[] Softmax(IReadOnlyList<float> logits)
        {
            var max = logits.ToArray().Max();
            var exp = logits.ToArray().Select(v => Math.Exp(v - max)).ToArray();
            var sum = exp.Sum();
            return exp.Select(v => (float)(v / sum)).ToArray();
        }

        public static async Task<float[]> Analyze(string text)
        {
            // text is: I am very happy today!
            if (ModelContext == null)
                Load();

            // Create an embedder bound to this context
            var embedder = new LLamaEmbedder(ModelWeights!, ModelSettings!);

            // For BERT with classifier head, GetEmbeddings returns classification logits
            var logits = await embedder.GetEmbeddings(text).ConfigureAwait(false); // should be length 28

            //var tokens = ModelContext!.NativeHandle.Tokenize(text, add_bos: true, special: false, Encoding.UTF8);
            //// correctly tokenized to 8 legit tokens

            //// Build batch for BERT
            //var batch = new LLamaBatch(); // <- no arguments sad face.
            //int lastRow = batch.AddRange(tokens, start: 0, sequence: LLamaSeqId.Zero, logitsLast: true);

            //// Run encoder forward pass
            //ModelContext.Decode(batch);

            //// Get classification logits (length = 28)
            //var logits = ModelContext.NativeHandle.GetLogitsIth(lastRow);
            Console.WriteLine($"Got {logits.Count} logits (should be 28)");

            // Apply softmax
            var probs = Softmax(logits[0]);

            // Print top 5 emotions
            var top = probs
                .Select((p, i) => (Index: i, Prob: p))
                .OrderByDescending(x => x.Prob)
                .Take(5);

            foreach (var (Index, Prob) in top)
                Console.WriteLine($"Class {Index}: {Prob:F3}");
            return probs;
        }
    }
}
