namespace LetheAISharp.Moods
{
    public enum Modifier { HighReduction, SmallReduction, None, SmallIncrease, HighIncrease }

    public interface IMoodlet
    {
        string Id { get; set; }
        string Description { get; set; }
        double NaturalChangeRate { get; set; }
        double NaturalValue { get; set; }

        string GetAdjective(double value);
        double InterpretMessage(double currentValue, string message);
        double OnTimePassed(double currentValue, TimeSpan timeSpan);
        double ProcessNewSession(double currentValue, Modifier modifier);
    }
}