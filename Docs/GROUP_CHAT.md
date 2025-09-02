# Group Chat Implementation Guide

## Overview

AIToolkit now supports group chat functionality through the `GroupPersona` class, allowing one user to interact with multiple bot personas in a single conversation. This extends the existing 1:1 conversation system while maintaining full backward compatibility.

## Key Features

- **Multi-Bot Conversations**: Chat with multiple bot personas simultaneously
- **Dynamic Bot Selection**: Automatically or manually select which bot responds
- **Group-Specific Macros**: New macros for handling multiple characters
- **Enhanced System Prompts**: Automatic generation of group-aware prompts
- **Backward Compatibility**: All existing 1:1 functionality remains unchanged

## Quick Start

### 1. Create Bot Personas

```csharp
// Create individual bot personas
var einstein = new BasePersona
{
    IsUser = false,
    Name = "Einstein",
    UniqueName = "einstein_bot",
    Bio = "A brilliant physicist with a love for thought experiments.",
    Scenario = "You are in Einstein's study, discussing physics."
};

var darwin = new BasePersona
{
    IsUser = false,
    Name = "Darwin", 
    UniqueName = "darwin_bot",
    Bio = "A naturalist passionate about evolution.",
    Scenario = "You are aboard the HMS Beagle, exploring natural history."
};

var user = new BasePersona
{
    IsUser = true,
    Name = "Student",
    UniqueName = "student_user",
    Bio = "A curious student eager to learn."
};
```

### 2. Load Personas and Create Group

```csharp
// Load personas into the system
LLMSystem.LoadPersona([user, einstein, darwin]);

// Create group persona
var scienceGroup = LLMSystem.CreateGroupPersona(
    ["einstein_bot", "darwin_bot"],
    "Science Discussion Group",
    "Choose which scientist should respond based on the topic. Einstein for physics, Darwin for biology."
);

// Set as current bot
LLMSystem.User = user;
LLMSystem.Bot = scienceGroup;
```

### 3. Send Messages to Group

```csharp
// Send message to group (auto-selects responding bot)
await LLMSystem.SendMessageToGroupBot(AuthorRole.User, "What do you think about the relationship between physics and biology?");

// Send message to specific bot
await LLMSystem.SendMessageToGroupBot(AuthorRole.User, "Can you explain relativity?", "einstein_bot");

// Check which bot is currently active
Console.WriteLine($"Active bot: {LLMSystem.CurrentGroup?.ActiveBot?.Name}");
```

## Group-Specific Macros

The group chat system introduces new macros while preserving existing ones:

### New Group Macros

- `{{groupchars}}` - Comma-separated list of all bot names in the group
- `{{groupbios}}` - Combined biographies of all bots in the group
- `{{groupexamples}}` - Combined dialog examples from all bots
- `{{groupscenario}}` - Combined scenarios from all bots

### Traditional Macros in Group Context

- `{{char}}` - Name of the currently active bot (or group name if no active bot)
- `{{charbio}}` - Biography of the currently active bot (or group bio)
- `{{user}}`, `{{userbio}}` - User information (unchanged)
- `{{examples}}`, `{{scenario}}` - From active bot or combined group content

### Example Usage

```csharp
var template = @"
You are part of {{groupchars}} discussing with {{user}}.

Active responder: {{char}}
{{charbio}}

All participants:
{{groupbios}}

Current scenario: {{scenario}}
";

var prompt = LLMSystem.ReplaceGroupMacros(template, user, scienceGroup);
```

## GroupPersona Class Reference

### Key Properties

```csharp
public class GroupPersona : BasePersona
{
    // List of bot persona IDs in the group
    public List<string> BotPersonaIds { get; set; }
    
    // Currently active bot for responses
    public string? ActiveBotId { get; set; }
    
    // Auto-select responder based on context
    public bool AutoSelectResponder { get; set; }
    
    // Instructions for managing group conversation
    public string GroupInstructions { get; set; }
    
    // Computed properties
    public List<BasePersona> BotPersonas { get; } // Loaded bot personas
    public BasePersona? ActiveBot { get; }        // Currently active bot
}
```

### Key Methods

```csharp
// Manage group membership
bool AddBot(string botId)
bool RemoveBot(string botId)
bool SetActiveBot(string botId)

// Override base functionality for group context
string GetBio(string othername)
string GetScenario(string othername)
string GetDialogExamples(string othername)
string GetWelcomeLine(string othername)
```

## System Integration

### Automatic Detection

The system automatically detects when using a `GroupPersona` and adjusts behavior:

```csharp
// This automatically uses group macro replacement
string result = LLMSystem.ReplaceMacros(input, user, groupPersona);

// This detects group context
bool isGroup = LLMSystem.IsGroupChat;
GroupPersona? group = LLMSystem.CurrentGroup;
```

### Message Logging

Messages are logged with specific bot IDs even in group context:

