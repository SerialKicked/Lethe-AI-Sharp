using AIToolkit.Agent;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Memory
{
    /// <summary>
    /// list of "what's up" and similar sentences open ended intros to trigger a memory recall
    /// </summary>
    internal static class MemoryTriggers
    {
        private static readonly List<string> EurekaTriggers =
        [
            "any updates",
            "any developments",
            "any breakthroughs",
            "any discoveries",
            "any news",
            "anything interesting",
            "anything new",
            "anything exciting",
            "anything noteworthy",
            "anything remarkable",
            "pick a topic",
            "pick something",
            "something new",
            "something to share",
            "something interesting",
            "share something",
            "share anything",
            "share news",
            "share updates",
            "talk about?",
            "what have you learned",
            "what's going on",
            "what's happening",
            "what's the latest",
            "what's the scoop",
            "what's the buzz",
            "what's the word",
            "what's up",
            "what's new",
        ];

        private static readonly List<string> ComplimentTriggers =
        [
            "you look nice",
            "you look great",
            "you did well",
            "good job",
            "well done",
            "congrats",
            "bravo",
            "kudos",
            "thank you",
            "thanks",
            "much appreciated",
            "I appreciate it",
            "you are amazing",
            "you are awesome",
            "you are the best",
            "you are incredible",
            "you are fantastic",
            "you are wonderful",
            "you are impressive",
            "you are outstanding",
            "you are remarkable",
            "you are extraordinary",
            "you are exceptional",
            "you are brilliant",
            "you are superb",
            "you're amazing",
            "you're awesome",
            "you're the best",
            "you're incredible",
            "you're fantastic",
            "you're wonderful",
            "you're impressive",
            "you're remarkable",
            "you're extraordinary",
            "you're exceptional",
            "you're brilliant",
        ];


        public static bool IsEurekaTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return EurekaTriggers.Any(trigger => lowered.Contains(trigger));
        }

        public static bool IsComplimentTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return ComplimentTriggers.Any(trigger => lowered.Contains(trigger));
        }
    }

    public class MoodState
    {
        private double energy = 0.5;
        private double cheer = 0.5;
        private double curiosity = 0.5;

        public double Energy 
        { 
            get => energy;
            set
            {
                energy = value;
                energy = Math.Clamp(energy, 0, 1);
            }
        }

        public double Cheer 
        { 
            get => cheer;
            set
            {
                cheer = value;
                cheer = Math.Clamp(cheer, 0, 1);
            }
        }

        public double Curiosity 
        { 
            get => curiosity;
            set
            {
                curiosity = value;
                curiosity = Math.Clamp(curiosity, 0, 1);
            }
        }

        public virtual void Update()
        {
            // Natural decay towards neutral state (0.5)
            Energy += (0.5 - Energy) * 0.005;
            Cheer += (0.5 - Cheer) * 0.005;
            Curiosity += (0.5 - Curiosity) * 0.005;

            // Special cases based on time since last message exchanged
            var msg = LLMSystem.History.LastMessage();
            if (msg != null)
            {
                var timeSinceLast = (DateTime.Now - msg.Date);
                if (timeSinceLast >= TimeSpan.FromDays(7))
                {
                    // Long gap increase energy and curiosity, but decreases cheer
                    Energy = 0.6;
                    Curiosity = 1;
                    Cheer -= 0.05 * timeSinceLast.TotalDays;
                }
                else if (timeSinceLast >= TimeSpan.FromDays(0.5))
                {
                    // Recent interaction increases cheer
                    Energy += 0.2 * timeSinceLast.TotalDays;
                    Curiosity += 0.02 * timeSinceLast.TotalDays;
                }
            }
        }

        public virtual void Interpret(string userMessage)
        {
            if (MemoryTriggers.IsComplimentTrigger(userMessage))
            {
                Cheer += 0.1;
                Energy += 0.05;
            };
        }

        public virtual string Describe()
        {
            var sb = new StringBuilder("{{char}} is currently feeling");
            if (Energy < 0.35)
                sb.Append(" tired");
            else if (Energy > 0.65)
                sb.Append(" energetic");
            else
                sb.Append(" rested");

            if (Cheer < 0.15)
                sb.Append(", sad");
            if (Cheer < 0.35)
                sb.Append(", moody");
            else if (Cheer > 0.65)
                sb.Append(", joyful");
            else if (Cheer > 0.85)
                sb.Append(", happy");

            if (Curiosity < 0.25)
                sb.Append(", and disinterested");
            else if (Curiosity > 0.65)
                sb.Append(", and curious");
            sb.Append('.');
            return sb.ToString();
        }
    }


    public class Brain(BasePersona basePersona)
    {
        [JsonIgnore] private BasePersona Owner { get; set; } = basePersona;
        public TimeSpan MinInsertDelay { get; set; } = TimeSpan.FromMinutes(15);
        public int MinMessageDelay { get; set; } = 4;
        public TimeSpan EurekaCutOff { get; set; } = TimeSpan.FromDays(15);
        public DateTime LastInsertTime { get; set; }
        public int CurrentDelay { get; set; } = 0;

        public List<MemoryUnit> Memories { get; set; } = [];
        public List<TopicSearch> RecentSearches { get; set; } = [];
        [JsonIgnore] public List<MemoryUnit> Eurekas { get; set; } = [];

        public virtual MoodState Mood { get; set; } = new MoodState();

        /// <summary>
        /// Checks if Brain functionality should be disabled (currently returns true for group conversations)
        /// </summary>
        private bool IsBrainDisabled => LLMSystem.IsGroupConversation;

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
            if (IsBrainDisabled)
                return;
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            RefreshMemories();
        }

        /// <summary>
        /// Move memories to to their proper slot, delete old stuff.
        /// </summary>
        private void RefreshMemories()
        {
            // Remove old natural memories
            Memories.RemoveAll(e => (e.Insertion == MemoryInsertion.Natural || e.Insertion == MemoryInsertion.NaturalForced)  && (DateTime.Now - e.Added) > EurekaCutOff);
            // Select all natural memories within the cutoff period, order by Added descending, and enqueue them
            Eurekas.Clear();
            var cutoff = DateTime.Now - EurekaCutOff;
            var recent = Memories.Where(m => (m.Insertion == MemoryInsertion.Natural || m.Insertion == MemoryInsertion.NaturalForced) && m.Added >= cutoff)
                                .OrderByDescending(m => m.Added).ToList();
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
            if (IsBrainDisabled || message.Role != AuthorRole.User || Owner.History.CurrentSession.Messages.Count < 1)
                return;

            // First, check if there's a long time between message and last message, if so do the mood related stuff
            if (Owner.SenseOfTime)
            {
                Mood.Update();
                Mood.Interpret(message.Message);
                var lastmsg = Owner.History.CurrentSession.Messages[^1];
                var timeSinceLast = (DateTime.Now - lastmsg.Date);
                if (timeSinceLast >= TimeSpan.FromHours(4))
                {
                    var info = LLMSystem.GetAwayString() + " " + Mood.Describe();
                    info = LLMSystem.ReplaceMacros(info.CleanupAndTrim());
                    var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, info, Owner.UniqueName, LLMSystem.User.UniqueName, true);
                    LLMSystem.History.LogMessage(tosend);
                    // Stop here, don't insert a eureka right after this one.
                    return;
                }
            }

            RefreshMemories();
            if (!string.IsNullOrWhiteSpace(LLMSystem.Settings.ScenarioOverride) || Eurekas.Count == 0)
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
        protected virtual async Task<MemoryUnit?> GetRelevantEureka(string userinput, float maxDistance = 0.075f)
        {
            if (IsBrainDisabled ||!RAGSystem.Enabled)
                return null;

            foreach (var item in Eurekas)
            {
                var dist = await RAGSystem.GetDistanceAsync(userinput, item).ConfigureAwait(false);
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
            if (IsBrainDisabled)
                return;
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
                LLMSystem.User.UniqueName,
                true);

            LLMSystem.History.LogMessage(tosend);
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

            if (RAGSystem.Enabled)
            {
                foreach (var item in RecentSearches)
                {
                    if (await RAGSystem.GetDistanceAsync(item.Topic, topic) < maxDistance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks if a memory of a specific type with the given session ID exists in the collection.
        /// </summary>
        /// <param name="memType"></param>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public bool Has(MemoryType memType, Guid sessionId) => Memories.Any(m => m.Category == memType && m.Guid == sessionId);

        /// <summary>
        /// Retrieves a local embed object with the specified unique identifier.
        /// </summary>
        /// <param name="iD">The unique identifier of the embed to retrieve.</param>
        /// <returns>The embed object with the specified identifier, or <see langword="null"/> if no matching embed is found.</returns>
        internal IEmbed? GetEmbedByID(Guid iD)
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

    }
}
