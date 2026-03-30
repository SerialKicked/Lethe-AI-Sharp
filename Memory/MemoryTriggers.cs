namespace LetheAISharp.Memory
{
    /// <summary>
    /// list of "what's up" and similar sentences open ended intros to trigger a memory recall
    /// </summary>
    internal static class MemoryTriggers
    {
        private static readonly List<string> EurekaTriggers =
        [
            "any updates",
            "any developments",
            "any breakthroughs",
            "any discoveries",
            "any news",
            "anything interesting",
            "anything new",
            "anything exciting",
            "anything noteworthy",
            "anything remarkable",
            "pick a topic",
            "pick something",
            "something new",
            "something to share",
            "something interesting",
            "share something",
            "share anything",
            "share news",
            "share updates",
            "talk about?",
            "what have you learned",
            "what's going on",
            "what's happening",
            "what's the latest",
            "what's the scoop",
            "what's the buzz",
            "what's the word",
            "what's up",
            "what's new",
        ];

        public static bool IsEurekaTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return EurekaTriggers.Any(trigger => lowered.Contains(trigger));
        }
    }
}
