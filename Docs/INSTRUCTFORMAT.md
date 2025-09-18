# InstructFormat Documentation

The `InstructFormat` class is a core component of the AIToolkit that defines how messages are formatted for different language models. This is particularly important for text completion backends (KoboldAPI), while chat completion backends (OpenAI) handle formatting internally.

## Overview

Different language models expect specific formatting for conversations. The `InstructFormat` class provides a flexible way to configure these formats without changing your application code.

## Common Instruction Formats

### ChatML Format
Used by many modern models including GPT-4, Claude, and instruction-tuned models:

```csharp
var chatml = new InstructFormat
{
    SysPromptStart = "<|im_start|>system\n",
    SysPromptEnd = "<|im_end|>\n",
    UserStart = "<|im_start|>user\n", 
    UserEnd = "<|im_end|>\n",
    BotStart = "<|im_start|>assistant\n",
    BotEnd = "<|im_end|>\n",
    AddNamesToPrompt = false
};
```

### Alpaca Format
Popular format for many fine-tuned models:

```csharp
var alpaca = new InstructFormat
{
    SysPromptStart = "### Instruction:\n",
    SysPromptEnd = "\n\n",
    UserStart = "### Instruction:\n",
    UserEnd = "\n\n",
    BotStart = "### Response:\n",
    BotEnd = "\n\n",
    AddNamesToPrompt = false
};
```

### Vicuna Format
Used by Vicuna and similar models:

```csharp
var vicuna = new InstructFormat
{
    SysPromptStart = "SYSTEM: ",
    SysPromptEnd = "\n",
    UserStart = "USER: ",
    UserEnd = "\n",
    BotStart = "ASSISTANT: ",
    BotEnd = "\n",
    AddNamesToPrompt = false
};
```

## Key Properties

### Message Delimiters

- **`SysPromptStart/End`**: Wraps the main system prompt at conversation start
- **`SystemStart/End`**: Wraps system messages within conversations  
- **`UserStart/End`**: Wraps user messages
- **`BotStart/End`**: Wraps assistant/bot responses

### Special Tokens

- **`BoSToken`**: Beginning-of-sequence token (rarely needed)
- **`StopSequence`**: Forces generation to stop when encountered

### Behavior Controls

- **`AddNamesToPrompt`**: Include character names before messages
- **`NewLinesBetweenMessages`**: Add newlines between message blocks

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
    UserEnd = "<|/user|>",
    BotStart = "<|assistant|>", 
    BotEnd = "<|/assistant|>"
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
    ThinkingStart = "<thinking>",
    ThinkingEnd = "</thinking>",
    PrefillThinking = true,
    ThinkingForcedThought = "Let me think about this step by step."
};
```

### Named Characters

When using character names in conversations:

```csharp
var namedFormat = new InstructFormat
{
    UserStart = "",
    UserEnd = ":",
    BotStart = "",
    BotEnd = ":",
    AddNamesToPrompt = true,
    NewLinesBetweenMessages = true
};

// Results in:
// John: Hello!
// 
// Alice: Hi there!
```

### Stop Sequences

Control when generation should stop:

```csharp
var format = new InstructFormat
{
    StopSequence = "[END]",
    // Generation stops when model outputs [END]
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

- **Llama 2 Chat**: ChatML or custom Llama format
- **Code Llama**: Alpaca format works well
- **Vicuna**: Vicuna format (obviously)
- **Alpaca**: Alpaca format
- **Most Modern Instruct Models**: ChatML

When in doubt, check the model's documentation or training details for the recommended instruction format.