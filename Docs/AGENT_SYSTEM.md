# Agent System Documentation

The Agent System in AIToolkit provides a powerful background task execution framework that can run while the user is not actively interacting with the AI. This system enables autonomous behavior, background research, reflection, and other intelligent tasks.

## Overview

The Agent System consists of:
- **AgentRuntime**: The core runtime that manages plugin lifecycle and task execution
- **IAgentPlugin**: Interface for creating custom agent plugins
- **AgentTask**: Represents individual tasks that plugins can observe and execute
- **AgentConfig**: Configuration for the agent system behavior
- **AgentState**: Persistent state tracking for budgets, tasks, and staged messages

## Architecture

```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   AgentRuntime  │    │  IAgentPlugin   │    │   AgentTask     │
│                 │◄───┤                 │    │                 │
│  - LoadPlugins  │    │  - ObserveAsync │    │  - Type         │
│  - RunLoop      │    │  - ExecuteAsync │    │  - Priority     │
│  - ExecuteTask  │    │  - CanHandle    │    │  - Payload      │
└─────────────────┘    └─────────────────┘    └─────────────────┘
         │                       │
         └───────────────────────┴──────────────────┐
                                                    │
┌─────────────────┐    ┌─────────────────┐    ┌─────▼─────────┐
│   AgentConfig   │    │   AgentState    │    │ Built-in      │
│                 │    │                 │    │ Plugins:      │
│  - Plugins[]    │    │  - Queue        │    │               │
│  - Budgets      │    │  - TokensUsed   │    │ • CoreReflect │
│  - Intervals    │    │  - SearchesUsed │    │ • WebIntel    │
└─────────────────┘    └─────────────────┘    └───────────────┘
```

## Agent Task Lifecycle

1. **Observation Phase**: Plugins examine current context and generate tasks
2. **Task Queuing**: Tasks are queued with priority and deduplication
3. **Task Execution**: Runtime selects highest priority runnable task
4. **Result Processing**: Handle success/failure, budget tracking, new tasks

## Creating Custom Plugins

### Basic Plugin Implementation

```csharp
using AIToolkit.Agent;

public class MyCustomPlugin : IAgentPlugin
{
    public string Id => "MyCustomPlugin";
    
    public IEnumerable<AgentTaskType> Supported => [AgentTaskType.PluginSpecific];

    public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
    {
        var tasks = new List<AgentTask>();
        
        // Check if we should create a task based on current context
        if (ShouldCreateTask(ctx))
        {
            tasks.Add(new AgentTask
            {
                Type = AgentTaskType.PluginSpecific,
                Priority = 3,
                PayloadJson = JsonSerializer.Serialize(new { CustomData = "example" }),
                CorrelationKey = "my-plugin-task",
                RequiresLLM = false
            });
        }
        
        return Task.FromResult<IEnumerable<AgentTask>>(tasks);
    }

    public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
    {
        if (task.Type != AgentTaskType.PluginSpecific)
            return AgentTaskResult.Fail();

        try
        {
            // Perform your custom task logic here
            await DoCustomWork(task, ctx, ct);
            
            return AgentTaskResult.Ok();
        }
        catch
        {
            return AgentTaskResult.Fail();
        }
    }

    private bool ShouldCreateTask(IAgentContext ctx)
    {
        // Implement your logic to determine when to create tasks
        return ctx.IdleTime.TotalMinutes > 5;
    }

    private async Task DoCustomWork(AgentTask task, IAgentContext ctx, CancellationToken ct)
    {
        // Implement your custom task execution logic
        await Task.Delay(100, ct); // Placeholder
    }
}
```

### Registering Custom Plugins

#### Option 1: Register Plugin Instance

```csharp
var myPlugin = new MyCustomPlugin();
AgentRuntime.Instance.RegisterPlugin("MyCustomPlugin", myPlugin);
```

#### Option 2: Register Plugin Factory

```csharp
AgentRuntime.Instance.RegisterPlugin("MyCustomPlugin", () => new MyCustomPlugin());
```

#### Option 3: Configuration-Based Loading

Add your plugin ID to the configuration:

```json
{
  "Enabled": true,
  "Plugins": ["CoreReflection", "WebIntelligence", "MyCustomPlugin"]
}
```

Then register the plugin before starting the agent:

```csharp
AgentRuntime.Instance.RegisterPlugin("MyCustomPlugin", () => new MyCustomPlugin());
AgentRuntime.Instance.Start();
```

## Agent Context

The `IAgentContext` provides plugins with information about the current state:

```csharp
public interface IAgentContext
{
    int SessionCount { get; }           // Number of chat sessions
    string LastUserMessage { get; }     // Content of last user message
    TimeSpan IdleTime { get; }          // Time since last user activity
    DateTime UtcNow { get; }            // Current UTC time
    AgentConfig Config { get; }         // Agent configuration
    AgentState State { get; }           // Current agent state
}
```

## Task Types

The system supports several predefined task types:

- **Observe**: General observation tasks
- **Reflect**: Reflection on conversation progress
- **PlanSearch**: Planning web searches
- **ExecuteSearch**: Executing web searches
- **PersonaUpdate**: Updating AI persona
- **StageMessage**: Staging messages for user
- **EmbedRefresh**: Refreshing embeddings
- **PluginSpecific**: Custom plugin tasks

## Task Priorities

Tasks are executed by priority (lower number = higher priority):
- **0**: Immediate/urgent tasks
- **1**: High priority (e.g., reflection)
- **2**: Medium priority (e.g., web searches)
- **3-5**: Normal priority
- **6+**: Low priority background tasks

## Budget Management

