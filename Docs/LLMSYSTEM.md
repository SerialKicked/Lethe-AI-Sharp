# LLMSystem Documentation

The `LLMSystem` static class is the main entry point for using the AIToolkit library. It provides a comprehensive interface for communicating with Large Language Models (LLMs), managing chat sessions, personas, and advanced features like RAG (Retrieval Augmented Generation), web search, and text-to-speech.

## Overview

LLMSystem handles:
- **Connection Management**: Connects to various LLM backends (KoboldAPI, OpenAI)
- **Chat Management**: Manages chat history, sessions, and message flow
- **Persona System**: Handles bot and user personas with customizable behaviors
- **Prompt Generation**: Automatically formats prompts for different instruction formats
- **Advanced Features**: RAG system, web search, TTS, vision models (VLM)
- **Event System**: Provides real-time streaming and completion events

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   LLMSystem     │    │  ILLMService... │    │   BasePersona   │
│                 │◄───┤       ...Client │    │                 │
│  - Setup()      │    │  - KoboldAPI    │    │  - Bot          │
│  - Connect()    │    │  - OpenAI       │    │  - User         │
│  - SendMessage()│    └─────────────────┘    └─────────────────┘
└─────────────────┘                                   │
         │                                            │
         ▼                                            ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Events        │    │   Advanced      │    │   Configuration │
│                 │    │   Features      │    │                 │
│  - OnInference  │    │  - RAG System   │    │  - Settings     │
│  - OnStreamed   │    │  - Web Search   │    │  - Sampler      │
│  - OnComplete   │    │  - TTS          │    │  - Instruct     │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

## Quick Start

### 1. Basic Setup

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

// Configure the backend
LLMSystem.Setup("http://localhost:5001", BackendAPI.KoboldAPI);

// Or for OpenAI-compatible APIs
LLMSystem.Setup("http://localhost:1234/v1", BackendAPI.OpenAI, "your-api-key");

// Connect to the backend
await LLMSystem.Connect();

// Check if connection is successful
if (LLMSystem.Status == SystemStatus.Ready)
{
    Console.WriteLine($"Connected to {LLMSystem.Backend} running {LLMSystem.CurrentModel}");
}
```

### 2. Basic Chat Interaction

```csharp
// Set up event handlers for streaming responses
LLMSystem.OnInferenceStreamed += (sender, token) => 
{
    Console.Write(token); // Real-time token streaming
};

LLMSystem.OnInferenceEnded += (sender, fullResponse) => 
{
    Console.WriteLine($"\nBot response: {fullResponse}");
    // Optionally log to chat history
    LLMSystem.History.LogMessage(AuthorRole.Assistant, fullResponse, LLMSystem.User, LLMSystem.Bot);
};

// Send a message to the bot
await LLMSystem.SendMessageToBot(AuthorRole.User, "Hello! How are you today?");
```

### 3. Persona Configuration

```csharp
// Configure the bot persona
var myBot = new BasePersona()
{
    Name = "Assistant",
    Bio = "You are a helpful AI assistant specialized in programming.",
    UniqueName = "coding_assistant",
    IsUser = false
};

// Configure the user persona
var myUser = new BasePersona()
{
    Name = "Developer",
    UniqueName = "user",
    IsUser = true
};

// Apply personas
LLMSystem.Bot = myBot;
LLMSystem.User = myUser;
```

## Core Properties and Methods

### System Properties

#### Status and Connection
```csharp
// System status (NotInit, Ready, Busy)
SystemStatus status = LLMSystem.Status;

// Current model information
string model = LLMSystem.CurrentModel;
string backend = LLMSystem.Backend;
int contextLength = LLMSystem.MaxContextLength;

// Backend capabilities
bool supportsTTS = LLMSystem.SupportsTTS;
bool supportsWebSearch = LLMSystem.SupportsWebSearch;
bool supportsVision = LLMSystem.SupportsVision;
```

#### Configuration Objects
```csharp
// Core configuration
LLMSettings settings = LLMSystem.Settings;
SamplerSettings sampler = LLMSystem.Sampler;
InstructFormat instruct = LLMSystem.Instruct;
SystemPrompt systemPrompt = LLMSystem.SystemPrompt;

// Personas
BasePersona bot = LLMSystem.Bot;
BasePersona user = LLMSystem.User;

// Chat history (shortcut to Bot.History)
Chatlog history = LLMSystem.History;
```

### Core Methods

#### Initialization
```csharp
// Basic initialization
LLMSystem.Init();

// Complete setup with backend configuration
LLMSystem.Setup(string url, BackendAPI backend, string? apiKey = null);

// Connect and retrieve backend information
await LLMSystem.Connect();

