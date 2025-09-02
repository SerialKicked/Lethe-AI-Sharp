using AIToolkit.Files;
using AIToolkit.LLM;

namespace AIToolkit.Tests
{
    /// <summary>
    /// Simple test to demonstrate and validate group chat functionality
    /// </summary>
    public static class GroupChatTest
    {
        public static void RunBasicTest()
        {
            Console.WriteLine("=== Group Chat Basic Test ===");
            
            // Create test personas
            var user = new BasePersona
            {
                IsUser = true,
                Name = "Alice",
                UniqueName = "alice_user",
                Bio = "A curious student who loves to learn."
            };

            var bot1 = new BasePersona
            {
                IsUser = false,
                Name = "Einstein",
                UniqueName = "einstein_bot",
                Bio = "A brilliant physicist with a love for thought experiments.",
                FirstMessage = ["Hello! I'm Albert Einstein. What physics question can I help you with today?"],
                Scenario = "You are in Einstein's study, discussing physics and the universe."
            };

            var bot2 = new BasePersona
            {
                IsUser = false,
                Name = "Darwin",
                UniqueName = "darwin_bot",
                Bio = "A naturalist passionate about evolution and the natural world.",
                FirstMessage = ["Greetings! I'm Charles Darwin. Shall we explore the wonders of natural selection?"],
                Scenario = "You are aboard the HMS Beagle, discussing evolution and natural history."
            };

            // Load personas into the system
            LLMSystem.LoadPersona([user, bot1, bot2]);

            // Create group persona
            var group = LLMSystem.CreateGroupPersona(
                ["einstein_bot", "darwin_bot"], 
                "Science Discussion Group",
                "This is a scientific discussion group. Choose which scientist should respond based on the topic being discussed. Einstein should respond to physics questions, Darwin to biology questions."
            );

            Console.WriteLine($"Group created: {group.Name}");
            Console.WriteLine($"Group has {group.BotPersonas.Count} bots: {string.Join(", ", group.BotPersonas.Select(b => b.Name))}");
            Console.WriteLine($"Active bot: {group.ActiveBot?.Name}");

            // Test macro replacement
            var testPrompt = "Hello {{groupchars}}! I'm {{user}} and I'd like to discuss {{char}} with you.";
            var replacedPrompt = LLMSystem.ReplaceGroupMacros(testPrompt, user, group);
            Console.WriteLine($"\nOriginal prompt: {testPrompt}");
            Console.WriteLine($"Replaced prompt: {replacedPrompt}");

            // Test system prompt generation
            var systemPrompt = new SystemPrompt();
            var rawPrompt = systemPrompt.GetSystemPromptRaw(group);
            var finalPrompt = LLMSystem.ReplaceMacros(rawPrompt, user, group);
            Console.WriteLine($"\nSystem prompt preview (first 500 chars):");
            Console.WriteLine(finalPrompt.Substring(0, Math.Min(500, finalPrompt.Length)) + "...");

            // Test adding/removing bots
            group.SetActiveBot("darwin_bot");
            Console.WriteLine($"\nChanged active bot to: {group.ActiveBot?.Name}");

            // Test group bio generation
            var groupBio = group.GetBio(user.Name);
            Console.WriteLine($"\nGroup bio preview (first 300 chars):");
            Console.WriteLine(groupBio.Substring(0, Math.Min(300, groupBio.Length)) + "...");

            Console.WriteLine("\n=== Group Chat Test Completed ===");
        }

        public static void TestMacroReplacement()
        {
            Console.WriteLine("=== Macro Replacement Test ===");
            
            var user = new BasePersona { IsUser = true, Name = "TestUser", Bio = "A test user." };
            var bot1 = new BasePersona { IsUser = false, Name = "Bot1", Bio = "First test bot." };
            var bot2 = new BasePersona { IsUser = false, Name = "Bot2", Bio = "Second test bot." };
            
            LLMSystem.LoadPersona([user, bot1, bot2]);
            
            var group = new GroupPersona();
            group.AddBot("Bot1");
            group.AddBot("Bot2");
            
            var testCases = new[]
            {
                "{{user}} talks to {{char}}",
                "{{userbio}} and {{charbio}}",
                "Group members: {{groupchars}}",
                "All bios: {{groupbios}}",
                "Current scenario: {{scenario}}"
            };

            foreach (var testCase in testCases)
            {
                var result = LLMSystem.ReplaceGroupMacros(testCase, user, group);
                Console.WriteLine($"'{testCase}' → '{result}'");
            }
            
            Console.WriteLine("=== Macro Test Completed ===");
        }
    }
}