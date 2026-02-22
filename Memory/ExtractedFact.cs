using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    /// <summary>
    /// Represents a discrete fact extracted from a chat session, used as a lightweight semantic index
    /// that points back to richer source material (MemoryUnits, session summaries).
    /// </summary>
    /// <remarks>
    /// <para>Facts act as clean, focused embeddings that are far easier to retrieve via semantic search than
    /// multi-topic session summaries. Each fact stores the GUIDs of the source MemoryUnits it was extracted from,
    /// enabling two-hop retrieval: user input matches a fact → fact's source GUIDs pull in full session context.</para>
    /// <para>Facts are extracted once via structured output and never LLM-rewritten. Newer facts can
    /// deduplicate or supersede older ones, but there is no iterative summarization pass.</para>
    /// </remarks>
    public class ExtractedFact : IEmbed
    {
        /// <summary>Unique identifier for this fact.</summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        /// <summary>The concise, single-sentence fact extracted from the session.</summary>
        public string Fact { get; set; } = string.Empty;

        /// <summary>Embedding vector for the fact text, used for semantic similarity search.</summary>
        public float[] EmbedSummary { get; set; } = [];

        /// <summary>When this fact was first extracted.</summary>
        public DateTime FirstSeen { get; set; } = DateTime.Now;

        /// <summary>When this fact was last seen or updated via deduplication.</summary>
        public DateTime LastSeen { get; set; } = DateTime.Now;

        /// <summary>
        /// Number of times this fact has been referenced or confirmed across sessions.
        /// Incremented each time a deduplication match is found.
        /// </summary>
        public int ReferenceCount { get; set; } = 1;

        /// <summary>
        /// GUIDs of the MemoryUnits (typically ChatSessions) that this fact was extracted from.
        /// These are the payload: when this fact matches a search query, its source memories are retrieved directly.
        /// </summary>
        public List<Guid> SourceMemories { get; set; } = [];

        /// <summary>
        /// Indicates whether this fact has been superseded by a newer, more accurate fact.
        /// Superseded facts are excluded from system prompt inclusion and new retrieval,
        /// but their SourceMemories remain valid for historical queries.
        /// </summary>
        public bool Superseded { get; set; } = false;

        /// <summary>
        /// GUID of the fact that superseded this one, if any.
        /// Allows tracing the chain of supersession for debugging or historical retrieval.
        /// </summary>
        public Guid? SupersededBy { get; set; } = null;

        /// <summary>
        /// Embeds the fact text into a vector for semantic similarity search.
        /// Does nothing if RAG is disabled.
        /// </summary>
        public async Task EmbedText()
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return;
            EmbedSummary = await EmbedTools.EmbeddingText(Fact).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the importance score used for system prompt inclusion ranking.
        /// Score = ReferenceCount × recency_factor, where recency_factor decays over time.
        /// </summary>
        /// <returns>Importance score; higher means more important.</returns>
        public float GetImportanceScore()
        {
            var daysSinceLastSeen = (float)(DateTime.Now - LastSeen).TotalDays;
            var recencyFactor = 1f / (1f + daysSinceLastSeen * 0.05f);
            return ReferenceCount * recencyFactor;
        }
    }
}
