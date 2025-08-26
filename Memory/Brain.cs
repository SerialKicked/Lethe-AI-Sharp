using AIToolkit.Files;
using AIToolkit.LLM;
using Newtonsoft.Json;
using System.Text;

namespace AIToolkit.Memory
{
    /// <summary>
    /// list of "what's up" and similar sentences open ended intros to trigger a memory recall
    /// </summary>
    internal static class MemoryTriggers
    {
        private static readonly List<string> Triggers =
        [
            "what's up",
            "what's new",
            "anything new",
            "anything interesting",
            "any updates",
            "what have you learned",
            "tell me something new",
            "tell me something interesting",
            "any news",
            "what's going on",
            "what's happening",
            "what's the latest",
            "any developments",
            "any breakthroughs",
            "any discoveries",
            "what's the scoop",
            "what's the buzz",
            "what's the word",
            "anything exciting",
            "anything noteworthy",
            "anything remarkable"
        ];

        public static bool IsTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return Triggers.Any(trigger => lowered.Contains(trigger));
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
            if ((CurrentDelay < MinMessageDelay || LastInsertTime + MinInsertDelay > DateTime.Now) && !MemoryTriggers.IsTrigger(userinput))
                return;
            InsertEureka();
        }

        public void InsertEureka()
        {
            CurrentDelay = 0;
            LastInsertTime = DateTime.Now;
            if (!Eurekas.TryDequeue(out var memory))
                return;
            var text = new StringBuilder($"You remember something you've researched on the web recently. This was about the following topic: {memory.Name}.").AppendLinuxLine().AppendLinuxLine($"{memory.Content}").AppendLinuxLine().Append("Mention this information when there's a lull in the discussion, or if the user makes a mention of it, or if you feel like it's a good idea to talk about it now.");
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
