using System;
using System.Threading.Tasks;
using LetheAISharp.LLM;
using LetheAISharp.Files;
using System.Linq;

namespace LetheAISharp.Examples
{
    /// <summary>
    /// Interactive console chat application demonstrating real-world usage
    /// </summary>
    public class InteractiveChatExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== Interactive Chat Example ===");
            Console.WriteLine("This example creates an interactive chat session.");
            Console.WriteLine("Commands: 'quit' to exit, 'reroll' to regenerate last response, 'new' for new session");
            Console.WriteLine();
            
            try
            {
                // Setup
                Console.WriteLine("Connecting to LLM backend...");
                LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
                await LLMEngine.Connect();
                
                if (LLMEngine.Status != SystemStatus.Ready)
                {
                    Console.WriteLine("Failed to connect. Make sure your LLM server is running on localhost:5001");
                    return;
                }
                
                Console.WriteLine($"Connected to {LLMEngine.CurrentModel}");
                
                // Create a chatbot persona
                var bot = new BasePersona
                {
                    Name = "ChatBot",
                    Bio = "A helpful and friendly AI assistant",
                    IsUser = false,
                    Scenario = "You are a helpful AI assistant. Be conversational and engaging.",
                    FirstMessage = new() { "Hi there! I'm your AI assistant. How can I help you today?" }
                };
                
                var user = new BasePersona
                {
                    Name = "User",
                    IsUser = true
                };
                
                LLMEngine.Bot = bot;
                LLMEngine.User = user;
                
                // Setup event handlers for real-time response display
                LLMEngine.OnInferenceStreamed += (sender, token) => Console.Write(token);
                LLMEngine.OnInferenceEnded += (sender, response) => Console.WriteLine();
                LLMEngine.OnStatusChanged += (sender, status) =>
                {
                    if (status == SystemStatus.Busy)
                        Console.Write($"{bot.Name}: ");
                };
                
                // Welcome message
                var welcome = bot.GetWelcomeLine(user.Name);
                Console.WriteLine($"{bot.Name}: {welcome}");
                LLMEngine.History.LogMessage(AuthorRole.Assistant, welcome, user, bot);
                Console.WriteLine();
                
                // Main chat loop
                while (true)
                {
                    Console.Write($"{user.Name}: ");
                    var input = Console.ReadLine()?.Trim();
                    
                    if (string.IsNullOrEmpty(input))
                        continue;
                    
                    // Handle commands
                    switch (input.ToLower())
                    {
                        case "quit":
                        case "exit":
                            Console.WriteLine("Goodbye!");
                            return;
                            
                        case "reroll":
                            if (LLMEngine.History.LastMessage()?.Role == AuthorRole.Assistant)
                            {
                                Console.WriteLine("Rerolling last response...");
                                await LLMEngine.RerollLastMessage();
                                await WaitForResponse();
                            }
                            else
                            {
                                Console.WriteLine("No bot message to reroll.");
                            }
                            continue;
                            
                        case "new":
                            await LLMEngine.History.StartNewChatSession();
                            Console.WriteLine("Started new session.");
                            continue;
                            
                        case "stats":
                            ShowChatStats();
                            continue;
                            
                        case "help":
                            ShowHelp();
                            continue;
                    }
                    
                    // Send message to bot
                    await LLMEngine.SendMessageToBot(AuthorRole.User, input);
                    await WaitForResponse();
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        
        private static async Task WaitForResponse()
        {
            while (LLMEngine.Status == SystemStatus.Busy)
            {
                await Task.Delay(50);
            }
        }
        
        private static void ShowChatStats()
        {
            var history = LLMEngine.History;
            var session = history.CurrentSession;
            
            Console.WriteLine("=== Chat Statistics ===");
            Console.WriteLine($"Messages in current session: {session.Messages.Count}");
            Console.WriteLine($"Session started: {session.StartTime:yyyy-MM-dd HH:mm}");
            Console.WriteLine($"Total sessions: {history.Sessions.Count}");
            
            if (session.Messages.Count > 0)
            {
                var userMessages = session.Messages.Count(m => m.Role == AuthorRole.User);
                var botMessages = session.Messages.Count(m => m.Role == AuthorRole.Assistant);
                Console.WriteLine($"User messages: {userMessages}");
                Console.WriteLine($"Bot messages: {botMessages}");
            }
            
            Console.WriteLine($"Model: {LLMEngine.CurrentModel}");
            Console.WriteLine($"Context window: {LLMEngine.MaxContextLength} tokens");
            Console.WriteLine("=======================");
        }
        
        private static void ShowHelp()
        {
            Console.WriteLine("=== Available Commands ===");
            Console.WriteLine("quit/exit - End the chat session");
            Console.WriteLine("reroll    - Regenerate the last bot response");
            Console.WriteLine("new       - Start a new chat session");
            Console.WriteLine("stats     - Show chat statistics");
            Console.WriteLine("help      - Show this help message");
            Console.WriteLine("==========================");
        }
    }
}