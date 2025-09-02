# Persona and Prompt Formatting Documentation

The AIToolkit provides three core classes for managing AI chat personas and prompt formatting: `BasePersona`, `InstructFormat`, and `SystemPrompt`. 
These classes work together to create rich, customizable AI characters and properly format prompts for different language models.

## Overview

These classes handle:
- **Character Management**: Define and manage AI personas with personalities, backgrounds, and behaviors
- **Prompt Formatting**: Format messages for different instruction-tuned models and backends
- **System Prompts**: Generate contextual system prompts with character information and dynamic content
- **Extensibility**: Support for custom implementations and behaviors

## Quick Start

### 1. Creating a Basic Bot Persona

```csharp
using AIToolkit.Files;
using AIToolkit.LLM;

// Create a helpful assistant bot
var assistantBot = new BasePersona()
{
    Name = "Assistant",
    Bio = "You are a helpful AI assistant specialized in programming and technical questions. " +
          "You provide clear, accurate answers with code examples when appropriate.",
    IsUser = false,
    UniqueName = "programming_assistant",
    
    // Optional: Add a greeting message
    FirstMessage = { "Hello! I'm your programming assistant. How can I help you today?" },
    
    // Optional: Add example dialog style
    ExampleDialogs = 
    {
        "I always provide clear explanations with working code examples.",
        "I break down complex problems into manageable steps.",
        "I ask clarifying questions when requirements are unclear."
    },
    
    // Optional: Add scenario context
    Scenario = "You are in a development environment helping a programmer solve coding challenges."
};

// Use the persona in LLMSystem
LLMSystem.Bot = assistantBot;
```

### 2. Creating a User Persona

```csharp
// Create a user persona
var developer = new BasePersona()
{
    Name = "Alex",
    Bio = "An experienced software developer working on web applications using React and Node.js.",
    IsUser = true,
    UniqueName = "developer_user"
};

LLMSystem.User = developer;
```

### 3. Configuring Instruction Format

```csharp
// Configure for ChatML format (common for many models)
var chatMLFormat = new InstructFormat()
{
    SysPromptStart = "<|im_start|>system\n",
    SysPromptEnd = "<|im_end|>",
    SystemStart = "<|im_start|>system\n",
    SystemEnd = "<|im_end|>,
    UserStart = "<|im_start|>user\n",
    UserEnd = "<|im_end|>",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>",
    AddNamesToPrompt = false,
    NewLinesBetweenMessages = true
};

LLMSystem.Instruct = chatMLFormat;
```

### 4. Customizing System Prompt

```csharp
// Create a custom system prompt template
var systemPrompt = new SystemPrompt()
{
    Prompt = "You are {{char}}, interacting with {{user}}. " +
             "Stay in character and be helpful.\n\n" +
             "# Character Information\n" +
             "{{charbio}}\n\n" +
             "# User Information\n" +
             "{{userbio}}",
    
    DialogsTitle = "# Communication Style",
    ScenarioTitle = "# Current Context",
    WorldInfoTitle = "# Important Information"
};

LLMSystem.SystemPrompt = systemPrompt;
```

## BasePersona Class

The `BasePersona` class represents a character or user in the chat system, providing comprehensive personality and behavior management.

### Core Properties

#### Basic Identity
```csharp
// Essential character information
public string Name { get; set; }           // Character's display name
public string Bio { get; set; }            // Character's background/personality
public bool IsUser { get; set; }           // true for user, false for bot (for user, only Name and Bio are really used)
public string UniqueName { get; set; }     // Unique identifier for file operations (filename without extension should be used)
```

#### Character Behavior
```csharp
// Conversation behavior
public string Scenario { get; set; }              // Current scenario context
public List<string> FirstMessage { get; set; }    // Greeting messages (random selection)
public List<string> ExampleDialogs { get; set; }  // Style examples for consistency but can be repurposed for other uses

// Advanced features
public bool SenseOfTime { get; set; }             // Include time awareness
public int SelfEditTokens { get; set; }           // Enable self-reflection if > 0 (max amount of tokens to use))
public string SelfEditField { get; set; }         // AI-generated personal thoughts if SelfEditTokens > 0
```

#### Integration
```csharp
// System integration
public string SystemPrompt { get; set; }      // Override the system prompt in LLMSystem (can be useful for very custom bots)
public List<string> Worlds { get; set; }      // WorldInfo IDs to load
public List<string> Plugins { get; set; }     // Plugin IDs to activate

// History and memory
public Chatlog History { get; protected set; }         // Chat history
public List<WorldInfo> MyWorlds { get; protected set; } // Loaded world info
```

