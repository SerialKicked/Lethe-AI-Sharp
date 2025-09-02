# Persona and Prompt Formatting Documentation

The AIToolkit provides three core classes for managing AI chat personas and prompt formatting: `BasePersona`, `InstructFormat`, and `SystemPrompt`. These classes work together to create rich, customizable AI characters and properly format prompts for different language models.

## Overview

These classes handle:
- **Character Management**: Define and manage AI personas with personalities, backgrounds, and behaviors
- **Prompt Formatting**: Format messages for different instruction-tuned models and backends
- **System Prompts**: Generate contextual system prompts with character information and dynamic content
- **Extensibility**: Support for custom implementations and behaviors

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   BasePersona   │    │ InstructFormat  │    │  SystemPrompt   │
│                 │    │                 │    │                 │
│  - Name/Bio     │───▶│  - UserStart    │───▶│  - Prompt       │
│  - Scenario     │    │  - BotStart     │    │  - Templates    │
│  - Examples     │    │  - SystemStart  │    │  - Sections     │
│  - History      │    │  - Thinking     │    │  - Macros       │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │                       │
         ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Extension     │    │   Backend       │    │   Dynamic       │
│   Support       │    │   Specific      │    │   Content       │
│                 │    │                 │    │                 │
│  - BeginChat()  │    │  - KoboldAPI    │    │  - WorldInfo    │
│  - EndChat()    │    │  - OpenAI       │    │  - RAG Data     │
│  - Custom Types │    │  - ChatML       │    │  - Time Info    │
└─────────────────┘    └─────────────────┘    └─────────────────┘
```

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
    SystemStart = "<|im_start|>system\n",
    SystemEnd = "<|im_end|>\n",
    UserStart = "<|im_start|>user\n",
    UserEnd = "<|im_end|>\n",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>\n",
    AddNamesToPrompt = false,
    NewLinesBetweenMessages = false
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
public bool IsUser { get; set; }           // true for user, false for bot
public string UniqueName { get; set; }     // Unique identifier for file operations
```

#### Character Behavior
```csharp
// Conversation behavior
public string Scenario { get; set; }              // Current scenario context
public List<string> FirstMessage { get; set; }    // Greeting messages (random selection)
public List<string> ExampleDialogs { get; set; }  // Style examples for consistency

// Advanced features
public bool SenseOfTime { get; set; }             // Include time awareness
public int SelfEditTokens { get; set; }           // Enable self-reflection
public string SelfEditField { get; set; }         // AI-generated personal thoughts
```

#### Integration
```csharp
// System integration
public string SystemPrompt { get; set; }      // Override default system prompt
public List<string> Worlds { get; set; }      // WorldInfo IDs to load
public List<string> Plugins { get; set; }     // Plugin IDs to activate

// History and memory
public Chatlog History { get; protected set; }         // Chat history
public List<WorldInfo> MyWorlds { get; protected set; } // Loaded world info
```

### Core Methods

#### Session Management
```csharp
// Override these for custom behavior
public virtual void BeginChat()
{
    // Called when character is loaded
    // Load chat history, world info, plugins
    LoadChatHistory("data/chars/");
    // Custom loading logic here
}

public virtual void EndChat(bool backup = false)
{
    // Called when switching characters or closing
    // Save chat history and other data
    SaveChatHistory("data/chars/", backup);
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
    
    SenseOfTime = true,
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

The `InstructFormat` class handles prompt formatting for different instruction-tuned language models. This is primarily used with text completion backends (KoboldAPI), while chat completion backends (OpenAI) handle formatting internally.

### Core Properties

#### Message Delimiters
```csharp
// System messages
public string SystemStart { get; set; }    // Before system messages
public string SystemEnd { get; set; }      // After system messages
public string SysPromptStart { get; set; } // Before main system prompt
public string SysPromptEnd { get; set; }   // After main system prompt

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

