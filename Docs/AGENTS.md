# Agent System Documentation

This document explains the Agent system in LetheAISharp, which enables personas to execute autonomous background tasks while users are away. The system is designed to be easily extensible and allows bots to perform intelligent actions such as research, memory management, and content analysis.

## Overview

The Agent system consists of several key components:

- **AgentRuntime**: Manages the agent lifecycle and task execution
- **IAgentTask**: Interface for creating custom background tasks
- **IAgentAction**: Interface for creating reusable actions that tasks can use
- **AgentTaskSetting**: Configuration system for task parameters
- **Built-in Tasks**: Ready-to-use tasks like `ActiveResearchTask` and `ResearchTask`

The system operates on a simple principle: when a user is inactive for a specified period, the agent system activates and runs configured tasks in the background. These tasks can analyze chat history, perform web searches, update memory, or execute any custom logic.

## Core Architecture

### AgentRuntime

The `AgentRuntime` class is the heart of the agent system. It:

- Monitors user activity and determines when to activate
- Loads and manages registered tasks
- Handles task configuration and persistence
- Provides a plugin registry for custom tasks
- Manages the action system for shared functionality

### Task Lifecycle

Each agent task follows this lifecycle:

1. **Observe**: Check if the task should run (conditions, timing, etc.)
2. **Execute**: Perform the actual task logic if conditions are met
3. **Save**: Persist any configuration changes automatically

Tasks run in sequence, with built-in delays to prevent overwhelming the system.

## Setting Up Agent-Enabled Personas

### Basic Setup

Enable agent mode on any persona by setting `AgentMode = true` and specifying tasks:

```csharp
var researchBot = new BasePersona
{
    Name = "Alice",
    Bio = "An intelligent research assistant",
    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask", "ResearchTask" }
};

// IMPORTANT: Always call BeginChat() to initialize the agent system
researchBot.BeginChat();

// The agent system is now running in the background
// It will activate when the user is inactive for 15+ minutes (default)
```

### Advanced Configuration

You can configure agent behavior through the `AgentConfig` class:

```csharp
var bot = new BasePersona
{
    Name = "ResearchBot",
    Bio = "Advanced autonomous research assistant",
    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask", "ResearchTask", "MyCustomTask" }
};

bot.BeginChat();

// Customize inactivity threshold (default is 15 minutes)
bot.AgentSystem.Config.MinInactivityTime = TimeSpan.FromMinutes(5);

// Task-specific settings are managed automatically but can be accessed
var activeResearchConfig = bot.AgentSystem.Config.PluginSettings["ActiveResearchTask"];
activeResearchConfig.SetSetting("MinMessages", 3); // Require 3+ new messages before researching
```

### User Activity Tracking

The agent system needs to know when users are active to avoid interrupting conversations:

```csharp
// Call this whenever the user sends a message or interacts with the system
bot.AgentSystem?.NotifyUserActivity();

// The agent will pause background tasks and reset the inactivity timer
```

## Built-in Tasks

### ActiveResearchTask

Monitors the current chat session for topics that might benefit from web research.

**What it does:**
- Analyzes new messages in the active session
- Identifies unfamiliar topics using AI
- Performs web searches on those topics
- Stores research results in the persona's Brain for natural recall

**Configuration Options:**
- `MinMessages`: Minimum new messages required before considering research (default: 2)
- `Delay`: Minimum time between research attempts (default: 30 minutes)
- `UseBio`: Include persona bio in topic analysis (default: true)

**Example setup:**
```csharp
var bot = new BasePersona
{
    Name = "ResearchBot",
    Bio = "Expert in technology and science",
    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask" }
};

bot.BeginChat();

// Customize research behavior
var config = bot.AgentSystem.Config.PluginSettings["ActiveResearchTask"];
config.SetSetting("MinMessages", 5); // Wait for more messages
config.SetSetting("Delay", TimeSpan.FromMinutes(45)); // Research less frequently
```

### ResearchTask

Analyzes completed chat sessions and performs research on topics discussed.

**What it does:**
- Examines the second-to-last chat session (when a new session starts)
- Identifies research-worthy topics from the entire session
- Performs comprehensive web searches
- Stores findings in memory with context about the previous conversation

