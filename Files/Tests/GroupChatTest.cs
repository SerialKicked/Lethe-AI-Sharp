using AIToolkit.Files;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;

namespace AIToolkit.Files.Tests
{
    /// <summary>
    /// Test class to verify group chat functionality
    /// </summary>
    public static class GroupChatTest
    {
        /// <summary>
        /// Test basic GroupPersona functionality
        /// </summary>
        public static void TestGroupPersonaBasics()
        {
            // Create individual bot personas
            var alice = new BasePersona
            {
                Name = "Alice",
                Bio = "A helpful AI assistant who loves solving problems.",
                UniqueName = "alice_bot",
                IsUser = false
            };

            var bob = new BasePersona
            {
                Name = "Bob", 
                Bio = "A creative AI who enjoys writing stories and poems.",
                UniqueName = "bob_bot",
                IsUser = false
            };

            // Create group persona
            var groupPersona = new GroupPersona
            {
                Name = "AI Team",
                Bio = "A collaborative group of AI assistants",
                UniqueName = "ai_team",
                Scenario = "A collaborative workspace where multiple AI assistants work together to help users."
            };

            // Test adding personas
            groupPersona.AddBotPersona(alice);
            groupPersona.AddBotPersona(bob);

            // Verify personas were added
            if (groupPersona.BotPersonas.Count != 2)
                throw new Exception("Failed to add bot personas to group");

            // Verify current bot is set
            if (groupPersona.CurrentBot == null)
                throw new Exception("Current bot should be set when first persona is added");

            if (groupPersona.CurrentBot.Name != "Alice")
                throw new Exception("Current bot should be Alice (first added)");

            // Test switching current bot
            groupPersona.SetCurrentBot("bob_bot");
            if (groupPersona.CurrentBot?.Name != "Bob")
                throw new Exception("Failed to switch current bot to Bob");

            // Test removing persona
            groupPersona.RemoveBotPersona("alice_bot");
            if (groupPersona.BotPersonas.Count != 1)
                throw new Exception("Failed to remove bot persona from group");

            // Current bot should still be Bob since we removed Alice
            if (groupPersona.CurrentBot?.Name != "Bob")
                throw new Exception("Current bot should still be Bob after removing Alice");

            Console.WriteLine("✓ GroupPersona basic functionality tests passed!");
        }

        /// <summary>
        /// Test group-specific macros
        /// </summary>
        public static void TestGroupMacros()
        {
            // Create test personas
            var alice = new BasePersona
            {
                Name = "Alice",
                Bio = "A helpful AI assistant who loves solving problems.",
                UniqueName = "alice_bot",
                IsUser = false
            };

            var bob = new BasePersona
            {
                Name = "Bob",
                Bio = "A creative AI who enjoys writing stories.",
                UniqueName = "bob_bot", 
                IsUser = false
            };

            var user = new BasePersona
            {
                Name = "TestUser",
                IsUser = true,
                UniqueName = "test_user"
            };

            // Create group persona
            var groupPersona = new GroupPersona
            {
                Name = "AI Team",
                Bio = "A collaborative group of AI assistants",
                UniqueName = "ai_team"
            };

            groupPersona.AddBotPersona(alice);
            groupPersona.AddBotPersona(bob);
            groupPersona.SetCurrentBot("alice_bot");

            // Test {{group}} macro
            var groupList = groupPersona.GetGroupPersonasList("TestUser");
            if (!groupList.Contains("Alice") || !groupList.Contains("Bob"))
                throw new Exception("Group list should contain both Alice and Bob");

            // Test macro replacement with group context
            var testString = "Current character: {{char}}, Group: {{group}}, Current bio: {{charbio}}";
            var result = LLMSystem.ReplaceMacros(testString, user, groupPersona);

            // Should use Alice as current character
            if (!result.Contains("Current character: Alice"))
                throw new Exception("{{char}} should resolve to current bot (Alice)");

            if (!result.Contains("Alice") || !result.Contains("Bob"))
                throw new Exception("{{group}} macro should list all participants");

            // Test switching current bot affects macros
            groupPersona.SetCurrentBot("bob_bot");
            var result2 = LLMSystem.ReplaceMacros(testString, user, groupPersona);

            if (!result2.Contains("Current character: Bob"))
                throw new Exception("{{char}} should resolve to current bot (Bob) after switching");

            Console.WriteLine("✓ Group macro replacement tests passed!");
        }

        /// <summary>
        /// Test LLMSystem group functionality
        /// </summary>
        public static void TestLLMSystemGroupSupport()
        {
            // Save original bot to restore later
            var originalBot = LLMSystem.Bot;
            var originalUser = LLMSystem.User;

            try
            {
                // Create test user
                var user = new BasePersona
                {
                    Name = "TestUser",
                    IsUser = true,
                    UniqueName = "test_user"
                };

                // Create group persona with bots
                var alice = new BasePersona
                {
                    Name = "Alice",
                    Bio = "Helpful assistant",
                    UniqueName = "alice_bot",
                    IsUser = false
                };

                var bob = new BasePersona
                {
                    Name = "Bob",
                    Bio = "Creative assistant", 
                    UniqueName = "bob_bot",
                    IsUser = false
                };

                var groupPersona = new GroupPersona
                {
                    Name = "AI Team",
                    UniqueName = "ai_team"
                };

                groupPersona.AddBotPersona(alice);
                groupPersona.AddBotPersona(bob);

                // Set up LLMSystem
                LLMSystem.User = user;
                LLMSystem.Bot = groupPersona;

                // Test group detection
                if (!LLMSystem.IsGroupConversation)
                    throw new Exception("LLMSystem should detect group conversation");

                var detectedGroup = LLMSystem.GetGroupPersona();
                if (detectedGroup == null)
                    throw new Exception("Should be able to get group persona from LLMSystem");

                // Test getting group bots
                var groupBots = LLMSystem.GetGroupBots();
                if (groupBots.Count != 2)
                    throw new Exception("Should return 2 group bots");

                // Test current bot management
                var currentBot = LLMSystem.GetCurrentGroupBot();
                if (currentBot?.Name != "Alice")
                    throw new Exception("Current group bot should be Alice");

                // Test switching current bot
                LLMSystem.SetCurrentGroupBot("bob_bot");
                currentBot = LLMSystem.GetCurrentGroupBot();
                if (currentBot?.Name != "Bob")
                    throw new Exception("Current group bot should be Bob after switching");

                Console.WriteLine("✓ LLMSystem group support tests passed!");
            }
            finally
            {
                // Restore original state
                LLMSystem.Bot = originalBot;
                LLMSystem.User = originalUser;
            }
        }

        /// <summary>
        /// Run all group chat tests
        /// </summary>
        public static void RunAllTests()
        {
            Console.WriteLine("Running Group Chat Tests...");
            Console.WriteLine();

            TestGroupPersonaBasics();
            TestGroupMacros();
            TestLLMSystemGroupSupport();

            Console.WriteLine();
            Console.WriteLine("✓ All Group Chat tests passed!");
        }
    }
}