using LetheAISharp.LLM;
using System.Net.Http.Headers;
using System.Text;

namespace LetheAISharp.Memory
{

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
            var msg = LLMEngine.History.GetLastMessageFrom(AuthorRole.User);
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

        protected virtual List<string> GetAdjectives()
        {
            var lst = new List<string>();
            if (Energy < 0.35)
                lst.Add("tired");
            else if (Energy > 0.65)
                lst.Add("energetic");
            else
                lst.Add("rested");

            if (Cheer < 0.15)
                lst.Add("sad");
            else if (Cheer < 0.35)
                lst.Add("moody");
            else if (Cheer > 0.65)
                lst.Add("happy");
            else if (Cheer > 0.85)
                lst.Add("joyful");

            if (Curiosity < 0.25)
                lst.Add("disinterested");
            else if (Curiosity > 0.65)
                lst.Add("curious");
            return lst;
        }

        public virtual string Describe()
        {
            var sb = new StringBuilder("{{char}} is currently feeling");
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
    }
}