**Configuration Options:**
- `IncludeBios`: Include persona bios in analysis (default: true)
- `LastSessionGuid`: Tracks which sessions have been processed

**Example setup:**
```csharp
var bot = new BasePersona
{
    Name = "HistoryBot",
    Bio = "Learns from past conversations",
    AgentMode = true,
    AgentTasks = new List<string> { "ResearchTask" }
};

bot.BeginChat();

// This task runs automatically when starting new chat sessions
// It will research topics from the previous session
```

### Using Both Tasks Together

For comprehensive coverage, use both tasks together:

```csharp
var comprehensiveBot = new BasePersona
{
    Name = "SmartBot",
    Bio = "AI assistant with continuous learning capabilities",
    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask", "ResearchTask" }
};

comprehensiveBot.BeginChat();

// ActiveResearchTask handles ongoing conversations
// ResearchTask handles retrospective analysis
// Together they provide comprehensive knowledge gathering
```

## Creating Custom Tasks

### Implementing IAgentTask

Create custom tasks by implementing the `IAgentTask` interface:

```csharp
using LetheAISharp.Agent;
using LetheAISharp.Files;

public class CustomAnalysisTask : IAgentTask
{
    public string Id => "CustomAnalysisTask";

    public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
    {
        // Check if task should run
        var lastRun = cfg.GetSetting<DateTime>("LastRun");
        var interval = cfg.GetSetting<TimeSpan>("RunInterval", TimeSpan.FromHours(1));
        
        if (DateTime.Now - lastRun < interval)
            return false;
            
        // Additional conditions...
        var messageCount = owner.History.CurrentSession.Messages.Count;
        return messageCount >= 5; // Only run if enough messages
    }

    public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
    {
        try
        {
            // Your task logic here
            await AnalyzeChatPatterns(owner, ct);
            
            // Update configuration
            cfg.SetSetting("LastRun", DateTime.Now);
            
            // Add memory if needed
            owner.Brain.AddUserReturnInsert("{{char}} has analyzed recent conversation patterns.");
        }
        catch (Exception ex)
        {
            // Handle errors gracefully
            LLMEngine.Logger?.LogError(ex, "Error in CustomAnalysisTask");
        }
    }

    public AgentTaskSetting GetDefaultSettings()
    {
        var settings = new AgentTaskSetting
        {
            PluginId = Id
        };
        
        settings.SetSetting("RunInterval", TimeSpan.FromHours(1));
        settings.SetSetting("LastRun", DateTime.MinValue);
        settings.SetSetting("AnalysisDepth", 5);
        
        return settings;
    }

    private async Task AnalyzeChatPatterns(BasePersona owner, CancellationToken ct)
    {
        // Implement your analysis logic
        var messages = owner.History.CurrentSession.Messages.TakeLast(10);
        
        // Use LLM actions, web search, or custom logic
        // Store results in owner.Brain for natural recall
    }
}
```

### Task Registration

Register your custom task before using it:

```csharp
// Option 1: Register a singleton instance
AgentRuntime.RegisterPlugin("CustomAnalysisTask", new CustomAnalysisTask());

// Option 2: Register a factory method (recommended for stateful tasks)
AgentRuntime.RegisterPlugin("CustomAnalysisTask", () => new CustomAnalysisTask());

// Now you can use it in personas
var bot = new BasePersona
{
    Name = "AnalyzerBot",
    AgentMode = true,
    AgentTasks = new List<string> { "CustomAnalysisTask" }
};
```

### Task Configuration

Tasks can be configured through their settings:

```csharp
bot.BeginChat();

// Access and modify task settings
var taskConfig = bot.AgentSystem.Config.PluginSettings["CustomAnalysisTask"];
taskConfig.SetSetting("AnalysisDepth", 10);
taskConfig.SetSetting("RunInterval", TimeSpan.FromMinutes(30));

// Settings are automatically saved when the task runs
```

## Action System

The action system provides reusable functionality that tasks can share.

### Built-in Actions

Several actions are available for common operations:

- **WebSearchAction**: Performs web searches and returns structured results
- **FindResearchTopicsAction**: Uses AI to identify research topics from text
- **MergeSearchResultsAction**: Combines search results into coherent summaries
- **SessionAnalysisAction**: Analyzes chat sessions for patterns

### Using Actions in Tasks

