using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.LLM
{
    public class Moodlet : IMoodlet
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Triggers { get; set; } = [];
        public double NeutralValue { get; set; } = 0.5;
        public double NeutralDecayRate { get; set; } = 0.005;

        public double OnTimePassed(double currentValue, TimeSpan timeSpan)
        {
            return currentValue;
        }

        public double InterpretMessage(double currentValue, string message)
        {
            return currentValue;
        }

        public string GetAdjective(double value)
        {
            return string.Empty;
        }
    }
}