#### ChatML Format
```csharp
var chatML = new InstructFormat()
{
    SystemStart = "<|im_start|>system\n",
    SystemEnd = "<|im_end|>\n",
    UserStart = "<|im_start|>user\n", 
    UserEnd = "<|im_end|>\n",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>\n",
    AddNamesToPrompt = false,
    NewLinesBetweenMessages = false
};
```

#### Alpaca Format
```csharp
var alpaca = new InstructFormat()
{
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

#### Vicuna Format
```csharp
var vicuna = new InstructFormat()
{
    SystemStart = "SYSTEM: ",
    SystemEnd = "\n",
    UserStart = "USER: ",
    UserEnd = "\n",
    BotStart = "ASSISTANT: ",
    BotEnd = "\n",
    AddNamesToPrompt = false,
    NewLinesBetweenMessages = false
};
```

#### Thinking Model Format
```csharp
var thinkingModel = new InstructFormat()
{
    // Standard ChatML
    SystemStart = "<|im_start|>system\n",
    SystemEnd = "<|im_end|>\n",
    UserStart = "<|im_start|>user\n",
    UserEnd = "<|im_end|>\n",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>\n",
    
    // Thinking-specific
    ThinkingStart = "<thinking>",
    ThinkingEnd = "</thinking>",
    PrefillThinking = true,
    ThinkingForcedThought = "Let me think about this step by step."
};
```

### Usage Examples

#### Custom Format for Specific Model
```csharp
// For a model that uses specific delimiters
var customFormat = new InstructFormat()
{
    SystemStart = "[SYSTEM]",
    SystemEnd = "[/SYSTEM]\n",
    UserStart = "[USER]",
    UserEnd = "[/USER]\n", 
    BotStart = "[BOT]",
    BotEnd = "[/BOT]\n",
    
    // Add character names to messages
    AddNamesToPrompt = true,
    
    // Custom stop strings for this model
    StopStrings = { "[END]", "[STOP]", "<<<" }
};

LLMSystem.Instruct = customFormat;
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

## Common Usage Patterns

### 1. Creating a Complete Character Setup

```csharp
public class CharacterBuilder
{
    public static BasePersona CreateRoleplayCharacter(string name, string personality, string background)
    {
        return new BasePersona()
        {
            Name = name,
            Bio = $"{personality}\n\nBackground: {background}",
            IsUser = false,
            UniqueName = name.ToLower().Replace(" ", "_"),
            
            // Enable rich features
            SenseOfTime = true,
            SelfEditTokens = 200,
            DatesInSessionSummaries = false, // Roleplay often timeless
            
            // Set up automatic memory
            Scenario = "An immersive roleplay environment where you can express your " +
                      "personality and interact naturally with the user."
        };
    }
    
    public static void ConfigureForRoleplay()
    {
        // Custom system prompt for roleplay
        LLMSystem.SystemPrompt = new SystemPrompt()
        {
            Prompt = "You are {{char}} in an immersive roleplay. Express your personality " +
                     "authentically and respond naturally to {{user}}.\n\n" +
                     "# {{char}}\n{{charbio}}\n\n# {{user}}\n{{userbio}}",
            
            DialogsTitle = "# Character Voice",
            ScenarioTitle = "# Setting",
            WorldInfoTitle = "# World Lore"
        };
        
        // Configure format for natural conversation
        LLMSystem.Instruct.AddNamesToPrompt = true;
        LLMSystem.Instruct.NewLinesBetweenMessages = true;
    }
}

// Usage
var knight = CharacterBuilder.CreateRoleplayCharacter(
    "Sir Lancelot", 
    "A brave and honorable knight, known for your skill in battle and dedication to justice.",
    "You grew up as a noble's son and earned your knighthood through valor in combat."
);

CharacterBuilder.ConfigureForRoleplay();
LLMSystem.Bot = knight;
```

### 2. Multi-Character Conversation System