// Check if backend is accessible
bool isWorking = await LLMSystem.CheckBackend();
```

#### Communication Methods
```csharp
// Send user message
await LLMSystem.SendMessageToBot(AuthorRole.User, "Your message here");

// Send a complete message object
var message = new SingleMessage(AuthorRole.User, DateTime.Now, "Hello", "user", "bot");
await LLMSystem.SendMessageToBot(message);

// Generate bot response based on chat history
await LLMSystem.AddBotMessage();

// Make bot impersonate user
await LLMSystem.ImpersonateUser();

// Regenerate last bot response
await LLMSystem.RerollLastMessage();

// Cancel ongoing generation
bool cancelled = LLMSystem.CancelGeneration();
```

#### Utility Methods
```csharp
// Count tokens in text
int tokenCount = LLMSystem.GetTokenCount("Your text here");

// Replace macros in text ({{user}}, {{char}}, {{date}}, etc.)
string processed = LLMSystem.ReplaceMacros("Hello {{user}}, I'm {{char}}!");

// Force prompt rebuild (call after changing settings)
LLMSystem.InvalidatePromptCache();

// Get time-based away message
string awayMsg = LLMSystem.GetAwayString();
```

#### Quick Inference
```csharp
// Non-streaming inference for system tasks
string response = await LLMSystem.SimpleQuery(promptObject);

// Quick system message with optional logging
string result = await LLMSystem.QuickInferenceForSystemPrompt(
    "Analyze this text for sentiment", 
    logToHistory: false
);
```

## Event System

The LLMSystem provides several events for handling LLM responses:

### Streaming Events
```csharp
// Called for each token during streaming
LLMSystem.OnInferenceStreamed += (sender, token) => 
{
    // Handle real-time token streaming
    Console.Write(token);
};

// Called when streaming completes
LLMSystem.OnInferenceEnded += (sender, fullResponse) => 
{
    // Handle complete response
    Console.WriteLine($"\nComplete response: {fullResponse}");
    
    // Log to chat history if desired
    LLMSystem.History.LogMessage(AuthorRole.Assistant, fullResponse, 
        LLMSystem.User, LLMSystem.Bot);
};
```

### Other Events
```csharp
// Status changes (NotInit → Ready → Busy → Ready)
LLMSystem.OnStatusChanged += (sender, newStatus) => 
{
    Console.WriteLine($"Status changed to: {newStatus}");
};

// Bot persona changes
LLMSystem.OnBotChanged += (sender, newBot) => 
{
    Console.WriteLine($"Bot changed to: {newBot.Name}");
};

// Quick inference completion
LLMSystem.OnQuickInferenceEnded += (sender, response) => 
{
    // Handle non-streaming inference completion
};

// Full prompt generation (useful for debugging)
LLMSystem.OnFullPromptReady += (sender, fullPrompt) => 
{
    Console.WriteLine($"Generated prompt: {fullPrompt}");
};
```

## Advanced Features

### RAG System Integration

The LLMSystem automatically integrates with the RAG (Retrieval Augmented Generation) system:

```csharp
// RAG is configured through settings
LLMSystem.Settings.RAGEnabled = true;
LLMSystem.Settings.RAGMaxEntries = 5;
LLMSystem.Settings.RAGIndex = 3; // Position in prompt

// RAG automatically finds relevant past conversations
// and injects them into the prompt context
```

### Web Search
```csharp
// Check if backend supports web search
if (LLMSystem.SupportsWebSearch)
{
    var searchResults = await LLMSystem.WebSearch("latest AI developments");
    foreach (var result in searchResults)
    {
        Console.WriteLine($"{result.Title}: {result.Description}");
    }
}
```

### Text-to-Speech
```csharp
// Generate speech from text (KoboldAPI only)
if (LLMSystem.SupportsTTS)
{
    byte[] audioData = await LLMSystem.GenerateTTS("Hello world!", "Tina");
    
    // Save or play the audio
    using var stream = new MemoryStream(audioData);
    // Use SoundPlayer or similar to play the audio
}
```

### Vision Language Models (VLM)
```csharp
// Add images for vision models
if (LLMSystem.SupportsVision)
{
    // Add image from file
    var image = Image.FromFile("path/to/image.jpg");
    LLMSystem.VLM_AddImage(image);
    
    // Or add base64 encoded image
    LLMSystem.VLM_AddB64Image("data:image/jpeg;base64,...");
    
    // Send message with images
    await LLMSystem.SendMessageToBot(AuthorRole.User, "What do you see in this image?");
    
    // Clear images after use
    LLMSystem.VLM_ClearImages();
}
```

## Configuration Examples

### Sampler Settings
```csharp
// Configure generation parameters
LLMSystem.Sampler.Temperature = 0.7;
LLMSystem.Sampler.TopP = 0.9;
LLMSystem.Sampler.TopK = 40;
LLMSystem.Sampler.RepetitionPenalty = 1.1;
LLMSystem.Sampler.MaxLength = 200;
```

### Instruction Format
```csharp
// Configure instruction format for different models
LLMSystem.Instruct.InstructionTemplate = "### Instruction:\n{0}\n\n### Response:\n";
LLMSystem.Instruct.AddNamesToPrompt = true;
LLMSystem.Instruct.SystemPromptInInstruction = true;

