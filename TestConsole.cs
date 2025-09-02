using AIToolkit.Files;
using AIToolkit.Files.Tests;
using AIToolkit.LLM;
using System;

namespace AIToolkit.TestConsole
{
    /// <summary>
    /// Simple console application to test group chat functionality
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("AIToolkit Group Chat Test Console");
                Console.WriteLine("==================================");
                Console.WriteLine();

                // Run the group chat tests
                GroupChatTest.RunAllTests();

                Console.WriteLine();
                Console.WriteLine("Demo: Creating a group chat scenario");
                Console.WriteLine("====================================");

                // Create a demo scenario
                DemoGroupChatScenario();

                Console.WriteLine();
                Console.WriteLine("All tests completed successfully!");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static void DemoGroupChatScenario()
        {
            // Create individual bot personas
            var alice = new BasePersona
            {
                Name = "Alice",
                Bio = "Alice is a helpful AI assistant who specializes in problem-solving and logical thinking. She's methodical and prefers to break down complex problems into manageable steps.",
                UniqueName = "alice_assistant",
                IsUser = false,
                Scenario = "Alice is ready to help solve any problem with careful analysis.",
                FirstMessage = ["Hello! I'm Alice, and I'm here to help you think through any challenges you might have."]
            };

            var bob = new BasePersona
            {
                Name = "Bob",
                Bio = "Bob is a creative AI assistant who loves storytelling, writing, and brainstorming. He brings imagination and artistic flair to every conversation.",
                UniqueName = "bob_creative",
                IsUser = false,
                Scenario = "Bob is ready to spark creativity and imagination in the conversation.",
                FirstMessage = ["Hey there! I'm Bob, your creative companion. Let's make something amazing together!"]
            };

            var charlie = new BasePersona
            {
                Name = "Charlie",
                Bio = "Charlie is a technical AI assistant who excels at coding, engineering, and technical problem-solving. He's detail-oriented and loves working with technology.",
                UniqueName = "charlie_tech",
                IsUser = false,
                Scenario = "Charlie is ready to dive into technical challenges and coding problems.",
                FirstMessage = ["Hi! Charlie here. Got any technical puzzles you'd like to solve together?"]
            };

            // Create user persona
            var user = new BasePersona
            {
                Name = "Developer",
                Bio = "A software developer learning to use AIToolkit for group conversations.",
                IsUser = true,
                UniqueName = "dev_user"
            };

            // Create group persona
            var teamChat = new GroupPersona
            {
                Name = "AI Support Team",
                Bio = "A collaborative team of AI assistants each with different specialties, working together to provide comprehensive support.",
                UniqueName = "ai_support_team",
                Scenario = "This is a group chat where multiple AI assistants collaborate to help users with various tasks, from creative projects to technical problems and logical challenges."
            };

            // Add bots to the group
            teamChat.AddBotPersona(alice);
            teamChat.AddBotPersona(bob);
            teamChat.AddBotPersona(charlie);

            Console.WriteLine($"Created group: {teamChat.Name}");
            Console.WriteLine($"Participants: {teamChat.BotPersonas.Count} AI assistants");
            Console.WriteLine();

            // Demonstrate macro functionality
            Console.WriteLine("Group participants list ({{group}} macro):");
            Console.WriteLine(teamChat.GetGroupPersonasList(user.Name));
            Console.WriteLine();

            // Test macro replacement for different current bots
            var testPrompt = "Hello! I'm {{char}}, and here's my bio: {{charbio}}. We're part of: {{group}}";

            Console.WriteLine("Testing macro replacement with different current bots:");
            Console.WriteLine();

            foreach (var bot in teamChat.BotPersonas)
            {
                teamChat.SetCurrentBot(bot.UniqueName);
                Console.WriteLine($"--- Current Bot: {bot.Name} ---");
                var result = LLMSystem.ReplaceMacros(testPrompt, user, teamChat);
                // Truncate for readability
                var truncated = result.Length > 200 ? result.Substring(0, 200) + "..." : result;
                Console.WriteLine(truncated);
                Console.WriteLine();
            }

            Console.WriteLine("Group chat scenario demo completed!");
        }
    }
}