```csharp
public class MultiCharacterManager
{
    private Dictionary<string, BasePersona> characters = new();
    private BasePersona currentSpeaker;
    
    public void AddCharacter(BasePersona character)
    {
        characters[character.UniqueName] = character;
    }
    
    public async Task SwitchToCharacter(string uniqueName)
    {
        if (characters.TryGetValue(uniqueName, out var character))
        {
            // Save current character state
            currentSpeaker?.EndChat(backup: true);
            
            // Switch to new character
            currentSpeaker = character;
            LLMSystem.Bot = character;
            
            // Load new character state
            character.BeginChat();
            
            Console.WriteLine($"Switched to {character.Name}");
        }
    }
    
    public async Task CharacterResponse(string input)
    {
        if (currentSpeaker != null)
        {
            await LLMSystem.SendMessageToBot(AuthorRole.User, input);
        }
    }
}
```

### 3. Advanced Instruction Format Detection

```csharp
public class FormatDetector
{
    public static InstructFormat DetectFormat(string modelName)
    {
        modelName = modelName.ToLower();
        
        if (modelName.Contains("gpt") || modelName.Contains("claude"))
        {
            // These use chat completion APIs, format handled internally
            return new InstructFormat(); // Default/empty format
        }
        else if (modelName.Contains("llama") || modelName.Contains("mistral"))
        {
            return new InstructFormat()
            {
                SystemStart = "<|im_start|>system\n",
                SystemEnd = "<|im_end|>\n",
                UserStart = "<|im_start|>user\n",
                UserEnd = "<|im_end|>\n", 
                BotStart = "<|im_start|>assistant\n",
                BotEnd = "<|im_end|>\n"
            };
        }
        else if (modelName.Contains("alpaca"))
        {
            return new InstructFormat()
            {
                SystemStart = "### System:\n",
                SystemEnd = "\n\n",
                UserStart = "### Human:\n", 
                UserEnd = "\n\n",
                BotStart = "### Assistant:\n",
                BotEnd = "\n\n"
            };
        }
        
        return new InstructFormat(); // Default format
    }
}

// Auto-configure based on connected model
var format = FormatDetector.DetectFormat(LLMSystem.CurrentModel);
LLMSystem.Instruct = format;
```

### 4. Dynamic System Prompt Generation

```csharp
public class DynamicPromptBuilder
{
    public static SystemPrompt CreateContextualPrompt(string taskType, string expertise)
    {
        return taskType.ToLower() switch
        {
            "coding" => new SystemPrompt()
            {
                Prompt = $"You are {{{{char}}}}, an expert programmer specializing in {expertise}. " +
                         "Help {{{{user}}}} with coding questions and provide working examples.\n\n" +
                         "# Your Expertise\n{{{{charbio}}}}\n\n# User Profile\n{{{{userbio}}}}",
                DialogsTitle = "# Coding Style Guidelines",
                ScenarioTitle = "# Development Context"
            },
            
            "creative" => new SystemPrompt()
            {
                Prompt = "You are {{{{char}}}}, a creative assistant helping with writing and brainstorming. " +
                         "Inspire {{{{user}}}} with imaginative ideas and engaging content.\n\n" +
                         "# Your Creative Style\n{{{{charbio}}}}\n\n# User's Interests\n{{{{userbio}}}}",
                DialogsTitle = "# Creative Approach",
                ScenarioTitle = "# Creative Challenge"
            },
            
            _ => new SystemPrompt()
            {
                Prompt = "You are {{{{char}}}}, ready to help {{{{user}}}} with any questions or tasks.\n\n" +
                         "# About You\n{{{{charbio}}}}\n\n# About {{{{user}}}}\n{{{{userbio}}}}"
            }
        };
    }
}
```

## Best Practices

### 1. Character Design
- **Clear personality**: Define distinct traits, speech patterns, and behaviors
- **Consistent bio**: Write comprehensive backgrounds that inform responses
- **Meaningful names**: Use descriptive `UniqueName` values for file organization
- **Appropriate settings**: Enable `SenseOfTime` for realistic characters, disable for fantasy

