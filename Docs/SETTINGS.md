# LLMSettings Documentation

## Overview

`LLMSettings` is the central configuration object for the LetheAISharp library. It controls everything from which backend to connect to, how much context the model gets, how memory and RAG work, and how group chats behave.

You access it through `LLMEngine.Settings` and can persist it to disk as JSON:

```csharp
// Access and modify settings
LLMEngine.Settings.MaxTotalTokens = 8192;
LLMEngine.Settings.RAGEnabled = true;

// Save to file
LLMEngine.Settings.SaveToFile("settings.json");

// Load from file and apply
var settings = LLMSettings.LoadFromFile("settings.json");
LLMEngine.Settings = settings;
```

> 📖 For a general introduction to the engine, see [LLMSYSTEM.md](LLMSYSTEM.md).

## Table of Contents

1. [Quick-Start Example](#quick-start-example)
2. [General Settings](#1-general-settings)
3. [Backend Connection](#2-backend-connection)
4. [Model Settings](#3-model-settings)
5. [Long Term Memory System and Summaries](#4-long-term-memory-system-and-summaries)
6. [Sentiment Analysis Module](#5-sentiment-analysis-module)
7. [RAG Settings](#6-rag-settings)
8. [Web Search API Settings](#7-web-search-api-settings)
9. [Group Chat Settings](#8-group-chat-settings)
10. [Full Reference Table](#full-reference-table)

---

## Quick-Start Example

```csharp
using LetheAISharp.LLM;
using LetheAISharp.Files;

// Load existing settings or start from defaults
var settings = File.Exists("settings.json")
    ? LLMSettings.LoadFromFile("settings.json")
    : new LLMSettings();

// Point at your backend
settings.BackendUrl = "http://localhost:5001";
settings.BackendAPI = BackendAPI.KoboldAPI;

// Give the model a reasonable context budget
settings.MaxTotalTokens = 16384;
settings.MaxReplyLength = 512;

// Enable long-term memory summaries
settings.SessionMemorySystem = true;
settings.SessionHandling = SessionHandling.FitAll;

// Apply and connect
LLMEngine.Settings = settings;
LLMEngine.Setup(settings.BackendUrl, settings.BackendAPI);
await LLMEngine.Connect();

// Persist for next launch
settings.SaveToFile("settings.json");
```

---

## 1. General Settings

### `DataPath`
**Type:** `string` | **Default:** `"data/chars/"`

The folder where agentic brain data is stored. Each persona gets its own files here, named after `BasePersona.UniqueName` with `.brain` and `.agent` extensions.

Change this if you want to store character data somewhere other than the default relative path — for example, when running multiple separate projects from the same binary.

---

## 2. Backend Connection

### `BackendUrl`
**Type:** `string` | **Default:** `"http://localhost:5001"`

The URL of the LLM backend, or the file path to a local GGUF model when using `LlamaSharp`.

```csharp
// KoboldCpp running locally
settings.BackendUrl = "http://localhost:5001";

// OpenAI-compatible server (e.g., LM Studio)
settings.BackendUrl = "http://localhost:1234";

// Local GGUF file for LlamaSharp
settings.BackendUrl = @"C:\Models\mymodel.Q4_K_M.gguf";
```

---

### `BackendAPI`
**Type:** `BackendAPI` | **Default:** `BackendAPI.KoboldAPI`

Selects which API protocol to use:

| Value | Description |
|-------|-------------|
| `KoboldAPI` | Text completion API — works with KoboldCpp and compatible servers |
| `OpenAI` | Chat completion API — works with OpenAI and any OpenAI-compatible server |
| `LlamaSharp` | Internal backend that loads and runs a GGUF file directly inside your process |

`LlamaSharp` makes your application fully self-contained (no external server needed), but requires the `LLamaSharp` NuGet package and the model file to be present locally.

---

### `OpenAIKey`
**Type:** `string` | **Default:** `"123"`

API key sent with requests when using the `OpenAI` backend. If your server doesn't require a real key (e.g., a local LM Studio instance), any non-empty string will do — the default `"123"` is intentionally a placeholder.

---

### `LlamaSharpGPULayers`
**Type:** `int` | **Default:** `255`

Only relevant when `BackendAPI` is `LlamaSharp`. Controls how many transformer layers are offloaded to the GPU. `255` is treated as "all layers". Lower this value if you're running out of VRAM and want to fall back to CPU for some layers.

---

### `LlamaSharpFlashAttention`
**Type:** `bool` | **Default:** `true`

Only relevant when `BackendAPI` is `LlamaSharp`. Enables Flash Attention, which speeds up inference and reduces VRAM usage on supported hardware. Leave this on unless you're hitting compatibility issues.

---

### `LlamaSharpNoKVoffload`
**Type:** `bool` | **Default:** `false`

Only relevant when `BackendAPI` is `LlamaSharp`. When `true`, the KV cache is kept entirely in CPU RAM instead of being offloaded to the GPU. This uses less VRAM at the cost of slower inference. Only enable this if you need to conserve GPU memory.

---

### `OpenAIProcessAllImages`
**Type:** `bool` | **Default:** `false`

Only relevant when `BackendAPI` is `OpenAI` and you're talking to a vision-capable model. When `false` (the default), only the image attached to the most recent user message is sent to the API. When `true`, every image in the conversation history is sent.

Sending all images can consume a very large number of tokens and significantly increase costs. Only enable this if you specifically need the model to be able to reference earlier images in the conversation.

---

## 3. Model Settings

### `MaxTotalTokens`
**Type:** `int` | **Default:** `16384`

The total token budget for the model's context window. This should match (or be lower than) the context size you configured in your backend. The library uses this value to decide how much history, RAG data, and other content to fit into each prompt.

---

### `MaxReplyLength`
**Type:** `int` | **Default:** `512`

Maximum number of tokens the model is allowed to generate in a single reply. Increase this for tasks that require longer outputs (creative writing, detailed analysis). Decrease it to keep responses short and snappy.

---

### `ImageEmbeddingSize`
**Type:** `int` | **Default:** `768`

The dimensionality of the image embedding vectors your vision model produces. `768` is the most common size. Only relevant if you're using image inputs — you shouldn't need to change this unless your specific vision model uses a different size.

---

### `ScenarioOverride`
**Type:** `string` | **Default:** `""`  (empty — no override)

When non-empty, this string replaces the `Scenario` field of the currently loaded character persona. Useful for programmatically switching the scenario without editing the character file itself.

---

### `StopGenerationOnFirstParagraph`
**Type:** `bool` | **Default:** `false`

When `true`, generation stops after the model produces its first complete paragraph (i.e., on the first double newline). Handy for use cases that only need a short, single-thought reply.

---

### `DisableThinking`
**Type:** `bool` | **Default:** `false`

For thinking/reasoning models (e.g., Qwen3 with thinking mode). When `true`, the library attempts to disable the model's thinking block so it skips the chain-of-thought preamble and goes straight to the answer. Only applicable to models that support this feature.

---

### `AllowWorldInfo`
**Type:** `bool` | **Default:** `true`

Enables the WorldInfo system: keyword-activated text snippets that get automatically injected into the prompt when their trigger keywords appear in the conversation. See the persona documentation for details on how to define WorldInfo entries.

---

### `MoveAllInsertsToSysPrompt`
**Type:** `bool` | **Default:** `false`

When `true`, all dynamically inserted content (RAG entries, WorldInfo snippets, Brain memories) is placed in the system prompt regardless of the individual settings for each subsystem. Some models respond better to contextual information in the system prompt; others prefer it inline in the dialogue. Experiment with this if retrieval quality feels off.

---

### `DisableDateAndMoodIfNotLastSession`
**Type:** `bool` | **Default:** `true`

In full chatlog mode the library lets users "go back in time" and continue from a past chat session, even when newer sessions exist. When this is `true` (the default), the date and mood modifiers are suppressed for those historical sessions.

Without this, you'd see a past session receiving timestamps and mood information from the current date, which contradicts the session's chronological position and can confuse the model. Keep this enabled unless you have a specific reason to inject current date/mood into historical sessions.

---

## 4. Long Term Memory System and Summaries

> These settings are relevant in full communication mode. See [LLMSYSTEM.md](LLMSYSTEM.md) for a description of how the session memory system works.

### `SessionMemorySystem`
**Type:** `bool` | **Default:** `false`

Master switch for the long-term session memory system. When enabled, summaries of previous chat sessions are generated and inserted into the system prompt, giving the model context about what happened in earlier conversations.

---

### `SessionHandling`
**Type:** `SessionHandling` | **Default:** `SessionHandling.FitAll`

Controls how much conversation history is loaded into the prompt:

| Value | Description |
|-------|-------------|
| `CurrentOnly` | Only messages from the current session are included in the chatlog |
| `FitAll` | Messages from all sessions are loaded, filling the context as much as possible |

`FitAll` gives the model broader context but may slow down prompt construction for personas with very long histories.

---

### `SessionReservedTokens`
**Type:** `int` | **Default:** `2048`

How many tokens to reserve in the context window for session summaries. Only relevant when `SessionMemorySystem` is `true`. Summaries will be trimmed to fit within this budget before being inserted into the prompt.

---

### `CutInTheMiddleSummaryStrategy`
**Type:** `bool` | **Default:** `false`

When a chat session has to be summarized (because it exceeds the context limit), this setting controls which part of the session is kept verbatim:

- **`false` (default)**: Trim from the beginning — keep the most recent messages and drop the oldest ones. The model sees the freshest context but may lose early setup information.
- **`true`**: Cut the session in the middle — keep both the very beginning (setup, early context) and the very end (most recent exchanges). This generally produces better summaries because important initial context is preserved alongside the latest dialogue.

> 💡 The best strategy is to avoid this situation entirely by ending or rotating sessions as the context limit approaches, rather than relying on automatic summarization.

---

### `AntiHallucinationMemoryFormat`
**Type:** `bool` | **Default:** `true`

In full chat mode with agentic tasks enabled, the library may insert system messages mid-conversation (for example, results from an autonomous web search that ran while the user was away). On local models (e.g., 32B parameter models), exposure to this pattern can sometimes cause the model to start hallucinating responses.

When this is `true`, extra instructional sentences are added to the system prompt telling the model how to properly interpret those inline system messages. This significantly reduces hallucination on local models.

- **Cloud models** (Claude, GPT-4, etc.): this is generally unnecessary — you can set it to `false`.
- **Local models**: keep this enabled.

---

### `SessionDetailedSummary`
**Type:** `bool` | **Default:** `false`

When `false`, the standard concise summary from structured output is used for session memory. When `true`, the library performs a second, more detailed summarization pass that captures more nuance about the session at the cost of additional time and tokens. Enable this if you find the default summaries are losing important details across sessions.

---

## 5. Sentiment Analysis Module

> ⚠️ **This module is work-in-progress / experimental.** It is intended to use a local classifier model to gauge the emotional tone of messages, but the feature is not fully complete. The settings are documented here for completeness.

### `SentimentEnabled`
**Type:** `bool` | **Default:** `true`

Enables or disables the sentiment analysis module. Even though the default is `true`, **it is recommended to set this to `false`** until the module is stable. Leaving it enabled on a system where the model files are absent will produce warnings but won't crash the engine.

---

### `SentimentModelPath`
**Type:** `string` | **Default:** `"data/classifiers/emotion-bert-classifier.gguf"`

Path to the GGUF classifier model used for emotion detection. The model file must be downloaded separately — refer to the [README](../README.md) for download links.

---

### `SentimentGoEmotionHeadPath`
**Type:** `string` | **Default:** `"data/classifiers/goemotions_head.json"`

Path to the GoEmotions classification head configuration file. Must be downloaded alongside the main classifier model.

---

### `SentimentThresholdsPath`
**Type:** `string` | **Default:** `"data/classifiers/optimized_thresholds.json"`

Path to the optimized per-class threshold file used to convert raw classifier scores into discrete emotion labels.

---

## 6. RAG Settings

> Retrieval-Augmented Generation (RAG) lets the library semantically search past conversation entries and inject the most relevant ones into the current prompt. See [LLMSYSTEM.md](LLMSYSTEM.md) for a conceptual overview.

### `RAGEnabled`
**Type:** `bool` | **Default:** `true`

Master switch for all RAG functionality. When `false`, no semantic retrieval happens regardless of other RAG settings.

---

### `RAGModelPath`
**Type:** `string` | **Default:** `"data/classifiers/gte-large.Q6_K.gguf"`

Path to the GGUF embedding model used to generate vector representations of text for similarity search. The file must be present for RAG to work — the recommended model can be downloaded from [ChristianAzinn/gte-large-gguf on Hugging Face](https://huggingface.co/ChristianAzinn/gte-large-gguf).

---

### `RAGMoveToThinkBlock`
**Type:** `bool` | **Default:** `false`

For thinking models only. When `true`, RAG and WorldInfo results are injected into the model's thinking block instead of the main prompt. This is highly experimental and may not work well with all models.

---

### `RAGConvertTo3rdPerson`
**Type:** `bool` | **Default:** `true`

Before performing a similarity search, the user's message is rewritten in third person (English only). For example, "I went to the park" becomes "She went to the park." This typically improves retrieval relevance, especially when searching past chat sessions, because stored entries are often written in third person.

---

### `RAGMaxEntries`
**Type:** `int` | **Default:** `3`

Maximum number of RAG entries retrieved from the memory vault per query. Increase this to give the model more retrieved context; decrease it to keep prompts shorter and more focused.

---

### `WorldInfoMaxEntries`
**Type:** `int` | **Default:** `3`

Maximum number of WorldInfo entries that can be injected into the prompt at once (when `AllowWorldInfo` is `true`).

---

### `RAGIndex`
**Type:** `int` | **Default:** `3`

The position (message index from the end of the chatlog) at which RAG entries are injected. Set to `-1` to inject them into the system prompt instead. Placing them a few messages before the end of the chatlog keeps them contextually close to the model's current focus.

---

### `RAGEmbeddingSize`
**Type:** `int` | **Default:** `1024`

The dimensionality of the embedding vectors produced by the RAG model. This must match the actual output size of the model specified in `RAGModelPath`. The default `1024` matches the recommended `gte-large` model.

---

### `RAGMValue`
**Type:** `int` | **Default:** `15`

The `M` parameter for the HNSW (Hierarchical Navigable Small World) graph used by the vector index. Higher values increase search accuracy at the cost of more memory and slightly slower index construction. The default of `15` is a good balance for most use cases.

---

### `RAGDistanceCutOff`
**Type:** `float` | **Default:** `0.1f`

The maximum cosine distance allowed for a retrieved entry. Cosine distance runs from `0` (identical) to `2` (opposite):

- **Lower values** (e.g., `0.05`) = stricter matching, only very similar results are returned.
- **Higher values** (e.g., `0.3`) = more permissive, more loosely related results come through.

Values between `0.1` and `0.2` work well with the recommended `gte-large` embedding model. Increase this if retrieval feels too sparse; decrease it if irrelevant entries are being injected.

---

### `RAGHeuristic`
**Type:** `RAGSelectionHeuristic` | **Default:** `RAGSelectionHeuristic.SelectSimple`

The graph traversal algorithm used during vector search:

| Value | Description |
|-------|-------------|
| `SelectSimple` | Simple graph traversal — fast, works well for most datasets |
| `SelectHeuristic` | Heuristic traversal — better results on large or highly varied datasets |
| `SelectExact` | Exact distance calculation — highest accuracy, slightly slower |

---

## 7. Web Search API Settings

### `WebSearchAPI`
**Type:** `BackendSearchAPI` | **Default:** `BackendSearchAPI.DuckDuckGo`

Selects the search provider used when the agent performs web searches:

| Value | Description |
|-------|-------------|
| `DuckDuckGo` | No registration required. On `OpenAI` backend: returns a basic AI-generated summary. On `KoboldAPI` with a properly configured KoboldCpp instance: provides detailed full-page results. |
| `Brave` | Requires a free registration and API key from [Brave Search](https://brave.com/search/api/). Always provides detailed structured results. |

---

### `WebSearchBraveAPIKey`
**Type:** `string` | **Default:** `""` (empty)

Your Brave Search API key. Only used when `WebSearchAPI` is `BackendSearchAPI.Brave`. Leave empty when using DuckDuckGo.

---

### `WebSearchDetailedResults`
**Type:** `bool` | **Default:** `true`

When `true`, the library attempts to scrape the full content of the most relevant search result pages, rather than relying solely on the search API's summary snippet. Produces more thorough answers at the cost of a bit more processing time.

---

## 8. Group Chat Settings

> These settings only take effect when using the group chat system. See [LLMSYSTEM.md](LLMSYSTEM.md) for an overview of how group chats work.

### `GroupSecondaryPersonaSeePastSessions`
**Type:** `GroupChatPastSessionMode` | **Default:** `GroupChatPastSessionMode.All`

Controls whether secondary personas in a group chat have access to session summaries from previous conversations (requires `SessionMemorySystem = true`):

| Value | Description |
|-------|-------------|
| `None` | Secondary personas have no memory of past sessions |
| `ActiveOnly` | They see summaries only from sessions in which they were active |
| `All` | They see summaries from all past sessions |

---

### `GroupInstructFormatAdapter`
**Type:** `bool` | **Default:** `false`

When `true`, group chat messages are reordered so that `user` and `assistant` roles strictly alternate for each persona, even if the original message sequence doesn't naturally alternate. Some models require strict role alternation to function correctly. Enable this only if your group chat responses are becoming incoherent or malformed.

---

### `CommitGroupSessionToSecondaryPersonaHistory`
**Type:** `bool` | **Default:** `false`

By default, when a group chat session ends, only the main persona's history is updated. When this is `true`, group chat activity is also written into the secondary personas' individual histories. This means they will remember group conversations even when later used outside of a group context. Enable this if you want secondary personas to have a persistent memory of group interactions.

---

## Full Reference Table

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `DataPath` | `string` | `"data/chars/"` | Folder for persona brain and agent data files |
| `BackendUrl` | `string` | `"http://localhost:5001"` | Backend server URL or GGUF file path |
| `BackendAPI` | `BackendAPI` | `KoboldAPI` | API protocol to use |
| `OpenAIKey` | `string` | `"123"` | API key for OpenAI-compatible backends |
| `LlamaSharpGPULayers` | `int` | `255` | GPU layers to offload (LlamaSharp only) |
| `LlamaSharpFlashAttention` | `bool` | `true` | Enable Flash Attention (LlamaSharp only) |
| `LlamaSharpNoKVoffload` | `bool` | `false` | Keep KV cache in CPU RAM (LlamaSharp only) |
| `OpenAIProcessAllImages` | `bool` | `false` | Send all images in history to OpenAI (not just the latest) |
| `MaxTotalTokens` | `int` | `16384` | Total token budget (context window size) |
| `MaxReplyLength` | `int` | `512` | Max tokens the model may generate per reply |
| `ImageEmbeddingSize` | `int` | `768` | Dimensionality of image embeddings |
| `ScenarioOverride` | `string` | `""` | Replaces the character's scenario field |
| `StopGenerationOnFirstParagraph` | `bool` | `false` | Stop generating after the first paragraph |
| `DisableThinking` | `bool` | `false` | Suppress thinking block on reasoning models |
| `AllowWorldInfo` | `bool` | `true` | Enable keyword-triggered WorldInfo snippets |
| `MoveAllInsertsToSysPrompt` | `bool` | `false` | Force all RAG/WI/memory inserts into the system prompt |
| `DisableDateAndMoodIfNotLastSession` | `bool` | `true` | Suppress date/mood modifiers when viewing historical sessions |
| `SessionMemorySystem` | `bool` | `false` | Enable long-term session memory summaries |
| `SessionHandling` | `SessionHandling` | `FitAll` | How much conversation history to include in the chatlog |
| `SessionReservedTokens` | `int` | `2048` | Token budget reserved for session summaries |
| `CutInTheMiddleSummaryStrategy` | `bool` | `false` | Preserve session start + end when summarizing (vs. end only) |
| `AntiHallucinationMemoryFormat` | `bool` | `true` | Add prompt guidance for handling mid-conversation system messages |
| `SessionDetailedSummary` | `bool` | `false` | Generate a richer, more detailed session summary |
| `SentimentEnabled` | `bool` | `true` | Enable sentiment analysis (WIP — recommend `false`) |
| `SentimentModelPath` | `string` | `"data/classifiers/emotion-bert-classifier.gguf"` | Path to emotion classifier model |
| `SentimentGoEmotionHeadPath` | `string` | `"data/classifiers/goemotions_head.json"` | Path to GoEmotions head config |
| `SentimentThresholdsPath` | `string` | `"data/classifiers/optimized_thresholds.json"` | Path to per-class threshold file |
| `RAGEnabled` | `bool` | `true` | Enable RAG retrieval |
| `RAGModelPath` | `string` | `"data/classifiers/gte-large.Q6_K.gguf"` | Path to GGUF embedding model |
| `RAGMoveToThinkBlock` | `bool` | `false` | Inject RAG results into thinking block (experimental) |
| `RAGConvertTo3rdPerson` | `bool` | `true` | Rewrite queries in 3rd person before embedding search |
| `RAGMaxEntries` | `int` | `3` | Max RAG entries to retrieve per query |
| `WorldInfoMaxEntries` | `int` | `3` | Max WorldInfo entries to inject per prompt |
| `RAGIndex` | `int` | `3` | Chatlog insertion position for RAG entries (`-1` = system prompt) |
| `RAGEmbeddingSize` | `int` | `1024` | Dimensionality of RAG embedding vectors |
| `RAGMValue` | `int` | `15` | HNSW graph M parameter |
| `RAGDistanceCutOff` | `float` | `0.1` | Max cosine distance for RAG retrieval |
| `RAGHeuristic` | `RAGSelectionHeuristic` | `SelectSimple` | Vector search algorithm |
| `WebSearchAPI` | `BackendSearchAPI` | `DuckDuckGo` | Web search provider |
| `WebSearchBraveAPIKey` | `string` | `""` | Brave Search API key |
| `WebSearchDetailedResults` | `bool` | `true` | Scrape full page content for search results |
| `GroupSecondaryPersonaSeePastSessions` | `GroupChatPastSessionMode` | `All` | Past session visibility for secondary personas |
| `GroupInstructFormatAdapter` | `bool` | `false` | Force strict role alternation in group chats |
| `CommitGroupSessionToSecondaryPersonaHistory` | `bool` | `false` | Save group chat activity to secondary persona histories |
