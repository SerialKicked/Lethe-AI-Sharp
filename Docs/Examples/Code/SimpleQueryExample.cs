using System;
using System.Threading.Tasks;
using LetheAISharp.LLM;
using LetheAISharp.Files;

namespace LetheAISharp.Examples
{
    /// <summary>
    /// Simple example demonstrating basic LLMEngine usage with simple queries
    /// </summary>
    public class SimpleQueryExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== Simple Query Example ===");
            
            try
            {
                // Step 1: Setup connection to your LLM backend
                Console.WriteLine("Setting up connection to LLM backend...");
                LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
                
                // Step 2: Connect to the backend
                Console.WriteLine("Connecting to backend...");
                await LLMEngine.Connect();
                
                if (LLMEngine.Status != SystemStatus.Ready)
                {
                    Console.WriteLine("Failed to connect to backend. Make sure your LLM server is running.");
                    return;
                }
                
                Console.WriteLine($"Connected successfully!");
                Console.WriteLine($"Model: {LLMEngine.CurrentModel}");
                Console.WriteLine($"Backend: {LLMEngine.Backend}");
                Console.WriteLine($"Max Context: {LLMEngine.MaxContextLength} tokens");
                Console.WriteLine();

                // Step 3: Set an instruction format 
                // We'll use a common format here, ChatML, but in practice this depends on the model being used.
                // Using the wrong format may lead to very poor results.
                var instructionFormat = new InstructFormat()
                {
                    SysPromptStart = "<|im_start|>system\n",
                    SysPromptEnd = "<|im_end|>",
                    SystemStart = "<|im_start|>system\n",
                    SystemEnd = "<|im_end|>",
                    UserStart = "<|im_start|>user\n",
                    UserEnd = "<|im_end|>",
                    BotStart = "<|im_start|>assistant\n",
                    BotEnd = "<|im_end|>",
                    AddNamesToPrompt = false,
                    NewLinesBetweenMessages = true
                };
                LLMEngine.Instruct = instructionFormat;

                // Step 4: Simple non-streaming query
                Console.WriteLine("--- Non-Streaming Query ---");

                // Build the prompt using the PromptBuilder, the backend-agnostic way to create prompts
                var builder = LLMEngine.GetPromptBuilder();
                builder.AddMessage(AuthorRole.SysPrompt, "You are an useful assistant.");
                builder.AddMessage(AuthorRole.User, "What is the capital of France? Please provide a short answer.");
                var query = builder.PromptToQuery(AuthorRole.Assistant);
                
                Console.WriteLine("Query: What is the capital of France? Please provide a short answer.");
                
                var response = await LLMEngine.SimpleQuery(query);
                Console.WriteLine($"Response: {response}");
                Console.WriteLine();
                
                // Step 5: Streaming query with events
                Console.WriteLine("--- Streaming Query ---");
                
                // Build the streaming prompt
                var streamBuilder = LLMEngine.GetPromptBuilder();
                streamBuilder.AddMessage(AuthorRole.SysPrompt, "You are an useful assistant.");
                streamBuilder.AddMessage(AuthorRole.User, "Write a very short story about a friendly robot in exactly two sentences.");
                var streamQuery = streamBuilder.PromptToQuery(AuthorRole.Assistant);
                
                Console.WriteLine("Query: Write a very short story about a friendly robot in exactly two sentences.");
                Console.Write("Response: ");
                
                // Setup event handlers for streaming
                string fullResponse = "";
                LLMEngine.OnInferenceStreamed += (sender, token) =>
                {
                    Console.Write(token);
                    fullResponse += token;
                };
                
                bool responseComplete = false;
                LLMEngine.OnInferenceEnded += (sender, complete) =>
                {
                    Console.WriteLine();
                    Console.WriteLine($"Complete response: {complete}");
                    responseComplete = true;
                };
                
                // Start streaming query
                await LLMEngine.SimpleQueryStreaming(streamQuery);
                
                // Wait for completion (in real apps, you'd handle this very differently)
                while (!responseComplete && LLMEngine.Status == SystemStatus.Busy)
                {
                    await Task.Delay(100);
                }
                
                Console.WriteLine();                              
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("\n=== Example Complete ===");
        }
    }
}