### 2. Instruction Format Configuration
- **Match your backend**: Use empty format for OpenAI-style APIs, specific formats for text completion
- **Test thoroughly**: Verify format works with your specific model
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
- **Regular saves**: Call `EndChat()` before switching characters or closing
- **Backup strategy**: Use backup parameters for important data
- **History limits**: Monitor chat history size and trim if necessary
- **Cache invalidation**: Call `LLMSystem.InvalidatePromptCache()` after significant changes

## Troubleshooting

### Common Issues

#### 1. Character Not Responding In-Character
```csharp
// Check system prompt generation
var generatedPrompt = LLMSystem.SystemPrompt.GetSystemPromptRaw(LLMSystem.Bot);
Console.WriteLine(generatedPrompt);

// Verify persona bio and examples
if (string.IsNullOrEmpty(LLMSystem.Bot.Bio))
{
    Console.WriteLine("Warning: Bot bio is empty");
}

if (LLMSystem.Bot.ExampleDialogs.Count == 0)
{
    Console.WriteLine("Consider adding example dialogs for consistency");
}
```

#### 2. Formatting Issues
```csharp
// Test message formatting
var testMessage = LLMSystem.Instruct.FormatSinglePrompt(
    AuthorRole.User, LLMSystem.User, LLMSystem.Bot, "Hello");
Console.WriteLine($"Formatted message: '{testMessage}'");

// Check for missing delimiters
if (string.IsNullOrEmpty(LLMSystem.Instruct.BotStart))
{
    Console.WriteLine("Warning: BotStart delimiter is empty");
}
```

#### 3. Macro Issues
```csharp
// Test macro replacement
string template = "Hello {{user}}, I'm {{char}}!";
string processed = LLMSystem.ReplaceMacros(template);
Console.WriteLine($"Processed: {processed}");

// Verify personas are set
if (LLMSystem.Bot == null || LLMSystem.User == null)
{
    Console.WriteLine("Error: Bot or User persona not set");
}
```

#### 4. File I/O Problems
```csharp
try
{
    LLMSystem.Bot.LoadChatHistory("data/chars/");
}
catch (Exception ex)
{
    Console.WriteLine($"Failed to load chat history: {ex.Message}");
    
    // Create directory if it doesn't exist
    Directory.CreateDirectory("data/chars/");
    
    // Start with fresh history
    LLMSystem.Bot.History = new Chatlog();
}
```

#### 5. Memory and Token Issues
```csharp
// Monitor system prompt size
LLMSystem.OnFullPromptReady += (sender, prompt) =>
{
    int tokens = LLMSystem.GetTokenCount(prompt);
    Console.WriteLine($"Total prompt tokens: {tokens}");
    
    if (tokens > LLMSystem.MaxContextLength * 0.8)
    {
        Console.WriteLine("Warning: Prompt approaching context limit");
    }
};

// Limit self-edit tokens for smaller models
if (LLMSystem.MaxContextLength < 4000)
{
    LLMSystem.Bot.SelfEditTokens = 50; // Reduce for smaller models
}
```

This comprehensive documentation covers the essential aspects of using BasePersona, InstructFormat, and SystemPrompt classes in AIToolkit. These classes provide the foundation for creating rich, interactive AI characters with proper prompt formatting for various language models.

## Additional Resources

For more information about AIToolkit, see:
- [LLMSystem Documentation](LLMSYSTEM.md) - Core system functionality
- [Extensibility Guide](EXTENSIBILITY.md) - Advanced customization patterns
- [Agent System Documentation](AGENT_SYSTEM.md) - AI agent capabilities

The combination of these three classes enables developers to build sophisticated AI chat applications with minimal code while maintaining full control over character behavior and prompt formatting.

This comprehensive documentation covers the essential aspects of using BasePersona, InstructFormat, and SystemPrompt classes in AIToolkit. These classes provide the foundation for creating rich, interactive AI characters with proper prompt formatting for various language models.