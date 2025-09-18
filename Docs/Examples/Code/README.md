# LetheAISharp Examples

This directory contains practical examples demonstrating how to use the LetheAISharp library and LLMEngine.

## Running the Examples

These examples are provided as reference code. To run them:

1. Make sure you have a compatible LLM backend running (KoboldCpp or OpenAI-compatible server)
2. Copy the example code into your own project
3. Ensure you have the LetheAISharp library referenced
4. Modify the connection URL and settings as needed

## Available Examples

### 1. SimpleQueryExample.cs
**Purpose**: Demonstrates basic LLMEngine usage with simple text queries

**Features shown**:
- Setting up and connecting to a backend
- Non-streaming queries with `SimpleQuery()`
- Streaming queries with `SimpleQueryStreaming()`
- Using `PromptBuilder` for complex prompts
- Event handling for streaming responses

**Good for**: Getting started with basic text generation

### 2. FullCommunicationExample.cs
**Purpose**: Shows the complete conversation system with personas and chat history

**Features shown**:
- Creating and configuring bot and user personas
- Using `SendMessageToBot()` for full conversations
- Chat history management
- Welcome messages and dialog examples
- Reroll functionality
- Saving chat sessions

**Good for**: Building chatbots and conversational applications

### 3. InteractiveChatExample.cs
**Purpose**: A complete interactive console chat application

**Features shown**:
- Real-time interactive chat loop
- Command system (quit, reroll, new session, stats)
- User input handling
- Chat statistics and session management
- Error handling and status monitoring

**Good for**: Understanding how to build a complete chat application

## Basic Setup Pattern

All examples follow this basic pattern:

```csharp
// 1. Setup connection
LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);

// 2. Connect to backend
await LLMEngine.Connect();

// 3. Check status
if (LLMEngine.Status == SystemStatus.Ready)
{
    // Ready to use!
}

// 4. Configure personas (for full communication mode)
LLMEngine.Bot = new BasePersona { /* configuration */ };
LLMEngine.User = new BasePersona { /* configuration */ };

// 5. Setup event handlers
LLMEngine.OnInferenceStreamed += (sender, token) => Console.Write(token);

// 6. Use the engine
var builder = LLMEngine.GetPromptBuilder();
builder.AddMessage(AuthorRole.User, "Your message here");
var query = builder.PromptToQuery(AuthorRole.Assistant);
await LLMEngine.SimpleQuery(query);
// or
await LLMEngine.SendMessageToBot(AuthorRole.User, "Your message");
```

## Backend Requirements

These examples assume you have a compatible backend running:

- **KoboldCpp**: Download from [GitHub](https://github.com/LostRuins/koboldcpp), default port 5001
- **LM Studio**: Available at [lmstudio.ai](https://lmstudio.ai/), usually runs on port 1234
- **Text Generation WebUI**: From [oobabooga](https://github.com/oobabooga/text-generation-webui)

Make sure to:
1. Load a model in your backend
2. Enable the API server
3. Note the correct port number
4. Update the URL in the examples accordingly

## Customization

Feel free to modify these examples:

- Change persona names, bios, and behaviors
- Adjust sampling settings via `LLMEngine.Sampler`
- Add custom event handlers
- Implement different conversation flows
- Save/load chat histories from files

## Need More Help?

- Check the main [LLMEngine Documentation](../LLMSYSTEM.md)
- Review the inline code comments
- Experiment with different settings and personas