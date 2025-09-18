using LetheAISharp.LLM;
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
}
