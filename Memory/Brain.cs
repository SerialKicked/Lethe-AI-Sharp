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


    public class MoodState
    {
        public double Energy = 0.5;   // 0 = tired, 1 = excitable
        public double Cheer = 0.5;   // 0 = moody, 1 = joyful
        public double Curiosity = 0.5; // 0 = disinterested, 1 = curious

        public void Update()
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
                if (timeSinceLast >= TimeSpan.FromDays(15))
                {
                    // Long gap increase energy and curiosity
                    Energy = 0.5;
                    Curiosity = 1;
                    Cheer -= 0.05 * timeSinceLast.TotalDays;
                }
                else if (timeSinceLast > TimeSpan.FromDays(1))
                {
                    // Recent interaction increases cheer
                    Energy += 0.2 * timeSinceLast.TotalDays;
                    Curiosity += 0.02 * timeSinceLast.TotalDays;
                }
            }

            // Clamp values between 0 and 1
            Energy = Math.Clamp(Energy, 0, 1);
            Cheer = Math.Clamp(Cheer, 0, 1);
            Curiosity = Math.Clamp(Curiosity, 0, 1);
        }

        public string Describe()
        {
            var sb = new StringBuilder("{{char}} is currently feeling");
            if (Energy < 0.35)
                sb.Append(" tired");
            else if (Energy > 0.65)
                sb.Append(" energetic");
            else
                sb.Append(" neutral in energy");
            if (Cheer < 0.35)
                sb.Append(", moody");
            else if (Cheer > 0.65)
                sb.Append(", joyful");
            if (Curiosity < 0.25)
                sb.Append(", disinterested");
            else if (Curiosity > 0.65)
                sb.Append(", curious");
            sb.Append(".");
            return sb.ToString();
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

        public MoodState Mood { get; set; } = new MoodState();

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
            Mood.Update();
        }

        public async Task RegenEmbeds()
        {
            foreach (var mem in Memories)
            {
                await mem.EmbedText();
            }
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
            var tosend = new SingleMessage(AuthorRole.System, DateTime.Now, memory.ToEureka(), LLMSystem.Bot.UniqueName, LLMSystem.User.UniqueName, true);
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

        internal IEmbed? GetMemoryByID(Guid iD)
        {
            return Memories.FirstOrDefault(m => m.Guid == iD);
        }
    }
}
