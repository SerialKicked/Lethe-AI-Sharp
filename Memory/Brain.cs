using LetheAISharp;
using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    public class UserReturnInsert(string info)
    {
        public Guid ID { get; set; } = new Guid();
        public DateTime Added { get; set; } = DateTime.Now;
        public string Info { get; set; } = info;
    }

    /// <summary>
    /// Brain functionality for a persona, handles memories, mood, and message inserts
    /// </summary>
    /// <param name="basePersona">Owner</param>
    public class Brain(BasePersona basePersona)
    {
        [JsonIgnore] private BasePersona Owner { get; set; } = basePersona;
        public TimeSpan MinInsertDelay { get; set; } = TimeSpan.FromMinutes(15);
        public int MinMessageDelay { get; set; } = 4;
        public float HoursBeforeAFK { get; set; } = 4;
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);
        public DateTime LastInsertTime { get; set; }
        public int CurrentDelay { get; set; } = 0;
        public HashSet<MemoryType> DecayableMemories { get; set; } = [ MemoryType.WebSearch, MemoryType.Goal ];
        public int MinNoRecallDaysBeforeDeletionPerPrioLevel { get; set; } = 10;

        [JsonProperty] protected List<MemoryUnit> Memories { get; set; } = [];
        [JsonProperty] protected List<UserReturnInsert> Inserts { get; set; } = [];

        public List<TopicSearch> RecentSearches { get; set; } = [];
        [JsonIgnore] protected List<MemoryUnit> Eurekas { get; set; } = [];

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
        protected void RefreshMemories()
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

            Mood.Update();
            Mood.Interpret(message.Message);

            // Prepare away message if need be.
            var msg = BuildAwayMessage();
            if (msg != null)
            {
                LLMEngine.History.LogMessage(msg);
                // Stop here, don't insert a eureka right after this one.
                return;
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
        /// <param name="searchstring"></param>
        /// <returns></returns>
        public virtual async Task UpdateRagAndInserts(PromptInserts target, string searchstring, int ragResCount, float ragDistance)
        {
            // Check for RAG entries and refresh the textual inserts
            target.DecreaseDuration();

            var searchmessage = string.IsNullOrWhiteSpace(searchstring) ?
                (Owner.History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty) : searchstring;
            searchmessage = Owner.ReplaceMacros(searchmessage);

            if (RAGEngine.Enabled)
            {
                var search = await RAGEngine.Search(searchmessage, ragResCount, ragDistance).ConfigureAwait(false);
                target.AddMemories(search);
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
                var usedguid = target.GetGuids();
                _currentWorldEntries.RemoveAll(e => usedguid.Contains(e.Guid));

                foreach (var entry in _currentWorldEntries)
                {
                    target.AddInsert(new PromptInsert(
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
            if (Owner.MyWorlds.Count > 0)
            {
                foreach (var world in Owner.MyWorlds)
                {
                    res = world.Entries.Find(e => e.Guid == iD);
                    if (res != null)
                        return res;
                }
            }
            // Check local memories
            return Memories.FirstOrDefault(m => m.Guid == iD);
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        protected void MemoryDecay()
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

        /// <summary>
        /// Adds a memory unit to the collection, optionally skipping duplicate checks.
        /// </summary>
        /// <remarks>If <paramref name="skipDuplicateCheck"/> is <see langword="false"/>, the method
        /// performs a similarity check  against existing memories in the same category. If a similar memory is found
        /// (based on a predefined distance threshold),  the existing memory is replaced with the new one. Otherwise,
        /// the new memory is added to the collection.</remarks>
        /// <param name="mem">The memory unit to be added. This object represents a specific memory with associated data.</param>
        /// <param name="skipDuplicateCheck">A boolean value indicating whether to skip the duplicate check.  If <see langword="true"/>, the memory unit
        /// is added directly without checking for duplicates.  Defaults to <see langword="false"/>.</param>
        /// 
        public void Memorize(MemoryUnit mem, bool skipDuplicateCheck = false)
        {
            if (skipDuplicateCheck || mem.EmbedSummary.Length == 0)
            {
                Memories.Add(mem);
                return;
            }

            var mindist = float.MaxValue;
            var bestmatch = (MemoryUnit?)null;
            var comparelist = Memories.FindAll(e => e.Category == mem.Category);

            foreach (var item in comparelist)
            {
                var dist = RAGEngine.GetDistance(item, mem);
                if (dist < mindist)
                {
                    mindist = dist;
                    bestmatch = item;
                }
            }

            if (mindist < 0.07f && bestmatch != null)
            {
                var idx = Memories.IndexOf(bestmatch);
                if (idx != -1)
                {
                    Memories[idx] = mem;
                    mem.Touch();
                    return;
                }
            }

            Memories.Add(mem);
        }

        /// <summary>
        /// Removes the specified memory unit from the collection of memories.
        /// </summary>
        /// <remarks>If the specified memory unit does not exist in the collection, no action is
        /// taken.</remarks>
        /// <param name="mem">The memory unit to remove. Cannot be <see langword="null"/>.</param>
        public void Forget(MemoryUnit mem)
        {
            Memories.Remove(mem);
        }

        /// <summary>
        /// Retrieves a list of memories filtered by the specified category.
        /// </summary>
        /// <param name="category">The category of memories to filter by. If <see langword="null"/>, all memories are returned.</param>
        /// <returns>A list of <see cref="MemoryUnit"/> objects that match the specified category. If <paramref name="category"/>
        /// is <see langword="null"/>, the entire list of memories is returned.</returns>
        public List<MemoryUnit> GetMemories(MemoryType? category)
        {
            return Memories.FindAll(m => category == null || m.Category == category);
        }

        internal List<MemoryUnit> GetMemoriesForRAG()
        {
            return Memories.FindAll(m => m.Insertion == MemoryInsertion.Trigger && m.EmbedSummary.Length > 0);
        }

        #endregion

        #region *** Eureka Management ***

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

        protected virtual MemoryUnit? GetImportantEureka(bool onlyForced)
        {
            if (Eurekas.Count == 0)
                return null;
            var mylist = new List<MemoryUnit>(Eurekas);
            // sort by descending priority
            mylist.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            // make a list with only the NaturalForced
            if (onlyForced)
            {
                mylist = mylist.FindAll(e => e.Insertion == MemoryInsertion.NaturalForced);
                return mylist.Count > 0 ? mylist[0] : null;
            }
            return mylist[0];
        }

        /// <summary>
        /// Inserts a selected memory into the conversation as a system message.
        /// </summary>
        /// <param name="insert">memory to insert</param>
        protected virtual void InsertEureka(MemoryUnit? insert = null, bool onlyForced = false)
        {
            // Work on a local variable; do not reassign the parameter for clarity.
            MemoryUnit? selected = insert;

            selected ??= GetImportantEureka(onlyForced);
            if (selected == null)
                return;

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

        #endregion

        #region *** User Return Inserts ***

        /// <summary>
        /// Add a message to be inserted when the user returns after a long absence. This is inserted in the same block as the mood and time message.
        /// </summary>
        /// <param name="info"></param>
        public UserReturnInsert? AddUserReturnInsert(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                return null;
            var existing = Inserts.Find(i => i.Info.Equals(info, StringComparison.InvariantCultureIgnoreCase));
            if (existing != null)
            {
                existing.Added = DateTime.Now;
                return existing;
            }
            else
            {
                var x = new UserReturnInsert(info);
                Inserts.Add(x);
                return x;
            }
        }

        public bool RemoveUserReturnInsert(Guid id)
        {
            var existing = Inserts.Find(i => i.ID == id);
            if (existing != null)
            {
                Inserts.Remove(existing);
                return true;
            }
            return false;
        }

        public virtual SingleMessage? BuildAwayMessage()
        {
            // no previous user message, nothing to do
            if (!Owner.SenseOfTime && Inserts.Count == 0)
                return null;

            // no previous user message, nothing to do
            var lastmsg = LLMEngine.History.GetLastMessageFrom(AuthorRole.User);
            if (lastmsg == null)
                return null;

            // check if we have a previous message in current session, and if it's already a system msg, gtfo
            if (LLMEngine.History.CurrentSession.Messages.Count > 1 && LLMEngine.History.CurrentSession.Messages[^2].Role == AuthorRole.System)
                return null;

            var timeSinceLast = (DateTime.Now - lastmsg.Date);
            if (timeSinceLast < TimeSpan.FromHours(HoursBeforeAFK))
                return null;

            var info = Owner.SenseOfTime ? GetAwayString() + " " + Mood.Describe() : Mood.Describe();
            foreach (var item in Inserts)
            {
                info += " " + item.Info;
            };
            Inserts.Clear();
            info = Owner.ReplaceMacros(info.CleanupAndTrim());
            var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, info, Owner.UniqueName, LLMEngine.User.UniqueName, true);
            return tosend;
        }

        /// <summary>
        /// Returns an away string depending on the last chat's date.
        /// </summary>
        /// <returns></returns>
        protected virtual string GetAwayString()
        {
            var lastusermsg = Owner.History.GetLastMessageFrom(AuthorRole.User);
            if (lastusermsg == null || Owner.History.CurrentSession != Owner.History.Sessions.Last())
                return string.Empty;

            var timespan = DateTime.Now - lastusermsg.Date;
            if (timespan <= new TimeSpan(2, 0, 0))
                return string.Empty;

            var msgtxt = (DateTime.Now.Date != lastusermsg.Date.Date) || (timespan > new TimeSpan(12, 0, 0)) ?
                $"We're {DateTime.Now.DayOfWeek} {StringExtensions.DateToHumanString(DateTime.Now)}." : string.Empty;
            if (timespan.Days > 1)
                msgtxt += $" The last chat was {timespan.Days} days ago. " + "It is {{time}} now.";
            else if (timespan.Days == 1)
                msgtxt += " The last chat happened yesterday. It is {{time}} now.";
            else
                msgtxt += $" The last chat was about {timespan.Hours} hours ago. " + "It is {{time}} now.";
            msgtxt = msgtxt.Trim();
            return Owner.ReplaceMacros(msgtxt);
        }

        #endregion

    }
}
