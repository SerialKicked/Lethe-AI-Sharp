# AIToolkit Extensibility Guide

This guide explains how to extend AIToolkit's core classes (`BasePersona`, `Chatlog`, and `ChatSession`) to add custom functionality to your application.

## Overview

AIToolkit now supports extensibility for all major chat-related classes through a factory method pattern:

- **BasePersona** - Already extensible (existing feature)
- **Chatlog** - Now extensible (new feature)  
- **ChatSession** - Now extensible (new feature)

This allows applications to add custom properties, methods, and behavior while maintaining full compatibility with AIToolkit's existing functionality.

## Quick Start

### 1. Extend ChatSession

```csharp
public class MyCustomChatSession : ChatSession
{
    public string CustomMoodAnalysis { get; set; } = "";
    public List<string> CustomTags { get; set; } = new();

    public override async Task UpdateSession()
    {
        await base.UpdateSession(); // Always call base implementation
        
        // Add your custom logic
        CustomMoodAnalysis = await AnalyzeConversationMood();
        CustomTags = await GenerateCustomTags();
    }

    private async Task<string> AnalyzeConversationMood()
    {
        var query = "Analyze the overall mood of this conversation in one word.";
        return await GenerateTaskRes(query, 50, true, false);
    }
}
```

### 2. Extend Chatlog

```csharp
public class MyCustomChatlog : Chatlog
{
    public string ApplicationVersion { get; set; } = "1.0.0";
    
    // Override factory method to create your custom sessions
    protected override ChatSession CreateChatSession()
    {
        return new MyCustomChatSession();
    }
    
    // Add custom methods
    public Dictionary<string, object> GetAdvancedAnalytics()
    {
        var analytics = new Dictionary<string, object>();
        
        // Custom analytics using your session properties
        var moodCounts = Sessions
            .OfType<MyCustomChatSession>()
            .GroupBy(s => s.CustomMoodAnalysis)
            .ToDictionary(g => g.Key, g => g.Count());
            
        analytics["MoodDistribution"] = moodCounts;
        return analytics;
    }
}
```

### 3. Extend BasePersona

```csharp
public class MyCustomPersona : BasePersona
{
    public bool EnableAdvancedFeatures { get; set; } = true;
    
    // Override factory method to create your custom chatlog
    protected override Chatlog CreateChatlog()
    {
        return new MyCustomChatlog
        {
            ApplicationVersion = "2.0.0"
        };
    }
    
    // Add convenience methods
    public Dictionary<string, object> GetPersonaAnalytics()
    {
        if (History is MyCustomChatlog customHistory)
        {
            return customHistory.GetAdvancedAnalytics();
        }
        return new Dictionary<string, object>();
    }
}
```

### 4. Use Your Custom Classes

```csharp
// Simply create and use your custom persona
var myBot = new MyCustomPersona
{
    Name = "CustomBot",
    Bio = "A bot with advanced analytics",
    UniqueName = "custom_bot_v1",
    EnableAdvancedFeatures = true
};

// Set it as the active bot - that's it!
LLMSystem.Bot = myBot;

// Now all chat functionality automatically uses your custom classes
// Sessions will be MyCustomChatSession instances
// History will be a MyCustomChatlog instance
// All existing AIToolkit features work normally
```

## How It Works

The extensibility is implemented through virtual factory methods:

1. **BasePersona** has `CreateChatlog()` and `CreateChatSession()` virtual methods
2. **Chatlog** has `CreateChatSession()` virtual method  
3. These methods are called automatically when new instances are needed

The inheritance hierarchy:
```
BasePersona (your custom persona)
├── CreateChatlog() → MyCustomChatlog
└── MyCustomChatlog
    └── CreateChatSession() → MyCustomChatSession
```

## Advanced Features

### Serialization Support

Your custom properties are automatically serialized/deserialized:

```csharp
public class MyCustomChatSession : ChatSession
{
    [JsonProperty("custom_data")]
    public Dictionary<string, string> CustomData { get; set; } = new();
    
    [JsonIgnore]
    public string CalculatedProperty => $"Calculated: {CustomData.Count}";
}
```

### Event Handling

You can override existing virtual methods to add custom behavior:

```csharp
public class MyCustomPersona : BasePersona
{
    public override void BeginChat()
    {
        base.BeginChat(); // Always call base
        
        // Custom initialization
        Console.WriteLine($"Starting advanced chat with {Name}");
        if (History is MyCustomChatlog customHistory)
        {
            customHistory.ApplicationVersion = "3.0.0";
        }
    }

    public override void EndChat(bool backup = false)
    {
        // Custom cleanup before ending
        if (History is MyCustomChatlog customHistory)
        {
            var analytics = customHistory.GetAdvancedAnalytics();
            Console.WriteLine($"Session ended. Analytics: {analytics}");
        }
        
        base.EndChat(backup);
    }
}
```

### Type Safety

Use pattern matching for type-safe access to your custom properties:

```csharp
public void ProcessCurrentSession()
{
    var session = LLMSystem.History.CurrentSession;
    
    if (session is MyCustomChatSession customSession)
    {
        // Access custom properties safely
        Console.WriteLine($"Mood: {customSession.CustomMoodAnalysis}");
        Console.WriteLine($"Tags: {string.Join(", ", customSession.CustomTags)}");
    }
}
```

## Best Practices

1. **Always call base methods**: When overriding virtual methods, call the base implementation to maintain functionality
2. **Use meaningful names**: Name your custom classes clearly (e.g., `AnalyticsChatSession`, `GamePersona`)
3. **Handle nulls gracefully**: Check types before casting (`is` operator, `as` operator)
4. **Document custom properties**: Add XML documentation for custom properties and methods
5. **Test thoroughly**: Verify that existing AIToolkit features work with your custom classes

## Migration from Existing Code

If you have existing code using AIToolkit, no changes are required. The new extensibility is:

- **Opt-in**: Only activated when you use custom classes
- **Backward compatible**: All existing code continues to work
- **Non-breaking**: Default behavior is unchanged

Simply replace your persona creation with a custom persona to enable the new features.

## Troubleshooting

### Custom properties not being serialized
- Ensure properties have public getters and setters
- Add `[JsonProperty]` attributes if needed
- Check that your classes are properly marked as public

### Factory methods not being called
- Verify methods are marked as `protected override`
- Ensure you're setting a custom persona as `LLMSystem.Bot`
- Check that the inheritance chain is correct

### Existing functionality broken
- Always call `base.MethodName()` when overriding virtual methods
- Verify custom constructors call base constructors
- Test with a minimal custom implementation first

## Examples Repository

For complete working examples, see the `examples/` directory in this repository, which contains:

- `CustomChatSession.cs` - Advanced session with mood analysis
- `CustomChatlog.cs` - Enhanced chatlog with analytics  
- `CustomPersona.cs` - Full persona implementation
- `ExampleUsage.cs` - Complete usage example

These examples demonstrate real-world usage patterns and best practices for extending AIToolkit.