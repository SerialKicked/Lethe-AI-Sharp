using LLama;
using LLama.Common;
using Microsoft.Extensions.Logging;
using System.Numerics;
using LetheAISharp.Files;
using LetheAISharp.Memory;

namespace LetheAISharp.LLM
{

    /// <summary>
    /// Retrieval Augmented Generation System
    /// </summary>
    public static class RAGEngine
    {
        /// <summary> Called when the system embedded a session </summary>
        public static event EventHandler<string>? OnEmbedText;

        private static void RaiseOnEmbedText(string toembed) => OnEmbedText?.Invoke(null, toembed);

        /// <summary> Toggle RAG functionalities on/off </summary>
        public static bool Enabled
        {
            get => LLMEngine.Settings.RAGEnabled;
            set
            {
                LLMEngine.Settings.RAGEnabled = value;
                if (!LLMEngine.Settings.RAGEnabled)
                    UnloadEmbedder();
            }
        }

        // Embedding model's weights and params
        private static ModelParams? EmbedSettings = null;
        private static LLamaWeights? EmbedWeights = null;
        private static LLamaEmbedder? Embedder = null;
        // Memory vault
        private static MemoryVault? Vault = null;

        public static void ApplySettings()
        {
            if (!Enabled)
                return;
            Vault = new MemoryVault();
            if (LLMEngine.History != null)
                VectorizeChatBot(LLMEngine.Bot);
        }

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
                Enabled = false;
                LLMEngine.Logger?.LogError("Embedding model not found: {path}", LLMEngine.Settings.RAGModelPath);
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
            if (!Enabled)
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

        #region *** SmallWorld's Vector Similarity Functions ***

        public static void VectorizeChatBot(BasePersona persona)
        {
            if (!Enabled)
                return;
            Vault = new MemoryVault();
            var log = persona.History;
            if (log.Sessions.Count == 0 && persona.MyWorlds.Count == 0)
                return;

            var vectors = new List<MemoryUnit>();
            for (int i = 0; i < log.Sessions.Count; i++)
            {
                var session = log.Sessions[i];
                if (session.EmbedSummary.Length == 0)
                    continue;
                vectors.Add(session);
            }

            var brainmemories = LLMEngine.Bot.Brain.GetMemoriesForRAG();
            foreach (var doc in brainmemories)
            {
                vectors.Add(doc);
            }

            foreach (var world in persona.MyWorlds)
            {
                if (!world.DoEmbeds)
                    continue;
                foreach (var entry in world.Entries)
                {
                    if (entry.Enabled && entry.EmbedSummary?.Length > 0)
                        vectors.Add(entry);
                }
            }   

            try
            {
                Vault?.AddMemories(vectors);
            }
            catch (Exception e)
            {
                throw new Exception("Error adding items to the VectorDB", e);
            }
        }

        public static async Task<List<VaultResult>> Search(string message, int maxRes, float maxDist)
        {
            if (!Enabled)
                return [];
            if (Vault is null || Vault.Count == 0)
            {
                VectorizeChatBot(LLMEngine.Bot);
            }

            var toretrieve = maxRes * 2 + 5;
            if (toretrieve < 30)
                toretrieve = 30;

            // Check if message contains the words RP or roleplay
            var requestIsAboutRoleplay = message.Contains(" RP", StringComparison.OrdinalIgnoreCase) || message.Contains(" roleplay", StringComparison.OrdinalIgnoreCase);

            var found = await Vault!.Search(
                LLMEngine.Settings.RAGConvertTo3rdPerson ? message.ConvertToThirdPerson() : message, 
                toretrieve, 
                maxDist).ConfigureAwait(false);

            foreach (var item in found)
            {
                if (item.Memory.Category == MemoryType.ChatSession && item.Memory is Files.ChatSession session)
                {

                    if (session.MetaData.IsRoleplaySession)
                    {
                        if (requestIsAboutRoleplay)
                            item.Distance -= 0.04f; // Boost RP sessions
                        else
                            item.Distance += 0.04f; // Decay RP sessions
                    }
                    // Mark sticky as not wanted because they are handled with different insertion method
                    if (session.Sticky)
                        item.Distance += 2f;
                }
            }
            // Remove entries with distance above limit
            found.RemoveAll(e => e.Distance > maxDist);
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            // If we have too many results, trim the list to maxRes
            if (found.Count > maxRes)
                found = found.GetRange(0, maxRes);
            return found;
        }

        public static async Task<List<VaultResult>> Search(string message)
        {
            return await Search(message, LLMEngine.Settings.RAGMaxEntries, LLMEngine.Settings.RAGDistanceCutOff).ConfigureAwait(false);
        }

        public static List<VaultResult> Search(float[] embed, int count)
        {
            if (Vault is null || Vault.Count == 0)
            {
                VectorizeChatBot(LLMEngine.Bot);
                if (Vault!.Count == 0)
                    return [];
            }
            return Vault.Search(embed, count, LLMEngine.Settings.RAGDistanceCutOff);
        }

        #endregion

        #region *** Self contained string similarity check ***

        /// <summary>
        /// Compute cosine similarity distance between two strings using the embedding model.
        /// Returns a value in [0, 2]. Requires RAG to be Enabled.
        /// </summary>
        public static async Task<float> GetDistanceAsync(string a, string b)
        {
            if (!Enabled)
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
            if (!Enabled)
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
