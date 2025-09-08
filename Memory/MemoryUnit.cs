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

    public enum MemoryType { General, WorldInfo, WebSearch, ChatSession, Journal, Image, File, Location, Event, Person, Goal }
    public enum MemoryInsertion { Trigger, Natural, NaturalForced, None }


    /// <summary>
    /// Individual long term and contextual memory entry
    /// </summary>
    public class MemoryUnit : KeywordEntry, IEmbed
    {
        /// <summary>
        /// Unique Identifier
        /// </summary>
        public Guid Guid { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Memory category/type - will likely affects how it's used
        /// </summary>
        public MemoryType Category { get; set; } = MemoryType.General;

        /// <summary>
        /// Insertion type. Trigger: used as a RAG entry. Natural: inserted into prompt during live conversation when relevant, and then converted to Trigger if of high enough relevance. None: Disabled.
        /// </summary>
        public MemoryInsertion Insertion { get; set; } = MemoryInsertion.Trigger;

        /// <summary>
        /// Name or Title for the entry. May be inserted for some Category (like people, file, locations...) and insertion types
        /// </summary>
        public virtual string Name { get; set; } = string.Empty;

        /// <summary>
        /// Raw content of the memory
        /// </summary>
        public virtual string Content { get; set; } = string.Empty;

        /// <summary>
        /// The context or reason why this topic is of interest to the bot, user, or both.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Should be used files
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// When this memory was added
        /// </summary>
        public DateTime Added { get; set; } = DateTime.Now;

        /// <summary>
        /// When this memory is meant to be deprecated or turned from Natural to Trigger insertion
        /// </summary>
        public DateTime EndTime { get; set; } = DateTime.Now;

        public DateTime LastTrigger { get; set; } = DateTime.Now;

        public int TriggerCount { get; set; } = 0;

        /// <summary>
        /// Embedding data for RAG
        /// </summary>
        public float[] EmbedSummary { get; set; } = [];

        /// <summary>
        /// How important this memory is
        /// </summary>
        public int Priority { get; set; } = 1;

        public int PositionIndex { get; set; } = 0;
        public int Duration { get; set; } = 1;
        public WEPosition Position { get; set; } = WEPosition.SystemPrompt;
        public float TriggerChance { get; set; } = 1;

        public virtual async Task EmbedText()
        {
            if (!RAGSystem.Enabled)
                return;
            var mixedcat = new HashSet<MemoryType>() 
            { 
                MemoryType.ChatSession, MemoryType.Journal, MemoryType.WebSearch, MemoryType.Person, MemoryType.Location, MemoryType.Event 
            };
            if (!mixedcat.Contains(Category))
            {
                EmbedSummary = await RAGSystem.EmbeddingText(LLMSystem.ReplaceMacros(Content));
                return;
            }
            var titleembed = await RAGSystem.EmbeddingText(Name);
            var sumembed = await RAGSystem.EmbeddingText(LLMSystem.ReplaceMacros(Content));
            EmbedSummary = RAGSystem.MergeEmbeddings(titleembed, sumembed);
        }

        /// <summary>
        /// Turn a memory into a Eureka prompt for the LLM to use during conversation
        /// </summary>
        /// <returns></returns>
        public string ToEureka()
        {
            var text = new StringBuilder();
            switch (Category)
            {
                case MemoryType.Person:
                    text.Append($"Here's the information you remember about {Name}.");
                    break;
                case MemoryType.Location:
                    text.Append($"You remember something about this location: {Name}.");
                    break;
                case MemoryType.Goal:
                    text.Append($"You remember you've set this goal for yourself: {Name}.");
                    break;
                case MemoryType.WebSearch:
                    text.Append($"You remember something you've found on the web recently about '{Name}'.");
                    break;
                default:
                    text.Append($"This is some information regarding '{Name}'.");
                    break;
            }

            if (!string.IsNullOrEmpty(Reason))
            {
                text.AppendLinuxLine($" Your reason for it was: {Reason}.");
            }
            text.AppendLinuxLine().AppendLinuxLine($"{Content}").AppendLinuxLine().Append("Mention this information when there's a lull in the discussion, if the user makes a mention of it, or if you feel like it's a good idea to talk about it.");
            return text.ToString();
        }
    }
}
