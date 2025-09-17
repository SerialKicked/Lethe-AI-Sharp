using System;
using System.Threading.Tasks;
using AIToolkit.LLM;
using AIToolkit.Files;

namespace AIToolkit.Examples
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
                
                // Step 3: Simple non-streaming query
                Console.WriteLine("--- Non-Streaming Query ---");
                var prompt = "What is the capital of France? Please provide a short answer.";
                Console.WriteLine($"Query: {prompt}");
                
                var response = await LLMEngine.SimpleQuery(prompt);
                Console.WriteLine($"Response: {response}");
                Console.WriteLine();
                
                // Step 4: Streaming query with events
                Console.WriteLine("--- Streaming Query ---");
                var streamingPrompt = "Write a very short story about a friendly robot in exactly two sentences.";
                Console.WriteLine($"Query: {streamingPrompt}");
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
                await LLMEngine.SimpleQueryStreaming(streamingPrompt);
                
                // Wait for completion (in real apps, you'd handle this differently)
                while (!responseComplete && LLMEngine.Status == SystemStatus.Busy)
                {
                    await Task.Delay(100);
                }
                
                Console.WriteLine();
                
                // Step 5: Using PromptBuilder for more complex prompts
                Console.WriteLine("--- Advanced Prompt with PromptBuilder ---");
                var builder = LLMEngine.GetPromptBuilder();
                
                builder.AddMessage(AuthorRole.System, "You are a helpful science teacher.");
                builder.AddMessage(AuthorRole.User, "Explain photosynthesis in simple terms.");
                
                var query = builder.PromptToQuery(AuthorRole.Assistant);
                var scienceResponse = await LLMEngine.SimpleQuery(query);
                
                Console.WriteLine("Query: System prompt + user question about photosynthesis");
                Console.WriteLine($"Response: {scienceResponse}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            
            Console.WriteLine("\n=== Example Complete ===");
        }
    }
}