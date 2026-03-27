# Quick Start Guide

This guide will get you up and running with the LetheAISharp LLMEngine in just a few minutes.

## Prerequisites

1. **Backend Server**: You need a running LLM backend server. Popular options:
   - [KoboldCpp](https://github.com/LostRuins/koboldcpp) (heavily recommended)
   - [LM Studio](https://lmstudio.ai/) 
   - [Text Generation WebUI](https://github.com/oobabooga/text-generation-webui)

You can also use the integrated backend, LLamaSharp. In that case replace the URL field by the path toward the GGUF model. But for this demonstration, just use KoboldCpp, it's a reliable backend.

2. **Model**: Any instruction tuned model in the GGUF format will do. You can use [Qwen 3.0 14B](https://huggingface.co/Qwen/Qwen3-14B-GGUF/resolve/main/Qwen3-14B-Q4_K_M.gguf?download=true) for instance. Load the model in your backend server (KoboldCpp). 

<img width="582" height="612" alt="koboldcpp_J4Z8q4DYDy" src="https://github.com/user-attachments/assets/0523f1f9-0d91-4023-b067-c57992088b46" /> 

If you RAM allows, put all layers on your GPU (GPU Layers = 255 will put everything on it). Enable Flash Attention for faster responses. And set Context Size to something like 16K (it really depends on available VRAM. You may need 8K if things don't load, or get too slow). Do NOT use context shifts (it tends to conflict with Lethe AI). For the other configs, check KoboldCpp docs, but defaults should work just fine.

3. **API Access**: Ensure the API is enabled and note the port number

<img width="582" height="612" alt="koboldcpp_KStltyvhW7" src="https://github.com/user-attachments/assets/c43b9868-7d25-477c-9391-af3a88c0238e" />

Enable Multiuser and Websearch to "unlock" some of Lethe AI's advanced functions.

## 5-Minute Setup

### Step 1: Basic Connection

```csharp
using LetheAISharp.LLM;

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
builder.AddMessage(AuthorRole.System, "You are a helpful assistant.");
builder.AddMessage(AuthorRole.User, "What is artificial intelligence?");
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
Console.WriteLine(response);

// Streaming query with real-time output
LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);

var streamBuilder = LLMEngine.GetPromptBuilder();
streamBuilder.AddMessage(AuthorRole.System, "You are a helpful assistant.");
streamBuilder.AddMessage(AuthorRole.User, "Write a haiku about programming.");
var streamQuery = streamBuilder.PromptToQuery(AuthorRole.Assistant);
await LLMEngine.SimpleQueryStreaming(streamQuery);
```

## Full Chat Mode

Here's a minimal working chat application:

```csharp
using LetheAISharp.LLM;
using LetheAISharp.Files;

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

        // Technically you should also set LLMEngine.Instruct to the correct instruction template for your model.
        // The default is Alpaca wwhich will probably work anyway, but will give less good results. We're just skipping
        // this part for demonstration purposes.
       
        // Setup events
        LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
        LLMEngine.OnStatusChanged += (_, status) => 
        {
            if (status == SystemStatus.Busy) Console.Write("Bot: ");
        };

        LLMEngine.OnInferenceEnded += (_, response) => 
        {
            Console.WriteLine("\n");
            LLMEngine.History.LogMessage(AuthorRole.Assistant, response, user, bot);
        }
        
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
        }
        // End chat session and save log
        LLMEngine.Bot.EndChat();

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
- Try the [examples](Examples/Code)
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