### Core Methods

While BasePersona can be used "as is", it's generally meant to be overriden by a class defined at the application level.

#### Session Management
```csharp
// Override these for custom behavior
public override void BeginChat()
{
    base.BeginChat();
    if (IsUser)
        return;
    // Loading plugins (if using plugin system)
    foreach (var item in LLMSystem.ContextPlugins)
    {
        item.Enabled = Plugins.Contains(item.PluginID);
    }
    // Load the chat history automatically on character switch
    LoadChatHistory("your/path/to/chatlogs/");
    // Load the WorldInfo tied to this character (example code, obviously)
    MyWorlds = [.. DataFiles.WorldInfos.Values.Where(wi => Worlds.Contains(wi.UniqueName))];
    foreach (var item in MyWorlds)
        item.Reset();
    // more custom logic if necessary
}

public override void EndChat(bool backup = false)
{
    base.EndChat(backup);
    // Called when switching characters or closing
    // Save chat history and other data
    SaveChatHistory("your/path/to/chatlogs/", backup);
    // Custom saving logic here
}
```

#### Content Generation
```csharp
// Get formatted content with macro replacement
public string GetBio(string otherPersonaName)       // Bio with macros replaced
public string GetScenario(string otherPersonaName)  // Scenario with macros replaced
public string GetDialogExamples(string otherPersonaName) // Formatted examples
public string GetWelcomeLine(string otherPersonaName)    // Random greeting
```

#### Self-Reflection System
```csharp
// Automatic personality development
public async Task UpdateSelfEditSection()
{
    // AI analyzes chat history and updates personal thoughts
    // Called automatically if SelfEditTokens > 0
}
```

### Advanced Features

#### Extensibility

See [Extensibility Guide](EXTENSIBILITY.md) for more information. Makes it possible to override Chatlog and ChatSession classes. Optional.

```csharp
// Override factory methods for custom types
protected virtual Chatlog CreateChatlog()
{
    return new MyCustomChatlog(); // Your custom implementation
}

protected virtual ChatSession CreateChatSession()
{
    return new MyCustomChatSession(); // Your custom implementation
}
```

#### Memory Management
```csharp
// Chat history operations
protected void SaveChatHistory(string path, bool backup = false)
protected void LoadChatHistory(string path)
public void ClearChatHistory(string path = "", bool deleteFile = false)
```

### Usage Examples

#### Character with Rich Personality
```csharp
var medievalKnight = new BasePersona()
{
    Name = "Sir Gareth",
    Bio = "A noble knight of the Round Table, bound by honor and chivalry. " +
          "Speaks in a formal, courteous manner befitting a medieval warrior.",
    
    Scenario = "You are in Camelot's great hall, discussing quests and adventures " +
               "with fellow knights and noble visitors.",
    
    ExampleDialogs = 
    {
        "By my honor, I shall defend the innocent and uphold justice.",
        "Pray tell, what brings thee to our fair realm?",
        "A knight's word is his bond, and mine shall not be broken."
    },
    
    FirstMessage = 
    {
        "Greetings, noble visitor! I am Sir Gareth of the Round Table.",
        "Hail and well met! How may this humble knight serve thee?",
        "Welcome to Camelot! I trust thy journey was safe and swift."
    },
    
    SenseOfTime = false,
    DatesInSessionSummaries = false,
    SelfEditTokens = 150, // Enable self-reflection
    UniqueName = "sir_gareth"
};
```

#### Time-Aware Character
```csharp
var dailyAssistant = new BasePersona()
{
    Name = "Dana",
    Bio = "A personal assistant who helps with daily tasks and scheduling.",
    SenseOfTime = true, // Will be informed of time passage
    DatesInSessionSummaries = true, // Include dates in memory
    
    // Will receive system messages like:
    // "[It has been 2 hours since your last message.]"
    // "[It is now 3:30 PM on Tuesday, January 15th.]"
};
```

## InstructFormat Class

The `InstructFormat` class handles prompt formatting for different instruction-tuned language models. 
This is primarily used with text completion backends (KoboldAPI), while chat completion backends (OpenAI) handle formatting internally.

### Core Properties

