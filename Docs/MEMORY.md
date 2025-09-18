# Memory Systems Documentation

This document explains the different memory systems used by the LetheAISharp library to extend the persona's memory and knowledge. All memory systems are unified under the `MemoryUnit` format and can be triggered either manually through keywords or automatically through the RAGEngine's embedding similarity search.

Note that most, if not all those systems are automatically handled by the library when in full chat mode. This is mostly to explain how things behave internally, especially as many classes and functions can be overriden to add more functionalities.

## Overview

The LetheAISharp library employs three primary memory systems that work together to provide comprehensive memory management for personas:

1. **Chat Session Summaries** - Automatic summarization and embedding of past conversations
2. **WorldInfo System** - Manual keyword-activated knowledge databases. It's the application's job to load them into the BasePersona.Worlds field.
3. **Brain/Agent System** - Dynamic research and memory creation through agent tasks

All memory types are stored using the unified `MemoryUnit` format, which provides consistent handling, embedding, and retrieval across the entire system.

## Table of Contents

1. [MemoryUnit Format](#memoryunit-format)
2. [Chat Session Summaries](#chat-session-summaries)
3. [WorldInfo System](#worldinfo-system)
4. [Brain/Agent System](#brainagent-system)
5. [RAGEngine Integration](#ragengine-integration)
6. [Memory Insertion Strategies](#memory-insertion-strategies)

## MemoryUnit Format

The `MemoryUnit` class is the unified format for all memory and knowledge storage in the library. Every piece of information - whether it's a chat summary, world knowledge, or research data - is stored as a `MemoryUnit`.

### Core Properties

| Property | Type | Purpose |
|----------|------|---------|
| `Guid` | `Guid` | Unique identifier for the memory entry |
| `Category` | `MemoryType` | Type of memory (General, WorldInfo, WebSearch, ChatSession, etc.) |
| `Insertion` | `MemoryInsertion` | How the memory is inserted (Trigger, Natural, NaturalForced, None) |
| `Name` | `string` | Title or name for the memory entry |
| `Content` | `string` | The actual memory content |
| `Reason` | `string` | Context or reason why this memory is important (optional) |
| `EmbedSummary` | `float[]` | Vector embedding for RAG similarity search |
| `Priority` | `int` | Importance level of the memory (affects retention and triggering) |

### Memory Types

- **General** - Basic memories and observations
- **WorldInfo** - Manual knowledge entries with keyword triggers
- **WebSearch** - Research results from web searches
- **ChatSession** - Summarized conversation history
- **Journal** - Personal notes and reflections
- **Image**, **File** - Media and document references
- **Location**, **Event**, **Person**, **Goal** - Categorized memories for specific types

### Memory Insertion Modes

- **Trigger** - Memory is activated by RAG similarity in user input. It stays in the prompt for "Duration" (unless it's a chat session, which is always 1)
- **Natural** - Automatically inserted when relevant just before the user input message, it behaves like a normal message and scrolls until it gets out of the context window. It's converted to Trigger after use.
- **NaturalForced** - Same as natural but is forcefully inserted into the discussion after a while if no relevant entry point present itself.
- **None** - Disabled memory

## Chat Session Summaries

The chat session memory system automatically summarizes and embeds past conversations, making them searchable through the RAGEngine. There are 2 different insertion methods. 

### Recent Past Sessions 

The summary of the chat sessions just before the current one can be inserted in the system prompt for long term contextual awareness. The behavior can be adjusted through the `LLMEngine.Settings`:

- **SessionMemorySystem** - true/false (allow or disallow the behavior entirely)
- **SessionReservedTokens** - The maximum amount of tokens you want to reserve for the feature
- **SessionHandling** - How to handle the chatlog itself
  - **CurrentOnly** - The chatlog will only contain the current session, with the previous ones summarized in system prompt
  - **FitAll** - The chatlog will feature as much log as possible (even across multiple sessions) depending on maximum Context Size, sessions coming before all this will be inserted in system prompt

### Long Term Recall

Older chat session can be retrieved through RAG. Their summary are compared to the user's input, for embedding distance / similarity. If judged relevant enough, those can be inserted into the prompt at different levels under different policies.

### How It Works

When a chat session ends, which is controlled by your app through the following command:
````csharp
LLMSystem.Bot.History.StartNewChatSession()
````
the library processes the chat to turn it into useful data:

1. **Automatic Summarization**: 
   - Session title, summary and metadata
   - Key topics discussed
   - Character interactions and developments
   - Future goals mentioned
   - Roleplay status detection

2. **Embedding Creation**: The summary is converted to vector embeddings for similarity search

3. **RAG Activation**: When users or personas mention topics related to past conversations, the RAGEngine automatically retrieves relevant session summaries

### Session Summary Process

Any chat session can be (re)processed individually, however it can take a few minutes depending on the backend, model, and processing power. 
```csharp
// Automatic session update process
await session.UpdateSession();
// This creates:
// - MetaData.Summary (detailed summary)
// - MetaData.Keywords (key topics)
// - MetaData.FutureGoals (mentioned objectives)
// - EmbedSummary (vector embedding)
```

Instead when the user or app decides that a chat session has ended, call:
````csharp
LLMSystem.Bot.History.StartNewChatSession()
````
It'll automatically process, and archive the current one before starting a new empty session.

### Memory Storage

Chat sessions are stored as `MemoryUnit` objects with:
- **Category**: `MemoryType.ChatSession`
- **Insertion**: `MemoryInsertion.Trigger` (RAG-activated)
- **Name**: Session title
- **Content**: Detailed session summary
- **EmbedSummary**: Vector representation for similarity search

## WorldInfo System

The WorldInfo system provides manually curated knowledge databases that can be activated through keyword matching or RAG similarity search.

### How It Works

1. **Manual Creation**: Developers or users create knowledge entries with specific keywords
2. **Keyword Activation**: Entries are triggered when conversation contains matching keywords
3. **Optional RAG Integration**: When `DoEmbeds` is enabled, entries also participate in similarity search
4. **Duration Control**: Activated entries remain active for a specified number of conversation turns

### WorldInfo Structure

```csharp
public class WorldInfo
{
    public string Name { get; set; }           // World database name
    public string Description { get; set; }    // Purpose description
    public bool DoEmbeds { get; set; }         // Enable RAG integration
    public int ScanDepth { get; set; }         // Messages to scan for keywords
    public List<MemoryUnit> Entries { get; set; } // Knowledge entries
}
```

### Entry Configuration

Each WorldInfo entry is a `MemoryUnit` with additional keyword settings:
- **KeyWordsMain**: Primary trigger keywords
- **KeyWordsSecondary**: Secondary trigger keywords  
- **WordLink**: Logic for keyword matching (And, Or, Not)
- **Duration**: How many turns the entry stays active
- **TriggerChance**: Probability of activation when keywords match

### Keyword Matching Logic

- **And**: Requires keywords from both Main and Secondary lists
- **Or**: Requires keywords from either Main or Secondary lists  
- **Not**: Requires Main keywords but not Secondary keywords

## Brain/Agent System

The Brain/Agent system provides dynamic memory creation and research capabilities through autonomous agent tasks.

### How It Works

1. **Topic Analysis**: Agent tasks (if enabled) analyze recent conversations to identify unfamiliar topics
2. **Automatic Research**: When `AgentSystem` is enabled and research tasks are assigned, the system:
   - Detects knowledge gaps in conversations
   - Performs web searches on unfamiliar topics
   - Merges search results into coherent summaries
   - Stores findings as new memories

3. **Memory Integration**: Research results are automatically added to the persona's memory with appropriate categorization

### Agent Memory Creation

```csharp
// Example from ActiveResearchTask
var mem = new MemoryUnit
{
    Category = MemoryType.WebSearch,
    Insertion = MemoryInsertion.NaturalForced,
    Name = topic.Topic,
    Content = merged.CleanupAndTrim(),
    Reason = topic.Reason,
    Priority = topic.Urgency + 1
};

await mem.EmbedText();
owner.Brain.Memorize(mem);
```

### Research Tasks

- **ResearchTask**: Analyzes archived sessions for research opportunities
- **ActiveResearchTask**: Performs real-time research during active conversations
- Both tasks create memories with `MemoryType.WebSearch` category

### Brain Memory Management

The `Brain` class provides memory management functionality:
- **Memorize()**: Add new memories with duplicate checking. This is particularly useful if your app intends to feed the persona with external information.
- **Forget()**: Remove specific memories
- **GetMemoriesForRAG()**: Retrieve memories available for similarity search
- **UpdateRagAndInserts()**: Refresh active memory insertions

## RAGEngine Integration

The RAGEngine (Retrieval-Augmented Generation) provides the core similarity search functionality that unifies all memory systems.

### How RAG Works

1. **Embedding Creation**: All memory content is converted to vector embeddings
2. **Similarity Search**: When processing user input, the system:
   - Embeds the current message
   - Searches for similar memories using vector distance
   - Returns the most relevant memories within a distance threshold

3. **Automatic Insertion**: Retrieved memories are automatically inserted into the conversation context

### RAG Search Process

```csharp
// RAG search in Brain.UpdateRagAndInserts()
if (RAGEngine.Enabled)
{
    var search = await RAGEngine.Search(searchmessage, ragResCount, ragDistance);
    target.AddMemories(search);
}
```

### Memory Eligibility for RAG

Memories participate in RAG search when:
- `Insertion` is set to `MemoryInsertion.Trigger`
- `EmbedSummary` contains valid embedding data
- `Enabled` is true

## Memory Insertion Strategies

The library supports multiple strategies for inserting memories into conversations:

### Trigger-Based Insertion

- **Activation**: Memories are inserted when triggered by RAG similarity or keyword matching
- **Use Case**: Most common for stored knowledge and past conversations
- **Behavior**: Memories remain dormant until relevance is detected and disappear quickly from context when not

### Natural Insertion

- **Activation**: Memories are automatically evaluated for relevance during conversation
- **Conversion**: After being used once, Natural memories become Trigger memories
- **Use Case**: Fresh insights or temporary information, 
- **Behavior**: Inserted like a system message (just above the last user message), scrolls out of context over time

### Forced Natural Insertion

- **Activation**: Always considered for insertion when relevant
- **Conversion**: After being used once, those memories become Trigger memories
- **Use Case**: Critical information that the user needs to know even if talking about something else.
- **Behavior**: Inserted like a system message (just above the last user message), scrolls out of context over time

### Memory Lifecycle

1. **Creation**: Memory is created with appropriate `MemoryType` and `Insertion` strategy
2. **Embedding**: Content is converted to vector embedding if RAG is enabled
3. **Storage**: Memory is added to the Brain's memory collection
4. **Activation**: Memory is triggered by keywords or similarity search
5. **Insertion**: Memory content is injected into conversation context
6. **Evolution**: Natural memories may convert to Trigger memories after use
7. **Decay**: Some memory types (Goals and WebSearch by default) will decay and be pruned if not triggered for a long time.

## Example of a Full Featured Chatlog

Here's an example of what a full prompt might look like when having all memory systems enabled and working together. Here, the user archived the last chat session a while back and is starting a new one. The program was kept running in the background. Bob is running the two ActiveResearchTask and ResearchTask agentic task, allowing it to do web research while the user is AFK.

```
[SystemPrompt]
You are a helpful assistant named Bob.

# Participants:
- Bob: A knowledgeable and friendly AI assistant who can remember a great deal
- User: Some user who knows very little

# Past sessions:    // Automatically inserted by the bot in the system prompt, those are former chat sessions summaries (if any and if they no longer fit in context)
- Session Title 1 (2025-05-16): Discussed the basics of AI and machine learning. User learned about supervised and unsupervised learning.
- Session Title 2 (2025-06-02): Discussed the meaning of life, we ended up figuring out it was 42

# Relevant Information: // Those are RAG triggered information from either past sessions, WorldInfo, or other tasks with their memory entries set to Trigger
- some relevant info from a worldinfo entry about the last user message
- some past chat session that's too old for the previous section but very relevant to the discussion at hand
[/SystemPrompt]

[Bot]
Hello! How can I assist you today?
[/Bot]

[User]
Hi Bob, did you look into the meaning of life we discussed last time?
[/User]
```

In this example, assuming that Bob got time to do the research in the background while the user was afk. The prompt becomes:

```
[SystemPrompt] 
(no changes)
[/SystemPrompt]

[Bot]
Hello! How can I assist you today?
[/Bot]

[SystemMessage] // those messages are automatically inserted by the bot just above the last user message, they scroll out of context over time and are tagged as "hidden", meaning that they don't have to be displayed in the UI
Bob has found the following information on the internet about the meaning of life: 
(lot of text here, summarized from various sources)
[/SystemMessage]

[User]
Hi Bob, Did you look into the meaning of life we discussed last time?
[/User]

[Bot]
Oh yeah, I did! Here's the info I found.... (proceeds to reuse the info from the SystemMessage above in its own words)
[/Bot]
```

Here the `Brain` class found that the user's query was very close to one of the memories it had with the Natural or NaturalForced trigger, so it inserted it just above the user message as a system message. The bot then can then reuse that information in its own words.

## Best Practices

### For Developers

- Use appropriate `MemoryType` categories for better organization
- Set realistic `Priority` levels to control memory retention (0 priority means that a natural memory will be pruned immediately after use)
- Configure `Duration` appropriately for WorldInfo entries
- Enable `DoEmbeds` for WorldInfo when RAG integration is desired

### For Memory Design

- Write clear, concise `Content` that provides context without being verbose, always aim for less than 1024 tokens (512 is optimal)
- Use descriptive `Name` fields for better identification
- The `Reason` information is optional, and mostly used for more natural intertion into the prompt
- Choose appropriate `Insertion` strategies based on memory importance and usage patterns

### Performance Considerations

- While extremely optimized, RAG searches are computationally expensive, going over >100k entries might take several seconds.
- Regular memory cleanup helps maintain system responsiveness
- WorldInfo keyword matching is faster than RAG similarity search for specific triggers
