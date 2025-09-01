# LLMSystem User Documentation

The `LLMSystem` static class is the main entry point and central hub for all LLM (Large Language Model) operations in AIToolkit. This comprehensive guide covers everything you need to know to effectively use the LLMSystem.

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Core Concepts](#core-concepts)
4. [Basic Operations](#basic-operations)
5. [Advanced Features](#advanced-features)
6. [Configuration](#configuration)
7. [Event Handling](#event-handling)
8. [Error Handling](#error-handling)
9. [Examples](#examples)
10. [Troubleshooting](#troubleshooting)

## Overview

The `LLMSystem` class manages all communications with language models, handling:
- Connection to LLM servers (KoboldAPI, OpenAI API)
- Prompt generation and management
- Chat history and session management
- Persona management (Bot and User)
- Advanced features like RAG, TTS, and web search
- Inference settings and instruction formats

## Getting Started

### Basic Setup

```csharp
using AIToolkit.LLM;

// Setup the connection to your LLM backend
LLMSystem.Setup("http://localhost:5001", BackendAPI.KoboldAPI);

// Connect to the server
await LLMSystem.Connect();

// Check if connection is successful
if (LLMSystem.Status == SystemStatus.Ready)
{
    Console.WriteLine($"Connected to {LLMSystem.Backend} running {LLMSystem.CurrentModel}");
}
```

### Supported Backends

**KoboldAPI** (Recommended - Full Features)
- Used by [KoboldCpp](https://github.com/LostRuins/koboldcpp)
- Supports all features including TTS, advanced sampling, etc.
- Uses text completion mode

**OpenAI API** (Limited Features)
- Used by LM Studio, Text Generation WebUI, and others
- Basic text generation support
- Uses chat completion mode

```csharp
// Check which completion type is being used
CompletionType completionType = LLMSystem.CompletionAPIType;
// Returns: CompletionType.Text or CompletionType.Chat
```

## Core Concepts

### System Status

The system operates in three states:
- `SystemStatus.NotInit` - Not initialized
- `SystemStatus.Ready` - Ready to accept requests
- `SystemStatus.Busy` - Currently processing a request

```csharp
// Check current status
if (LLMSystem.Status == SystemStatus.Ready)
{
    // Safe to send messages
    await LLMSystem.SendMessageToBot(AuthorRole.User, "Hello!");
}
```

### Personas

Personas represent the participants in the conversation:

```csharp
// Access current bot and user personas
BasePersona bot = LLMSystem.Bot;
BasePersona user = LLMSystem.User;

// Modify persona properties
LLMSystem.Bot.Name = "Assistant";
LLMSystem.Bot.Bio = "A helpful AI assistant";
LLMSystem.User.Name = "John";

// Load multiple personas at once
var personas = new List<BasePersona>
{
    new BasePersona { Name = "Doctor", Bio = "A medical professional", UniqueName = "doc01" },
    new BasePersona { Name = "Assistant", Bio = "A helpful AI", UniqueName = "ai01" }
};
LLMSystem.LoadPersona(personas);
```

### Chat History

The chat history is automatically managed:

```csharp
// Access chat history
Chatlog history = LLMSystem.History;

// Get message count
int messageCount = history.CurrentSession.Messages.Count;

// Access last message
var lastMessage = history.LastMessage();
```

## Basic Operations

### Sending Messages

The system supports different author roles for messages:

- `AuthorRole.User` - Messages from the user
- `AuthorRole.Assistant` - Messages from the bot/assistant  
- `AuthorRole.System` - System messages (instructions, context)
- `AuthorRole.SysPrompt` - System prompt content

```csharp
// Send a user message and get bot response
await LLMSystem.SendMessageToBot(AuthorRole.User, "What's the weather like?");

// Send using a SingleMessage object
var message = new SingleMessage(AuthorRole.User, DateTime.Now, "Hello!", 
    LLMSystem.User.UniqueName, LLMSystem.Bot.UniqueName);
await LLMSystem.SendMessageToBot(message);

// Add a bot message (impersonate the bot)
await LLMSystem.AddBotMessage();

// Impersonate user (make bot respond as user)
await LLMSystem.ImpersonateUser();
```

### Rerolling Responses

```csharp
// Regenerate the last bot response
await LLMSystem.RerollLastMessage();
```

### Quick System Queries

```csharp
// Send a system message without logging to history
string response = await LLMSystem.QuickInferenceForSystemPrompt(
    "Summarize this conversation in one sentence.", 
    logSystemPrompt: false
);
```

### Token Management

Token usage is automatically handled by the library based on the `MaxContextLength` value retrieved during the `Connect()` function. Manual token management is primarily useful for custom queries or when you need precise control:

```csharp
// Count tokens in text (mainly for custom queries)
int tokenCount = LLMSystem.GetTokenCount("Your text here");

// Check context limits (automatically managed for regular operations)
int maxTokens = LLMSystem.MaxContextLength;
Console.WriteLine($"Max context: {maxTokens} tokens");

// Custom validation before sending non-standard queries
if (tokenCount > maxTokens - LLMSystem.Settings.MaxReplyLength)
{
    Console.WriteLine("Custom query too long for context window");
}
```

### Cancelling Generation

```csharp
// Cancel ongoing generation
bool cancelled = LLMSystem.CancelGeneration();
```

### Model Slot Management

The LLMSystem uses a semaphore to ensure only one operation at a time:

```csharp
// Acquire model slot manually (for advanced scenarios)
using var slot = await LLMSystem.AcquireModelSlotAsync(CancellationToken.None);

// Try to acquire with timeout
using var slot = await LLMSystem.TryAcquireModelSlotAsync(
    TimeSpan.FromSeconds(30), CancellationToken.None);

if (slot != null)
{
    // Perform operations while holding the slot
    // Slot is automatically released when disposed
}
```

## Advanced Features

### RAG (Retrieval Augmented Generation)

```csharp
// Enable RAG system
RAGSystem.Enabled = true;

// RAG will automatically search and inject relevant context
// from previous conversations and world information
```

### Text-to-Speech

```csharp
if (LLMSystem.SupportsTTS)
{
    byte[] audioData = await LLMSystem.GenerateTTS("Hello world", "female_voice");
    // Save or play the audio data
}
```

### Web Search

```csharp
if (LLMSystem.SupportsWebSearch)
{
    var results = await LLMSystem.WebSearch("latest AI news");
    foreach (var result in results)
    {
        Console.WriteLine($"{result.Title}: {result.Snippet}");
    }
}
```

### Vision Language Models (VLM)

```csharp
if (LLMSystem.SupportsVision)
{
    // Clear previous images
    LLMSystem.VLM_ClearImages();
    
    // Add image from file
    using var image = Image.FromFile("path/to/image.jpg");
    LLMSystem.VLM_AddImage(image);
    
    // Or add base64 image
    LLMSystem.VLM_AddB64Image(base64ImageString);
    
    // Send message with image context
    await LLMSystem.SendMessageToBot(AuthorRole.User, "What do you see in this image?");
}
```

### Macro Replacement

```csharp
// Replace template macros with actual values
string template = "Hello {{user}}, today is {{date}}";
string processed = LLMSystem.ReplaceMacros(template);
// Result: "Hello John, today is January 15, 2024"
```

Available macros:
- `{{user}}` - User name
- `{{char}}` - Bot name  
- `{{date}}` - Current date (human readable format)
- `{{time}}` - Current time (short format)
- `{{day}}` - Day of week (e.g., "Monday")
- `{{userbio}}` - User biography/description
- `{{charbio}}` - Bot biography/description
- `{{examples}}` - Dialog examples for the bot
- `{{scenario}}` - Current scenario or scenario override
- `{{selfedit}}` - Bot's self-edit field (custom character data)

## Configuration

### LLM Settings

```csharp
// Access settings object
LLMSettings settings = LLMSystem.Settings;

// Backend configuration
settings.BackendUrl = "http://localhost:5001";
settings.BackendAPI = BackendAPI.KoboldAPI;
settings.OpenAIKey = "your-api-key-here";

// Response configuration
settings.MaxReplyLength = 512;
settings.StopGenerationOnFirstParagraph = false;

// RAG system configuration
settings.RAGEnabled = true;
settings.RAGMaxEntries = 5;
settings.RAGDistanceCutOff = 0.7f;
settings.RAGIndex = 200;               // Position for RAG insertions
settings.RAGMoveToSysPrompt = false;   // Move RAG to system prompt
settings.RAGMoveToThinkBlock = false;  // Move RAG to thinking block

// Session and memory management
settings.SessionHandling = SessionHandling.FitAll;  // How to handle multiple sessions
settings.ReservedSessionTokens = 2048;              // Tokens reserved for session summaries
settings.AllowWorldInfo = true;                     // Enable world info/keyword insertions

// Advanced settings
settings.ScenarioOverride = "";        // Override character scenario
settings.DisableThinking = false;     // Disable thinking for thinking models
```

### Instruction Format

```csharp
// Configure how prompts are formatted for the model
LLMSystem.Instruct.AddNamesToPrompt = true;
LLMSystem.Instruct.SystemPromptPrefix = "[SYSTEM]";
LLMSystem.Instruct.UserPrefix = "[USER]";
LLMSystem.Instruct.AssistantPrefix = "[ASSISTANT]";
```

### Sampling Settings

```csharp
// Configure inference parameters
SamplerSettings sampler = LLMSystem.Sampler;
sampler.Temperature = 0.7;        // Randomness (0.0 = deterministic, 1.0+ = creative)
sampler.Top_p = 0.9;             // Nucleus sampling threshold
sampler.Top_k = 40;              // Top-k sampling limit (0 = disabled)
sampler.Rep_pen = 1.1;           // Repetition penalty
sampler.Min_p = 0.0;             // Minimum probability threshold
sampler.Top_a = 0.0;             // Top-a sampling (0 = disabled)
sampler.Tfs = 1.0;               // Tail free sampling
sampler.Typical = 1.0;           // Typical sampling
sampler.Mirostat = 0;            // Mirostat mode (0 = disabled, 1 = v1, 2 = v2)
sampler.Mirostat_tau = 5.0;      // Mirostat target entropy
sampler.Mirostat_eta = 0.1;      // Mirostat learning rate

// Advanced settings
sampler.Xtc_threshold = 0.1;     // XTC sampling threshold
sampler.Xtc_probability = 0.33;  // XTC probability
sampler.Dry_multiplier = 0.8;    // DRY repetition penalty multiplier
sampler.Dry_base = 1.75;         // DRY base penalty
```

### System Prompt

```csharp
// Configure system prompt
SystemPrompt sysPrompt = LLMSystem.SystemPrompt;
sysPrompt.Content = "You are a helpful assistant.";
sysPrompt.WorldInfoTitle = "Background Information:";
```

### Force Temperature Override

```csharp
// Override temperature setting
LLMSystem.ForceTemperature = 0.8; // Set to -1 to disable override
```

### Logging

```csharp
// Set up logging (requires ILogger implementation)
LLMSystem.Logger = yourLoggerInstance;
```

## Event Handling

### Streaming Events

```csharp
// Handle streaming text as it's generated
LLMSystem.OnInferenceStreamed += (sender, token) =>
{
    Console.Write(token); // Display each token as it arrives
};

// Handle completion of inference
LLMSystem.OnInferenceEnded += (sender, fullResponse) =>
{
    Console.WriteLine($"\\nComplete response: {fullResponse}");
    
    // Log the response to chat history
    LLMSystem.History.LogMessage(AuthorRole.Assistant, fullResponse, 
        LLMSystem.User, LLMSystem.Bot);
};
```

### System Events

```csharp
// Handle status changes
LLMSystem.OnStatusChanged += (sender, status) =>
{
    Console.WriteLine($"Status changed to: {status}");
};

// Handle bot persona changes
LLMSystem.OnBotChanged += (sender, newBot) =>
{
    Console.WriteLine($"Bot changed to: {newBot.Name}");
};

// Handle prompt generation
LLMSystem.OnFullPromptReady += (sender, fullPrompt) =>
{
    Console.WriteLine($"Generated prompt: {fullPrompt}");
};

// Handle quick inference completion
LLMSystem.OnQuickInferenceEnded += (sender, response) =>
{
    Console.WriteLine($"Quick inference result: {response}");
};
```

### Removing Event Handlers

```csharp
// Remove all quick inference event handlers
LLMSystem.RemoveQuickInferenceEventHandler();
```

## Error Handling

### Connection Errors

```csharp
try
{
    await LLMSystem.Connect();
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to connect: {ex.Message}");
    // Check LLMSystem.Backend and LLMSystem.CurrentModel for error details
}
```

### Generation Errors

```csharp
// Check backend availability
bool isAvailable = await LLMSystem.CheckBackend();
if (!isAvailable)
{
    Console.WriteLine("Backend is not available");
    return;
}

// Ensure system is ready before sending messages
if (LLMSystem.Status != SystemStatus.Ready)
{
    Console.WriteLine("System is not ready");
    return;
}
```

### Token Limit Handling

Token limits are automatically managed for regular chat operations. Manual checking is useful for custom queries:

```csharp
// Monitor token usage for custom queries (automatic for regular chat)
int tokenCount = LLMSystem.GetTokenCount(yourCustomText);
if (tokenCount > LLMSystem.MaxContextLength - LLMSystem.Settings.MaxReplyLength)
{
    Console.WriteLine("Custom text too long for context window");
    // Consider shortening the text or using summarization
}
```

## Examples

### Basic Chat Loop

```csharp
using AIToolkit.LLM;

class Program
{
    static async Task Main(string[] args)
    {
        // Setup
        LLMSystem.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMSystem.Connect();
        
        if (LLMSystem.Status != SystemStatus.Ready)
        {
            Console.WriteLine("Failed to connect to LLM");
            return;
        }
        
        // Setup event handlers
        LLMSystem.OnInferenceStreamed += (sender, token) => Console.Write(token);
        LLMSystem.OnInferenceEnded += (sender, response) =>
        {
            Console.WriteLine(); // New line after response
            LLMSystem.History.LogMessage(AuthorRole.Assistant, response, 
                LLMSystem.User, LLMSystem.Bot);
        };
        
        // Chat loop
        Console.WriteLine("Chat started. Type 'quit' to exit.");
        while (true)
        {
            Console.Write("You: ");
            string input = Console.ReadLine();
            
            if (input?.ToLower() == "quit") break;
            
            if (!string.IsNullOrEmpty(input))
            {
                await LLMSystem.SendMessageToBot(AuthorRole.User, input);
                
                // Wait for generation to complete
                while (LLMSystem.Status == SystemStatus.Busy)
                {
                    await Task.Delay(100);
                }
            }
        }
    }
}
```

### Using Custom Personas

```csharp
// Create custom bot persona
var customBot = new BasePersona
{
    Name = "Dr. Smith",
    Bio = "A knowledgeable medical doctor who provides helpful health advice.",
    IsUser = false,
    UniqueName = "doctor_smith"
};

// Set custom greeting
customBot.WelcomeMessage = "Hello! I'm Dr. Smith. How can I help you today?";

// Apply the persona
LLMSystem.Bot = customBot;

// The bot will now use this persona for all interactions
await LLMSystem.SendMessageToBot(AuthorRole.User, "I have a headache");
```

### Implementing RAG with Custom Content

```csharp
// Enable RAG
RAGSystem.Enabled = true;

// The RAG system will automatically:
// 1. Embed previous conversations
// 2. Search for relevant context when user asks questions
// 3. Inject relevant information into prompts

// For example, if user previously discussed "Python programming"
// and later asks "How do I use loops?", RAG will find and inject
// the relevant Python conversation context
```

### Batch Processing

```csharp
var questions = new[]
{
    "What is machine learning?",
    "Explain neural networks",
    "What is deep learning?"
};

var responses = new List<string>();

foreach (var question in questions)
{
    string response = await LLMSystem.QuickInferenceForSystemPrompt(
        $"Answer this question briefly: {question}", 
        logSystemPrompt: false
    );
    
    responses.Add(response);
    Console.WriteLine($"Q: {question}");
    Console.WriteLine($"A: {response}\\n");
}
```

## Troubleshooting

### Common Issues

**Connection Failed**
- Verify the backend URL is correct
- Ensure the LLM server is running
- Check firewall settings
- Verify the backend type matches your server

**Generation Hangs**
- Check if system status is stuck in Busy
- Try cancelling generation: `LLMSystem.CancelGeneration()`
- Restart the connection if needed

**Out of Memory/Context**
- Reduce `MaxReplyLength` in settings
- Enable session summarization
- Clear chat history: `LLMSystem.History.CurrentSession.Messages.Clear()`

**Invalid Responses**
- Check instruction format settings
- Verify model is properly loaded on backend
- Adjust sampling parameters (temperature, top_p, etc.)

### Debug Information

```csharp
// Get system information
Console.WriteLine($"Status: {LLMSystem.Status}");
Console.WriteLine($"Backend: {LLMSystem.Backend}");
Console.WriteLine($"Model: {LLMSystem.CurrentModel}");
Console.WriteLine($"Max Context: {LLMSystem.MaxContextLength}");
Console.WriteLine($"Message Count: {LLMSystem.History.CurrentSession.Messages.Count}");

// Check feature support
Console.WriteLine($"TTS Support: {LLMSystem.SupportsTTS}");
Console.WriteLine($"Vision Support: {LLMSystem.SupportsVision}");
Console.WriteLine($"Web Search Support: {LLMSystem.SupportsWebSearch}");
```

### Performance Tips

1. **Use appropriate context lengths** - Don't set MaxContextLength higher than needed
2. **Monitor token usage for custom queries** - Use `GetTokenCount()` for custom text (automatic for regular chat)
3. **Use RAG wisely** - Enable only when you need historical context
4. **Batch similar operations** - Use `QuickInferenceForSystemPrompt` for non-chat queries
5. **Handle events efficiently** - Avoid heavy processing in event handlers

---

## Summary

The `LLMSystem` static class provides a comprehensive interface for working with Large Language Models in C#. Key points to remember:

1. **Always check `LLMSystem.Status`** before sending messages
2. **Use event handlers** for streaming responses and status updates
3. **Configure settings** appropriate for your use case (context length, sampling, etc.)
4. **Enable RAG** for better context awareness in longer conversations
5. **Handle errors gracefully** with try-catch blocks and status checks
6. **Monitor token usage for custom queries** (automatic for regular chat operations)
7. **Use appropriate backends** based on your feature requirements

For additional features and advanced use cases, refer to the source code and other documentation files in this repository, particularly:
- `EXTENSIBILITY.md` - For extending the system with custom classes
- `AGENT_SYSTEM.md` - For background agent functionality  
- `IMPLEMENTATION_SUMMARY.md` - For technical implementation details