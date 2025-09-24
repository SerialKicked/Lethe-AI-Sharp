# Persona Development Guide

This document explains how to create and customize personas using `BasePersona` in the LetheAISharp library, and how to extend it for application-specific behaviors. It complements `LLMSYSTEM.md` and `QUICKSTART.md`.

## Overview

A persona represents either the user or the bot (NPC/agent) in a conversation. The `BasePersona` class provides the foundation for creating customizable characters with:

- **Identity**: Name, biography, scenario descriptions
- **Behavior**: Example dialogs, first messages, system prompt overrides
- **Runtime Systems**: Memory (`Brain`), chat history (`Chatlog`), background agent tasks
- **Context Integration**: World information and plugin management
- **Self-Evolution**: Automatic self-editing field that evolves based on chat history

`BasePersona` is designed to be extended - override its virtual methods to add persistence, custom behaviors, new mechanics, or application-specific features.

## Core Properties

| Property | Purpose |
|----------|---------|
| `IsUser` | Distinguishes user vs bot personas (affects macro replacement) |
| `Name`, `Bio`, `Scenario` | Core identity and narrative context |
| `FirstMessage` | List of possible greeting messages (randomly selected) |
| `ExampleDialogs` | Style guidance for consistent character voice |
| `SystemPrompt` | Optional per-persona system prompt override |
| `Worlds` | IDs of WorldInfo entries to load for this persona |
| `Plugins` | IDs of context plugins to enable automatically |
| `AgentMode`, `AgentTasks` | Enable autonomous background behavior |
| `SelfEditTokens`, `SelfEditField` | Auto-evolving internal thoughts |
| `SenseOfTime`, `DatesInSessionSummaries` | Temporal awareness settings |

## Runtime Objects (Read-Only)

