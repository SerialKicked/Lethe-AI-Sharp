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
4. [Author Roles and Message Types](#author-roles-and-message-types)
5. [Simple Queries](#simple-queries)
6. [Full Communication Mode](#full-communication-mode)
7. [Personas and Chat Management](#personas-and-chat-management)
8. [Instruction Format (LLMEngine.Instruct)](#instruction-format-llmengineinstruct)
9. [Sampling Settings (LLMEngine.Sampler)](#sampling-settings-llmenginesampler)
10. [System Prompt (LLMEngine.SystemPrompt)](#system-prompt-llmenginesystemprompt)
11. [Macros System](#macros-system)
12. [Events and Streaming](#events-and-streaming)
13. [Advanced Features](#advanced-features)
14. [Examples](#examples)

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

## Author Roles and Message Types

The AIToolkit uses specific author roles to distinguish between different types of messages. Understanding these roles is crucial for proper prompt construction:

### Core Roles

```csharp
public enum AuthorRole
{
    System,     // System messages within conversation
    User,       // User/human messages  
    Assistant,  // AI assistant responses
    Unknown,    // Unknown/unspecified role
    SysPrompt   // Main system prompt (beginning of conversation)
}
```

### Important Distinction: SysPrompt vs System

- **`AuthorRole.SysPrompt`**: Used for the **main system prompt** at the beginning of a conversation. This sets the overall context, character personality, and instructions for the entire session.

- **`AuthorRole.System`**: Used for **system messages** inserted within the conversation flow. These are typically status updates, instructions, or contextual information that appear between user and assistant messages.

```csharp
// Example: Main system prompt (beginning of conversation)
builder.AddMessage(AuthorRole.SysPrompt, "You are a helpful AI assistant specialized in science.");

// Example: System message within conversation
builder.AddMessage(AuthorRole.User, "What's the weather like?");
builder.AddMessage(AuthorRole.System, "Weather data is currently unavailable.");
builder.AddMessage(AuthorRole.Assistant, "I apologize, but I don't have access to current weather information.");
```

**Note**: For 90% of instruction formats, `SysPrompt` and `System` are handled identically. However, the library automatically handles the differences when using models with specific formatting requirements.

## Settings Configuration

For basic text generation without conversation management, use the simple query methods. These methods require using the `IPromptBuilder` interface to construct backend-appropriate prompts.

### Non-Streaming Queries

```csharp
// Get a prompt builder for the current backend
var builder = LLMEngine.GetPromptBuilder();

// Add your prompt content
builder.AddMessage(AuthorRole.User, "What is the capital of France?");

// Convert to query and execute
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
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

// Build the prompt
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.User, "Write a short story about a robot.");

// Convert to query and start streaming
var query = builder.PromptToQuery(AuthorRole.Assistant);
await LLMEngine.SimpleQueryStreaming(query);
```

### Using PromptBuilder for Advanced Prompts

The PromptBuilder allows you to create complex, multi-role conversations:

```csharp
// Example with system prompt and user message
var builder = LLMEngine.GetPromptBuilder();

// Add messages with different roles
builder.AddMessage(AuthorRole.SysPrompt, "You are a helpful assistant.");
builder.AddMessage(AuthorRole.User, "Explain quantum physics in simple terms.");

// Convert to query and execute
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
```

**Note**: Use `AuthorRole.SysPrompt` for system prompts and `AuthorRole.System` for system messages in conversation. The exact behavior depends on your backend and instruction format.

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

## Instruction Format (LLMEngine.Instruct)

The `LLMEngine.Instruct` property controls how messages are formatted for the underlying language model. This is crucial for proper model communication, especially with text completion backends like KoboldAPI.

### Understanding Instruction Formats

Different models expect different formatting. For example:
- **ChatML**: `<|im_start|>user\nHello<|im_end|>`
- **Alpaca**: `### Instruction:\nHello\n### Response:`
- **Vicuna**: `USER: Hello\nASSISTANT:`

### Accessing and Configuring

```csharp
// Access current instruction format
var instruct = LLMEngine.Instruct;

// Key properties for message formatting
Console.WriteLine($"User start: '{instruct.UserStart}'");
Console.WriteLine($"User end: '{instruct.UserEnd}'");
Console.WriteLine($"Bot start: '{instruct.BotStart}'");
Console.WriteLine($"Bot end: '{instruct.BotEnd}'");
Console.WriteLine($"System prompt start: '{instruct.SysPromptStart}'");
Console.WriteLine($"System prompt end: '{instruct.SysPromptEnd}'");
```

### ChatML Example Configuration

```csharp
// Configure for ChatML format
LLMEngine.Instruct.SysPromptStart = "<|im_start|>system\n";
LLMEngine.Instruct.SysPromptEnd = "<|im_end|>\n";
LLMEngine.Instruct.UserStart = "<|im_start|>user\n";
LLMEngine.Instruct.UserEnd = "<|im_end|>\n";
LLMEngine.Instruct.BotStart = "<|im_start|>assistant\n";
LLMEngine.Instruct.BotEnd = "<|im_end|>\n";
LLMEngine.Instruct.AddNamesToPrompt = false;
```

### Key Settings

- **`AddNamesToPrompt`**: Whether to include character names in messages
- **`NewLinesBetweenMessages`**: Add newlines between message blocks
- **`ThinkingStart/End`**: For chain-of-thought models
- **`StopSequence`**: When to stop generation

**Note**: OpenAI-compatible backends handle formatting internally, while KoboldAPI backends use these settings to properly format prompts.

For detailed instruction format documentation, see [InstructFormat.md](INSTRUCTFORMAT.md).

## Sampling Settings (LLMEngine.Sampler)

The `LLMEngine.Sampler` controls how the model generates text, affecting creativity, randomness, and quality of responses.

### Key Settings

```csharp
// Access current sampler settings
var sampler = LLMEngine.Sampler;

// Common settings
sampler.Temperature = 0.7;      // Creativity (0.0-2.0, higher = more creative)
sampler.Top_p = 0.9;           // Nucleus sampling (0.0-1.0)
sampler.Top_k = 40;            // Top-k sampling (0 = disabled)
sampler.Max_length = 512;      // Maximum response length in tokens
sampler.Rep_pen = 1.1;         // Repetition penalty (1.0 = no penalty)

// Advanced settings
sampler.Min_p = 0.05;          // Minimum probability threshold
sampler.Typical = 1.0;         // Typical sampling
sampler.Tfs = 1.0;             // Tail-free sampling
```

### Quick Configuration Examples

```csharp
// Creative writing
sampler.Temperature = 1.2;
sampler.Top_p = 0.95;

// Factual/precise responses  
sampler.Temperature = 0.3;
sampler.Top_p = 0.85;

// Balanced conversation
sampler.Temperature = 0.7;
sampler.Top_p = 0.9;
```

The sampler settings are automatically applied to all generation methods (SimpleQuery, full communication mode, etc.).

## System Prompt (LLMEngine.SystemPrompt)

The `LLMEngine.SystemPrompt` is only relevant for **full communication mode** and defines the structure and content of the system prompt that's sent to the model.

### Structure and Usage

```csharp
// Access system prompt settings
var sysPrompt = LLMEngine.SystemPrompt;

// Configure the main prompt template
sysPrompt.Prompt = @"You are {{char}}, interacting with {{user}}.

# Character Information
Name: {{char}}
Bio: {{charbio}}

# User Information  
Name: {{user}}
Bio: {{userbio}}

# Instructions
Stay in character and respond naturally to {{user}}.";

// Configure section titles
sysPrompt.ScenarioTitle = "# Current Scenario";
sysPrompt.DialogsTitle = "# Character's Writing Style";
sysPrompt.WorldInfoTitle = "# Important Context";
```

### Dynamic Content Integration

The system prompt automatically integrates:
- **Character bio and personality** from `LLMEngine.Bot`
- **Scenario information** from the loaded persona
- **Example dialogs** to guide response style
- **RAG/WorldInfo** retrieved content
- **Previous session summaries** when enabled

### Macro Support

The system prompt fully supports the macro system (see [Macros System](#macros-system)) for dynamic content replacement.

## Macros System

The AIToolkit includes a powerful macro system that allows dynamic text replacement throughout the library. Macros can be used in system prompts, personas, and even simple queries.

### Available Macros

#### Character and User Macros
- **`{{char}}`**: Current bot character's name
- **`{{charbio}}`**: Current bot character's biography
- **`{{user}}`**: Current user's name
- **`{{userbio}}`**: Current user's biography
- **`{{examples}}`**: Character's example dialogs
- **`{{scenario}}`**: Current scenario description
- **`{{selfedit}}`**: Character's self-editable field

#### Time and Date Macros
- **`{{time}}`**: Current time (e.g., "02:30 PM")
- **`{{date}}`**: Current date in human-readable format
- **`{{day}}`**: Current day of the week (e.g., "Monday")

### Usage Examples

#### In System Prompts
```csharp
LLMEngine.SystemPrompt.Prompt = @"You are {{char}}, a {{charbio}}.
Today is {{day}}, {{date}} at {{time}}.
You are talking with {{user}}.

{{scenario}}";
```

#### In Simple Queries
```csharp
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.SysPrompt, "You are {{char}}, interacting with {{user}} on {{day}}.");
builder.AddMessage(AuthorRole.User, "Hello {{char}}!");
var query = builder.PromptToQuery(AuthorRole.Assistant);
var response = await LLMEngine.SimpleQuery(query);
```

#### In Persona Definitions
```csharp
var persona = new BasePersona
{
    Name = "Alice",
    Bio = "I am {{char}}, a helpful assistant created to help {{user}}.",
    Scenario = "{{char}} and {{user}} are working together on {{day}} morning.",
    FirstMessage = new() { "Good morning {{user}}! It's {{time}} on {{day}}. How can I help?" }
};
```

### Automatic Processing

Macros are automatically processed in:
- **Full communication mode**: All system prompts, character bios, scenarios
- **Simple queries**: When using character context
- **Chat history**: Welcome messages and dialog examples
- **RAG content**: Retrieved information with character context

### Manual Macro Processing

You can also manually process macros:

```csharp
// Process macros for current bot and user
string processedText = LLMEngine.Bot.ReplaceMacros("Hello {{user}}, I'm {{char}}!");

// Process macros with specific user
string customText = LLMEngine.Bot.ReplaceMacros("{{user}} is talking to {{char}} at {{time}}", specificUser);
```

This macro system ensures dynamic, contextual content that adapts to your current characters, time, and conversation state.

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
            
            // Build prompt properly
            var builder = LLMEngine.GetPromptBuilder();
            builder.AddMessage(AuthorRole.User, input);
            var query = builder.PromptToQuery(AuthorRole.Assistant);
            
            await LLMEngine.SimpleQueryStreaming(query);
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