using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.Runtime.ConstrainedExecution;
using System.Text;

namespace LetheAISharp.Moods
{
    public class MoodCuriosity : IMoodlet
    {
        public string Id { get; set; } = "Curiosity";
        public string Description { get; set; } = "How curious the persona feels.";
        public double NaturalValue { get; set; } = 0.5;
        public double NaturalChangeRate { get; set; } = 0.005;

        public double OnTimePassed(double currentValue, TimeSpan timeSpan)
        {
            var val = currentValue;
            if (timeSpan >= TimeSpan.FromDays(7))
            {
                val = 0.6;
            }
            else if (timeSpan >= TimeSpan.FromDays(0.5))
            {
                val += timeSpan.TotalDays;
            }
            return Math.Clamp(val, 0, 1);
        }

        public double InterpretMessage(double currentValue, string message)
        {
            var val = currentValue;
            if (LLMEngine.History.GetLastFromInSession(AuthorRole.Assistant)?.Message?.Contains('?') == true)
            {
                val -= 0.005;
            }
            return Math.Clamp(val, 0, 1);
        }

        public string GetAdjective(double value)
        {
            if (value < 0.25)
                return "disinterested";
            else if (value > 0.7 && value <= 0.85)
                return "curious";
            else if (value > 0.85)
                return "inquisitive";
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
