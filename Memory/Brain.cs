using LetheAISharp;
using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Text;
using System.ComponentModel;
using LetheAISharp.Moods;

namespace LetheAISharp.Memory
{
    public class UserReturnInsert(string info)
    {
        public Guid ID { get; set; } = new Guid();
        public DateTime Added { get; set; } = DateTime.Now;
        public string Info { get; set; } = info;
        public string? UID { get; set; } = null;
    }

    /// <summary>
    /// Brain functionality for a persona, handles memories, mood, and message inserts
    /// </summary>
    /// <param name="basePersona">Owner</param>
    public class Brain(BasePersona basePersona)
    {
        [JsonIgnore] protected BasePersona Owner { get; set; } = basePersona;

        /// <summary>
        /// Minimum time between two automatic memory inserts.
        /// </summary>
        [Description("Minimum time between two forced memory inserts.")]
        public TimeSpan MinInsertDelay { get; set; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Minimum number of user messages between two automatic memory inserts.
        /// </summary>
        [Description("Minimum number of user messages between two forced memory inserts.")]
        public int MinMessageDelay { get; set; } = 4;

        /// <summary>
        /// Time in hours of inactivity after which the bot will send a mood/away message.
        /// </summary>
        [Description("Time in hours of user inactivity after which the bot will be sent a system message informing them of time, mood, and other contextual information.")]
        public float HoursBeforeAFK { get; set; } = 4;

        /// <summary>
        /// Determines how long a natural memory remains available for insertion.
        /// </summary>
        [Description("Determines how long a forced memory insert remains available for insertion. Memories older than this cutoff will be removed during maintenance.")]
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);

        /// <summary>
        /// Disable all automatic natural memory inserts.
        /// </summary>
        [Description("If checked, disables all automatic forced memory inserts.")]
        public bool DisableEurekas { get; set; } = false;

        /// <summary>
        /// Gets or sets the minimum number of days that an item must remain unaccessed  before it is eligible for
        /// deletion, (it's multiplied by its priority level).
        /// </summary>
        [Description("Minimum number of days that an item must remain unaccessed before it is eligible for deletion, multiplied by its priority level.")]
        public int MinNoRecallDaysBeforeDeletionPerPrioLevel { get; set; } = 10;

        /// <summary>
        /// If set to true, the bot will use a basic mood system to adjust its responses based on its mood state. 
        /// This only has basic functionalities featured as a demo for roleplay characters. 
        /// The Brain and Mood classes are meant to be overridden for more advanced behavior.
        /// </summary>
        [Description("If checked, enables the basic mood system to adjust responses based on mood state.")]
        public bool MoodHandling { get; set; } = false;

        /// <summary>
        /// If mood handling is enabled, setting this to true will keep the mood static and not update it over time.
        /// </summary>
        [Description("If checked, keeps the mood static and does not update it over time.")]
        public bool StaticMood { get; set; } = false;


        public DateTime LastInsertTime { get; protected set; }
        public int CurrentDelay { get; protected set; } = 0;

        [JsonIgnore] protected HashSet<MemoryType> DecayableMemories => LLMEngine.Settings.DecayableMemories;
        [JsonIgnore] protected HashSet<MemoryType> DisableRAG => LLMEngine.Settings.DisableRAG;

        [JsonProperty] public List<MemoryUnit> Memories { get; set; } = [];
        [JsonProperty] protected List<UserReturnInsert> Inserts { get; set; } = [];

        /// <summary>
        /// Extracted facts about the user, used as a lightweight semantic index over session memories.
        /// Each fact embeds cleanly and points back to the source MemoryUnits via GUIDs for two-hop retrieval.
        /// </summary>
        [JsonProperty] public List<ExtractedFact> ExtractedFacts { get; set; } = [];

        public List<TopicSearch> RecentSearches { get; set; } = [];

        public string DailySchedulePrefix { get; set; } = "Today's Schedule:";
        public string[] DailySchedule { get; set; } = new string[7];

        [JsonIgnore] protected List<MemoryUnit> Eurekas { get; set; } = [];

        [JsonIgnore] protected MemoryVault MindPalace { get; set; } = new MemoryVault();

