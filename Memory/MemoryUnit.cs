using AIToolkit.Files;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Memory
{
    public enum MemoryInsertionStrategy
    {
        /// <summary> Insert when a trigger (RAG or Keyword) is activated during chat </summary>
        Trigger,
        /// <summary> Automatically inserted into prompt </summary>
        Auto,
        /// <summary> Turned off </summary>
        None
    }

    public enum MemoryType { General, WorldInfo, WebSearch, Journal, Image, File, Location, Event, ChatSession }
    public enum MemoryInsertion { Trigger, Natural, None }

    public class MemoryUnit : KeywordEntry, IEmbed
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public MemoryType Category { get; set; } = MemoryType.General;
        public MemoryInsertion Insertion { get; set; } = MemoryInsertion.Trigger;
        public string Name { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime Added { get; set; } = DateTime.Now;
        public DateTime EndTime { get; set; } = DateTime.Now;
        public float[] EmbedSummary { get; set; } = [];
        public float[] EmbedName { get; set; } = [];

        public async Task EmbedText()
        {
            EmbedSummary = await RAGSystem.EmbeddingText(Content);
            if (!string.IsNullOrWhiteSpace(Name))
                EmbedName = await RAGSystem.EmbeddingText(Name);
        }
    }

    public class Brain
    {
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);

        public List<MemoryUnit> Memories { get; set; } = [];

        public Queue<MemoryUnit> Eurekas { get; set; } = [];

        public void LoadEurekas()
        {
            Eurekas.Clear();
            var cutoff = DateTime.Now - EurekaCutOff;
            var recent = Memories.Where(m => m.Insertion == MemoryInsertion.Natural && m.Added >= cutoff)
                                .OrderByDescending(m => m.Added).ToList();
            foreach (var item in recent)
                Eurekas.Enqueue(item);
        }

        public bool Has(MemoryType memType, Guid sessionId) => Memories.Any(m => m.Category == memType && m.Guid == sessionId);

    }
}