The agent system respects daily budgets:
- **Token Budget**: Limits LLM usage per day
- **Search Budget**: Limits web searches per day

Tasks requiring these resources will be deferred if budget is exhausted.

## Staged Messages

Plugins can stage messages that appear to users when appropriate:

```csharp
var staged = new StagedMessage
{
    TopicKey = "my-plugin-notification",
    Draft = "I completed some background work for you.",
    Rationale = "Detailed explanation of what was done",
    ExpireUtc = DateTime.UtcNow.AddHours(6)
};

return AgentTaskResult.Ok(staged: [staged]);
```

## Best Practices

### Plugin Development

1. **Use appropriate task types**: Choose the most specific task type for your use case
2. **Implement proper error handling**: Always catch exceptions and return appropriate results
3. **Respect budgets**: Check budget constraints before creating expensive tasks
4. **Use correlation keys**: Prevent duplicate tasks with meaningful correlation keys
5. **Keep tasks small**: Break complex work into smaller, manageable tasks

### Resource Management

1. **Check idle time**: Only create tasks when user is idle for sufficient time
2. **Use cancellation tokens**: Respect cancellation for long-running operations
3. **Limit task frequency**: Avoid creating too many tasks in short periods
4. **Clean up resources**: Dispose of any resources properly

### Context Awareness

1. **Check session state**: Adapt behavior based on conversation history
2. **Respect user preferences**: Use configuration to honor user settings
3. **Provide meaningful output**: Stage helpful messages for users

## Integration Examples

### Simple Notification Plugin

```csharp
public class NotificationPlugin : IAgentPlugin
{
    public string Id => "Notification";
    public IEnumerable<AgentTaskType> Supported => [AgentTaskType.StageMessage];

    public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
    {
        // Create a daily reminder task
        if (ctx.IdleTime.TotalHours > 24)
        {
            return Task.FromResult<IEnumerable<AgentTask>>([
                new AgentTask
                {
                    Type = AgentTaskType.StageMessage,
                    Priority = 4,
                    CorrelationKey = "daily-reminder"
                }
            ]);
        }
        
        return Task.FromResult<IEnumerable<AgentTask>>([]);
    }

    public Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
    {
        var staged = new StagedMessage
        {
            TopicKey = "daily-reminder",
            Draft = "Good to see you again! I'm ready to help with anything you need.",
            Rationale = "Daily greeting for returning users",
            ExpireUtc = DateTime.UtcNow.AddHours(2)
        };

        return Task.FromResult(AgentTaskResult.Ok(staged: [staged]));
    }
}
```

### Background Data Processing Plugin

```csharp
public class DataProcessingPlugin : IAgentPlugin
{
    public string Id => "DataProcessing";
    public IEnumerable<AgentTaskType> Supported => [AgentTaskType.PluginSpecific];

    public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
    {
        // Check if we have data to process
        if (HasPendingData() && ctx.IdleTime.TotalMinutes > 10)
        {
            return Task.FromResult<IEnumerable<AgentTask>>([
                new AgentTask
                {
                    Type = AgentTaskType.PluginSpecific,
                    Priority = 5,
                    CorrelationKey = "data-processing",
                    RequiresLLM = false
                }
            ]);
        }

        return Task.FromResult<IEnumerable<AgentTask>>([]);
    }

    public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
    {
        try
        {
            await ProcessPendingData(ct);
            return AgentTaskResult.Ok();
        }
        catch
        {
            return AgentTaskResult.Fail();
        }
    }

    private bool HasPendingData() => true; // Implement your logic
    private Task ProcessPendingData(CancellationToken ct) => Task.CompletedTask; // Implement your logic
}
```

## Configuration Reference

### AgentConfig Properties

```csharp
public sealed class AgentConfig : BaseFile
{
    public bool Enabled { get; set; } = true;                                    // Enable/disable agent
    public int LoopIntervalMs { get; set; } = 1500;                             // Main loop delay
    public int DailyTokenBudget { get; set; } = 8000;                           // Daily LLM token limit
    public int DailySearchBudget { get; set; } = 25;                            // Daily search limit
    public int MinIdleMinutesBeforeBackgroundWork { get; set; } = 3;            // Minimum idle time
    public int SearchCooldownMinutesPerTopic { get; set; } = 120;               // Search cooldown
    public int StageMessageTTLMinutes { get; set; } = 360;                      // Message expiry
    public string[] Plugins { get; set; } = ["CoreReflection", "WebIntelligence"]; // Enabled plugins
}
```

## API Reference

### IAgentRuntime Methods

- `void Start()`: Start the agent runtime
- `void Stop()`: Stop the agent runtime
- `void RegisterPlugin(string id, IAgentPlugin plugin)`: Register plugin instance
- `void RegisterPlugin(string id, Func<IAgentPlugin> factory)`: Register plugin factory
- `void UnregisterPlugin(string id)`: Remove registered plugin
- `IReadOnlyList<string> GetRegisteredPluginIds()`: Get all registered plugin IDs

### IAgentPlugin Methods

- `Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)`: Observe context and create tasks
- `Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)`: Execute a task
- `bool CanHandle(AgentTask t)`: Check if plugin can handle a task type

## Migration Guide

### Existing Code

If you're already using the Agent system, no changes are required. The new registration system is fully backward compatible.

### New Plugin Development

1. Implement `IAgentPlugin` interface
2. Register your plugin using `RegisterPlugin()` 
3. Add plugin ID to configuration
4. Start the agent runtime

The extensible Agent system provides a robust foundation for building intelligent, autonomous behavior into your applications while maintaining full backward compatibility with existing code.