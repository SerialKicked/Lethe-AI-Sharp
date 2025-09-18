# Quick Start Guide

This guide will get you up and running with the AIToolkit LLMEngine in just a few minutes.

## Prerequisites

1. **Backend Server**: You need a running LLM backend server. Popular options:
   - [KoboldCpp](https://github.com/LostRuins/koboldcpp) (recommended)
   - [LM Studio](https://lmstudio.ai/) 
   - [Text Generation WebUI](https://github.com/oobabooga/text-generation-webui)

2. **Model**: Load a model in your backend server

3. **API Access**: Ensure the API is enabled and note the port number

## 5-Minute Setup

### Step 1: Basic Connection

```csharp
using AIToolkit.LLM;

// Connect to your backend (adjust URL/port as needed)
LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
await LLMEngine.Connect();

// Verify connection
if (LLMEngine.Status == SystemStatus.Ready)
{
    Console.WriteLine($"✅ Connected to {LLMEngine.CurrentModel}");
}
```

### Step 2: Simple Text Generation

```csharp
// Non-streaming query
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.User, "What is artificial intelligence?");
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
Console.WriteLine(response);

// Streaming query with real-time output
LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);

var streamBuilder = LLMEngine.GetPromptBuilder();
streamBuilder.AddMessage(AuthorRole.User, "Write a haiku about programming.");
var streamQuery = streamBuilder.PromptToQuery(AuthorRole.Assistant);
await LLMEngine.SimpleQueryStreaming(streamQuery);
```

### Step 3: Conversational Chat

```csharp
// Create a chatbot persona
LLMEngine.Bot = new BasePersona
{
    Name = "Assistant",
    Bio = "A helpful AI assistant",
    IsUser = false
};

// Setup streaming display
LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
LLMEngine.OnStatusChanged += (_, status) => 
{
    if (status == SystemStatus.Busy) Console.Write("Bot: ");
};

// Have a conversation
await LLMEngine.SendMessageToBot(AuthorRole.User, "Hello, how are you?");

// Wait for response to complete
while (LLMEngine.Status == SystemStatus.Busy)
    await Task.Delay(100);
```

## Complete Example

Here's a minimal working chat application:

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

class Program
{
    static async Task Main()
    {
        // Setup
        LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMEngine.Connect();
        
        if (LLMEngine.Status != SystemStatus.Ready)
        {
            Console.WriteLine("Failed to connect to LLM backend");
            return;
        }
        
        // Create persona
        LLMEngine.Bot = new BasePersona
        {
            Name = "ChatBot",
            Bio = "A friendly AI assistant",
            IsUser = false
        };
        
        // Setup events
        LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
        LLMEngine.OnStatusChanged += (_, status) => 
        {
            if (status == SystemStatus.Busy) Console.Write("Bot: ");
        };
        
        // Chat loop
        Console.WriteLine("Chat started! Type 'quit' to exit.");
        
        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();
            
            if (input == "quit") break;
            
            await LLMEngine.SendMessageToBot(AuthorRole.User, input);
            
            // Wait for response
            while (LLMEngine.Status == SystemStatus.Busy)
                await Task.Delay(50);
                
            Console.WriteLine(); // New line after response
        }
    }
}
```

## Common Issues

**Connection Failed**: 
- Verify your backend server is running
- Check the URL and port number
- Ensure API is enabled in your backend

**Empty Responses**: 
- Confirm a model is loaded in your backend
- Check if the model supports your prompt format

**Slow Responses**: 
- Use streaming (`SimpleQueryStreaming` or full communication mode)
- Check your model size vs available resources

## Next Steps

- Explore the [complete documentation](LLMSYSTEM.md)
- Try the [examples](Examples/)
- Customize personas and conversation flow
- Add RAG and web search capabilities

## Backend-Specific Setup

### KoboldCpp (Recommended)
```bash
# Download and run KoboldCpp with your model
./koboldcpp.exe --model your-model.gguf --port 5001 --api
```

### LM Studio
1. Load a model in LM Studio
2. Go to "Local Server" tab
3. Start server (usually port 1234)
4. Use: `LLMEngine.Setup("http://localhost:1234", BackendAPI.OpenAI);`

### Text Generation WebUI
1. Launch with `--api` flag
2. Load a model
3. Note the port (usually 5000)
4. Use: `LLMEngine.Setup("http://localhost:5000", BackendAPI.OpenAI);`