```csharp
public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
{
    // Get a web search action
    var webSearch = AgentRuntime.GetAction<List<EnrichedSearchResult>, TopicSearch>("WebSearchAction");
    
    if (webSearch != null)
    {
        var searchParams = new TopicSearch 
        { 
            Query = "artificial intelligence trends",
            MaxResults = 5 
        };
        
        var results = await webSearch.Execute(searchParams, ct);
        
        // Process results...
        foreach (var result in results)
        {
            // Store in memory or use as needed
            var memory = new BrainMemory(result.Content, MemoryImportance.Medium, "web_research");
            await memory.EmbedText();
            owner.Brain.Memorize(memory);
        }
    }
}
```

### Creating Custom Actions

Implement `IAgentAction<TResult, TParam>` for reusable functionality:

```csharp
public class SentimentAnalysisAction : IAgentAction<SentimentResult, SentimentParams>
{
    public string Id => "SentimentAnalysisAction";
    
    public HashSet<AgentActionRequirements> Requirements => 
        new() { AgentActionRequirements.LLM };

    public async Task<SentimentResult> Execute(SentimentParams param, CancellationToken ct)
    {
        // Implement sentiment analysis logic
        // Use LLMEngine for AI processing
        var prompt = $"Analyze the sentiment of: {param.Text}";
        // ... implementation
        
        return new SentimentResult { Sentiment = "positive", Confidence = 0.85 };
    }
}

// Register the action
AgentRuntime.RegisterAction(new SentimentAnalysisAction());

// Use in tasks
var sentimentAction = AgentRuntime.GetAction<SentimentResult, SentimentParams>("SentimentAnalysisAction");
```

## Memory Integration

Agent tasks commonly store information in the persona's Brain for later recall:

### Storing Research Results

```csharp
// Create a memory with research findings
var memory = new BrainMemory(
    content: "Recent AI developments show increased focus on multimodal models...",
    importance: MemoryImportance.High,
    memoryType: "research_finding"
)
{
    Keywords = new List<string> { "AI", "multimodal", "technology trends" },
    Source = "web_research",
    CreatedAt = DateTime.Now
};

// Embed the text for semantic search
await memory.EmbedText();

// Store in the persona's brain
owner.Brain.Memorize(memory);
```

### Adding User Return Messages

Inform the user about background activities:

```csharp
// Add a message that will be shown when the user returns
owner.Brain.AddUserReturnInsert("{{char}} has researched recent developments in AI technology.");

// Multiple messages can be queued
owner.Brain.AddUserReturnInsert("{{char}} found some interesting papers on machine learning.");
```

### Memory Querying

Tasks can query existing memories:

```csharp
// Search for related memories
var relatedMemories = owner.Brain.QueryMemories("artificial intelligence", maxResults: 5);

// Check if topic was recently researched
var recentResearch = owner.Brain.GetMemoriesByType("web_research")
    .Where(m => m.CreatedAt > DateTime.Now.AddDays(-7))
    .Any(m => m.Content.Contains("neural networks"));
```

## Configuration and Persistence

### Task Settings

Tasks automatically persist their settings:

```csharp
public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
{
    // Read settings
    var threshold = cfg.GetSetting<double>("ConfidenceThreshold", 0.8);
    var lastRun = cfg.GetSetting<DateTime>("LastRun");
    
    // Your task logic...
    
    // Update settings (automatically saved)
    cfg.SetSetting("LastRun", DateTime.Now);
    cfg.SetSetting("ProcessedCount", cfg.GetSetting<int>("ProcessedCount") + 1);
}
```

### Agent Configuration Files

Agent configurations are saved as `.agent` files in the data directory:

```
data/
  YourPersonaName.agent  # Agent configuration
  YourPersonaName.log    # Chat history
  YourPersonaName.brain  # Brain/memory data
```

The agent file contains:
- Plugin settings for each task
- Inactivity timeouts
- Custom configuration parameters

## Best Practices

### Task Design

1. **Keep tasks focused**: Each task should have a single, clear purpose
2. **Handle failures gracefully**: Use try-catch blocks and log errors
3. **Respect system resources**: Add delays between operations
4. **Check prerequisites**: Verify LLM status, web search availability, etc.

