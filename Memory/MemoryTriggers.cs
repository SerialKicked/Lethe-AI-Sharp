namespace AIToolkit.Memory
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

        private static readonly List<string> ComplimentTriggers =
        [
            "you look nice",
            "you look great",
            "you did well",
            "good job",
            "well done",
            "congrats",
            "bravo",
            "kudos",
            "thank you",
            "thanks",
            "much appreciated",
            "I appreciate it",
            "you are amazing",
            "you are awesome",
            "you are the best",
            "you are incredible",
            "you are fantastic",
            "you are wonderful",
            "you are impressive",
            "you are outstanding",
            "you are remarkable",
            "you are extraordinary",
            "you are exceptional",
            "you are brilliant",
            "you are superb",
            "you're amazing",
            "you're awesome",
            "you're the best",
            "you're incredible",
            "you're fantastic",
            "you're wonderful",
            "you're impressive",
            "you're remarkable",
            "you're extraordinary",
            "you're exceptional",
            "you're brilliant",
        ];

        public static bool IsEurekaTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return EurekaTriggers.Any(trigger => lowered.Contains(trigger));
        }

        public static bool IsComplimentTrigger(string input)
        {
            var lowered = input.ToLowerInvariant();
            return ComplimentTriggers.Any(trigger => lowered.Contains(trigger));
        }
    }
}
