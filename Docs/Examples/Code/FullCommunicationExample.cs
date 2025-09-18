using System;
using System.Threading.Tasks;
using AIToolkit.LLM;
using AIToolkit.Files;
using System.Collections.Generic;

namespace AIToolkit.Examples
{
    /// <summary>
    /// Example demonstrating full communication mode with personas and chat history
    /// </summary>
    public class FullCommunicationExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== Full Communication Example ===");
            
            try
            {
                // Step 1: Setup and connect
                Console.WriteLine("Setting up connection...");
                LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
                await LLMEngine.Connect();
                
                if (LLMEngine.Status != SystemStatus.Ready)
                {
                    Console.WriteLine("Failed to connect to backend.");
                    return;
                }
                
                Console.WriteLine("Connected successfully!");
                Console.WriteLine();
                
                // Step 2: Create personas
                Console.WriteLine("Creating personas...");
                
                var bot = new BasePersona
                {
                    Name = "Dr. Science",
                    Bio = "A knowledgeable scientist who loves to explain complex topics in simple terms",
                    IsUser = false,
                    Scenario = "You are Dr. Science, a friendly researcher who enjoys teaching others about science and technology.",
                    FirstMessage = new List<string>
                    {
                        "Hello! I'm Dr. Science. What scientific question can I help you explore today?",
                        "Greetings! I'm here to discuss any scientific topics you're curious about.",
                        "Welcome to my lab! What would you like to learn about today?"
                    },
                    ExampleDialogs = new List<string>
                    {
                        "Dr. Science: *adjusts lab goggles* That's a fascinating question!",
                        "Dr. Science: Let me explain this concept step by step...",
                        "Dr. Science: *points to diagram* As you can see here..."
                    }
                };
                
                var user = new BasePersona
                {
                    Name = "Alex",
                    Bio = "A curious student interested in learning about science",
                    IsUser = true
                };
                
                // Set the personas
                LLMEngine.Bot = bot;
                LLMEngine.User = user;
                
                Console.WriteLine($"Bot persona: {bot.Name} - {bot.Bio}");
                Console.WriteLine($"User persona: {user.Name} - {user.Bio}");
                Console.WriteLine();
                
                // Step 3: Setup event handlers
                LLMEngine.OnInferenceStreamed += (sender, token) =>
                {
                    Console.Write(token);
                };
                
                LLMEngine.OnInferenceEnded += (sender, response) =>
                {
                    Console.WriteLine(); // New line after response
                };
                
                LLMEngine.OnStatusChanged += (sender, status) =>
                {
                    if (status == SystemStatus.Busy)
                        Console.Write($"{bot.Name}: ");
                };
                
                // Step 4: Start conversation with welcome message
                Console.WriteLine("Starting conversation...");
                var welcomeMessage = bot.GetWelcomeLine(user.Name);
                Console.WriteLine($"{bot.Name}: {welcomeMessage}");
                
                // Log the welcome message to history
                LLMEngine.History.LogMessage(AuthorRole.Assistant, welcomeMessage, user, bot);
                
                Console.WriteLine();
                
                // Step 5: Simulate a conversation
                var questions = new[]
                {
                    "What causes the sky to be blue?",
                    "How do birds fly?",
                    "What is gravity?"
                };
                
                foreach (var question in questions)
                {
                    Console.WriteLine($"{user.Name}: {question}");
                    
                    // Send message and get response
                    await LLMEngine.SendMessageToBot(AuthorRole.User, question);
                    
                    // Wait for response to complete
                    while (LLMEngine.Status == SystemStatus.Busy)
                    {
                        await Task.Delay(100);
                    }
                    
                    Console.WriteLine();
                }
                
                // Step 6: Demonstrate chat history features
                Console.WriteLine("=== Chat History Analysis ===");
                var history = LLMEngine.History;
                
                Console.WriteLine($"Total messages in current session: {history.CurrentSession.Messages.Count}");
                Console.WriteLine($"Session started: {history.CurrentSession.StartTime}");
                
                var lastMessage = history.LastMessage();
                if (lastMessage != null)
                {
                    Console.WriteLine($"Last message was from: {lastMessage.Role}");
                    Console.WriteLine($"Last message preview: {lastMessage.Message.Substring(0, Math.Min(50, lastMessage.Message.Length))}...");
                }
                
                // Step 7: Demonstrate reroll functionality
                Console.WriteLine("\n=== Demonstrating Reroll ===");
                Console.WriteLine("Rerolling the last bot response...");
                
                await LLMEngine.RerollLastMessage();
                
                while (LLMEngine.Status == SystemStatus.Busy)
                {
                    await Task.Delay(100);
                }
                
                Console.WriteLine();
                
                // Step 8: Save chat history
                var filename = $"science_chat_{DateTime.Now:yyyy-MM-dd_HH-mm}.json";
                LLMEngine.History.SaveToFile(filename);
                Console.WriteLine($"Chat history saved to: {filename}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("\n=== Example Complete ===");
        }
    }
}