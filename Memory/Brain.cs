using AIToolkit;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace AIToolkit.Memory
{

    /// <summary>
    /// Brain functionality for a persona, handles memories, mood, and message inserts
    /// </summary>
    /// <param name="basePersona">Owner</param>
    public class Brain(BasePersona basePersona)
    {
        [JsonIgnore] private BasePersona Owner { get; set; } = basePersona;
        public TimeSpan MinInsertDelay { get; set; } = TimeSpan.FromMinutes(15);
        public int MinMessageDelay { get; set; } = 4;
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);
        public DateTime LastInsertTime { get; set; }
        public int CurrentDelay { get; set; } = 0;
        public HashSet<MemoryType> DecayableMemories { get; set; } = [ MemoryType.WebSearch, MemoryType.Goal ];

        public int MinNoRecallDaysBeforeDeletionPerPrioLevel { get; set; } = 10;

        public List<MemoryUnit> Memories { get; set; } = [];
        public List<TopicSearch> RecentSearches { get; set; } = [];
        [JsonIgnore] public List<MemoryUnit> Eurekas { get; set; } = [];

        public virtual MoodState Mood { get; set; } = new MoodState();

        /// <summary>
        /// Called to initialize the brain with its owner persona.
        /// </summary>
        /// <param name="owner"></param>
        public virtual void Init(BasePersona owner)
        {
            Owner = owner;
            RefreshMemories();
            owner.History.OnNewSession += DoOnNewSession;
        }

        /// <summary>
        /// Cleans up event subscriptions when the brain is no longer needed.
        /// </summary>
        public virtual void Close()
        {
            Owner.History.OnNewSession -= DoOnNewSession;
        }

        /// <summary>
        /// Initialization done when a new chat session starts.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected virtual void DoOnNewSession(object? sender, ChatSession e)
        {
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            RefreshMemories();
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        private void RefreshMemories()
        {
            MemoryDecay();
            // Select all natural memories within the cutoff period, order by Added descending, and enqueue them
            Eurekas.Clear();
            var cutoff = DateTime.Now - EurekaCutOff;
            var recent = Memories.Where(m => (m.Insertion == MemoryInsertion.Natural || m.Insertion == MemoryInsertion.NaturalForced) && m.Added >= cutoff).OrderByDescending(m => m.Added).ToList();
            foreach (var item in recent)
                Eurekas.Add(item);
        }

        /// <summary>
        /// Handles incoming user messages and processes them based on the system's state, mood, and context.
        /// </summary>
        /// <remarks>This method performs several operations depending on the system's configuration and
        /// the context of the message: <list type="bullet"> <item> If a significant amount of time has passed since the
        /// last message, the system may generate a mood-related response. </item> <item> If the system detects a
        /// relevant "eureka" moment based on the message content, it may insert it immediately. </item> <item> The
        /// method respects configured delays and conditions to ensure appropriate timing for responses. </item> </list>
        /// The method will return early if certain conditions are met, such as when the system is disabled, the message
        /// is not from a user,  or there are no prior messages in the session.</remarks>
        /// <param name="message">The user message to process, including its role, content, and metadata.</param>
        /// <returns></returns>
        public virtual async Task HandleMessages(SingleMessage message)
        {
            if (message.Role != AuthorRole.User)
                return;

            // First, check if there's a long time between message and last message, if so do the mood related stuff
            if (Owner.SenseOfTime)
            {
                // no previous user message, nothing to do
                var lastmsg = LLMEngine.History.GetLastMessageFrom(AuthorRole.User);
                if (lastmsg == null)
                    return;
                Mood.Update();
                Mood.Interpret(message.Message);
                // check if we have a previous message in current session, and if it's already a system msg, gtfo
                if (LLMEngine.History.CurrentSession.Messages.Count > 1 && LLMEngine.History.CurrentSession.Messages[^2].Role == AuthorRole.System)
                    return;

                var timeSinceLast = (DateTime.Now - lastmsg.Date);
                if (timeSinceLast >= TimeSpan.FromHours(4))
                {
                    var info = LLMEngine.GetAwayString() + " " + Mood.Describe();
                    info = LLMEngine.ReplaceMacros(info.CleanupAndTrim());
                    var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, info, Owner.UniqueName, LLMEngine.User.UniqueName, true);
                    LLMEngine.History.LogMessage(tosend);
                    // Stop here, don't insert a eureka right after this one.
                    return;
                }
            }

            RefreshMemories();
            if (!string.IsNullOrWhiteSpace(LLMEngine.Settings.ScenarioOverride) || Eurekas.Count == 0)
                return;
            CurrentDelay++;
            // If there's a super relevant eureka to the user input, insert it immediately
            var foundunit = await GetRelevantEureka(message.Message, 0.09f).ConfigureAwait(false);
            if (foundunit != null)
            {
                InsertEureka(foundunit);
                return;
            }
            var iseurekatriggerword = MemoryTriggers.IsEurekaTrigger(message.Message);
            if (CurrentDelay >= MinMessageDelay && LastInsertTime + MinInsertDelay <= DateTime.Now || iseurekatriggerword)
            {
                InsertEureka(null, !iseurekatriggerword);
            }
        }

        /// <summary>
        /// Retrieve the most relevant Eureka from the collection based on the similarity to the specified user input.
        /// </summary>
        /// <remarks>If the RAG system is disabled or the brain is disabled, the method returns null</remarks>
        /// <param name="userinput">The input string to compare against the Eurekas.</param>
        /// <param name="maxDistance">The maximum allowable distance for a Eureka to be considered relevant. Defaults to 0.075.</param>
        /// <returns>A <see cref="MemoryUnit"/> representing the most relevant Eureka if one is found within the specified distance; otherwise null.</returns>
        protected virtual async Task<MemoryUnit?> GetRelevantEureka(string userinput, float maxDistance = 0.085f)
        {
            if (!RAGEngine.Enabled)
                return null;

            foreach (var item in Eurekas)
            {
                var dist = await RAGEngine.GetDistanceAsync(userinput, item).ConfigureAwait(false);
                if (dist <= maxDistance)
                {
                    return item;
                }
            }
            return null;
        }

        /// <summary>
        /// Inserts a selected memory into the conversation as a system message.
        /// </summary>
        /// <param name="insert">memory to insert</param>
        protected virtual void InsertEureka(MemoryUnit? insert = null, bool onlyForced = false)
        {
            // Work on a local variable; do not reassign the parameter for clarity.
            MemoryUnit? selected = insert;

            // If only forced, search the forced pool
            if (onlyForced && selected is null)
            {
                selected = Eurekas.Find(e => e.Insertion == MemoryInsertion.NaturalForced);
                if (selected is null)
                    return; // nothing to insert
            }

            if (selected is null)
            {
                if (Eurekas.Count == 0)
                    return;
                selected = Eurekas[0];
            }

            // Avoid immediate re-use in the current window
            Eurekas.Remove(selected);
            LastInsertTime = DateTime.Now;
            CurrentDelay = 0;

            // Persist the intent so RefreshMemories will not bring it back:
            if (selected.Priority > 1)
            {
                // Keep important memories but stop them from being considered "natural" next time
                selected.Insertion = MemoryInsertion.Trigger;
            }
            else
            {
                // One-shot natural memories are consumed
                Memories.Remove(selected);
            }

            var tosend = new SingleMessage(
                AuthorRole.System,
                DateTime.Now,
                selected.ToEureka(),
                Owner.UniqueName,
                LLMEngine.User.UniqueName,
                true);
            selected.Touch();
            LLMEngine.History.LogMessage(tosend);
        }

        /// <summary>
        /// Iterate through the stored searches and see if this topic or a similar one was searched recently.
        /// </summary>
        /// <param name="topic">topic</param>
        /// <param name="maxDistance">distance higher than this will count as different</param>
        /// <returns>true if a previous search topic is similar</returns>
        public virtual async Task<bool> WasSearchedRecently(string topic, float maxDistance = 0.075f)
        {
            // If RecentSearches > 20, remove entries starting with the first index until count is 20
            while (RecentSearches.Count > 20)
                RecentSearches.RemoveAt(0);

            var lowered = topic.ToLowerInvariant();
            if (RecentSearches.Find(s => s.Topic.Equals(lowered, StringComparison.InvariantCultureIgnoreCase)) != default)
            {
                return true;
            }

            if (RAGEngine.Enabled)
            {
                foreach (var item in RecentSearches)
                {
                    if (await RAGEngine.GetDistanceAsync(item.Topic, topic) < maxDistance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Regenerates the embeddings for all memories in the collection.
        /// </summary>
        /// <remarks>This method iterates through the collection of memories and invokes the
        /// <c>EmbedText</c> method  on each memory asynchronously. It ensures that the embeddings are updated for all
        /// items in the collection.</remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task RegenEmbeds()
        {
            foreach (var mem in Memories)
            {
                await mem.EmbedText().ConfigureAwait(false);
            }
        }


        #region *** LTM - Memory Management ***

        /// <summary>
        /// Checks for RAG entries and refreshes the textual inserts.
        /// </summary>
        /// <param name="MsgSender"></param>
        /// <param name="newMessage"></param>
        /// <returns></returns>
        public virtual async Task UpdateRagAndInserts(PromptInserts dataInserts, string newMessage)
        {
            // Check for RAG entries and refresh the textual inserts
            dataInserts.DecreaseDuration();

            var searchmessage = string.IsNullOrWhiteSpace(newMessage) ?
                (Owner.History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty) : newMessage;
            searchmessage = LLMEngine.ReplaceMacros(searchmessage);

            if (RAGEngine.Enabled)
            {
                var search = await RAGEngine.Search(searchmessage).ConfigureAwait(false);
                dataInserts.AddMemories(search);
            }

            // Check for keyword-activated world info entries
            if (LLMEngine.Settings.AllowWorldInfo)
            {
                var _currentWorldEntries = new List<MemoryUnit>();
                // Add world entries from the group/bot itself
                if (Owner.MyWorlds.Count > 0)
                {
                    foreach (var world in Owner.MyWorlds)
                    {
                        _currentWorldEntries.AddRange(world.FindEntries(Owner.History, searchmessage));
                    }
                }
                // If in group conversation, also add world entries from the current active persona
                if (Owner is GroupPersona group)
                {
                    var currentBot = group.CurrentBot;
                    if (currentBot?.MyWorlds.Count > 0)
                    {
                        foreach (var world in currentBot.MyWorlds)
                        {
                            var entries = world.FindEntries(Owner.History, searchmessage);
                            // Only add entries that aren't already included from the group
                            foreach (var entry in entries)
                            {
                                if (!_currentWorldEntries.Any(e => e.Guid == entry.Guid))
                                {
                                    _currentWorldEntries.Add(entry);
                                }
                            }
                        }
                    }
                }
                var usedguid = dataInserts.GetGuids();
                _currentWorldEntries.RemoveAll(e => usedguid.Contains(e.Guid));

                foreach (var entry in _currentWorldEntries)
                {
                    dataInserts.AddInsert(new PromptInsert(
                        entry.Guid, entry.Content, entry.PositionIndex, entry.Duration)
                        );
                }
            }

        }

        /// <summary>
        /// Retrieves a local embed object with the specified unique identifier.
        /// </summary>
        /// <param name="iD">The unique identifier of the embed to retrieve.</param>
        /// <returns>The embed object with the specified identifier, or <see langword="null"/> if no matching embed is found.</returns>
        internal MemoryUnit? GetLocalMemoryByID(Guid iD)
        {
            return Memories.FirstOrDefault(m => m.Guid == iD);
        }

        /// <summary>
        /// Checks for a memory by its GUID across sessions, world info, and local memories.
        /// </summary>
        /// <param name="iD"></param>
        /// <returns></returns>
        public virtual MemoryUnit? GetMemoryByID(Guid iD)
        {
            // Check Sessions
            MemoryUnit? res = Owner.History.GetSessionByID(iD);
            if (res != null)
                return res;
            // Check WorldInfo
            res = Owner.GetWIEntryByGUID(iD);
            if (res != null)
                return res;
            // Check local memories
            return Memories.FirstOrDefault(m => m.Guid == iD);
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        private void MemoryDecay()
        {
            // Remove old natural memories that haven't been inserted yet are are passed the cutoff
            Memories.RemoveAll(e => (e.Insertion == MemoryInsertion.Natural || e.Insertion == MemoryInsertion.NaturalForced) && (DateTime.Now - e.Added) > EurekaCutOff);

            // Remove old trigger memories that are decayable and haven't been recalled in a while
            Memories.RemoveAll(e =>
            {
                if (e.Insertion != MemoryInsertion.Trigger || !DecayableMemories.Contains(e.Category))
                    return false;
                var noRecallDays = MinNoRecallDaysBeforeDeletionPerPrioLevel * (e.Priority + 1) + e.TriggerCount;

                // If never triggered, use Added date
                var since = (e.TriggerCount == 0) ? (DateTime.Now - e.Added) : (DateTime.Now - e.LastTrigger);
                return (since.TotalDays > noRecallDays);
            });
        }

        #endregion

    }
}
