using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    /// <summary>
    /// Represents the type of memory or context used in the application.
    /// </summary>
    /// <remarks>This enumeration categorizes different types of memory or contextual information that can be
    /// stored or processed. It is used to distinguish between various domains or purposes. Internally, only
    /// ChatSession, WorldInfo, and WebSearch are actively used. Other types are there for convenience and do
    /// not have to correspond to their stated purpose.</remarks>
    public enum MemoryType { General, WorldInfo, WebSearch, ChatSession, Journal, Image, File, Location, Event, Person, Goal, Reminder }

    /// <summary>
    /// Specifies the modes of memory insertion for a specific MemoryUnit.
    /// </summary>
    /// <remarks>This enumeration defines the available options for memory insertion behavior, which can be
    /// used to control how memory is inserted or managed in specific scenarios.</remarks>
    public enum MemoryInsertion 
    {
        /// <summary>
        /// Trigger-based insertion: The memory is inserted in the prompt in a preset and fixed position based on similarity to user input or keyword. 
        /// Insertion location and duration are determined by MemoryUnit properties.
        /// </summary>
        Trigger,
        /// <summary>
        /// Natural insertion: The memory is inserted into the prompt as a system message above the user last input, based on similarity to user input or keyword. 
        /// It will "scroll" alongside the chat messages as more are being added, ensuring a long term impact. It is converted to Trigger after use. 
        /// Triggered
        /// </summary>
        Natural,
        /// <summary>
        /// The memory will be inserted into the prompt in a similar fashion to Natural, but no trigger is required. 
        /// Instead, the MemoryUnit is inserted as soon as possible (no other natural insert in the last few question-response pairs). 
        /// Converted to Trigger after use.
        /// </summary>
        /// <remarks>It is not recommended to use this insertion type often as it will disrupt the flow of conversation and may confuse the chatbot.</remarks>
        NaturalForced,
        /// <summary>
        /// The memory will not be inserted into the prompt automatically.
        /// </summary>
        None,
        /// <summary>
        /// The memory's content will be inserted when the user returns from AFK as a part of the hidden system message usually containing date info, 
        /// and (optionally) mood state. It will only be inserted if Added field is lower than latest user message. Memory is set to None after use to avoid 
        /// re-insertion on subsequent returns. Content will be inserted "as is" with no title, it is recommended to keep it brief.
        /// </summary>
        UserReturn
    }

    /// <summary>
    /// Specifies the types of title insertions that can be applied.
    /// </summary>
    /// <remarks>This enumeration defines the available formats for inserting titles, ranging from no formatting
    /// to specific styles such as bold or Markdown headers. Use the appropriate value to control how titles are rendered.</remarks>
    public enum TitleInsertType { None, Simple, Bold, MarkdownH2, MarkdownH3 }


    /// <summary>
    /// Represents a unit of memory that can be used to store and manage information relevant to the bot's operation.
    /// </summary>
    /// <remarks>The <see cref="MemoryUnit"/> class provides a flexible structure for managing various types
    /// of memory, such as general information, world knowledge, chat sessions, and more. Each memory unit is
    /// categorized by a <see cref="MemoryType"/> and can be configured with properties such as priority, duration, and
    /// insertion behavior. It supports keyword-based triggering, embedding for AI tasks, and sentiment analysis. This
    /// class is designed to facilitate the organization and retrieval of information in conversational AI systems,
    /// enabling context-aware interactions and dynamic memory management.</remarks>
    public class MemoryUnit : IEmbed
    {
        /// <summary>
        /// Helpful keywords for each memory type to populate keyword lists or nudge similarity searches
        /// </summary>
        public static Dictionary<MemoryType, List<string>> EmbedHelpers { get; private set; } = new Dictionary<MemoryType, List<string>>
        {
            { MemoryType.General, [] },
            { MemoryType.Reminder, [] },
            { MemoryType.WorldInfo, [] },
            { MemoryType.WebSearch, [ "web", "internet", "search", "online" ] },
            { MemoryType.ChatSession, [ "remember", "that time", "recall", "discussion" ] },
            { MemoryType.Journal, [ "journal", "diary", "log", "entry" ] },
            { MemoryType.Image, [ "image", "picture", "photo", "visual" ] },
            { MemoryType.File, [ "file", "document", "record" ] },
            { MemoryType.Location, [ "location", "place", "area" ] },
            { MemoryType.Event, [ "event", "happening", "occasion", "incident" ] },
            { MemoryType.Person, [ "person", "individual", "character", "friend", "buddy", "people" ] },
            { MemoryType.Goal, [ "goal", "objective", "aim", "target" ] }
        };

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

        /// <summary>
        /// Date of the last time this memory was inserted into the prompt
        /// </summary>
        public DateTime LastTrigger { get; set; } = DateTime.Now;

        /// <summary>
        /// Number of times this memory has been triggered
        /// </summary>
        public int TriggerCount { get; set; } = 0;

        /// <summary>
        /// Embedding data for RAG
        /// </summary>
        public float[] EmbedSummary { get; set; } = [];

        /// <summary>
        /// How important this memory is
        /// </summary>
        public int Priority { get; set; } = 1;

        /// <summary>
        /// Insertion position index. 0 = most recent -> 100 = least recent. -1 to insert into system prompt instead.
        /// </summary>
        public int PositionIndex { get; set; } = 0;

        /// <summary>
        /// How long this memory should last (in turns) in the prompt before being pruned
        /// </summary>
        public int Duration { get; set; } = 1;

        /// <summary>
        /// Likelihood (0.0 to 1.0) of this memory being triggered when its conditions are met
        /// </summary>
        public float TriggerChance { get; set; } = 1;

        /// <summary>
        /// If set to true, this memory will always be included in the prompt
        /// </summary>
        public bool Sticky { get; set; } = false;

        /// <summary>
        /// If set to true, this memory cannot be pruned automatically or overwritten by similar ones when adding new memories
        /// </summary>
        public bool Protected { get; set; } = false;

        /// <summary>
        /// Keyword trigger: If set to true, keyword checking is enabled for this memory, otherwise no keyword checking is performed
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Keyword trigger: list of main keywords that will trigger this memory (any keyword found in list count as true)
        /// </summary>
        public List<string> KeyWordsMain { get; set; } = [];

        /// <summary>
        /// Keyword trigger: list of secondary keywords that will trigger this memory (any keyword found in list count as true)
        /// </summary>
        public List<string> KeyWordsSecondary { get; set; } = [];

        /// <summary>
        /// Keyword trigger: logic to apply between main and secondary keywords
        /// </summary>
        public KeyWordLink WordLink { get; set; } = KeyWordLink.And;

        /// <summary>
        /// Keyword trigger: If set to true, keyword matching is case sensitive
        /// </summary>
        public bool CaseSensitive { get; set; } = false;

        public bool MetaInfoToggle { get; set; } = false;

        public string MetaInfo { get; set; } = string.Empty;

        /// <summary>
        /// Generates a formatted text snippet based on the specified options.
        /// </summary>
        /// <remarks>This method allows for flexible formatting of the snippet, supporting various title
        /// styles, optional inclusion of dates,  and compacting of content. The output is cleaned and trimmed to ensure
        /// a polished result.</remarks>
        /// <param name="includeTitle">Specifies whether and how the title should be included in the snippet.  Use <see
        /// cref="TitleInsertType.None"/> to exclude the title, or other values to format the title accordingly.</param>
        /// <param name="includeDate">A value indicating whether the date range should be included in the snippet. If <see langword="true"/>, the
        /// date range will be appended to the content.</param>
        /// <param name="includeReason">A value indicating whether the reason should be included in the snippet. If <see langword="true"/>, the
        /// reason will be appended at the end of the snippet (assuming the field is not empty).</param>
        /// <param name="compactContent">A value indicating whether the content should be compacted by removing new lines. If <see langword="true"/>,
        /// new lines in the content will be removed.</param>
        /// <returns>A formatted string containing the generated snippet based on the specified options. The snippet may include
        /// the title, date range, content, and reason, depending on the provided parameters.</returns>
        public virtual string ToSnippet(TitleInsertType includeTitle, bool includeDate, bool includeReason, bool compactContent)
        {
            var sb = new StringBuilder();
            if (includeTitle != TitleInsertType.None && !string.IsNullOrEmpty(Name))
            {
                sb.Append(includeTitle switch
                {
                    TitleInsertType.Simple => $"{Name}: ",
                    TitleInsertType.Bold => $"**{Name}:**\n",
                    TitleInsertType.MarkdownH2 => $"## {Name}\n",
                    TitleInsertType.MarkdownH3 => $"### {Name}\n",
                    _ => $"{Name}\n"
                });
            }
            var content = compactContent ? Content.RemoveNewLines() : Content;
            if (includeDate)
            {
                sb.AppendLinuxLine($" {StringExtensions.FormatDateRange(Added, EndTime)}: {content}");
            }
            else
            {
                sb.AppendLinuxLine(content);
            }
            if (includeReason && !string.IsNullOrEmpty(Reason))
            {
                sb.AppendLinuxLine($"The reason for it was: {Reason}");
            }
            return LLMEngine.Bot.ReplaceMacros(sb.ToString()).CleanupAndTrim();
        }

        /// <summary>
        /// Updates the last trigger timestamp to the current time and increments the trigger count.
        /// </summary>
        /// <remarks>This method sets the <see cref="LastTrigger"/> property to the current date and time 
        /// and increases the <see cref="TriggerCount"/> property by one. It is typically used to record that the memory got triggered.</remarks>
        public void Touch()
        {
            LastTrigger = DateTime.Now; TriggerCount++;
        }

        /// <summary>
        /// Embeds the text content into a vector representation for use in AI-related tasks, such as semantic search.
        /// </summary>
        /// <remarks>This method performs text embedding based on the current category of the content. If
        /// the category is not one of the predefined mixed categories,  the content is embedded directly. For mixed
        /// categories, the method combines embeddings of the content and its associated name to produce a merged
        /// embedding. The operation is asynchronous and depends on the RAG (Retrieval-Augmented Generation) setting
        /// being enabled in the LLM engine.</remarks>
        /// <returns></returns>
        public virtual async Task BuildEmbedding()
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return;
            var mixedcat = new HashSet<MemoryType>() 
            { 
                MemoryType.ChatSession, MemoryType.Journal, MemoryType.WebSearch, MemoryType.Person, MemoryType.Location, MemoryType.Event, MemoryType.Goal
            };
            if (!mixedcat.Contains(Category))
            {
                EmbedSummary = await EmbedTools.EmbeddingText(LLMEngine.Bot.ReplaceMacros(Content)).ConfigureAwait(false);
                return;
            }
            var titleembed = await EmbedTools.EmbeddingText(Name).ConfigureAwait(false);
            var sumembed = await EmbedTools.EmbeddingText(LLMEngine.Bot.ReplaceMacros(Content)).ConfigureAwait(false);
            EmbedSummary = EmbedTools.MergeEmbeddings(titleembed, sumembed);
        }

        /// <summary>
        /// Turn a memory into a Eureka prompt for the LLM to use during conversation
        /// </summary>
        /// <returns></returns>
        public string ToEureka()
        {
            var text = new StringBuilder();
            if (LLMEngine.Settings.AntiHallucinationMemoryFormat)
            {
                text.Append($"<SystemEvent>[{Category.ToString().ToUpperInvariant()}] - Topic: {Name}.");
            }
            else
            {
                switch (Category)
                {
                    case MemoryType.Person:
                        text.Append($"Here's relevant information about {Name}.");
                        break;
                    case MemoryType.Location:
                        text.Append($"You remember something about this location: {Name}.");
                        break;
                    case MemoryType.Goal:
                        text.Append($"{LLMEngine.Bot.Name} remembers they set this goal for themselves: {Name}.");
                        break;
                    case MemoryType.WebSearch:
                        text.Append($"{LLMEngine.Bot.Name} remembers something they found on the web about '{Name}'.");
                        break;
                    default:
                        text.Append($"This is some information regarding '{Name}'.");
                        break;
                }
            }

            if (!string.IsNullOrEmpty(Reason))
            {
                text.AppendLinuxLine($" The reason for it was: {Reason}.");
            }
            text.AppendLinuxLine().AppendLinuxLine($"{Content}");

            if (Category == MemoryType.Goal)
            {
                text.AppendLinuxLine().Append("Note: Use this information to guide the discussion if it's contextually relevant or adjacent. Make sure it fits in the conversation's flow naturally.");
            }
            else
                text.AppendLinuxLine().Append("Note: Mention this information when there's a lull in the discussion, if {{user}} makes a mention of it, or if you feel like it's a good idea to talk about it. Make sure it fits in the conversation's flow naturally.");

            return LLMEngine.Bot.ReplaceMacros(text.ToString().CleanupAndTrim());
        }

        /// <summary>
        /// Determines whether the specified message contains keywords that match the configured criteria.
        /// </summary>
        /// <remarks>The method evaluates the presence of keywords in the message based on the following
        /// criteria: <list type="bullet"> <item><description>The <c>Enabled</c> property must be <see langword="true"/>
        /// for the method to perform the check.</description></item> <item><description>The <c>KeyWordsMain</c>
        /// collection must contain at least one keyword, or the <c>KeyWordsSecondary</c> collection must contain at
        /// least one keyword.</description></item> <item><description>The <c>CaseSensitive</c> property determines
        /// whether the keyword matching is case-sensitive.</description></item> <item><description>The <c>WordLink</c>
        /// property specifies how the main and secondary keyword conditions are combined: <list type="bullet">
        /// <item><description><c>KeyWordLink.And</c>: Both main and secondary keyword conditions must be
        /// satisfied.</description></item> <item><description><c>KeyWordLink.Or</c>: Either the main or secondary
        /// keyword condition must be satisfied.</description></item> <item><description><c>KeyWordLink.Not</c>: The
        /// main keyword condition must be satisfied, and the secondary keyword condition must not be
        /// satisfied.</description></item> </list> </description></item> </list></remarks>
        /// <param name="message">The message to evaluate. Cannot be <see langword="null"/> or empty.</param>
        /// <returns><see langword="true"/> if the message contains keywords that satisfy the configured conditions; otherwise,
        /// <see langword="false"/>.</returns>
        public bool CheckKeywords(string message)
        {
            if (!IsKeyword || string.IsNullOrEmpty(message))
                return false;

            var comparison = CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            bool ContainsWholeWord(string input, string word)
            {
                return Regex.IsMatch(input, $@"\b{Regex.Escape(word)}\b", comparison);
            }

            var main = KeyWordsMain.Any(kw => ContainsWholeWord(message, kw));
            var secondary = KeyWordsSecondary.Count == 0 || KeyWordsSecondary.Any(kw => ContainsWholeWord(message, kw));

            return WordLink switch
            {
                KeyWordLink.And => main && secondary,
                KeyWordLink.Or => main || secondary,
                KeyWordLink.Not => main && !secondary,
                _ => false
            };
        }

        public bool IsKeyword => Enabled && (KeyWordsMain.Count > 0 || KeyWordsSecondary.Count > 0);
    }
}