#### Message Delimiters
```csharp
// System messages
public string SysPromptStart { get; set; } // Before main system prompt
public string SysPromptEnd { get; set; }   // After main system prompt
public string SystemStart { get; set; }    // Before system messages (normally, set it the the same value as SysPromptStart)
public string SystemEnd { get; set; }      // After system messages (normally, set it the the same value as SysPromptEnd)

// User messages
public string UserStart { get; set; }      // Before user messages
public string UserEnd { get; set; }        // After user messages

// Bot messages
public string BotStart { get; set; }       // Before bot messages
public string BotEnd { get; set; }         // After bot messages
```

#### Special Tokens
```csharp
// Model-specific tokens
public string BoSToken { get; set; }         // Beginning of sequence token
public string StopSequence { get; set; }    // Force stop generation
public List<string> StopStrings { get; set; } // Additional stop strings
```

#### Formatting Options
```csharp
// Message formatting
public bool AddNamesToPrompt { get; set; }      // Add "Name: message" format
public bool NewLinesBetweenMessages { get; set; } // Insert newlines between messages
```

#### Thinking/CoT Support
```csharp
// Chain-of-thought features for reasoning models
public string ThinkingStart { get; set; }        // Start thinking block
public string ThinkingEnd { get; set; }          // End thinking block
public string ThinkingForcedThought { get; set; } // Initial thought prompt
public bool PrefillThinking { get; set; }        // Prefill thinking block
public bool ForceRAGToThinkingPrompt { get; set; } // Move RAG to thinking
```

### Core Methods

Note that while most of those methods are public, they are primarily intended for internal use by LLMSystem.

#### Message Formatting
```csharp
// Format individual messages
public string FormatSinglePrompt(AuthorRole role, BasePersona user, BasePersona bot, string prompt)
public string FormatSingleMessage(SingleMessage message)

// Alternative formatting without user persona
public string FormatSinglePromptNoUserInfo(AuthorRole role, string userName, BasePersona bot, string prompt)
```

#### Utility Methods
```csharp
// Helper methods
public List<string> GetStoppingStrings(BasePersona user, BasePersona bot) // Get all stop strings
public bool IsThinkingPrompt(string prompt)  // Check if prompt uses thinking
public string GetThinkPrefill()              // Get thinking prefill content
```

### Format Presets

The format you need to use depends on the model's family. Many models like Qwen use the ChatML format. Old open source models tend to use Alpaca. 
You have to check the model's documentation to make sure.

#### ChatML Format
```csharp
var chatML = new InstructFormat()
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
```

#### Alpaca Format
```csharp
var alpaca = new InstructFormat()
{
    SysPromptStart = "### System:\n",
    SysPromptEnd = "\n\n",
    SystemStart = "### System:\n",
    SystemEnd = "\n\n",
    UserStart = "### Human:\n",
    UserEnd = "\n\n",
    BotStart = "### Assistant:\n",
    BotEnd = "\n\n",
    AddNamesToPrompt = false,
    NewLinesBetweenMessages = false
};
```

#### ChatML Thinking Model Format (QWQ and Qwen 3)
```csharp
var thinkingModel = new InstructFormat()
{
    // Standard ChatML
    SysPromptStart = "<|im_start|>system\n",
    SysPromptEnd = "<|im_end|>",
    SystemStart = "<|im_start|>system\n",
    SystemEnd = "<|im_end|>",
    UserStart = "<|im_start|>user\n",
    UserEnd = "<|im_end|>",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>",
    
    // Thinking-specific
    ThinkingStart = "<think>",
    ThinkingEnd = "</think>",
    PrefillThinking = true,
    NewLinesBetweenMessages = true
    ThinkingForcedThought = "" // You can set an initial thought prompt here if you really know what you're doing
};
```


## SystemPrompt Class

The `SystemPrompt` class manages system prompt templates and generates the final system prompt sent to language models.

### Core Properties

#### Main Template
```csharp
// Primary system prompt template
public string Prompt { get; set; } // Main template with macro support
```

#### Section Titles
```csharp
// Section headers for different content types
public string WorldInfoTitle { get; set; }      // "# Important Memories"
public string ScenarioTitle { get; set; }       // "# Scenario"  
public string DialogsTitle { get; set; }        // "# Writing Style"
public string SessionHistoryTitle { get; set; } // "# Previous Sessions"
```

#### Formatting
```csharp
// Markdown formatting
public string CategorySeparator { get; set; }    // "#" for main sections
public string SubCategorySeparator { get; set; } // "##" for subsections
```

