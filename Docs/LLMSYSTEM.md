# LLMEngine Documentation

## Overview

The `LLMEngine` is the core component of the AIToolkit library that provides a high-level interface for communicating with Large Language Models (LLMs). It acts as a middleware layer that handles connections to various backends, manages chat history, personas, and provides both simple query methods and full conversation management.

> 🚀 **New to AIToolkit?** Start with the [Quick Start Guide](QUICKSTART.md) for a 5-minute setup!

## Key Features

- **Backend Compatibility**: Supports KoboldAPI and OpenAI API
- **Simple Queries**: Direct text queries with streaming and non-streaming options
- **Full Communication**: Complete chat system with personas, history, and context
- **Persona Management**: Bot and user personas with customizable attributes
- **Chat History**: Automatic session management and message logging
- **RAG Integration**: Retrieval-Augmented Generation support
- **Event System**: Real-time streaming and status updates

## Table of Contents

1. [Quick Start Guide](QUICKSTART.md) - Get running in 5 minutes!
2. [Basic Setup and Initialization](#basic-setup-and-initialization)
3. [Settings Configuration](#settings-configuration)
4. [Simple Queries](#simple-queries)
5. [Full Communication Mode](#full-communication-mode)
6. [Personas and Chat Management](#personas-and-chat-management)
7. [Events and Streaming](#events-and-streaming)
8. [Advanced Features](#advanced-features)
9. [Examples](#examples)

## Basic Setup and Initialization

### Step 1: Setup the Backend Connection

The first step is to configure and connect to your LLM backend:

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

// Setup connection to KoboldCpp (recommended)
LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);

// Or setup connection to OpenAI-compatible API
LLMEngine.Setup("http://localhost:1234", BackendAPI.OpenAI, "your-api-key");
```

### Step 2: Connect to the Backend

After setting up the connection parameters, establish the connection:

```csharp
// Connect and retrieve model information
await LLMEngine.Connect();

// Check if connection was successful
if (LLMEngine.Status == SystemStatus.Ready)
{
    Console.WriteLine($"Connected to: {LLMEngine.CurrentModel}");
    Console.WriteLine($"Backend: {LLMEngine.Backend}");
    Console.WriteLine($"Max Context: {LLMEngine.MaxContextLength} tokens");
}
```

### Step 3: Basic Status Monitoring

You can monitor the engine status:

```csharp
// Subscribe to status changes
LLMEngine.OnStatusChanged += (sender, status) =>
{
    Console.WriteLine($"LLMEngine status changed to: {status}");
};

// Check current status
switch (LLMEngine.Status)
{
    case SystemStatus.NotInit:
        Console.WriteLine("Engine not initialized");
        break;
    case SystemStatus.Ready:
        Console.WriteLine("Engine ready for queries");
        break;
    case SystemStatus.Busy:
        Console.WriteLine("Engine is processing a request");
        break;
}
```

## Settings Configuration

The `LLMEngine.Settings` object provides extensive configuration options:

```csharp
// Access current settings
var settings = LLMEngine.Settings;

// Basic backend settings
settings.BackendUrl = "http://localhost:5001";
settings.BackendAPI = BackendAPI.KoboldAPI;
settings.OpenAIKey = "your-api-key";

// Generation settings
settings.MaxReplyLength = 512;
settings.StopGenerationOnFirstParagraph = false;

// RAG and memory settings
settings.AllowWorldInfo = true;
settings.RAGMaxEntries = 3;
settings.RAGIndex = 3;

// Web search settings
settings.WebSearchAPI = BackendSearchAPI.DuckDuckGo;
settings.WebSearchDetailedResults = true;

// Save settings to file
settings.SaveToFile("path/to/settings.json");

// Load settings from file
settings = LLMSettings.LoadFromFile("path/to/settings.json");
LLMEngine.Settings = settings;
```

## Simple Queries

For basic text generation without conversation management, use the simple query methods:

### Non-Streaming Queries

```csharp
// Simple text completion
var prompt = "What is the capital of France?";
var response = await LLMEngine.SimpleQuery(prompt);
Console.WriteLine($"Response: {response}");
```

### Streaming Queries

For real-time text generation with streaming:

```csharp
// Subscribe to streaming events
LLMEngine.OnInferenceStreamed += (sender, token) =>
{
    Console.Write(token); // Print each token as it arrives
};

LLMEngine.OnInferenceEnded += (sender, fullResponse) =>
{
    Console.WriteLine($"\nComplete response: {fullResponse}");
};

// Start streaming query
var prompt = "Write a short story about a robot.";
await LLMEngine.SimpleQueryStreaming(prompt);
```

### Using PromptBuilder for Advanced Prompts

For more complex prompts, use the PromptBuilder:

```csharp
// Get a prompt builder for the current backend
var builder = LLMEngine.GetPromptBuilder();

// Add messages with different roles
builder.AddMessage(AuthorRole.System, "You are a helpful assistant.");
builder.AddMessage(AuthorRole.User, "Explain quantum physics in simple terms.");

// Convert to query and execute
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
```

## Full Communication Mode

The full communication mode provides complete conversation management with personas, chat history, and context awareness.

### Setting Up Personas

```csharp
// Create a bot persona
var bot = new BasePersona
{
    Name = "Alice",
    Bio = "A knowledgeable AI assistant with expertise in science and technology.",
    IsUser = false,
    Scenario = "You are Alice, a helpful AI assistant in a research lab.",
    FirstMessage = new List<string> { "Hello! I'm Alice, how can I help you today?" }
};

// Create a user persona
var user = new BasePersona
{
    Name = "John",
    Bio = "A curious researcher working on AI projects.",
    IsUser = true
};

// Set the personas
LLMEngine.Bot = bot;
LLMEngine.User = user;
```

### Sending Messages

```csharp
// Send a user message and get bot response
await LLMEngine.SendMessageToBot(AuthorRole.User, "What is machine learning?");

// The response will be streamed through events
LLMEngine.OnInferenceStreamed += (sender, token) =>
{
    Console.Write(token);
};

LLMEngine.OnInferenceEnded += (sender, response) =>
{
    Console.WriteLine($"\nBot response complete: {response}");
    
    // The response is automatically logged to chat history
    // Access it via LLMEngine.History
};
```

### Managing Chat History

```csharp
// Access the chat history
var history = LLMEngine.History;

// Get the last message
var lastMessage = history.LastMessage();
Console.WriteLine($"Last message: {lastMessage?.Message}");

// Get all messages in current session
var messages = history.CurrentSession.Messages;
foreach (var msg in messages)
{
    Console.WriteLine($"{msg.Role}: {msg.Message}");
}

// Clear chat history
history.Clear();

// Start a new session (archiving the previous one if it has content)
await history.StartNewChatSession();

// Save history to file
history.SaveToFile("path/to/chat.json");

// Load history from file
var loadedHistory = Chatlog.LoadFromFile("path/to/chat.json");
LLMEngine.Bot.History = loadedHistory;
```

### Bot Actions

```csharp
// Let the bot generate a message based on current context
await LLMEngine.AddBotMessage();

// Reroll the last bot response
await LLMEngine.RerollLastMessage();

// Have the bot impersonate the user
await LLMEngine.ImpersonateUser();
```

## Personas and Chat Management

### Advanced Persona Configuration

```csharp
var persona = new BasePersona
{
    Name = "Dr. Sarah",
    Bio = "A medical researcher specializing in genetics",
    Scenario = "You are Dr. Sarah, working in a cutting-edge genetics lab.",
    
    // Multiple possible first messages (one chosen randomly)
    FirstMessage = new List<string>
    {
        "Welcome to the genetics lab! How can I assist you today?",
        "Hello! I'm Dr. Sarah. What would you like to know about genetics?",
        "Greetings! Ready to explore the world of genetic research?"
    },
    
    // Example dialog style
    ExampleDialogs = new List<string>
    {
        "Dr. Sarah: *adjusts lab coat* Let me explain this concept clearly...",
        "Dr. Sarah: That's a fascinating question about DNA structure!",
        "Dr. Sarah: *points to molecular diagram* As you can see here..."
    },
    
    // Enable agent mode for autonomous behavior
    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask" },
    
    // Plugin integration
    Plugins = new List<string> { "WebSearchPlugin", "MemoryPlugin" }
};

// Load the persona
LLMEngine.Bot = persona;
persona.BeginChat(); // Initializes plugins and loads context
```

### Session Management

```csharp
// Start a new chat session
await LLMEngine.History.StartNewChatSession();

// Add a welcome message
var welcomeMessage = LLMEngine.Bot.GetWelcomeLine(LLMEngine.User.Name);
if (!string.IsNullOrEmpty(welcomeMessage))
{
    LLMEngine.History.LogMessage(AuthorRole.Assistant, welcomeMessage, 
                                LLMEngine.User, LLMEngine.Bot);
}

// Check session statistics
var session = LLMEngine.History.CurrentSession;
Console.WriteLine($"Session started: {session.StartTime}");
Console.WriteLine($"Message count: {session.Messages.Count}");
```

## Events and Streaming

The LLMEngine provides several events for real-time updates:

```csharp
// Full prompt generation
LLMEngine.OnFullPromptReady += (sender, prompt) =>
{
    Console.WriteLine($"Generated prompt: {prompt}");
};

// Streaming text generation
LLMEngine.OnInferenceStreamed += (sender, token) =>
{
    Console.Write(token); // Real-time text output
};

// Generation completion
LLMEngine.OnInferenceEnded += (sender, fullResponse) =>
{
    Console.WriteLine($"\nGeneration complete: {fullResponse}");
    
    // Log the response to history if needed
    LLMEngine.History.LogMessage(AuthorRole.Assistant, fullResponse, 
                                LLMEngine.User, LLMEngine.Bot);
};

// Quick inference (non-streaming) completion
LLMEngine.OnQuickInferenceEnded += (sender, response) =>
{
    Console.WriteLine($"Quick inference result: {response}");
};

// Status changes
LLMEngine.OnStatusChanged += (sender, status) =>
{
    Console.WriteLine($"Status: {status}");
};

// Bot persona changes
LLMEngine.OnBotChanged += (sender, newBot) =>
{
    Console.WriteLine($"Bot changed to: {newBot.Name}");
};
```

## Advanced Features

### RAG (Retrieval-Augmented Generation)

```csharp
// Enable RAG in settings
LLMEngine.Settings.AllowWorldInfo = true;
LLMEngine.Settings.RAGMaxEntries = 5;
LLMEngine.Settings.RAGIndex = 3; // Insert at message index 3

// RAG automatically retrieves relevant context based on the conversation
// and inserts it into the prompt at the specified index
```

### Web Search Integration

```csharp
// Configure web search
LLMEngine.Settings.WebSearchAPI = BackendSearchAPI.DuckDuckGo;
LLMEngine.Settings.WebSearchDetailedResults = true;

// Web search is automatically triggered by agent tasks
// when the bot needs current information
```

### Token Management

```csharp
// Count tokens in text
var tokenCount = LLMEngine.GetTokenCount("Hello, world!");
Console.WriteLine($"Token count: {tokenCount}");

// Check context limits
var maxTokens = LLMEngine.MaxContextLength;
var currentUsage = LLMEngine.History.GetCurrentTokenUsage();
var remaining = maxTokens - currentUsage;
Console.WriteLine($"Tokens remaining: {remaining}");
```

### Grammar and Structured Output

```csharp
// For backends that support GBNF grammar
if (LLMEngine.SupportsGrammar)
{
    // Define a class for structured output
    public class ResponseFormat
    {
        public string answer { get; set; }
        public int confidence { get; set; }
        public string[] sources { get; set; }
    }
    
    // Generate grammar from class
    var grammar = await LLMEngine.Client.SchemaToGrammar(typeof(ResponseFormat));
    
    // Use in query (implementation depends on backend)
}
```

## Examples

### Example 1: Simple Q&A Bot

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

class SimpleBot
{
    public static async Task Main()
    {
        // Setup and connect
        LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMEngine.Connect();
        
        // Configure streaming
        LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
        LLMEngine.OnInferenceEnded += (_, response) => Console.WriteLine("\n");
        
        // Interactive loop
        while (true)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();
            if (input == "quit") break;
            
            Console.Write("Bot: ");
            await LLMEngine.SimpleQueryStreaming($"User: {input}\nBot:");
        }
    }
}
```

### Example 2: Character Chat with Personas

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

class CharacterChat
{
    public static async Task Main()
    {
        // Setup
        LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMEngine.Connect();
        
        // Create personas
        var bot = new BasePersona
        {
            Name = "Einstein",
            Bio = "Albert Einstein, the famous physicist",
            Scenario = "You are Albert Einstein. Speak with wisdom and curiosity about science.",
            FirstMessage = new() { "Guten Tag! I am Albert Einstein. What scientific mysteries shall we explore today?" }
        };
        
        var user = new BasePersona
        {
            Name = "Student",
            IsUser = true
        };
        
        LLMEngine.Bot = bot;
        LLMEngine.User = user;
        
        // Setup events
        LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
        LLMEngine.OnInferenceEnded += (_, response) => Console.WriteLine("\n");
        
        // Start conversation
        var welcome = bot.GetWelcomeLine(user.Name);
        Console.WriteLine($"Einstein: {welcome}");
        LLMEngine.History.LogMessage(AuthorRole.Assistant, welcome, user, bot);
        
        // Chat loop
        while (true)
        {
            Console.Write("Student: ");
            var input = Console.ReadLine();
            if (input == "quit") break;
            
            Console.Write("Einstein: ");
            await LLMEngine.SendMessageToBot(AuthorRole.User, input);
        }
        
        // Save history
        LLMEngine.History.SaveToFile("einstein_chat.json");
    }
}
```

### Example 3: Multi-Session Chat Manager

```csharp
using AIToolkit.LLM;
using AIToolkit.Files;

class ChatManager
{
    public static async Task Main()
    {
        // Setup
        LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
        await LLMEngine.Connect();
        
        // Load or create personas
        var bot = BasePersona.LoadFromFile("my_character.json") ?? new BasePersona
        {
            Name = "Assistant",
            Bio = "A helpful AI assistant"
        };
        
        LLMEngine.Bot = bot;
        
        // Setup events
        LLMEngine.OnInferenceEnded += (_, response) =>
        {
            Console.WriteLine($"\n[{DateTime.Now:HH:mm}] Bot: {response}");
            
            // Auto-save after each response
            LLMEngine.History.SaveToFile($"chat_{DateTime.Now:yyyy-MM-dd}.json");
        };
        
        // Load previous session if exists
        var todayFile = $"chat_{DateTime.Now:yyyy-MM-dd}.json";
        if (File.Exists(todayFile))
        {
            LLMEngine.Bot.History = Chatlog.LoadFromFile(todayFile);
            Console.WriteLine("Previous session loaded.");
        }
        
        // Chat interface
        Console.WriteLine("Chat started. Type 'new' for new session, 'quit' to exit.");
        
        while (true)
        {
            Console.Write($"[{DateTime.Now:HH:mm}] You: ");
            var input = Console.ReadLine();
            
            switch (input?.ToLower())
            {
                case "quit":
                    return;
                case "new":
                    await LLMEngine.History.StartNewChatSession();
                    Console.WriteLine("New session started.");
                    continue;
                case "reroll":
                    await LLMEngine.RerollLastMessage();
                    continue;
                default:
                    await LLMEngine.SendMessageToBot(AuthorRole.User, input);
                    break;
            }
        }
    }
}
```

## Best Practices

1. **Always check backend connectivity** before making queries
2. **Monitor token usage** to avoid context overflow
3. **Use streaming** for better user experience with long responses
4. **Save chat history regularly** to preserve conversations
5. **Handle errors gracefully** with try-catch blocks around async calls
6. **Use personas** to create more engaging and consistent character behavior
7. **Leverage events** for real-time UI updates
8. **Configure settings** appropriately for your use case

## Troubleshooting

### Common Issues

1. **Connection Failed**: Ensure the backend URL is correct and the service is running
2. **Empty Responses**: Check if the model is loaded correctly in your backend
3. **Token Overflow**: Monitor context length and implement history trimming
4. **Slow Responses**: Consider using streaming for better UX
5. **Persona Not Loading**: Ensure JSON files are properly formatted and accessible

### Debug Information

```csharp
// Check engine status
Console.WriteLine($"Status: {LLMEngine.Status}");
Console.WriteLine($"Backend: {LLMEngine.Backend}");
Console.WriteLine($"Model: {LLMEngine.CurrentModel}");
Console.WriteLine($"Max Context: {LLMEngine.MaxContextLength}");

// Check current token usage
var usage = LLMEngine.History.GetCurrentTokenUsage();
Console.WriteLine($"Current token usage: {usage}");

// Verify backend connection
var isConnected = await LLMEngine.CheckBackend();
Console.WriteLine($"Backend connected: {isConnected}");
```

This documentation provides a comprehensive guide to using the LLMEngine. For more advanced features like agent tasks, RAG configuration, and plugin development, refer to the specific documentation files for those components.