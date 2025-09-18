# InstructFormat Documentation

The `InstructFormat` class is a core component of the AIToolkit that defines how messages are formatted for different language models. This is particularly important for text completion backends (KoboldAPI). While chat completion backends (OpenAI) handle formatting internally, providing the correct instruction format will lead to much better token count evaluation and handling of CoT / Thinking models.

## Overview

Different language models expect specific formatting for conversations. The `InstructFormat` class provides a flexible way to configure these formats without changing your application code.

The library has json formated presets for many popular models in the examples folder.

## Common Instruction Formats

### ChatML Format
Used by most modern models including Qwen.

```csharp
var chatml = new InstructFormat
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

### ChatML Thinking
Used by models that support chain-of-thought reasoning.

```csharp
var chatmlThinking = new InstructFormat
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
    NewLinesBetweenMessages = true,
    ThinkingStart = "<think>\n",
    ThinkingEnd = "</think>",
    PrefillThinking = true
};
```

## Key Properties

### Message Delimiters

- **`SysPromptStart/End`**: Wraps the main system prompt at conversation start
- **`SystemStart/End`**: Wraps system messages within conversations (when in doubt, use the same as with SysPrompt)
- **`UserStart/End`**: Wraps user messages
- **`BotStart/End`**: Wraps assistant/bot responses

### Special Sequences

- **`BoSToken`**: Beginning-of-sequence token (rarely needed)
- **`StopSequence`**: Forces generation to stop when encountered
- **`StopStrings`**: Forces generation to stop when any of the strings are encountered (useful for bad fine tunes, or when you want to inforce some behaviors, like forcing the model to stop "talking" after a new line).

### Behavior Controls

- **`AddNamesToPrompt`**: Include character names before messages (some roleplay models like that)
- **`NewLinesBetweenMessages`**: Add newlines between message blocks (to avoid adding '\n' after all your End)

### Chain-of-Thought Support

- **`ThinkingStart/End`**: Delimiters for thinking/reasoning blocks
- **`PrefillThinking`**: Automatically start with thinking mode
- **`ThinkingForcedThought`**: Default thinking content

## Usage with LLMEngine

### Setting the Format

```csharp
// Set globally for all operations
LLMEngine.Instruct = new InstructFormat
{
    UserStart = "<|user|>",
    UserEnd = "<|end|>",
    BotStart = "<|assistant|>", 
    BotEnd = "<|end|>"
};
```

### Format-Specific Examples

The instruction format affects how your prompts are actually sent to the model:

```csharp
// Your code
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.User, "Hello!");
builder.AddMessage(AuthorRole.Assistant, "Hi there!");

// With ChatML format, becomes:
// <|im_start|>user
// Hello!<|im_end|>
// <|im_start|>assistant  
// Hi there!<|im_end|>

// With Alpaca format, becomes:
// ### Instruction:
// Hello!
//
// ### Response:
// Hi there!
```

## Advanced Features

### Thinking Models

For models that support chain-of-thought reasoning:

```csharp
var thinkingFormat = new InstructFormat
{
    ThinkingStart = "<think>",
    ThinkingEnd = "</think>",
    PrefillThinking = true, // most local models will require this to trigger CoT mode
    ThinkingForcedThought = "Let me think about this step by step." // optional, leave empty unless you know what you're doing
};
```

### Stop Sequences

Control when generation should stop:

```csharp
var format = new InstructFormat
{
    StopSequence = "</s>",
    // Generation stops when the model outputs this sequence (the sequence is not put into history contrary to Bot/User/...-End sequences)
    // Mostly used for models with badly defined Instruction Formats (old Mistral models) 
};
```

## Backend Differences

### KoboldAPI (Text Completion)
- Uses all InstructFormat settings
- Manually constructs formatted prompts
- Full control over message structure

### OpenAI API (Chat Completion)  
- Ignores most InstructFormat settings
- Uses native message format internally
- Limited customization options

## Best Practices

1. **Match Your Model**: Use the format your specific model was trained with
2. **Test Thoroughly**: Different formats can significantly affect model behavior
3. **Keep It Simple**: Start with standard formats before customizing
4. **Document Changes**: Track format modifications for reproducibility

## Loading and Saving

```csharp
// Save format to file
var format = LLMEngine.Instruct;
format.SaveToFile("my_format.json");

// Load format from file
var loadedFormat = InstructFormat.LoadFromFile("my_format.json");
LLMEngine.Instruct = loadedFormat;
```

## Troubleshooting

### Common Issues

- **Empty Responses**: Wrong format for your model
- **Repetitive Output**: Missing stop sequences
- **Poor Quality**: Format doesn't match training data

### Debugging

```csharp
// See the actual formatted prompt
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.User, "Test message");
string formatted = builder.PromptToText();
Console.WriteLine(formatted);
```

This will show you exactly how your messages are being formatted, helping identify format issues.

## Model-Specific Recommendations

### Popular Models and Their Preferred Formats

- **Qwen and most models**: ChatML / ChatML-Thinker
- **Mistral Small**: L7 Tekken (sometimes ChatML)
- **Llama 3 and over**: Llama3 Chat

When in doubt, check the model's documentation or internal files for the recommended instruction format. 

