using HNSW.Net;
using LLama;
using LLama.Common;
using LLama.Extensions;
using MessagePack;
using Microsoft.Extensions.Logging;
using System.Numerics;
using AIToolkit.Files;

namespace AIToolkit.LLM
{
    /// <summary>
    /// Basic RNG for the SmallWorld implementation (not thread safe)
    /// </summary>
    class RNGPlus : IProvideRandomValues
    {
        private readonly Random RNG = new();
        public bool IsThreadSafe => false;
        public float NextFloat() => (float)RNG.NextDouble();
        public int Next(int minValue, int maxValue) => RNG.Next(minValue, maxValue);
        public void NextFloats(Span<float> buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = (float)RNG.NextDouble();
        }
    }

    /// <summary>
    /// Thread-safe RNG for the SmallWorld implementation
    /// </summary>
    class ThreadSafeRNG : IProvideRandomValues
    {
        private readonly ThreadLocal<Random> threadLocalRandom = new(() => new Random(Interlocked.Increment(ref seed)));
        private static int seed = Environment.TickCount;

        //private readonly Random RNG = new();
        public bool IsThreadSafe => true;
        public float NextFloat() => (float)threadLocalRandom.Value!.NextDouble();
        public int Next(int minValue, int maxValue) => threadLocalRandom.Value!.Next(minValue, maxValue);
        public void NextFloats(Span<float> buffer)
        {
            var rng = threadLocalRandom.Value;
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = (float)rng!.NextDouble();
        }
    }

    class VectorSearchResult(Guid id, EmbedType category, float dist)
    {
        public Guid ID = id;
        public EmbedType Category = category;
        public float Distance = dist;
    }

    public enum EmbedType { Title, Summary, Session, Document, WorldInfo }

    /// <summary>
    /// Retrieval Augmented Generation System
    /// 
    /// </summary>
    public static class RAGSystem
    {
        /// <summary> Called when the system embedded a session </summary>
        public static event EventHandler<Files.ChatSession>? OnEmbedSession;

        private static void RaidOnEmbedSession(Files.ChatSession session) => OnEmbedSession?.Invoke(null, session);

        /// <summary> Toggle RAG functionalities on/off </summary>
        public static bool Enabled
        {
            get => LLMSystem.Settings.RAGEnabled;
            set
            {
                LLMSystem.Settings.RAGEnabled = value;
                if (!LLMSystem.Settings.RAGEnabled)
                    UnloadEmbedder();
            }
        }
        // Embedding model's weights and params
        private static ModelParams? EmbedSettings = null;
        private static LLamaWeights? EmbedWeights = null;
        private static LLamaEmbedder? Embedder = null;
        private static SmallWorld<float[], float> VectorDB = null!;
        private static int VectorDBCount => VectorDB?.Items?.Count ?? 0;
        private static bool IsVectorDBLoaded = false;

        public static Dictionary<int, (Guid ID, EmbedType embedType)> LookupDB { get; private set; } = [];

        public static void ApplySettings()
        {
            if (!Enabled)
                return;
            ResetVectorDB();
            if (LLMSystem.History != null)
                VectorizeChatBot(LLMSystem.Bot);
        }

        private static void ResetVectorDB()
        {
            var parameters = new SmallWorld<float[], float>.Parameters()
            {
                M = LLMSystem.Settings.RAGMValue,
                LevelLambda = 1 / Math.Log(LLMSystem.Settings.RAGMValue),
                NeighbourHeuristic = LLMSystem.Settings.RAGHeuristic,
            };
            VectorDB = new SmallWorld<float[], float>(Vector.IsHardwareAccelerated ? CosineDistance.SIMDForUnits : CosineDistance.ForUnits, new RNGPlus(), parameters, false);
            IsVectorDBLoaded = false;
        }