```csharp
// Log message from specific bot in group
History.LogMessage(AuthorRole.Assistant, response, user.UniqueName, "einstein_bot");

// The SingleMessage automatically resolves personas
var message = History.LastMessage();
Console.WriteLine($"Response from: {message.Bot.Name}"); // "Einstein"
```

### System Prompts

System prompts automatically include group information:

```csharp
// Automatically generates group-aware system prompt
var systemPrompt = LLMSystem.SystemPrompt.GetSystemPromptRaw(groupPersona);

// Includes sections like:
// # Group Characters
// {{groupbios}}
```

## Advanced Usage

### Custom Group Instructions

```csharp
var group = new GroupPersona
{
    Name = "Expert Panel",
    GroupInstructions = @"
        This is a panel discussion. Rules:
        - Einstein should answer physics questions
        - Darwin should answer biology questions  
        - If the topic overlaps, both can contribute
        - Always identify yourself when responding
        - Build on each other's answers when appropriate
    "
};
```

### Dynamic Bot Management

```csharp
// Add bots dynamically
group.AddBot("newton_bot");
group.AddBot("curie_bot");

// Change active responder
group.SetActiveBot("curie_bot");

// Check group composition
Console.WriteLine($"Group has {group.BotPersonas.Count} members");
foreach (var bot in group.BotPersonas)
{
    Console.WriteLine($"- {bot.Name}: {bot.Bio}");
}
```

### Integration with Existing Features

Group chat works seamlessly with existing features:

```csharp
// Works with existing event handlers
LLMSystem.OnInferenceEnded += (sender, response) => {
    // Response from group chat
    var activeBot = LLMSystem.CurrentGroup?.ActiveBot?.Name ?? "Unknown";
    Console.WriteLine($"Response from {activeBot}: {response}");
};

// Works with existing chat history
var tokenCount = LLMSystem.History.GetCurrentChatSessionInfo();

// Works with existing persona loading
LLMSystem.LoadPersona(allPersonas);
```

## Best Practices

### 1. Clear Bot Roles

Define clear specializations for each bot:

```csharp
var mathBot = new BasePersona { 
    Name = "MathBot", 
    Bio = "Expert in mathematics and calculations",
    Scenario = "Solve mathematical problems and explain concepts"
};

var historyBot = new BasePersona {
    Name = "HistoryBot",
    Bio = "Knowledgeable about historical events and figures", 
    Scenario = "Discuss historical context and events"
};
```

### 2. Meaningful Group Instructions

Provide clear guidance for bot selection:

```csharp
var group = new GroupPersona
{
    GroupInstructions = @"
        Academic Discussion Group:
        - MathBot: Handle calculations, equations, mathematical theory
        - HistoryBot: Provide historical context and timeline information
        - Both can collaborate on topics like history of mathematics
        - Always state your name when responding
    "
};
```

### 3. Manage Active Bot Selection

```csharp
// Set appropriate active bot based on context
if (userMessage.Contains("equation") || userMessage.Contains("calculate"))
{
    group.SetActiveBot("math_bot");
}
else if (userMessage.Contains("history") || userMessage.Contains("when"))
{
    group.SetActiveBot("history_bot");
}
```

## Migration from 1:1 Chat

Existing code continues to work unchanged:

```csharp
// This still works exactly as before
LLMSystem.User = userPersona;
LLMSystem.Bot = singleBotPersona;
await LLMSystem.SendMessageToBot(AuthorRole.User, "Hello!");

// Group chat is purely additive
LLMSystem.Bot = groupPersona; // Now it's a group chat
await LLMSystem.SendMessageToGroupBot(AuthorRole.User, "Hello everyone!");
```

## Troubleshooting

### Common Issues

1. **Bot not found in group**: Ensure bot is loaded via `LLMSystem.LoadPersona()`
2. **Macros not replacing**: Verify bot IDs match exactly between `LoadPersona()` and `BotPersonaIds`
3. **No active bot**: Set `ActiveBotId` or call `SetActiveBot()`

### Debugging

```csharp
// Check loaded personas
foreach (var persona in LLMSystem.LoadedPersonas)
{
    Console.WriteLine($"Loaded: {persona.Key} = {persona.Value.Name}");
}

// Check group composition
if (LLMSystem.IsGroupChat)
{
    var group = LLMSystem.CurrentGroup!;
    Console.WriteLine($"Group: {group.Name}");
    Console.WriteLine($"Bots: {string.Join(", ", group.BotPersonas.Select(b => b.Name))}");
    Console.WriteLine($"Active: {group.ActiveBot?.Name ?? "None"}");
}
```

## Limitations

1. **Build Dependencies**: Current implementation requires resolving HNSW package dependencies for full compilation
2. **Performance**: Group prompts may be longer due to multiple persona information
3. **Context Length**: More personas mean more tokens used in system prompts

## Future Enhancements

Potential areas for expansion:
- Automatic bot selection based on conversation analysis
- Bot-to-bot conversation capabilities
- Group memory and shared knowledge
- Role-based permissions and capabilities
- Integration with voice synthesis for multi-voice output