// For thinking models
LLMSystem.Instruct.ThinkingStart = "<thinking>";
LLMSystem.Instruct.ThinkingEnd = "</thinking>";
LLMSystem.Instruct.PrefillThinking = true;
```

### LLM Settings
```csharp
// Core settings
LLMSystem.Settings.MaxReplyLength = 400;
LLMSystem.Settings.BackendUrl = "http://localhost:5001";
LLMSystem.Settings.BackendAPI = BackendAPI.KoboldAPI;

// RAG settings
LLMSystem.Settings.RAGEnabled = true;
LLMSystem.Settings.RAGMaxEntries = 3;
LLMSystem.Settings.RAGMoveToSysPrompt = false;

// Session handling
LLMSystem.Settings.SessionHandling = SessionHandling.FitAll;
LLMSystem.Settings.ReservedSessionTokens = 1000;

// World info and plugins
LLMSystem.Settings.AllowWorldInfo = true;
LLMSystem.Settings.StopGenerationOnFirstParagraph = false;
```

## Common Usage Patterns

### 1. Basic Chat Application
```csharp
class SimpleChatApp
{
    public async Task RunAsync()
    {
        // Setup
        LLMSystem.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMSystem.Connect();
        
        // Event handlers
        LLMSystem.OnInferenceStreamed += (s, token) => Console.Write(token);
        LLMSystem.OnInferenceEnded += (s, response) => 
        {
            Console.WriteLine();
            LLMSystem.History.LogMessage(AuthorRole.Assistant, response, 
                LLMSystem.User, LLMSystem.Bot);
        };
        
        // Chat loop
        while (true)
        {
            Console.Write("You: ");
            string input = Console.ReadLine();
            if (string.IsNullOrEmpty(input)) break;
            
            await LLMSystem.SendMessageToBot(AuthorRole.User, input);
            
            // Wait for response to complete
            while (LLMSystem.Status == SystemStatus.Busy)
                await Task.Delay(100);
        }
    }
}
```

### 2. Non-Streaming Query
```csharp
public async Task<string> AskQuestionAsync(string question)
{
    // For simple queries without chat history
    var promptBuilder = LLMSystem.Client.GetPromptBuilder();
    promptBuilder.AddMessage(AuthorRole.SysPrompt, "You are a helpful assistant.");
    promptBuilder.AddMessage(AuthorRole.User, question);
    
    var query = promptBuilder.PromptToQuery(AuthorRole.Assistant);
    return await LLMSystem.SimpleQuery(query);
}
```

### 3. Advanced Persona Setup
```csharp
public void SetupCustomBot()
{
    var bot = new BasePersona()
    {
        Name = "CodeMentor",
        Bio = "You are an expert programmer who helps with coding questions. " +
              "Always provide clear explanations and working code examples.",
        UniqueName = "code_mentor",
        IsUser = false,
        
        // Custom greeting
        FirstMessage = "Hello! I'm CodeMentor, ready to help with your programming questions.",
        
        // Enable advanced features
        SenseOfTime = true,
        SessionMemorySystem = true,
        DatesInSessionSummaries = true
    };
    
    // Add world info for programming context
    var worldInfo = new WorldInfo();
    worldInfo.AddEntry(new WorldEntry
    {
        Keywords = ["code", "programming", "function"],
        Content = "When discussing code, always format it properly with syntax highlighting.",
        Position = WEPosition.AfterScenario
    });
    
    bot.MyWorlds.Add(worldInfo);
    LLMSystem.Bot = bot;
}
```

### 4. Error Handling
```csharp
public async Task SafeSendMessageAsync(string message)
{
    try
    {
        // Check system status
        if (LLMSystem.Status != SystemStatus.Ready)
        {
            Console.WriteLine("System not ready. Current status: " + LLMSystem.Status);
            return;
        }
        
        // Check backend connection
        if (!await LLMSystem.CheckBackend())
        {
            Console.WriteLine("Backend connection failed");
            await LLMSystem.Connect(); // Try to reconnect
            return;
        }
        
        await LLMSystem.SendMessageToBot(AuthorRole.User, message);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending message: {ex.Message}");
        LLMSystem.Logger?.LogError(ex, "Failed to send message");
    }
}
```

## Best Practices

### 1. Always Handle Events
Set up event handlers before sending messages to ensure you capture all responses:

```csharp
// Set up handlers first
LLMSystem.OnInferenceEnded += HandleResponse;
LLMSystem.OnInferenceStreamed += HandleToken;