```csharp
public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
{
    // Always check system status
    if (LLMEngine.Status != SystemStatus.Ready)
        return false;
        
    // Check specific requirements
    if (!LLMEngine.SupportsWebSearch && RequiresWebSearch)
        return false;
        
    // Check rate limiting
    var lastRun = cfg.GetSetting<DateTime>("LastRun");
    if (DateTime.Now - lastRun < MinimumInterval)
        return false;
        
    return true;
}
```

### Memory Management

1. **Set appropriate importance levels**: Use `MemoryImportance.High` sparingly
2. **Add relevant keywords**: Help with memory retrieval
3. **Include source information**: Track where information came from
4. **Clean up old memories**: Implement archival logic for outdated information

```csharp
var memory = new BrainMemory(content, MemoryImportance.Medium, "task_result")
{
    Keywords = ExtractKeywords(content),
    Source = $"{Id}_{DateTime.Now:yyyyMMdd}",
    ExpirationDate = DateTime.Now.AddMonths(6) // Optional expiration
};
```

### User Experience

1. **Inform users of activities**: Use `AddUserReturnInsert()` appropriately
2. **Don't overwhelm**: Limit the number of return messages
3. **Be specific**: Describe what the agent accomplished

```csharp
// Good: Specific and useful
owner.Brain.AddUserReturnInsert("{{char}} researched quantum computing advances while you were away.");

// Bad: Vague and not helpful
owner.Brain.AddUserReturnInsert("{{char}} did some work.");
```

### Error Handling

1. **Log errors with context**: Include task ID and relevant parameters
2. **Fail gracefully**: Don't crash the entire agent system
3. **Implement retry logic**: For transient failures

```csharp
public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
{
    try
    {
        await YourTaskLogic(owner, cfg, ct);
    }
    catch (OperationCanceledException)
    {
        // Expected during shutdown
        throw;
    }
    catch (Exception ex)
    {
        LLMEngine.Logger?.LogError(ex, "Error in {TaskId}: {Message}", Id, ex.Message);
        // Don't rethrow - let other tasks continue
    }
}
```

## Troubleshooting

### Common Issues

**Agent tasks not running:**
- Check that `AgentMode = true` on the persona
- Verify `BeginChat()` was called
- Ensure user inactivity period has passed (default 15 minutes)
- Check `LLMEngine.Status == SystemStatus.Ready`

**Tasks failing silently:**
- Enable detailed logging to see error messages
- Check that required actions are registered
- Verify system requirements (web search, grammar support, etc.)

**Configuration not persisting:**
- Ensure `UniqueName` is set on the persona
- Check write permissions to the data directory
- Verify `EndChat()` is called when switching personas

### Debugging

Enable verbose logging to troubleshoot issues:

```csharp
// In your application startup
LLMEngine.Logger = yourLoggerInstance;

// Task execution will be logged with details about:
// - When tasks are considered for execution
// - Why tasks are skipped
// - Errors during execution
// - Configuration changes
```

### Manual Task Testing

Test tasks independently:

```csharp
var task = new CustomAnalysisTask();
var testPersona = new BasePersona { /* ... */ };
var testConfig = task.GetDefaultSettings();

// Test observation logic
var shouldRun = await task.Observe(testPersona, testConfig, CancellationToken.None);

// Test execution if conditions are met
if (shouldRun)
{
    await task.Execute(testPersona, testConfig, CancellationToken.None);
}
```

## Example: Complete Custom Task

Here's a complete example of a custom task that analyzes conversation mood:

