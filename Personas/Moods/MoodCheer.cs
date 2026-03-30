using LetheAISharp.LLM;
using LetheAISharp.Memory;
using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using System.Text;

namespace LetheAISharp.Moods
{
    public class MoodCheer : IMoodlet
    {
        public string Id { get; set; } = "Cheer";
        public string Description { get; set; } = "How cheerful the persona feels.";
        public double NaturalValue { get; set; } = 0.5;
        public double NaturalChangeRate { get; set; } = 0.005;

        public double OnTimePassed(double currentValue, TimeSpan timeSpan)
        {
            var val = currentValue;
            if (timeSpan >= TimeSpan.FromDays(7))
            {
                // Long gap increase energy and curiosity, but decreases cheer
                val = 0.6;
            }
            else if (timeSpan >= TimeSpan.FromDays(0.5))
            {
                val += 0.2 * timeSpan.TotalDays;
            }
            return Math.Clamp(val, 0, 1);
        }

        public double InterpretMessage(double currentValue, string message)
        {
            var val = currentValue;
            if (MoodManager.IsComplimentTrigger(message))
                val += 0.075;
            return Math.Clamp(val, 0, 1);
        }

        public string GetAdjective(double value)
        {
            if (value < 0.15)
                return "sad";
            else if (value < 0.35)
                return "moody";
            else if (value > 0.65)
                return "happy";
            else if (value > 0.85)
                return "joyful";

            return string.Empty;
        }

        public double ProcessNewSession(double currentValue, Modifier modifier)
        {
            var cvalue = currentValue;
            switch (modifier)
            {
                case Modifier.HighReduction:
                    cvalue -= 0.2;
                    break;
                case Modifier.SmallReduction:
                    cvalue -= 0.1;
                    break;
                case Modifier.None:
                    break;
                case Modifier.SmallIncrease:
                    cvalue += 0.1;
                    break;
                case Modifier.HighIncrease:
                    cvalue += 0.2;
                    break;
                default:
                    break;
            }
            return Math.Clamp(cvalue, 0, 1);
        }
    }
}
