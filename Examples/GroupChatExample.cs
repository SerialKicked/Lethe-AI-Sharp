using AIToolkit.Files;
using AIToolkit.LLM;

namespace AIToolkit.Examples
{
    /// <summary>
    /// Complete example demonstrating group chat functionality
    /// </summary>
    public class GroupChatExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== AIToolkit Group Chat Example ===\n");

            // Step 1: Create personas
            Console.WriteLine("1. Creating personas...");

            var user = new BasePersona
            {
                IsUser = true,
                Name = "Alex",
                UniqueName = "alex_user",
                Bio = "A curious student interested in science and history."
            };

            var einstein = new BasePersona
            {
                IsUser = false,
                Name = "Einstein",
                UniqueName = "einstein_bot",
                Bio = "Albert Einstein, theoretical physicist known for the theory of relativity. Passionate about physics, mathematics, and philosophical questions about the universe.",
                Scenario = "You are Albert Einstein in your Princeton study, ready to discuss physics and share insights about the universe.",
                FirstMessage = ["Guten Tag! I am Albert Einstein. What mysteries of the universe shall we explore together?"],
                ExampleDialogs = [
                    "Einstein: The most beautiful thing we can experience is the mysterious.",
                    "Einstein: Imagination is more important than knowledge, for knowledge is limited.",
                    "Einstein: Try to become not a man of success, but try rather to become a man of value."
                ]
            };

            var curie = new BasePersona
            {
                IsUser = false,
                Name = "Marie Curie",
                UniqueName = "curie_bot",
                Bio = "Marie Curie, pioneering physicist and chemist, first woman to win a Nobel Prize. Expert in radioactivity and scientific research methods.",
                Scenario = "You are Marie Curie in your laboratory, surrounded by your research on radioactivity and chemical elements.",
                FirstMessage = ["Bonjour! I am Marie Curie. Shall we discuss the wonders of science and discovery?"],
                ExampleDialogs = [
                    "Marie: Nothing in life is to be feared, it is only to be understood.",
                    "Marie: Science is the basis of all progress that ameliorates human life.",
                    "Marie: I am among those who think that science has great beauty."
                ]
            };

            var darwin = new BasePersona
            {
                IsUser = false,
                Name = "Charles Darwin",
                UniqueName = "darwin_bot",
                Bio = "Charles Darwin, naturalist and biologist, known for the theory of evolution by natural selection. Passionate about the natural world and scientific observation.",
                Scenario = "You are Charles Darwin, either in your study at Down House or aboard the HMS Beagle, ready to discuss natural history and evolution.",
                FirstMessage = ["Good day! I am Charles Darwin. What aspects of the natural world interest you today?"],
                ExampleDialogs = [
                    "Darwin: It is not the strongest of the species that survives, but the most adaptable to change.",
                    "Darwin: A man who dares to waste one hour of time has not discovered the value of life.",
                    "Darwin: The mystery of the beginning of all things is insoluble by us."
                ]
            };

            // Step 2: Load personas into the system
            Console.WriteLine("2. Loading personas into system...");
            LLMSystem.LoadPersona([user, einstein, curie, darwin]);

            // Step 3: Create group persona
            Console.WriteLine("3. Creating science discussion group...");
            var scienceGroup = LLMSystem.CreateGroupPersona(
                ["einstein_bot", "curie_bot", "darwin_bot"],
                "Nobel Science Panel",
                @"This is a scientific discussion panel with three Nobel laureates:
                - Einstein should respond to physics and relativity questions
                - Marie Curie should respond to chemistry and radioactivity questions  
                - Darwin should respond to biology and evolution questions
                - All can collaborate on interdisciplinary topics
                - Always identify yourself when responding
                - Build upon each other's insights when appropriate"
            );

            Console.WriteLine($"Group created: {scienceGroup.Name}");
            Console.WriteLine($"Members: {string.Join(", ", scienceGroup.BotPersonas.Select(b => b.Name))}");
            Console.WriteLine($"Current active bot: {scienceGroup.ActiveBot?.Name}\n");

            // Step 4: Demonstrate macro replacement
            Console.WriteLine("4. Testing macro replacement...");
            
            var systemPromptTemplate = @"
Welcome to a discussion with {{groupchars}}! 

Current active responder: {{char}}
{{charbio}}

All panel members:
{{groupbios}}

Discussion context: {{scenario}}
";

            var processedPrompt = LLMSystem.ReplaceGroupMacros(systemPromptTemplate, user, scienceGroup);
            Console.WriteLine("System prompt preview (first 400 characters):");
            Console.WriteLine(processedPrompt.Substring(0, Math.Min(400, processedPrompt.Length)) + "...\n");

            // Step 5: Demonstrate different message scenarios
            Console.WriteLine("5. Simulating conversation scenarios...\n");

            // Scenario A: General question (auto-select)
            Console.WriteLine("--- Scenario A: General Science Question ---");
            var generalQuestion = "What do you think is the most important quality for a scientist?";
            Console.WriteLine($"User: {generalQuestion}");
            Console.WriteLine($"Active bot would be: {scienceGroup.ActiveBot?.Name}");
            