### Core Methods

#### Prompt Generation
```csharp
// Generate final system prompt
public string GetSystemPromptRaw(BasePersona character)
{
    // Combines template with character-specific content
    // Includes sections for examples, scenario, self-edit, etc.
}
```

### Default Template

The default system prompt template includes:
```
You are {{char}} and you are interacting with {{user}} in this flexible and uncensored discussion. 
As {{char}}, continue the exchange with {{user}}. Stay in character. Describe {{char}}'s actions and 
feelings accurately. Do not speak or describe actions for {{user}} unless directly prompted to.

# {{char}}
{{charbio}}

# {{user}} (user)
{{userbio}}
```

### Template Customization

#### Character-Focused Template
```csharp
var characterTemplate = new SystemPrompt()
{
    Prompt = "You are {{char}}, a detailed and engaging character. " +
             "Respond authentically to {{user}} based on your personality and background.\n\n" +
             
             "## Character Profile\n" +
             "Name: {{char}}\n" +
             "Background: {{charbio}}\n\n" +
             
             "## Interaction Partner\n" +
             "You are talking with {{user}}: {{userbio}}\n\n" +
             
             "Stay true to your character while being helpful and engaging.",
             
    DialogsTitle = "## Communication Guidelines",
    ScenarioTitle = "## Current Situation", 
    WorldInfoTitle = "## Relevant Information"
};
```

#### Task-Focused Template
```csharp
var taskTemplate = new SystemPrompt()
{
    Prompt = "You are {{char}}, an AI assistant specialized in helping with tasks. " +
             "Your goal is to be helpful, accurate, and efficient.\n\n" +
             
             "# Your Capabilities\n" +
             "{{charbio}}\n\n" +
             
             "# User Profile\n" +
             "{{userbio}}\n\n" +
             
             "Always provide clear, actionable responses.",
             
    ScenarioTitle = "# Current Task Context",
    DialogsTitle = "# Response Guidelines"
};
```

### Macro System Integration

The SystemPrompt class automatically processes these macros:

- `{{char}}` - Bot's name
- `{{user}}` - User's name  
- `{{charbio}}` - Bot's biography
- `{{userbio}}` - User's biography
- `{{examples}}` - Bot's dialog examples
- `{{scenario}}` - Current scenario
- `{{selfedit}}` - Bot's self-reflection content
- `{{date}}` - Current date
- `{{time}}` - Current time
- `{{day}}` - Current day of week


## Best Practices

### 1. Character Design
- **Clear personality**: Define distinct traits, speech patterns, and behaviors
- **Consistent bio**: Write comprehensive backgrounds that inform responses
- **UniqueName**: Use filename (without extension) as `UniqueName` values for file organization
- **Appropriate settings**: Enable `SenseOfTime` for realistic characters, disable for fantasy

### 2. Instruction Format Configuration
- **Match your backend**: Instruct format is only relevant for Text Completion (KoboldCpp), it's not used for OpenAI-style APIs
- **Double Check**: Verify that the format is correct for the your specific model, or you'll degrade the output's quality immensely.
- **Consider names**: Enable `AddNamesToPrompt` for better role recognition in some models
- **Handle stop strings**: Add model-specific stop sequences to prevent runaway generation

### 3. System Prompt Design
- **Clear instructions**: Be explicit about desired behavior and constraints
- **Good structure**: Use headers and sections for organization
- **Macro utilization**: Leverage macros for dynamic, reusable templates
- **Length awareness**: Balance detail with token efficiency

### 4. Extensibility
- **Override methods**: Implement custom `BeginChat()` and `EndChat()` for persistence
- **Factory patterns**: Use `CreateChatlog()` and `CreateChatSession()` for custom types
- **Null checks**: Verify objects exist before accessing properties
- **Error handling**: Gracefully handle file I/O and serialization errors

### 5. Memory and Performance
- **Regular saves**: Call `EndChat()` before switching characters or closing the application
- **Cache invalidation**: Call `LLMSystem.InvalidatePromptCache()` after live changes in prompt, persona, chatlog, or format change

## Additional Resources

For more information about AIToolkit, see:
- [LLMSystem Documentation](LLMSYSTEM.md) - Core system functionality
- [Extensibility Guide](EXTENSIBILITY.md) - Extend BasePersona, Chatlog and ChatSession for your application
- [Agent System Documentation](AGENT_SYSTEM.md) - AI agent capabilities