| Property | Purpose |
|----------|---------|
| `Brain` | Memory management and RAG retrieval |
| `History` | Chat session history (`Chatlog`) |
| `MyWorlds` | Loaded WorldInfo entries (it's on the app to load those, if they are needed) |
| `AgentSystem` | Background agent runtime (when enabled) |

## Essential Lifecycle Management

**Critical**: `BasePersona.BeginChat()` and `BasePersona.EndChat()` handles essential initialization and cleanup when switching persona or closing the application. Setting the `LLMEngine.Bot` property to persona will make those calls automatically using `EndChat()` on the previous one, if any, and using `BeginChat()` on the new one. However, the library doesn't intercepts the application being closed, so, if you want to make sure that the chatlog, and other information is being correctly saved, you *must* manually call `EndChat()` on the currently loaded persona before exiting.


```csharp
var persona = new BasePersona
{
    Name = "Assistant",
    Bio = "A helpful AI assistant",
    // ... configure properties
};

// Automatically calls BeginChat() at the library's level.
LLMEngine.Bot = persona;
```

### EndChat() - Required when closing the application
```csharp
// REQUIRED: Cleanup when switching personas or closing app
persona.EndChat(backup: true); // backup = true saves .bak files
```

**What these methods do:**
- `BeginChat()`: Loads brain/memory, initializes agent system, loads chat history, sets up plugins
- `EndChat()`: Saves chat history, saves brain/memory, shuts down agent system, creates backups

## Basic Usage Example

```csharp
using LetheAISharp.Files;
using LetheAISharp.LLM;

// Create a basic bot persona
var bot = new BasePersona
{
    Name = "Alice",
    Bio = "A knowledgeable research assistant with expertise in science",
    IsUser = false,
    Scenario = "You are Alice, helping with research questions",
    FirstMessage = new List<string> 
    { 
        "Hello! I'm Alice. What would you like to research today?",
        "Hi there! Ready to dive into some interesting topics?"
    },
    ExampleDialogs = new List<string>
    {
        "Alice: *thoughtfully* That's a fascinating question about quantum mechanics...",
        "Alice: Let me break this down into simpler terms for you."
    }
};

// Set and init bot
LLMEngine.Bot = bot;

// Use in conversation...
var welcome = bot.GetWelcomeLine("User");
Console.WriteLine($"Bot: {welcome}");

// REQUIRED: Cleanup when done before exiting app 
bot.EndChat(backup: true);
```

## Extending BasePersona

Override virtual methods to add custom functionality:

### Basic Extension Example

```csharp
public class GamePersona : BasePersona
{
    // Custom properties
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public string CharacterClass { get; set; } = "Warrior";

    public override void BeginChat()
    {
        // ALWAYS call base first - essential initialization
        base.BeginChat();
        
        // Your custom initialization here
        Console.WriteLine($"{Name} (Level {Level} {CharacterClass}) enters the game!");
        
        // Skip custom logic for user personas if needed
        if (IsUser) return;
        
        // Bot-specific initialization
        LoadCustomGameData();
    }

    public override void EndChat(bool backup = false)
    {
        // Save your custom state BEFORE calling base
        SaveExperiencePoints();
        
        // ALWAYS call base - essential cleanup
        base.EndChat(backup);
        
        Console.WriteLine($"{Name} leaves the game.");
    }
    
    private void LoadCustomGameData()
    {
        // Load game-specific data
    }
    
    private void SaveExperiencePoints()
    {
        // Persist XP and level changes
    }
}
```

### Advanced Extension with Custom Storage

```csharp
public class DatabasePersona : BasePersona
{
    public string DatabaseId { get; set; } = string.Empty;
    
    // Override chat history storage to use database
    public override void SaveChatHistory(bool backup = false)
    {
        if (string.IsNullOrEmpty(DatabaseId)) 
        {
            base.SaveChatHistory(backup); // Fallback to file storage
            return;
        }
        
        // Save to database instead
        SaveChatToDatabase(History, DatabaseId);
    }
    
    public override void LoadChatHistory()
    {
        if (string.IsNullOrEmpty(DatabaseId))
        {
            base.LoadChatHistory(); // Fallback to file storage
            return;
        }
        
        // Load from database
        History = LoadChatFromDatabase(DatabaseId) ?? CreateChatlog();
    }
    
    private void SaveChatToDatabase(Chatlog chatlog, string id)
    {
        // Your database save logic
    }
    
    private Chatlog? LoadChatFromDatabase(string id)
    {
        // Your database load logic
        return null;
    }
}
```

## Custom Macros

Add new macros by overriding `ReplaceMacrosInternal`:

```csharp
public class RPGPersona : BasePersona
{
    public string Guild { get; set; } = "Adventurers Guild";
    public string CurrentLocation { get; set; } = "Town Square";

    protected override string ReplaceMacrosInternal(string inputText, string userName, string userBio)
    {
        // Process base macros first
        var result = base.ReplaceMacrosInternal(inputText, userName, userBio);
        
        // Add your custom macros
        result = result.Replace("{{guild}}", Guild);
        result = result.Replace("{{location}}", CurrentLocation);
        
        return result;
    }
}
```

Use in bio or scenario: `{{char}} of the {{guild}} stands in the {{location}}, ready to help {{user}}.`

## Available Macros

Standard macros available in bios, scenarios, and system prompts:

### Character Macros
- `{{char}}` - Bot character name
- `{{charbio}}` - Bot character biography  
- `{{user}}` - User name
- `{{userbio}}` - User biography
- `{{examples}}` - Character's example dialogs
- `{{scenario}}` - Current scenario
- `{{selfedit}}` - Character's self-editing field

### Time Macros
- `{{date}}` - Current date in human-readable format
- `{{time}}` - Current time (e.g., "02:30 PM")
- `{{day}}` - Current day of week

## Factory Methods for Custom Types

Override factory methods to use custom `Chatlog` or `ChatSession` subclasses:

```csharp
public class MetadataPersona : BasePersona
{
    protected override Chatlog CreateChatlog()
    {
        return new MetadataChatlog(); // Your custom chatlog class
    }
    
    protected override ChatSession CreateChatSession()
    {
        return new MetadataChatSession(); // Your custom session class
    }
}

public class MetadataChatlog : Chatlog
{
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class MetadataChatSession : ChatSession
{
    public string SessionTheme { get; set; } = "";
    
    protected override async Task<string> GenerateSummary()
    {
        var baseSummary = await base.GenerateSummary();
        return baseSummary + $"\n[Theme: {SessionTheme}]";
    }
}
```

## Plugin and World Integration

Control which plugins and world information are active:

```csharp
public override void BeginChat()
{
    base.BeginChat();
    
    if (IsUser) return; // Skip for user personas
    
    // Enable only specified plugins for this character
    foreach (var plugin in LLMEngine.ContextPlugins)
    {
        plugin.Enabled = Plugins.Contains(plugin.PluginID);
    }
    
    // Load world information (requires your world loading system)
    MyWorlds = LoadWorldInfoByIds(Worlds);
    foreach (var world in MyWorlds)
    {
        world.Reset(); // Reset any state
    }
}
```

## Agent Mode and Background Tasks

Enable autonomous behavior. Bots can run tasks in the background while the user is AFK. The library comes with two sample tasks: `ActiveResearchTask` and `ResearchTask`.
The first one will check the current chat sessions for things to search on the internet, while the second one will search in the previous chat session. 

See [AGENTS.md](AGENTS.md) for comprehensive documentation on the agent system, including how to create custom tasks.

```csharp
var autonomousBot = new BasePersona
{
    Name = "ResearchBot",
    Bio = "An autonomous research assistant",
    AgentMode = true, // Enable background agent
    AgentTasks = new List<string> { "ActiveResearchTask", "ResearchTask" }
};

autonomousBot.BeginChat(); // Agent system starts automatically
```

## Self-Editing Field

Enable automatic character evolution:

```csharp
var evolvingBot = new BasePersona
{
    Name = "LearningBot",
    Bio = "A bot that learns and grows",
    SelfEditTokens = 256, // Allow 256 tokens for self-reflection
    SelfEditField = "I am just beginning my journey..." // Initial thoughts
};

// After several chat sessions, SelfEditField will automatically update
// with the character's reflections on important conversations
```

## Persistence Patterns

### File-Based Storage (Default)
By default files are put in the `data/chars/` folder with the following naming conventions. LLMEngine.Settings.DataPath can be changed to customize the base data folder. You can also override most functions to finetune the behavior.

```csharp
// Uses default file storage
// Persona: data/chars/{UniqueName}.json
// Chatlog: data/chars/{UniqueName}.log  
// Brain: data/chars/{UniqueName}.brain
// Agent: data/chars/{UniqueName}.agent
```

### Custom Storage Override
```csharp
protected override void SaveBrain(string path, bool backup = false)
{
    Brain.Close();
    
    // Your custom save logic (database, cloud, encrypted, etc.)
    var brainData = JsonConvert.SerializeObject(Brain, Formatting.Indented);
    SaveToCustomStorage(UniqueName + ".brain", brainData);
}

protected override void LoadBrain(string path)
{
    // Your custom load logic
    var brainData = LoadFromCustomStorage(UniqueName + ".brain");
    if (brainData != null)
    {
        Brain = JsonConvert.DeserializeObject<Brain>(brainData) ?? new Brain(this);
    }
    else
    {
        Brain = new Brain(this);
    }
    Brain.Init(this);
}

public override void LoadChatHistory() => LoadChatHistory("data/chatlogs/");

public override void SaveChatHistory(bool backup = false) => SaveChatHistory("data/chatlogs/", backup);

```

## Best Practices

1. **Always call base methods first** in `BeginChat()` - they handle essential initialization
2. **Always call base methods** in `EndChat()` - they handle essential cleanup and persistence  
3. **Handle IsUser properly** - skip bot-specific logic when `IsUser = true`

## Summary

- `BasePersona` is the foundation for all conversation participants
- **MUST** call `BeginChat()` before use and `EndChat()` when done
- Override virtual methods to add custom behavior
- Use macros for dynamic text replacement
- Enable agent mode for autonomous behavior  
- Leverage the self-editing field for character evolution
- Follow best practices for reliable persona management

For deeper integration with the conversation system, see `LLMSYSTEM.md` for full chat management and `QUICKSTART.md` for basic setup.
