namespace LetheAISharp.LLM
{
    public interface IMoodlet
    {
        string Name { get; set; }
        double NeutralDecayRate { get; set; }
        double NeutralValue { get; set; }
        List<string> Triggers { get; set; }

        string GetAdjective(double value);
        double InterpretMessage(double currentValue, string message);
        double OnTimePassed(double currentValue, TimeSpan timeSpan);
    }
}