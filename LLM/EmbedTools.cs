using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;
using System.Numerics;
using LetheAISharp.Memory;

namespace LetheAISharp.LLM
{



    /// <summary>
    /// Embedding and Distance Evaluation tools
    /// </summary>
    public static class EmbedTools
    {
        /// <summary> Called when the system embedded a session </summary>
        public static event EventHandler<string>? OnEmbedText;

        private static void RaiseOnEmbedText(string toembed) => OnEmbedText?.Invoke(null, toembed);

        // Embedding model's weights and params
        private static ModelParams? EmbedSettings = null;
        private static LLamaWeights? EmbedWeights = null;
        private static LLamaEmbedder? Embedder = null;


        #region *** Embedding Functions ***

        /// <summary>
        /// Load the Embedding model in memory
        /// </summary>
        /// <returns></returns>
        private static LLamaEmbedder LoadEmbedder()
        {
            if (EmbedSettings != null)
                UnloadEmbedder();
            if (!File.Exists(LLMEngine.Settings.RAGModelPath))
            {
                EmbedSettings = null;
                LLMEngine.Settings.RAGEnabled = false;
                LLMEngine.Logger?.LogError("Embedding model not found: {path}", LLMEngine.Settings.RAGModelPath);
                throw new InvalidOperationException($"Embedding model not found: {LLMEngine.Settings.RAGModelPath}");
            }
            EmbedSettings = new ModelParams(LLMEngine.Settings.RAGModelPath)
            { 
                GpuLayerCount = 255,
                Embeddings = true
            };
            EmbedWeights = LLamaWeights.LoadFromFile(EmbedSettings);
            Embedder = new LLamaEmbedder(EmbedWeights, EmbedSettings);
            
            return Embedder;
        }

        /// <summary>
        /// Unload the Embedding model from memory (if any model loaded)
        /// </summary>
        private static void UnloadEmbedder()
        {
            if (Embedder != null)
            {
                EmbedWeights?.Dispose();
                Embedder?.Dispose();
                Embedder = null;
                EmbedWeights = null;
                EmbedSettings = null;
            }
        }

        /// <summary>
        /// Embdding of a single message (async)
        /// </summary>
        /// <param name="textToEmbed"></param>
        /// <returns></returns>
        public static async Task<float[]> EmbeddingText(string textToEmbed)
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return [];
            var embed = Embedder ?? LoadEmbedder();
            var emb = textToEmbed;
            if (emb.Length > LLMEngine.Settings.RAGEmbeddingSize)
                emb = emb[..LLMEngine.Settings.RAGEmbeddingSize];
            var tsk = await embed.GetEmbeddings(emb).ConfigureAwait(false);
            RaiseOnEmbedText(textToEmbed);
            return tsk[0].EuclideanNormalization();
        }

        public static void RemoveEmbedEventHandler()
        {
            OnEmbedText = null;
        }

        #endregion

        #region *** Self contained string similarity check ***

        /// <summary>
        /// Compute cosine similarity distance between two strings using the embedding model.
        /// Returns a value in [0, 2]. Requires RAG to be Enabled.
        /// </summary>
        public static async Task<float> GetDistanceAsync(string a, string b)
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return 2f;

            var ea = await EmbeddingText(a).ConfigureAwait(false);
            var eb = await EmbeddingText(b).ConfigureAwait(false);

            if (ea.Length == 0 || eb.Length == 0 || ea.Length != eb.Length)
                return 2f;

            return ToCosineDistance(CosineSimilarityUnit(ea, eb));
        }

        /// <summary>
        /// Compute cosine similarity distance between a string and a IEmbed.
        /// Returns a value in [0, 2]. Requires RAG to be Enabled.
        /// </summary>
        public static async Task<float> GetDistanceAsync(string a, IEmbed b)
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return 2f;

            var ea = await EmbeddingText(a).ConfigureAwait(false);

            if (ea.Length == 0 || b.EmbedSummary.Length == 0 || ea.Length != b.EmbedSummary.Length)
                return 2f;

            return ToCosineDistance(CosineSimilarityUnit(ea, b.EmbedSummary));
        }

        /// <summary>
        /// Compute cosine similarity distance between two IEmbed.
        /// Returns a value in [0, 2].
        /// </summary>
        public static float GetDistance(IEmbed a, IEmbed b)
        {
            if (a.EmbedSummary.Length == 0 || b.EmbedSummary.Length == 0 || a.EmbedSummary.Length != b.EmbedSummary.Length)
                return 2f;

            return ToCosineDistance(CosineSimilarityUnit(a.EmbedSummary, b.EmbedSummary));
        }

        public static float GetDistance(float[] a, float[] b)
        {
            if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
                return 2f;

            return ToCosineDistance(CosineSimilarityUnit(a, b));
        }

        /// <summary>
        /// Synchronous wrapper for GetSimilarityAsync. May block the calling thread.
        /// Prefer the async version when possible.
        /// </summary>
        public static float GetDistance(string a, string b) => GetDistanceAsync(a, b).GetAwaiter().GetResult();

        /// <summary>
        /// Merge 2 embeddings with weights and re-normalize.
        /// </summary>
        public static float[] MergeEmbeddings(float[] firstembed, float[] secondembed, float firstweight = 0.2f, float secondweight = 0.8f)
        {
            if (firstembed.Length != secondembed.Length)
                throw new ArgumentException("Title and summary embeddings must have the same length.");
            int dim = firstembed.Length;
            float[] merged = new float[dim];
            // weighted merge
            for (int i = 0; i < dim; i++)
            {
                merged[i] = (firstweight * firstembed[i]) + (secondweight * secondembed[i]);
            }
            return merged.EuclideanNormalization();
        }

        /// <summary>
        /// Cosine similarity for unit-normalized vectors (EmbeddingText already normalizes).
        /// </summary>
        private static float CosineSimilarityUnit(float[] a, float[] b)
        {
            var len = a.Length;
            float dot = 0f;
            for (int i = 0; i < len; i++)
                dot += a[i] * b[i];

            // Clamp for numerical stability
            if (dot > 1f)
                dot = 1f;
            else if (dot < -1f)
                dot = -1f;
            return
                dot;
        }

        /// <summary>
        /// Utility: convert cosine similarity [-1,1] to cosine distance [0,2].
        /// </summary>
        private static float ToCosineDistance(float similarity)
        {
            var d = 1f - similarity;
            if (d < 0f) return 0f;
            if (d > 2f) return 2f;
            return d;
        }

        #endregion
    }
}