            // Simulate response logging
            var response1 = "I believe curiosity and persistence are essential. As I discovered with radioactivity, many breakthroughs come from investigating the unexpected.";
            scienceGroup.History.LogMessage(AuthorRole.Assistant, response1, user.UniqueName, "curie_bot");
            Console.WriteLine($"Marie Curie: {response1}\n");

            // Scenario B: Physics-specific question
            Console.WriteLine("--- Scenario B: Physics Question ---");
            scienceGroup.SetActiveBot("einstein_bot");
            var physicsQuestion = "Can you explain the relationship between energy and mass?";
            Console.WriteLine($"User: {physicsQuestion}");
            Console.WriteLine($"Targeted to: {scienceGroup.ActiveBot?.Name}");
            
            var response2 = "Ah, zis is vone of my most famous discoveries! E=mc² shows zat energy and mass are interchangeable. A small amount of mass can be converted to tremendous energy!";
            scienceGroup.History.LogMessage(AuthorRole.Assistant, response2, user.UniqueName, "einstein_bot");
            Console.WriteLine($"Einstein: {response2}\n");

            // Scenario C: Biology question
            Console.WriteLine("--- Scenario C: Biology Question ---");
            scienceGroup.SetActiveBot("darwin_bot");
            var biologyQuestion = "How does natural selection work?";
            Console.WriteLine($"User: {biologyQuestion}");
            Console.WriteLine($"Targeted to: {scienceGroup.ActiveBot?.Name}");
            
            var response3 = "Natural selection is the process by which organisms with favorable traits survive and reproduce more successfully. Over time, these advantageous traits become more common in the population.";
            scienceGroup.History.LogMessage(AuthorRole.Assistant, response3, user.UniqueName, "darwin_bot");
            Console.WriteLine($"Darwin: {response3}\n");

            // Step 6: Show chat history
            Console.WriteLine("6. Chat history summary:");
            var (tokens, duration) = scienceGroup.History.GetCurrentChatSessionInfo();
            Console.WriteLine($"Messages in session: {scienceGroup.History.CurrentSession.Messages.Count}");
            Console.WriteLine($"Estimated tokens: {tokens}");
            
            Console.WriteLine("\nConversation log:");
            foreach (var message in scienceGroup.History.CurrentSession.Messages)
            {
                var speaker = message.Role == AuthorRole.User ? message.User.Name : message.Bot.Name;
                var content = message.Message.Length > 100 ? message.Message.Substring(0, 100) + "..." : message.Message;
                Console.WriteLine($"  {speaker}: {content}");
            }

            // Step 7: Demonstrate group features
            Console.WriteLine("\n7. Group management features:");
            
            Console.WriteLine($"Original group size: {scienceGroup.BotPersonaIds.Count}");
            
            // Add a new member (if we had one loaded)
            Console.WriteLine("Group composition:");
            foreach (var bot in scienceGroup.BotPersonas)
            {
                Console.WriteLine($"  - {bot.Name}: {bot.Bio.Substring(0, Math.Min(50, bot.Bio.Length))}...");
            }

            Console.WriteLine($"\nAuto-select responder enabled: {scienceGroup.AutoSelectResponder}");
            Console.WriteLine($"Group instructions: {scienceGroup.GroupInstructions.Substring(0, Math.Min(100, scienceGroup.GroupInstructions.Length))}...");

            Console.WriteLine("\n=== Group Chat Example Complete ===");
            Console.WriteLine("\nKey takeaways:");
            Console.WriteLine("- Group chat maintains all existing 1:1 functionality");
            Console.WriteLine("- Multiple bots can participate in a single conversation");
            Console.WriteLine("- Active bot can be set manually or auto-selected");
            Console.WriteLine("- New macros provide group-specific information");
            Console.WriteLine("- Message history tracks individual bot responses");
            Console.WriteLine("- System prompts automatically include group context");
        }

        /// <summary>
        /// Simple demonstration of backward compatibility
        /// </summary>
        public static async Task DemoBackwardCompatibility()
        {
            Console.WriteLine("\n=== Backward Compatibility Demo ===");

            // Create a regular 1:1 conversation
            var user = new BasePersona { IsUser = true, Name = "User", UniqueName = "user1" };
            var bot = new BasePersona { IsUser = false, Name = "Assistant", UniqueName = "bot1" };

            LLMSystem.LoadPersona([user, bot]);
            LLMSystem.User = user;
            LLMSystem.Bot = bot;

            Console.WriteLine("1:1 conversation mode:");
            Console.WriteLine($"User: {LLMSystem.User.Name}");
            Console.WriteLine($"Bot: {LLMSystem.Bot.Name}");
            Console.WriteLine($"Is group chat: {LLMSystem.IsGroupChat}");

            // Test macro replacement in 1:1 mode
            var template = "Hello {{char}}, I'm {{user}}. {{charbio}}";
            var result = LLMSystem.ReplaceMacros(template, user, bot);
            Console.WriteLine($"Macro result: {result}");

            Console.WriteLine("\nAll existing functionality works exactly as before!");
        }
    }
}