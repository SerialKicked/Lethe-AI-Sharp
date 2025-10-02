using HNSW.Net;
using LetheAISharp.LLM;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    public class VaultResult(MemoryUnit memory, float dist)
    {
        public MemoryUnit Memory { get; set; } = memory;
        public float Distance { get; set; } = dist;
    }

    public class MemoryVault
    {
        private SmallWorld<float[], float> VectorDB;

        private readonly Dictionary<int, MemoryUnit> LookupDB = [];

        public int Count => VectorDB?.Items?.Count ?? 0;


        public MemoryVault()
        {
            VectorDB = new SmallWorld<float[], float>(Vector.IsHardwareAccelerated ? CosineDistance.SIMDForUnits : CosineDistance.ForUnits, new RNGPlus(), GetParams(), false);
        }

        private static SmallWorld<float[], float>.Parameters GetParams()
        {
            return new SmallWorld<float[], float>.Parameters()
            {
                M = LLMEngine.Settings.RAGMValue,
                LevelLambda = 1 / Math.Log(LLMEngine.Settings.RAGMValue),
                NeighbourHeuristic = LLMEngine.Settings.RAGHeuristic,
            };
        }

        public void Clear()
        {
            LookupDB.Clear();
            VectorDB = new SmallWorld<float[], float>(Vector.IsHardwareAccelerated ? CosineDistance.SIMDForUnits : CosineDistance.ForUnits, new RNGPlus(), GetParams(), false);
        }

        public void AddMemories(List<MemoryUnit> memories)
        {
            var vectors = new List<float[]>();
            var id = 0;
            foreach (var mem in memories)
            {
                if (mem.EmbedSummary == null || mem.EmbedSummary.Length == 0)
                {
                    LLMEngine.Logger?.LogWarning("MemoryVault: Memory '{MemoryId}' has no embedding, skipping.", mem.Name);
                    continue;
                }
                vectors.Add(mem.EmbedSummary);
                LookupDB[id] = mem;
                id++;
            }
            VectorDB.AddItems(vectors);
        }

        public async Task<List<VaultResult>> Search(string search, int maxRes, float? maxDist)
        {
            if (Count == 0 || !RAGEngine.Enabled)
                return [];
            var emb = await RAGEngine.EmbeddingText(search).ConfigureAwait(false);
            return Search(emb, maxRes, maxDist);
        }

        public List<VaultResult> Search(float[] search, int maxCount, float? maxDist)
        {
            if (Count == 0 || !RAGEngine.Enabled)
                return [];

            var found = VectorDB.KNNSearch(search, maxCount);
            var res = new List<VaultResult>();
            foreach (var item in found)
            {
                res.Add(new VaultResult(LookupDB[item.Id], item.Distance));
            }
            if (maxDist is not null)
                res.RemoveAll(e => e.Distance > maxDist);
            res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return res;
        }


        public void ExportVectorDB(string filePath)
        {
            var tosave = VectorDB.Items;
            byte[] bytes = MessagePackSerializer.Serialize(tosave);
            File.WriteAllBytes(filePath, bytes);
        }

        public void ImportVectorDB(string filePath)
        {
            Clear();
            byte[] bytes = File.ReadAllBytes(filePath);
            var x = MessagePackSerializer.Deserialize<IReadOnlyList<float[]>>(bytes);
            if (x == null || x.Count == 0)
                return;
            VectorDB.AddItems(x);
        }

    }

    /// <summary>
    /// Basic RNG for the SmallWorld implementation (not thread safe)
    /// </summary>
    internal class RNGPlus : IProvideRandomValues
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
    internal class ThreadSafeRNG : IProvideRandomValues
    {
        private readonly ThreadLocal<Random> threadLocalRandom = new(() => new Random(Interlocked.Increment(ref seed)));
        private static int seed = Environment.TickCount;

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
}
