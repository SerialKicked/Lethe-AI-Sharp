using AIToolkit.Files;
using AIToolkit.Files.Tests;
using AIToolkit.LLM;
using System;
using System.Reflection;

namespace AIToolkit.TestRunner
{
    public static class TestRunner
    {
        public static void RunTests()
        {
            Console.WriteLine("AIToolkit Group Chat Test Runner");
            Console.WriteLine("=================================");
            Console.WriteLine();

            try
            {
                // Run the group chat tests
                GroupChatTest.RunAllTests();
                
                Console.WriteLine();
                Console.WriteLine("Testing macro functionality...");
                TestMacroFunctionality();
                
                Console.WriteLine();
                Console.WriteLine("All tests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static void TestMacroFunctionality()
        {
            // Create simple test personas
            var alice = new BasePersona
            {
                Name = "Alice",
                Bio = "Helpful assistant",
                UniqueName = "alice",
                IsUser = false
            };

            var bob = new BasePersona
            {
                Name = "Bob",
                Bio = "Creative assistant",
                UniqueName = "bob",
                IsUser = false
            };

            var user = new BasePersona
            {
                Name = "User",
                IsUser = true,
                UniqueName = "user"
            };

            var group = new GroupPersona
            {
                Name = "Team",
                UniqueName = "team"
            };

            group.AddBotPersona(alice);
            group.AddBotPersona(bob);

            // Test basic macro replacement
            var testString = "Hi {{char}}! User: {{user}}";
            var result = LLMSystem.ReplaceMacros(testString, user, group);
            
            Console.WriteLine($"Input: {testString}");
            Console.WriteLine($"Output: {result}");
            
            if (!result.Contains("Hi Alice!"))
                throw new Exception("Macro replacement failed");
                
            Console.WriteLine("✓ Macro functionality test passed!");
        }
    }
}