        /// <summary>
        /// Load the Embedding model in memory
        /// </summary>
        /// <returns></returns>
        private static LLamaEmbedder LoadEmbedder()
        {
            if (EmbedSettings != null)
                UnloadEmbedder();
            EmbedSettings = new ModelParams(string.Format("data/models/{0}.gguf", "gte-large.Q6_K"))
            { 
                GpuLayerCount = 255,
                Embeddings = true
            };
            LLMSystem.Settings.RAGEmbeddingSize = 1024;
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
        /// Embedding of all the messages in the chatlog
        /// </summary>
        /// <param name="log"></param>
        /// <returns></returns>
        public static async Task EmbedChatSessions(Chatlog log)
        {
            if (!Enabled)
                return;
            _ = Embedder ?? LoadEmbedder();
            // Embed all the messages in the chatlog
            foreach (var session in log.Sessions)
            {
                await session.GenerateEmbeds();
                RaidOnEmbedSession(session);
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
            if (emb.Length > LLMSystem.Settings.RAGEmbeddingSize)
                emb = emb[..LLMSystem.Settings.RAGEmbeddingSize];
            var tsk = await embed.GetEmbeddings(emb);
            return tsk[0].EuclideanNormalization();
        }

        public static void VectorizeChatBot(BasePersona persona)
        {
            if (!Enabled)
                return;
            ResetVectorDB();
            var log = persona.History;
            if (log.Sessions.Count == 0 && persona.MyWorlds.Count == 0)
                return;

            var vectors = new List<float[]>();
            LookupDB = [];
            var currentID = 0;

            for (int i = 0; i < log.Sessions.Count; i++)
            {
                var session = log.Sessions[i];
                if (session.EmbedTitle.Length == 0 || session.EmbedSummary.Length == 0)
                    continue;
                if (LLMSystem.Settings.RAGUseTitles)
                {
                    vectors.Add(session.EmbedTitle);
                    LookupDB[currentID] = (session.Guid, EmbedType.Title);
                    currentID++;
                }
                if (LLMSystem.Settings.RAGUseSummaries)
                {
                    vectors.Add(session.EmbedSummary);
                    LookupDB[currentID] = (session.Guid, EmbedType.Summary);
                    currentID++;
                }
            }

            foreach (var world in persona.MyWorlds)
            {
                if (!world.DoEmbeds)
                    continue;
                foreach (var entry in world.Entries)
                {
                    if (!entry.Enabled || entry.EmbedSummary.Length == 0)
                        continue;
                    vectors.Add(entry.EmbedSummary);
                    LookupDB[currentID] = (entry.Guid, EmbedType.WorldInfo);
                    currentID++;
                }
            }   

            try
            {
                VectorDB.AddItems(vectors);
            }
            catch (Exception e)
            {
                throw new Exception("Error adding items to the VectorDB", e);
            }
            IsVectorDBLoaded = true;
        }

        public static async Task<List<(IEmbed session, EmbedType category, float distance)>> Search(string message)
        {
            if (!Enabled)
                return [];
            if (!IsVectorDBLoaded || VectorDB == null || VectorDBCount == 0)
            {
                ResetVectorDB();
                VectorizeChatBot(LLMSystem.Bot);
            }
            // Check if message contains the words RP or roleplay
            var RPCheck = message.Contains(" RP", StringComparison.OrdinalIgnoreCase) || message.Contains(" roleplay", StringComparison.OrdinalIgnoreCase);

            var emb = await EmbeddingText(message);
            var subcount = LLMSystem.Settings.RAGMaxEntries + 2;
            // If we have both titles and summaries double result count to get a better picture
            if (LLMSystem.Settings.RAGUseSummaries && LLMSystem.Settings.RAGUseTitles)
                subcount += LLMSystem.Settings.RAGMaxEntries + 2;
            if (LLMSystem.Settings.AllowWorldInfo)
                subcount += 1;
            var res = Search(emb, subcount);
            if (LLMSystem.Settings.RAGUseSummaries && LLMSystem.Settings.RAGUseTitles)
            {
                // look for ID triggered by both summary and title
                var newlist = new List<VectorSearchResult>();
                foreach (var item in res)
                {
                    // if no item or item already in newlist, skip
                    if (item == null || newlist.Find(e => e.ID == item.ID) != null)
                        continue;

                    // find if other with same GUID
                    var copy = res.Find(e => e != item && e.ID == item.ID);
                    if (copy != null)
                    {
                        // we have a second candidate in the list, this makes this result a lot more probable
                        var boosted = new VectorSearchResult(item.ID, EmbedType.Session, (item.Distance + copy.Distance) / 2.5f);
                        newlist.Add(boosted);
                    }
                    else
                        newlist.Add(item);
                }
                res = newlist;
                res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            }
            foreach (var item in res)
            {
                if (item.Category == EmbedType.Summary || item.Category == EmbedType.Title)
                {
                    var found = LLMSystem.History.GetSessionByID(item.ID);
                    if (found != null && found.MetaData.IsRoleplaySession)
                    {
                        if (RPCheck)
                            item.Distance -= 0.1f; // Boost RP sessions
                        else
                            item.Distance += 0.1f; // Decay RP sessions
                    }
                }
            }
            res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            // Make sure we got the correct amount of results
            if (res.Count > LLMSystem.Settings.RAGMaxEntries)
                res = res.GetRange(0, LLMSystem.Settings.RAGMaxEntries);
            res.RemoveAll(e => e.Distance > LLMSystem.Settings.RAGDistanceCutOff);
            var list = new List<(IEmbed session, EmbedType category, float distance)>();
            foreach (var item in res)
            {
                if (item == null)
                    continue;
                if (item.Category == EmbedType.WorldInfo)
                {
                    var found = LLMSystem.Bot.GetWIEntryByGUID(item.ID);
                    if (found != null)
                        list.Add((found, item.Category, item.Distance));
                }
                else
                {
                    var found = LLMSystem.History.GetSessionByID(item.ID);
                    if (found != null)
                        list.Add((found, item.Category, item.Distance));
                }
            }
            return list;
        }

        private static List<VectorSearchResult> Search(float[] message, int count)
        {
            LLMSystem.Logger?.LogInformation("LTM Size: {size} out of {logsize}", VectorDBCount.ToString(), LLMSystem.History.Sessions.Count.ToString());
            if (!IsVectorDBLoaded || VectorDBCount == 0)
                return [];
            var found = VectorDB.KNNSearch(message, count);
            var res = new List<VectorSearchResult>();
            foreach (var item in found)
            {
                res.Add(new VectorSearchResult(LookupDB[item.Id].ID, LookupDB[item.Id].embedType, item.Distance));
                LLMSystem.Logger?.LogInformation("LTM Found: {id} ({distance})", item.Id.ToString(), item.Distance.ToString());
            }
            res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return res;
        }

        public static void ExportVectorDB(string filePath)
        {
            var tosave = VectorDB.Items;
            byte[] bytes = MessagePackSerializer.Serialize(tosave);
            File.WriteAllBytes(filePath, bytes);
        }

        public static void ImportVectorDB(string filePath)
        {
            ResetVectorDB();
            byte[] bytes = File.ReadAllBytes(filePath);
            var x = MessagePackSerializer.Deserialize<IReadOnlyList<float[]>>(bytes);
            if (x == null || x.Count == 0)
                return;
            VectorDB.AddItems(x);
            IsVectorDBLoaded = true;
        }

        public static void RemoveEmbedEventHandler()
        {
            OnEmbedSession = null;
        }
    }
}
