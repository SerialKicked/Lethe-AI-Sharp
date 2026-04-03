# Memory Systems Documentation

This document explains the different memory systems used by the LetheAISharp library to extend the persona's memory and knowledge. All memory systems are unified under the `MemoryUnit` format and can be triggered either manually through keywords or automatically through the embedding similarity search.

Note that most, if not all those systems are automatically handled by the library when in full chat mode. This is mostly to explain how things behave internally, especially as many classes and functions can be overriden to add more functionalities.

## Overview

The LetheAISharp library employs three primary memory systems that work together to provide comprehensive memory management for personas:

1. **Chat Session Summaries** - Automatic summarization and embedding of past conversations
2. **WorldInfo System** - Manual keyword-activated knowledge databases. It's the application's job to load them into the BasePersona.Worlds field.
3. **Brain/Agent System** - Dynamic research and memory creation through agent tasks

All memory types are stored using the unified `MemoryUnit` format, which provides consistent handling, embedding, and retrieval across the entire system.

The persona's `Brain` class provides methods to manage memories, including adding new entries, forgetting old ones, and retrieving relevant information based on user input.

## Table of Contents

1. [MemoryUnit Format](#memoryunit-format)
2. [Chat Session Summaries](#chat-session-summaries)
3. [WorldInfo System](#worldinfo-system)
4. [Agent System](#agent-system)
5. [RAG Integration](#rag-integration)
6. [Known Facts (Extracted Facts)](#known-facts-extracted-facts)
7. [Memory Insertion Strategies](#memory-insertion-strategies)

## MemoryUnit Format

The `MemoryUnit` class is the unified format for all memory and knowledge storage in the library. Every piece of information - whether it's a chat summary, world knowledge, or research data - is stored as a `MemoryUnit`.

### Core Properties

| Property | Type | Purpose |
|----------|------|---------|
| `Guid` | `Guid` | Unique identifier for the memory entry |
| `Category` | `MemoryType` | Type of memory (General, WorldInfo, WebSearch, ChatSession, etc.) |
| `Insertion` | `MemoryInsertion` | How the memory is inserted (Trigger, Natural, None) |
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

This library makes use of WorldInfo, WebSearch, and ChatSession types. The remaining types can be used by your app when you want to feed the 
persona with external information. The type doesn't have to 1:1 match the function.

### Memory Insertion Modes

- **Trigger** - Memory is activated by RAG similarity in user input. It stays in the prompt for "Duration" (unless it's a chat session, which is always 1)
- **Natural** - Automatically inserted when relevant just before the user input message, it behaves like a normal message and scrolls until it gets out of the context window. It's converted to Trigger after use.
- **NaturalForced** - Will be forcefully injected into the prompt (no RAG check) in a timed fashion and behave like **Natural** memory otherwise. May lead to jarring disconnect during conversation
- **UserReturn** - Will be inserted as part of the system message generated when the user comes back after being AFK. Memory is set to None after use to avoid  re-insertion on subsequent returns. Content will be inserted "as is" with no title, it is recommended to keep it brief.
- **None** - Disabled memory

In all cases, the "Added" field is checked, meaning that if the memory's Added field is set in the future, this memory won't surface until the date is correct. This allows for features like future planning, and make calendar type applications easier to build.

## Chat Session Summaries

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

3. **RAG Activation**: When users or personas mention topics related to past conversations, the persona's `Brain` automatically retrieves relevant session summaries

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
It'll automatically process and archive the current session before starting a new empty session.

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

## Agent System

The Agent system provides dynamic memory creation and research capabilities through autonomous agent tasks. For comprehensive documentation on the agent system, see [AGENTS.md](AGENTS.md).

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
    Insertion = MemoryInsertion.Natural,
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

## RAG Integration

The persona's `Brain` class provides memory management and RAG functionalities:
- **Memorize()**: Add new memories with duplicate checking. This is particularly useful if your app intends to feed the persona with external information.
- **Forget()**: Remove specific memories
- **ReloadMemories()**: Rebuild the RAG index from current memories (to be called after adding or forgetting many memories)
- **GetRAGandInserts()**: Get a list of RAG and WorldInfo inserts that would be triggered by a given message
- **Search()**: Perform RAG similarity search on stored memories

### How RAG Works

1. **Embedding Creation**: All memory content is converted to vector embeddings
2. **Similarity Search**: When processing user input, the system:
   - Embeds the current message
   - Searches for similar memories using vector distance
   - Returns the most relevant memories within a distance threshold
3. **Automatic Insertion**: Retrieved memories are automatically inserted into the conversation context

### RAG Search Process

```csharp
if (LLMEngine.Settings.RAGEnabled)
{
    var searchresults = await LLMEngine.Bot.Brain.Search(searchmessage, ragResCount, ragDistance);
    // results is a list of VaultResult (containing each a MemoryUnit and a distance value)
}
```

### Memory Eligibility for RAG

Memories participate in RAG search when:
- `Insertion` is set to `MemoryInsertion.Trigger`
- `EmbedSummary` contains valid embedding data
- `Enabled` is true

By default, this includes:
- Summaries of past chat sessions
- WorldInfo entries with `DoEmbeds` property enabled
- Research results from agent tasks after they've been inserted into conversation

## Known Facts (Extracted Facts)

The `ExtractedFact` system is a lightweight semantic index that sits on top of the regular RAG memory vault. Rather than embedding long session summaries (which cover many topics and can be hard to match precisely), the brain extracts concise, single-sentence facts from each session and embeds those instead.

### How It Works

When a session is processed, short facts are extracted from it via structured output — statements like *"User works as a software engineer"* or *"User's cat is named Whiskers."* Each fact is embedded independently and stored in `Brain.ExtractedFacts`.

At retrieval time, **two-hop retrieval** is used:
1. The user's message is compared against all fact embeddings.
2. Any fact whose embedding is close enough (within `FactRetrievalThreshold`) has its `SourceMemories` GUIDs used to directly load the originating `MemoryUnit` objects (typically full session summaries).
3. Those sessions are then injected into the prompt, bypassing the standard vector distance check on the session summaries themselves.

This dramatically improves recall for personal facts that span many topics in a session, because "does the user mention their cat?" resolves through a tiny, focused embedding rather than a sprawling multi-topic summary.

### ExtractedFact Properties

| Property | Type | Purpose |
|----------|------|---------|
| `Guid` | `Guid` | Unique identifier |
| `Fact` | `string` | The concise single-sentence fact |
| `EmbedSummary` | `float[]` | Embedding vector for similarity search |
| `FirstSeen` | `DateTime` | When the fact was first extracted |
| `LastSeen` | `DateTime` | When the fact was last confirmed (updated on dedup) |
| `ReferenceCount` | `int` | How many times this fact has been seen across sessions |
| `SourceMemories` | `List<Guid>` | GUIDs of `MemoryUnit` objects this fact was extracted from |
| `Superseded` | `bool` | `true` if a newer fact replaced this one |
| `SupersededBy` | `Guid?` | GUID of the superseding fact, if any |

### Deduplication and Supersession

To keep the fact list from growing unbounded, the system uses cosine-distance comparison when a new fact arrives:

- **Duplicate** (distance ≤ `FactDeduplicationThreshold`): same fact — update `LastSeen` and `ReferenceCount`.
- **Supersession** (`FactDeduplicationThreshold` < distance ≤ `FactSupersessionThreshold`): related but updated fact — mark the old fact as `Superseded`, carry forward `SourceMemories`.
- **New fact** (distance > `FactSupersessionThreshold`): stored as a brand-new entry.

### System Prompt Inclusion

In addition to driving RAG retrieval, the highest-ranked facts are injected directly into the system prompt on every turn. Ranking is based on `ReferenceCount × recency_factor` (recency decays slowly over time). The number of facts included is controlled by `CoreFactsTokenBudget` in `LLMSettings`. The section title in the system prompt is configured via `SystemPrompt.CoreFactsTitle`.

### Relevant Settings

| Setting | Default | Purpose |
|---------|---------|---------|
| `FactRetrievalEnabled` | `true` | Enable/disable the entire fact layer |
| `CoreFactsTokenBudget` | `512` | Token budget for facts in the system prompt |
| `FactDeduplicationThreshold` | `0.05` | Distance for treating two facts as identical |
| `FactSupersessionThreshold` | `0.075` | Distance for treating a fact as superseding another |
| `FactRetrievalThreshold` | `0.10` | Distance for fact-triggered memory retrieval |

## Memory Insertion Strategies

### Trigger-Based Insertion

- **Activation**: Memories are inserted when triggered by RAG similarity or keyword matching
- **Use Case**: Most common for stored knowledge and past conversations
- **Behavior**: Memories remain dormant until relevance is detected and disappear quickly from context when not

### Natural Insertion

- **Activation**: Memories are automatically evaluated for relevance during conversation
- **Conversion**: After being used once, Natural memories become Trigger memories
- **Use Case**: Fresh insights or temporary information 
- **Behavior**: Inserted like a system message (just above the last user message), scrolls out of context over time

### NaturalForced Insertion

- **Activation**: Memories are automatically inserted independantly of context, but the system will prevent overload by spacing them by a few messages
- **Conversion**: After being used once, NaturalForced memories become Trigger memories
- **Use Case**: Critical messages that need to be addressed
- **Behavior**: Inserted like a system message (just above the last user message), scrolls out of context over time

### UserReturn Insertion

- **Activation**: Automatically triggered when the user returns after being AFK
- **Conversion**: After being used once, UserReturn memories become None memories to prevent further recall
- **Use Case**: Important messages, topic conversation steering, calendar events
- **Behavior**: Integrated in the system message that would normally be generated there, scrolls out of context over time

## Memory Lifecycle

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

Here the `Brain` class found that the user's query was very close to one of the memories it had with the Natural trigger, so it inserted it just above the user message as a system message. The bot then can then reuse that information in its own words.

## Best Practices

- Set realistic `Priority` levels to control memory retention (0 priority means that a natural memory will be pruned immediately after use)
- Configure `Duration` appropriately for WorldInfo entries (2-5 turns is typical)
- Enable `DoEmbeds` for WorldInfo when RAG integration is desired
- Write clear, concise `Content` that provides context without being verbose, aim for less than 1024 tokens (only first 1024 tokens are considered for RAG)
- Use descriptive `Name` fields for better identification
- The `Reason` information is optional, and mostly used for more natural intertion into the prompt
- Choose appropriate `Insertion` strategies based on memory importance and usage patterns
- While extremely optimized, RAG searches are computationally expensive, going over >100k entries might take several seconds.
- WorldInfo keyword matching is faster than RAG similarity search for specific triggers