        public virtual MoodManager Mood { get; set; } = new MoodManager();

        /// <summary>
        /// Called to initialize the brain with its owner persona.
        /// </summary>
        /// <param name="owner"></param>
        public virtual void Init(BasePersona owner)
        {
            Owner = owner;
            LocalMemoryMaintenance();
        }

        /// <summary>
        /// Cleans up event subscriptions when the brain is no longer needed.
        /// </summary>
        public virtual void Close()
        {
            MindPalace.Clear();
        }

        public virtual async Task ProcessPreviousSession()
        {
            await Task.Delay(1).ConfigureAwait(false);
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            LocalMemoryMaintenance();
            foreach (var moodlet in Mood.MoodData)
                if (MoodManager.Moodlets.TryGetValue(moodlet.Key, out var m))
                    Mood.MoodData[moodlet.Key] = m.ProcessNewSession(moodlet.Value, Modifier.None);
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        protected void LocalMemoryMaintenance()
        {
            MemoryDecay();
            // Select all natural memories within the cutoff period, order by Added descending, and enqueue them
            Eurekas.Clear();
            var cutoff = DateTime.Now - EurekaCutOff;
            var recent = Memories.Where(m => (m.Insertion == MemoryInsertion.Natural || m.Insertion == MemoryInsertion.NaturalForced) && m.Added <= DateTime.Now && m.Added >= cutoff).OrderByDescending(m => m.Added).ToList();
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
            if (message.Role != AuthorRole.User || LLMEngine.User.DisableBotGuidance || Owner.DisableBotGuidance)
                return;

            if (LLMEngine.Settings.DisableDateAndMoodIfNotLastSession && LLMEngine.History.CurrentSession != LLMEngine.History.Sessions.Last())
                return;

            if (MoodHandling && !StaticMood)
            {
                Mood.Update();
                Mood.Interpret(message.Message);
            }

            // Prepare away message if need be.
            var msg = BuildAwayMessage();
            if (msg != null)
            {
                // check if previous message is system
                if (LLMEngine.History.CurrentSession.Messages.Count > 0 && LLMEngine.History.CurrentSession.Messages.Last().Role == AuthorRole.System)
                {
                    // edit the last system message instead of adding a new one
                    var lastmsg = LLMEngine.History.CurrentSession.Messages.Last();
                    lastmsg.Message = msg.Message;
                }
                else
                    LLMEngine.History.LogMessage(msg);
                // Stop here, don't insert a eureka right after this one.
                return;
            }

            LocalMemoryMaintenance();
            if (Eurekas.Count == 0 || DisableEurekas)
                return;
            CurrentDelay++;
            // If there's a super relevant eureka to the user input, insert it immediately
            var foundunit = await GetRelevantEureka(message.Message, 0.09f).ConfigureAwait(false);
            if (foundunit != null)
            {
                InsertEureka(foundunit);
                return;
            }
            var useraskingfornews = MemoryTriggers.IsEurekaTrigger(message.Message);
            if (CurrentDelay >= MinMessageDelay && LastInsertTime + MinInsertDelay <= DateTime.Now || useraskingfornews)
            {
                InsertEureka(null, !useraskingfornews);
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

            if (LLMEngine.Settings.RAGEnabled)
            {
                foreach (var item in RecentSearches)
                {
                    if (await EmbedTools.GetDistanceAsync(item.Topic, topic) < maxDistance)
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
                await mem.BuildEmbedding().ConfigureAwait(false);
            }
        }


        #region *** LTM - Memory Management ***

        /// <summary>
        /// Reloads the memory vault with relevant memory units based on the current state of the system.
        /// </summary>
        /// <remarks>This method clears the existing memory vault and repopulates it with memory units
        /// derived from  session logs, brain memories, and world entries. It only processes memory units that meet
        /// specific  criteria, such as having non-empty embeddings and not being disabled. If RAG (Retrieval-Augmented 
        /// Generation) is disabled in the settings, the method exits early without modifying the memory
        /// vault.</remarks>
        /// <exception cref="Exception">Thrown if an error occurs while adding memory units to the memory vault.</exception>
        public virtual void ReloadMemories()
        {
            MindPalace = new MemoryVault();
            if (!LLMEngine.Settings.RAGEnabled)
                return;
            var log = Owner.History;
            if (log.Sessions.Count == 0 && Owner.MyWorlds.Count == 0)
                return;
            var vectors = new List<MemoryUnit>();
            for (int i = 0; i < log.Sessions.Count; i++)
            {
                var session = log.Sessions[i];
                if (session.EmbedSummary.Length == 0)
                    continue;
                vectors.Add(session);
            }

            var brainmemories = Memories.FindAll(m => m.Added <= DateTime.Now && m.Insertion == MemoryInsertion.Trigger && m.EmbedSummary.Length > 0 && !LLMEngine.Settings.DisableRAG.Contains(m.Category));
            vectors.AddRange(brainmemories);

            foreach (var world in Owner.MyWorlds)
            {
                if (!world.DoEmbeds)
                    continue;
                vectors.AddRange(world.Entries.FindAll(e => e.Enabled && e.EmbedSummary?.Length > 0));
            }

            try
            {
                MindPalace.AddMemories(vectors);
            }
            catch (Exception e)
            {
                throw new Exception("Error adding items to the VectorDB", e);
            }
        }

        /// <summary>
        /// Updates the specified <see cref="PromptInserts"/> instance with relevant RAG (Retrieval-Augmented
        /// Generation)  and world information entries based on the provided search criteria.
        /// </summary>
        /// <remarks>This method performs the following operations: <list type="bullet">
        /// <item><description>Decreases the duration of existing inserts in the <paramref name="target"/>
        /// instance.</description></item> <item><description> Retrieves RAG entries if RAG is enabled in the engine
        /// settings, using the specified search criteria and distance threshold. </description></item>
        /// <item><description> Retrieves world information entries if world info is allowed in the engine settings,
        /// including entries from the group, bot,  or active persona, and adds them to the <paramref name="target"/>
        /// instance. </description></item> </list> Entries are prioritized and filtered to avoid duplicates, and the
        /// total number of entries added respects the specified limits.</remarks>
        /// <param name="target">The <see cref="PromptInserts"/> instance to update with the retrieved entries.</param>
        /// <param name="searchstring">The search string used to query RAG and world information entries. If null or whitespace, the last user
        /// message  from the session history is used as the search string.</param>
        /// <param name="ragResCount">The maximum number of RAG entries to retrieve. If set to -1, the default maximum from the engine settings is
        /// used.</param>
        /// <param name="ragDistance">The maximum distance threshold for RAG entry retrieval. Entries with a distance greater than this value are
        /// excluded.</param>
        /// <returns></returns>
        public virtual async Task GetRAGandInserts(PromptInserts target, string searchstring, int ragResCount, float ragDistance)
        {
            // Check for RAG entries and refresh the textual inserts
            target.DecreaseDuration();

            var ragentries = ragResCount == -1 ? LLMEngine.Settings.RAGMaxEntries : ragResCount;
            var wientries = ragResCount == -1 ? LLMEngine.Settings.WorldInfoMaxEntries : ragResCount;

            // Embed the search message once so both standard RAG and fact retrieval can reuse it.
            var searchmessage = string.IsNullOrWhiteSpace(searchstring) ? (Owner.History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty) : searchstring;
            searchmessage = Owner.ReplaceMacros(searchmessage);
            var searchString = LLMEngine.Settings.RAGConvertTo3rdPerson ? searchmessage.ConvertToThirdPerson() : searchmessage;
            var searchEmbed = await EmbedTools.EmbeddingText(searchString).ConfigureAwait(false);


            if (LLMEngine.Settings.RAGEnabled && searchEmbed.Length > 0)
            {
                var ragfindings = new List<VaultResult>();

                // Add memories from direct RAG search
                var foundstuff = await Search(searchString, ragentries, ragDistance);
                ragfindings.AddRange(foundstuff);

                // Fact-boosted retrieval: compare against short fact embeddings for two-hop memory access.
                // Facts embed cleanly as single-sentence statements, giving much better recall than comparing
                // user input directly against long multi-topic session summaries.
                if (LLMEngine.Settings.FactRetrievalEnabled && ExtractedFacts.Count > 0)
                {
                    var factThreshold = LLMEngine.Settings.FactRetrievalThreshold;
                    foreach (var fact in ExtractedFacts)
                    {
                        if (fact.Superseded || fact.EmbedSummary.Length == 0 || fact.SourceMemories.Count == 0)
                            continue;
                        var dist = EmbedTools.GetDistance(searchEmbed, fact.EmbedSummary);
                        if (dist > factThreshold)
                            continue;
                        // Fact matched — pull in its source MemoryUnits directly by GUID
                        foreach (var sourceGuid in fact.SourceMemories)
                        {
                            var mem = GetMemoryByID(sourceGuid);
                            if (mem != null && mem.Insertion != MemoryInsertion.None) 
                            {
                                if (!ragfindings.Contains(mem))
                                    ragfindings.Add(new VaultResult(mem, dist));
                                else
                                {
                                    // If the memory is already in the findings from direct RAG, check if this fact match is closer and update the distance
                                    var existing = ragfindings.Find(r => r.Memory == mem);
                                    if (existing != null && dist < existing.Distance)
                                        existing.Distance = dist;
                                }
                            }
                        }
                    }
                }
                ragfindings.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                // keep only ragentries first entries
                if (ragfindings.Count > ragentries)
                    ragfindings = [.. ragfindings.Take(ragentries)];
                target.AddMemories(ragfindings);
            }

            // always add sticky
            var stickies = Memories.FindAll(e => e.Sticky && e.Added <= DateTime.Now && !DisableRAG.Contains(e.Category));
            foreach (var item in stickies)
            {
                if (!target.Contains(item))
                    target.AddInsert(item);
            }

            // Check for keyword-activated world info entries
            if (LLMEngine.Settings.AllowWorldInfo)
            {
                var _currentWorldEntries = new List<MemoryUnit>();

                foreach (var item in Owner.History.Sessions)
                {
                    if (item.CheckKeywords(searchmessage))
                    {
                        _currentWorldEntries.Add(item);
                    }
                }

                // Get the last message with user or assistant role from the current session
                var found = Owner.History.CurrentSession.Messages.TakeLast(1).Where(m => m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant).ToList();

                var keywordsearch = found?.Count > 0 ? searchmessage + " " + found[0].Message : searchmessage;

                foreach (var item in Memories)
                {
                    if (!item.Sticky && item.Insertion != MemoryInsertion.None && item.CheckKeywords(keywordsearch) && item.Added <= DateTime.Now && !DisableRAG.Contains(item.Category))
                    {
                        _currentWorldEntries.Add(item);
                    }
                }

                var list = Memories.FindAll(e => e.Insertion != MemoryInsertion.None && e.Added <= DateTime.Now && (e.Category == MemoryType.Person || e.Category == MemoryType.Location) && searchmessage.Contains(e.Name, StringComparison.InvariantCultureIgnoreCase));
                _currentWorldEntries.AddRange(list);


                // Add world entries from the group/bot itself
                if (Owner.MyWorlds.Count > 0)
                {
                    foreach (var world in Owner.MyWorlds)
                    {
                        _currentWorldEntries.AddRange(world.FindEntries(Owner.History, searchmessage));
                    }
                }

                var usedguid = target.GetGuids();
                // sort by decreasing prio (higher = first)
                _currentWorldEntries.Sort((a, b) => 
                {
                    var aprio = a is ChatSession asession ? asession.Priority + asession.MetaData.Relevance * 10 : a.Priority;
                    var bprio = b is ChatSession bsession ? bsession.Priority + bsession.MetaData.Relevance * 10 : b.Priority;
                    return bprio.CompareTo(aprio);
                });
                if (_currentWorldEntries.Count > wientries)
                    _currentWorldEntries = [.. _currentWorldEntries.Take(wientries)];

                foreach (var entry in _currentWorldEntries)
                {
                    target.AddInsert(entry);
                }
            }

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
        /// Checks for a memory by its title across sessions, world info, and local memories.
        /// </summary>
        /// <param name="title"></param>
        /// <returns></returns>
        public virtual List<MemoryUnit> GetMemoriesByTitle(string title, bool partialok = true)
        {
            // Check Sessions
            List<MemoryUnit> res = Owner.History.GetSessionsByTitle(title, partialok).ConvertAll(a => a as MemoryUnit);

            // Check WorldInfo
            if (Owner.MyWorlds.Count > 0)
            {
                foreach (var world in Owner.MyWorlds)
                {
                    if (partialok)
                        res.AddRange(world.Entries.FindAll(e => e.Name.Contains(title, StringComparison.InvariantCultureIgnoreCase)));
                    else
                        res.AddRange(world.Entries.FindAll(e => e.Name.Equals(title, StringComparison.InvariantCultureIgnoreCase)));
                }
            }
            // Check local memories
            if (partialok)
                res.AddRange(Memories.FindAll(m => m.Name.Contains(title, StringComparison.InvariantCultureIgnoreCase)));
            else
                res.AddRange(Memories.FindAll(m => m.Name.Equals(title, StringComparison.InvariantCultureIgnoreCase)));
            return res;
        }

        /// <summary>
        /// Searches for memories that match the specified search string across all available various sources.
        /// </summary>
        /// <remarks>This method searches through the bot's session history, world entries, and local
        /// memory entries to find matches for the specified search string. Matches are determined by checking if the
        /// search string is contained in the name or content of the memory entries.</remarks>
        /// <param name="searchstring">The string to search for. The search is case-insensitive and matches are found in the name or content of
        /// memory entries.</param>
        /// <returns>A list of <see cref="MemoryUnit"/> objects that match the search criteria. The list may include results from
        /// session history, world entries, and local memories.</returns>
        public virtual List<MemoryUnit> SearchMemories(string searchstring)
        {
            // Check Sessions

            List<MemoryUnit> res = Owner.History.SearchSessions(searchstring).ConvertAll(a => a as MemoryUnit);
            // Check WorldInfo
            if (Owner.MyWorlds.Count > 0)
            {
                foreach (var world in Owner.MyWorlds)
                {
                    res.AddRange(world.Entries.FindAll(e => 
                    e.Name.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase) ||
                    e.Content.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase)
                    ));
                }
            }
            res.AddRange(Memories.FindAll(m => 
                m.Name.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase) ||
                m.Content.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase)));
            return res;
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        protected virtual void MemoryDecay()
        {
            // Remove old natural memories that haven't been inserted yet are are passed the cutoff
            Memories.RemoveAll(e => !e.Protected && (e.Insertion == MemoryInsertion.Natural || e.Insertion == MemoryInsertion.NaturalForced) && (DateTime.Now - e.Added) > EurekaCutOff);

            // Remove old trigger memories that are decayable and haven't been recalled in a while
            Memories.RemoveAll(e =>
            {
                if (e.Insertion != MemoryInsertion.Trigger || !DecayableMemories.Contains(e.Category) || e.Protected)
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
        public virtual void Memorize(MemoryUnit mem, bool skipDuplicateCheck = false)
        {
            if (skipDuplicateCheck || mem.EmbedSummary.Length == 0)
            {
                Memories.Add(mem);
                return;
            }

            // special case, just check name first
            if (mem.Category == MemoryType.Person || mem.Category == MemoryType.Location)
            {
                var existing = Memories.Find(e => !e.Protected && e.Category == MemoryType.Person && e.Name.Equals(mem.Name, StringComparison.InvariantCultureIgnoreCase));
                if (existing != null)
                {
                    var idx = Memories.IndexOf(existing);
                    if (idx != -1)
                    {
                        Memories[idx] = mem;
                        mem.Touch();
                        return;
                    }
                }
            }

            var mindist = float.MaxValue;
            var bestmatch = (MemoryUnit?)null;
            var comparelist = Memories.FindAll(e => e.Category == mem.Category && !e.Protected);

            foreach (var item in comparelist)
            {
                var dist = EmbedTools.GetDistance(item, mem);
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
        public virtual void Forget(MemoryUnit mem)
        {
            Memories.Remove(mem);
        }

        /// <summary>
        /// Adds a new extracted fact or updates an existing one via deduplication or supersession.
        /// </summary>
        /// <remarks>
        /// <para>Three outcomes are possible based on the embedding distance to the closest existing fact:</para>
        /// <list type="bullet">
        ///   <item><b>Deduplication</b> (distance &lt; <see cref="LLMSettings.FactDeduplicationThreshold"/>):
        ///     The existing fact's <c>LastSeen</c> and <c>ReferenceCount</c> are updated and
        ///     <paramref name="sourceSessionGuid"/> is added to its <c>SourceMemories</c>.</item>
        ///   <item><b>Supersession</b> (<see cref="LLMSettings.FactDeduplicationThreshold"/> ≤ distance &lt;
        ///     <see cref="LLMSettings.FactSupersessionThreshold"/>): The existing fact is marked
        ///     <c>Superseded</c>, the new fact carries forward the existing fact's <c>SourceMemories</c>,
        ///     and both facts are stored.</item>
        ///   <item><b>New fact</b> (distance ≥ <see cref="LLMSettings.FactSupersessionThreshold"/>):
        ///     The fact is added to <see cref="ExtractedFacts"/> as a fresh entry.</item>
        /// </list>
        /// </remarks>
        /// <param name="fact">The concise, single-sentence fact text to store.</param>
        /// <param name="sourceSessionGuid">GUID of the session this fact was extracted from. Pass <see langword="null"/> if there is no associated session.</param>
        /// <returns>The <see cref="ExtractedFact"/> that was added or updated, or <see langword="null"/> if <paramref name="fact"/> is null or whitespace.</returns>
        public virtual async Task<ExtractedFact?> AddOrUpdateFact(string fact, Guid? sourceSessionGuid = null)
        {
            if (string.IsNullOrWhiteSpace(fact))
                return null;

            var newFact = new ExtractedFact { Fact = fact };
            if (sourceSessionGuid.HasValue)
                newFact.SourceMemories.Add(sourceSessionGuid.Value);

            await newFact.BuildEmbedding().ConfigureAwait(false);

            // No embedding available (RAG disabled) — just store the fact without dedup
            if (newFact.EmbedSummary.Length == 0)
            {
                ExtractedFacts.Add(newFact);
                return newFact;
            }

            var dedupThreshold = LLMEngine.Settings.FactDeduplicationThreshold;
            var supersessionThreshold = LLMEngine.Settings.FactSupersessionThreshold;

            var minDist = float.MaxValue;
            ExtractedFact? closest = null;

            foreach (var existing in ExtractedFacts)
            {
                if (existing.Superseded || existing.EmbedSummary.Length == 0)
                    continue;
                var dist = EmbedTools.GetDistance(existing, newFact);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = existing;
                }
            }

            if (closest != null && minDist < dedupThreshold)
            {
                // Same fact — update metadata only
                closest.LastSeen = DateTime.Now;
                closest.ReferenceCount++;
                if (sourceSessionGuid.HasValue && !closest.SourceMemories.Contains(sourceSessionGuid.Value))
                    closest.SourceMemories.Add(sourceSessionGuid.Value);
                return closest;
            }

            if (closest != null && minDist < supersessionThreshold)
            {
                // Related but different — supersede the old fact
                closest.Superseded = true;
                closest.SupersededBy = newFact.Guid;

                // Carry forward source memories from the superseded fact
                foreach (var guid in closest.SourceMemories)
                {
                    if (!newFact.SourceMemories.Contains(guid))
                        newFact.SourceMemories.Add(guid);
                }
            }

            ExtractedFacts.Add(newFact);
            return newFact;
        }

        /// <summary>
        /// Returns the highest-importance non-superseded facts that fit within the specified token budget,
        /// formatted as a bullet list for inclusion in the system prompt.
        /// </summary>
        /// <remarks>
        /// Facts are ranked by importance score (<see cref="ExtractedFact.GetImportanceScore"/>),
        /// which combines reference count and recency. Facts are added in descending importance order
        /// until the token budget is exhausted.
        /// </remarks>
        /// <param name="tokenBudget">Maximum number of tokens the returned string may consume.</param>
        /// <returns>A formatted bullet list of core facts, or an empty string if there are no facts or the
        /// budget is zero.</returns>
        public virtual string GetCoreFacts(int tokenBudget)
        {
            if (tokenBudget <= 0 || ExtractedFacts.Count == 0)
                return string.Empty;

            var active = ExtractedFacts.FindAll(f => !f.Superseded && !string.IsNullOrWhiteSpace(f.Fact));
            if (active.Count == 0)
                return string.Empty;

            active.Sort((a, b) => b.GetImportanceScore().CompareTo(a.GetImportanceScore()));

            var sb = new System.Text.StringBuilder();
            var remaining = tokenBudget;

            foreach (var f in active)
            {
                var line = $"- {f.Fact}";
                var tokens = LLMEngine.GetTokenCount(line);
                if (tokens > remaining)
                    break;
                sb.AppendLinuxLine(line);
                remaining -= tokens;
            }

            return sb.ToString().CleanupAndTrim();
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

        /// <summary>
        /// Searches the memory vault for results that match the specified message, within the given constraints.
        /// </summary>
        /// <remarks>The search functionality depends on the RAG (Retrieval-Augmented Generation) feature
        /// being enabled in the settings. If RAG is disabled, the method returns an empty list. If the memory vault is
        /// uninitialized or empty, it will be reloaded before performing the search.</remarks>
        /// <param name="message">The input message to search for. This can be converted to third-person format based on the current settings.</param>
        /// <param name="maxRes">The maximum number of results to return.</param>
        /// <param name="maxDist">The maximum allowable distance for a result to be considered a match.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of <see
        /// cref="VaultResult"/> objects that match the search criteria. The list will be empty if no matches are found
        /// or if the search feature is disabled.</returns>
        public virtual async Task<List<VaultResult>> Search(string message, int maxRes, float maxDist)
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return [];
            if (MindPalace is null || MindPalace.Count == 0)
                ReloadMemories();
            return await MindPalace!.Search(LLMEngine.Settings.RAGConvertTo3rdPerson ? message.ConvertToThirdPerson() : message, maxRes, maxDist).ConfigureAwait(false);
        }

        public virtual bool ReplaceMemory(MemoryUnit source, MemoryUnit replacement)
        {
            var idx = Memories.IndexOf(source);
            if (idx != -1)
            {
                Memories[idx] = replacement;
                return true;
            }
            return false;
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
            if (!LLMEngine.Settings.RAGEnabled)
                return null;

            foreach (var item in Eurekas)
            {
                if (item.Added > DateTime.Now)
                    continue;
                if (item.CheckKeywords(userinput))
                    return item;

                // get item.Name and compare it to userinput to count amount of identical words (ignoring case)
                var itemWords = item.Name.ToLowerInvariant().Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
                var inputWords = userinput.ToLowerInvariant().Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':'], StringSplitOptions.RemoveEmptyEntries);
                var commonWordCount = itemWords.Intersect(inputWords).Count();
               
                var dist = await EmbedTools.GetDistanceAsync(userinput, item).ConfigureAwait(false);
                dist -= commonWordCount * 0.02f; // each common word reduces distance by 0.02
                if (item.Insertion == MemoryInsertion.NaturalForced)
                    dist -= 0.02f;

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
                mylist = mylist.FindAll(e => e.Insertion == MemoryInsertion.NaturalForced && e.Added <= DateTime.Now);
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
        /// <param name="info">text to be inserted</param>
        /// <param name="uid">optional unique identifier for the insert, if it exists in current inserts, it'll be updated</param>
        public UserReturnInsert? AddUserReturnInsert(string info, string? uid = null)
        {
            if (string.IsNullOrWhiteSpace(info))
                return null;
            // Check if an insert with the same info already exists, if so, update its timestamp and return it
            var existing = Inserts.Find(i => i.Info.Equals(info, StringComparison.InvariantCultureIgnoreCase));
            if (existing is not null)
            {
                existing.UID = uid;
                existing.Added = DateTime.Now;
                return existing;
            }
            // If a UID is provided, check for an existing insert with the same UID, if so, update its info and timestamp and return it
            if (!string.IsNullOrWhiteSpace(uid))
            {
                existing = Inserts.Find(i => i.UID == uid);
                if (existing is not null)
                {
                    existing.Info = info;
                    existing.Added = DateTime.Now;
                    return existing;
                }
            }
            // If no existing insert is found or no uid, create a new one and add it to the list
            var x = new UserReturnInsert(info)
            {
                UID = uid,
                Added = DateTime.Now
            };
            Inserts.Add(x);
            return x;
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

        public bool RemoveUserReturnInsert(string id)
        {
            var existing = Inserts.Find(i => i.UID == id);
            if (existing != null)
            {
                Inserts.Remove(existing);
                return true;
            }
            return false;
        }

        public virtual SingleMessage? BuildAwayMessage(bool forced = false)
        {
            if (LLMEngine.Settings.DisableDateAndMoodIfNotLastSession && LLMEngine.History.CurrentSession != LLMEngine.History.Sessions.Last() && !forced)
                return null;

            // no previous user message, nothing to do either, chat just started
            var lastmsg = LLMEngine.History.GetLastMessageFrom(LLMEngine.User);
            if (lastmsg == null || ((DateTime.Now - lastmsg.Date) < TimeSpan.FromHours(HoursBeforeAFK) && !forced))
                return null;

            // check if we have a previous message in current session, and if it's already a system msg, gtfo
            if (LLMEngine.History.CurrentSession.Messages.Count > 1 && LLMEngine.History.CurrentSession.Messages[^2].Role == AuthorRole.System && !forced)
                return null;

            if (!Owner.SenseOfTime && !MoodHandling && Inserts.Count == 0 && !forced)
                return null;

            var totalmessage = new StringBuilder();
            if (Owner.SenseOfTime)
            {
                var res = GetTimeSinceLastMessage();
                if (!string.IsNullOrEmpty(res))
                {
                    totalmessage.AppendLinuxLine(res);
                    totalmessage.AppendLinuxLine();
                }

                res = GetDailySchedule(DateTime.Now.DayOfWeek);
                if (!string.IsNullOrWhiteSpace(res))
                {
                    totalmessage.AppendLinuxLine($"{DailySchedulePrefix} {res}");
                    totalmessage.AppendLinuxLine();
                }
            }

            if (MoodHandling)
            {
                var res = Mood.Describe();
                if (!string.IsNullOrWhiteSpace(res))
                {
                    totalmessage.AppendLinuxLine(res);
                    totalmessage.AppendLinuxLine();
                }
            }

            if (Inserts.Count > 0)
            {
                foreach (var item in Inserts)
                {
                    totalmessage.AppendLinuxLine(item.Info);
                    totalmessage.AppendLinuxLine();
                }
                Inserts.Clear();
            }

            var InsertMems = Memories.FindAll(m => m.Insertion == MemoryInsertion.UserReturn && m.Added <= DateTime.Now);
            foreach (var mem in InsertMems)
            {
                totalmessage.AppendLinuxLine(mem.ToSnippet(TitleInsertType.None, false, false, false));
                totalmessage.AppendLinuxLine();
                mem.Touch();
                mem.Insertion = MemoryInsertion.None;
            }

            if (totalmessage.Length == 0)
                return null;

            var final = Owner.ReplaceMacros(totalmessage.ToString()).CleanupAndTrim();
            var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, final, Owner.GetIdentifier(), LLMEngine.User.GetIdentifier(), true);
            return tosend;
        }

        /// <summary>
        /// Returns an away string depending on the last chat's date.
        /// </summary>
        /// <returns></returns>
        protected virtual string GetTimeSinceLastMessage()
        {
            var lastusermsg = Owner.History.GetLastMessageFrom(AuthorRole.User);
            if (lastusermsg == null || Owner.History.CurrentSession != Owner.History.Sessions.Last())
                return string.Empty;

            var timespan = DateTime.Now - lastusermsg.Date;

            var msgtxt = (DateTime.Now.Date != lastusermsg.Date.Date) || (timespan > new TimeSpan(6, 0, 0)) ?
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

        #region *** Daily Schedule ***

        public virtual void SetDailySchedule(DayOfWeek day, string schedule)
        {
            DailySchedule[(int)day] = schedule;
        }

        public virtual string GetDailySchedule(DayOfWeek day)
        {
            return DailySchedule[(int)day];
        }

        #endregion
    }
}
