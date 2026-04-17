using CommunityToolkit.HighPerformance.Helpers;
using LetheAISharp.Agent;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace LetheAISharp.Moods
{


    public class MoodManager
    {
        [JsonIgnore] public static readonly List<string> ComplimentTriggers = [           
            "you look nice", "you look great", "you did well", "good job", "well done", "congrats", "bravo", "kudos", "thank you",
            "thanks", "much appreciated","I appreciate it", "you are amazing", "you are awesome", "you are the best", "you are incredible",
            "you are fantastic", "you are wonderful", "you are impressive", "you are outstanding", "you are remarkable", "you are extraordinary",
            "you are exceptional", "you are brilliant", "you are superb", "you're amazing", "you're awesome", "you're the best", 
            "you're incredible", "you're fantastic", "you're wonderful", "you're impressive", "you're remarkable", "you're extraordinary",
            "you're exceptional", "you're brilliant",
        ];

        [JsonIgnore] public static Dictionary<string, IMoodlet> Moodlets { get; protected set; } = [];

        public Dictionary<string, double> MoodData { get; set; } = [];

        public virtual void Update()
        {
            var msg = LLMEngine.History.GetLastMessageFrom(AuthorRole.User);
            foreach (var moodlet in MoodData)
            {
                if (Moodlets.TryGetValue(moodlet.Key, out var m))
                {
                    MoodData[moodlet.Key] += (m.NaturalValue - moodlet.Value) * m.NaturalChangeRate;
                    if (msg != null)
                    {
                        var timeSinceLast = (DateTime.Now - msg.Date);
                        MoodData[moodlet.Key] = m.OnTimePassed(moodlet.Value, timeSinceLast);
                    }
                }
            }
        }

        public virtual void Interpret(string userMessage)
        {
            foreach (var moodlet in MoodData)
            {
                if (Moodlets.TryGetValue(moodlet.Key, out var m))
                {
                    MoodData[moodlet.Key] = m.InterpretMessage(moodlet.Value, userMessage);
                }
            }
        }

        protected virtual List<string> GetAdjectives()
        {
            var lst = new List<string>();
            foreach (var moodlet in MoodData)
            {
                if (Moodlets.TryGetValue(moodlet.Key, out var m))
                {
                    var adj = m.GetAdjective(moodlet.Value);
                    if (!string.IsNullOrEmpty(adj))
                    {   
                        lst.Add(adj);
                    }
                }
            }
            return lst;
        }

        public virtual string Describe()
        {
            var sb = new StringBuilder("{{mchar}} is currently feeling");
            var moods = GetAdjectives();
            if (moods.Count > 0)
            {
                sb.Append(' ');
                sb.Append(string.Join(", ", moods));
                sb.Append('.');
                return sb.ToString();
            }
            return string.Empty;
        }

        public static bool IsComplimentTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return ComplimentTriggers.Any(trigger => lowered.Contains(trigger));
        }

        public static void LoadDefaultMoods()
        {
            Moodlets["Cheer"] = new MoodCheer();
            Moodlets["Curiosity"] = new MoodCuriosity();
            Moodlets["Energy"] = new MoodEnergy();
        }

        /// <summary>
        /// Scans an already-loaded assembly and auto-discovers and registers all
        /// <see cref="IMoodlet"/> implementations.
        /// </summary>
        /// <param name="assembly">Assembly to scan for moodlet plugin types.</param>
        public static void RegisterFromAssembly(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            var dllName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";

            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IMoodlet).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                var instance = (IMoodlet)Activator.CreateInstance(type)!;
                if (Moodlets.ContainsKey(instance.Id))
                    continue;
                Moodlets[instance.Id] = instance;
                LLMEngine.Logger?.LogInformation("Auto-registered moodlet plugin: {name} from {dll}", instance.Id, dllName);
            }
        }
    }
}
