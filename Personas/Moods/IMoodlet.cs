namespace LetheAISharp.Moods
{
    public enum Modifier { HighReduction, SmallReduction, None, SmallIncrease, HighIncrease }

    public interface IMoodlet
    {
        string Id { get; set; }
        string Description { get; set; }

        /// <summary>
        /// Gets or sets the natural rate of change for the associated value. This this the speed at which the moodlet's value will naturally move toward the natural value over time, independent of any external factors.
        /// </summary>
        double NaturalChangeRate { get; set; }

        /// <summary>
        /// The value toward the mood will naturally gravitate toward over time. 
        /// For example, a "hunger" moodlet might have a natural value of 1, meaning that over time the character will become more hungry if not affected by other factors. 
        /// A "happiness" moodlet might have a natural value of 0.5, meaning that over time the character will turn back to neutral.
        /// </summary>
        double NaturalValue { get; set; }

        /// <summary>
        /// The initial value for a freshly created character or when a moodlet is first applied. 
        /// This can be used to set a baseline for the moodlet's value, which will then evolve over time based on the natural change rate and any external factors.
        /// </summary>
        double StartingValue { get; set; }

        string GetAdjective(double value);
        double InterpretMessage(double currentValue, string message);
        double OnTimePassed(double currentValue, TimeSpan timeSpan);
        double ProcessNewSession(double currentValue, Modifier modifier);
    }
}