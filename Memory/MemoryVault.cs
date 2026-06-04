using HNSW.Net;
using LetheAISharp.LLM;
using MessagePack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    /// <summary>
    /// RAG selection method
    /// </summary>
    public enum RAGSelectionHeuristic
    {
        /// <summary>
        /// Uses SmallWorld vector DB simple graph building: fast, but slightly less accurate.
        /// </summary>
        SelectSimple,
        /// <summary>
        /// Uses SmallWorld vector DB heuristic graph building: better choice for large datasets and varied types of data.
        /// </summary>
        SelectHeuristic,
        /// <summary>
        /// Uses exact distance calculation for all entries: best accuracy but slightly slower.
        /// </summary>
        SelectExact
    }

    public class VaultResult(MemoryUnit memory, float dist)
    {
        public MemoryUnit Memory { get; set; } = memory;
        public float Distance { get; set; } = dist;
    }

    public static class VaultResultExtensions
    {
        public static bool Contains(this List<VaultResult> list, MemoryUnit mem) => list.Find(e => e.Memory == mem) != null;
    }

    public class MemoryVault
    {
        private SmallWorld<float[], float> VectorDB;

        private readonly Dictionary<int, MemoryUnit> LookupDB = [];

        public SmallWorldParameters Parameters;

        public int Count => VectorDB?.Items?.Count ?? 0;

        public MemoryVault()
        {
            Parameters = new()
            {
                M = LLMEngine.Settings.RAGMValue,
                LevelLambda = 1.0 / Math.Log(LLMEngine.Settings.RAGMValue),
                ExpandBestSelection = true,
                NeighbourHeuristic = LLMEngine.Settings.RAGHeuristic == RAGSelectionHeuristic.SelectHeuristic ? NeighbourSelectionHeuristic.SelectHeuristic : NeighbourSelectionHeuristic.SelectSimple,
            };
            VectorDB = new SmallWorld<float[], float>(Vector.IsHardwareAccelerated ? CosineDistance.SIMDForUnits : CosineDistance.ForUnits, new ThreadSafeRNG(), Parameters, false);
        }


        public void Clear()
        {
            LookupDB.Clear();
            Parameters = new()
            {
                M = LLMEngine.Settings.RAGMValue,
                LevelLambda = 1.0 / Math.Log(LLMEngine.Settings.RAGMValue),
                ExpandBestSelection = true,
                NeighbourHeuristic = LLMEngine.Settings.RAGHeuristic == RAGSelectionHeuristic.SelectHeuristic ? NeighbourSelectionHeuristic.SelectHeuristic : NeighbourSelectionHeuristic.SelectSimple,
            };
            VectorDB = new SmallWorld<float[], float>(Vector.IsHardwareAccelerated ? CosineDistance.SIMDForUnits : CosineDistance.ForUnits, new ThreadSafeRNG(), Parameters, false);
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

        public async Task<List<VaultResult>> Search(string search, int maxRes, float? maxDist = null)
        {
            if (Count == 0 || !LLMEngine.Settings.RAGEnabled)
                return [];
            var emb = await EmbedTools.EmbeddingText(search).ConfigureAwait(false);
            return Search(emb, maxRes, maxDist);
        }

        public List<VaultResult> Search(float[] search, int maxCount, float? maxDist = null)
        {
            if (Count == 0 || !LLMEngine.Settings.RAGEnabled)
                return [];

            if (LLMEngine.Settings.RAGHeuristic == RAGSelectionHeuristic.SelectExact)
            {
                return NativeSearch(search, maxCount, maxDist);
            }

            var found = VectorDB.KNNSearch(search, maxCount);
            var res = new List<VaultResult>();
            foreach (var item in found)
                res.Add(new VaultResult(LookupDB[item.Id], item.Distance));
            if (maxDist is not null)
                res.RemoveAll(e => e.Distance > maxDist);
            res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return res;
        }

        private List<VaultResult> NativeSearch(float[] search, int maxCount, float? maxDist = null)
        {
            var res = new List<VaultResult>();
            foreach (var item in LookupDB)
            {
                var dist = EmbedTools.GetDistance(search, item.Value.EmbedSummary);
                if (maxDist is not null && dist > maxDist)
                    continue;
                res.Add(new VaultResult(item.Value, dist));
            }
            if (res.Count > maxCount)
                res = res.GetRange(0, maxCount);
            res.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return res;
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
