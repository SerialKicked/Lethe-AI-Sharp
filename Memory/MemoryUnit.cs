using AIToolkit.Files;
using AIToolkit.LLM;
using Newtonsoft.Json;
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

        public int Priority { get; set; } = 1;

        public async Task EmbedText()
        {
            EmbedSummary = await RAGSystem.EmbeddingText(Content);
            if (!string.IsNullOrWhiteSpace(Name))
                EmbedName = await RAGSystem.EmbeddingText(Name);
        }
    }

    public class Brain
    {
        public TimeSpan MinInsertDelay { get; set; } = TimeSpan.FromMinutes(15);
        public int MinMessageDelay { get; set; } = 5;
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);
        public DateTime LastInsertTime { get; set; }
        public int CurrentDelay = 0;
        public List<MemoryUnit> Memories { get; set; } = [];


        [JsonIgnore] public Queue<MemoryUnit> Eurekas { get; set; } = [];

        public void CharacterLoad()
        {
            // Remove old natural memories
            Memories.RemoveAll(e => e.Insertion == MemoryInsertion.Natural && (DateTime.Now - e.Added) > EurekaCutOff);
            // Select all natural memories within the cutoff period, order by Added descending, and enqueue them
            Eurekas.Clear();
            var cutoff = DateTime.Now - EurekaCutOff;
            var recent = Memories.Where(m => m.Insertion == MemoryInsertion.Natural && m.Added >= cutoff)
                                .OrderByDescending(m => m.Added).ToList();
            foreach (var item in recent)
                Eurekas.Enqueue(item);
        }

        public void OnUserPost(string userinput)
        {
            if (!string.IsNullOrWhiteSpace(LLMSystem.Settings.ScenarioOverride) || Eurekas.Count == 0)
                return;
            CurrentDelay++;
            if (CurrentDelay < MinMessageDelay || LastInsertTime + MinInsertDelay > DateTime.Now)
                return;
            InsertEureka();
        }

        public void InsertEureka()
        {
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            if (!Eurekas.TryDequeue(out var memory))
                return;
            var text = new StringBuilder($"{{char}} remembers something they've researched on the web recently. This was about the following topic: {memory.Name}.").AppendLinuxLine().AppendLinuxLine($"{memory.Content}");
            var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, text.ToString(), LLMSystem.Bot.UniqueName, LLMSystem.User.UniqueName, true);
            LLMSystem.History.LogMessage(tosend);
            // High Priority memories are kept and set back to Trigger insertion
            if (memory.Priority > 1)
            {
                memory.Insertion = MemoryInsertion.Trigger;
            }
        }


        public void OnNewSession()
        {
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            CharacterLoad();
        }

        public bool Has(MemoryType memType, Guid sessionId) => Memories.Any(m => m.Category == memType && m.Guid == sessionId);

    }
}