```csharp
using LetheAISharp.Agent;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.Memory;

public class MoodAnalysisTask : IAgentTask
{
    public string Id => "MoodAnalysisTask";

    public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
    {
        // Basic system checks
        if (LLMEngine.Status != SystemStatus.Ready || !LLMEngine.SupportsGrammar)
            return false;

        // Check timing
        var lastRun = cfg.GetSetting<DateTime>("LastAnalysis");
        var interval = cfg.GetSetting<TimeSpan>("AnalysisInterval", TimeSpan.FromHours(2));
        
        if (DateTime.Now - lastRun < interval)
            return false;

        // Check if enough new content
        var session = owner.History.CurrentSession;
        var minMessages = cfg.GetSetting<int>("MinimumMessages", 10);
        
        return session.Messages.Count >= minMessages;
    }

    public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
    {
        try
        {
            var session = owner.History.CurrentSession;
            var recentMessages = session.Messages.TakeLast(20).ToList();
            
            // Analyze mood using LLM
            var moodAnalysis = await AnalyzeMood(recentMessages, ct);
            
            // Store analysis in memory
            var memory = new BrainMemory(
                content: $"Conversation mood analysis: {moodAnalysis.Summary}. " +
                        $"Overall tone: {moodAnalysis.Tone}. Engagement level: {moodAnalysis.Engagement}.",
                importance: MemoryImportance.Low,
                memoryType: "mood_analysis"
            )
            {
                Keywords = new List<string> { "mood", "analysis", moodAnalysis.Tone },
                Source = "mood_analysis_task"
            };
            
            await memory.EmbedText();
            owner.Brain.Memorize(memory);
            
            // Update configuration
            cfg.SetSetting("LastAnalysis", DateTime.Now);
            cfg.SetSetting("AnalysisCount", cfg.GetSetting<int>("AnalysisCount") + 1);
            
            // Notify user if mood shift detected
            if (moodAnalysis.IsSignificantChange)
            {
                owner.Brain.AddUserReturnInsert(
                    $"{{char}} noticed the conversation mood shifted to {moodAnalysis.Tone}."
                );
            }
        }
        catch (Exception ex)
        {
            LLMEngine.Logger?.LogError(ex, "Error in MoodAnalysisTask: {Message}", ex.Message);
        }
    }

    public AgentTaskSetting GetDefaultSettings()
    {
        var settings = new AgentTaskSetting { PluginId = Id };
        
        settings.SetSetting("AnalysisInterval", TimeSpan.FromHours(2));
        settings.SetSetting("MinimumMessages", 10);
        settings.SetSetting("LastAnalysis", DateTime.MinValue);
        settings.SetSetting("AnalysisCount", 0);
        
        return settings;
    }

    private async Task<MoodAnalysisResult> AnalyzeMood(List<ChatMessage> messages, CancellationToken ct)
    {
        // Implement mood analysis using LLM
        var messageText = string.Join("\n", messages.Select(m => $"{m.Name}: {m.Message}"));
        
        var prompt = $"""
            Analyze the mood and tone of this conversation:
            
            {messageText}
            
            Provide analysis in this format:
            Tone: [positive/negative/neutral/mixed]
            Engagement: [high/medium/low]
            Summary: [brief description]
            SignificantChange: [true/false]
            """;
            
        // Use LLMEngine to get analysis
        var response = await LLMEngine.GenerateResponse(prompt, ct);
        
        // Parse response and return structured result
        return ParseMoodResponse(response);
    }

    private MoodAnalysisResult ParseMoodResponse(string response)
    {
        // Implementation depends on your parsing needs
        return new MoodAnalysisResult
        {
            Tone = "positive",
            Engagement = "high", 
            Summary = "Friendly and engaging conversation",
            IsSignificantChange = false
        };
    }
}

public class MoodAnalysisResult
{
    public string Tone { get; set; } = "";
    public string Engagement { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool IsSignificantChange { get; set; }
}

// Register and use the task
AgentRuntime.RegisterPlugin("MoodAnalysisTask", () => new MoodAnalysisTask());

var emotionalBot = new BasePersona
{
    Name = "EmotionalAI",
    Bio = "An emotionally aware AI assistant",
    AgentMode = true,
    AgentTasks = new List<string> { "MoodAnalysisTask" }
};

emotionalBot.BeginChat();
```

This example demonstrates all the key concepts: observation logic, execution with LLM integration, memory storage, configuration management, and error handling.

## Summary

The Agent system in LetheAISharp provides a powerful framework for creating autonomous, intelligent personas that can operate in the background. Key takeaways:

1. **Easy to enable**: Just set `AgentMode = true` and specify tasks
2. **Extensible**: Create custom tasks by implementing `IAgentTask`
3. **Intelligent**: Built-in tasks provide research and analysis capabilities
4. **Configurable**: Extensive configuration options for fine-tuning behavior
5. **Memory-integrated**: Tasks can store and retrieve information naturally
6. **Robust**: Built-in error handling and persistence

The system is designed to enhance user interactions by having AI assistants that continuously learn and prepare relevant information, creating more engaging and intelligent conversations.