// Then send messages
await LLMSystem.SendMessageToBot(AuthorRole.User, "Hello");
```

### 2. Check System Status
Always verify the system is ready before attempting operations:

```csharp
if (LLMSystem.Status == SystemStatus.Ready)
{
    await LLMSystem.SendMessageToBot(AuthorRole.User, message);
}
```

### 3. Invalidate Cache When Needed
Call `InvalidatePromptCache()` after changing settings that affect prompt generation:

```csharp
LLMSystem.Settings.RAGEnabled = true;
LLMSystem.Instruct.AddNamesToPrompt = false;
LLMSystem.InvalidatePromptCache(); // Important!
```

### 4. Use Logging
Configure logging to debug issues:

```csharp
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder => 
    builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
LLMSystem.Logger = loggerFactory.CreateLogger("LLMSystem");
```

### 5. Handle Long Operations
For long-running operations, provide user feedback:

```csharp
LLMSystem.OnStatusChanged += (s, status) =>
{
    switch (status)
    {
        case SystemStatus.Busy:
            Console.WriteLine("Generating response...");
            break;
        case SystemStatus.Ready:
            Console.WriteLine("Ready for next input.");
            break;
    }
};
```

## Troubleshooting

### Common Issues

#### 1. Connection Problems
```csharp
// Check if backend is running
if (!await LLMSystem.CheckBackend())
{
    Console.WriteLine("Backend not responding. Check URL and server status.");
}

// Verify backend type matches server
LLMSystem.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
```

#### 2. No Response from Model
```csharp
// Check event handlers are set
if (LLMSystem.OnInferenceEnded == null)
{
    Console.WriteLine("No event handler set for OnInferenceEnded");
}

// Check if generation was cancelled
if (LLMSystem.Status == SystemStatus.Ready && /* no response received */)
{
    Console.WriteLine("Generation may have been cancelled or failed");
}
```

#### 3. Context Length Issues
```csharp
// Monitor token usage
LLMSystem.OnFullPromptReady += (s, prompt) =>
{
    int tokens = LLMSystem.GetTokenCount(prompt);
    Console.WriteLine($"Prompt tokens: {tokens}/{LLMSystem.MaxContextLength}");
    
    if (tokens > LLMSystem.MaxContextLength - LLMSystem.Settings.MaxReplyLength)
    {
        Console.WriteLine("Warning: Prompt may be too long");
    }
};
```

#### 4. Memory/Performance Issues
```csharp
// Limit chat history size
LLMSystem.Settings.SessionHandling = SessionHandling.CurrentOnly;

// Disable RAG if not needed
LLMSystem.Settings.RAGEnabled = false;

// Clear old sessions periodically
if (LLMSystem.History.Sessions.Count > 10)
{
    LLMSystem.History.Sessions.RemoveRange(0, 5);
}
```

## Macro System

LLMSystem includes a powerful macro replacement system for dynamic content:

### Available Macros
- `{{user}}` - User's name
- `{{char}}` - Bot's name  
- `{{userbio}}` - User's biography
- `{{charbio}}` - Bot's biography
- `{{examples}}` - Bot's dialog examples
- `{{scenario}}` - Current scenario text
- `{{date}}` - Current date in human format
- `{{time}}` - Current time
- `{{day}}` - Current day of week
- `{{selfedit}}` - Bot's self-edit field

### Usage
```csharp
string template = "Hello {{user}}, I'm {{char}}. Today is {{day}}, {{date}}.";
string processed = LLMSystem.ReplaceMacros(template);
// Result: "Hello John, I'm Assistant. Today is Monday, January 15th."
```

## Integration with Other Systems

### Working with RAGSystem
```csharp
// Configure RAG integration
LLMSystem.Settings.RAGEnabled = true;
LLMSystem.Settings.RAGMaxEntries = 5;

// RAG automatically enhances prompts with relevant information
// from past conversations and documents
```

### Working with Plugins
```csharp
// Add context plugins
LLMSystem.ContextPlugins.Add(new MyCustomPlugin());

// Plugins can modify user input, add system messages,
// and post-process responses
```

This comprehensive documentation covers all major aspects of the LLMSystem class. For more specific examples or advanced use cases, refer to the source code documentation and other parts of the AIToolkit documentation.
