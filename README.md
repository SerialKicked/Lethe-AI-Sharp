# Lethe AI - A C# Middleware LLM Library

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
![GitHub Stars](https://img.shields.io/github/stars/SerialKicked/Lethe-AI-Sharp)

## 🚀 What Is It?

Lethe AI Sharp is a modular, object‑oriented C# library connecting local or remote Large Language Model (LLM) backends to your applications. It is centered around **persona-driven AI entities**: define a pragmatic research assistant, an autonomous background agent, or even a creative roleplay companion, each with its own personality, memory, tools, mood behavior, and task system, then switch between them at runtime.

It unifies chat personas, conversation/session management, streaming inference, long‑term memory, RAG (retrieval augmented generation), background agentic tasks, web search tools, TTS, and structured output generation in one backend-agnostic system. *No matter the backend, setup, or model, you write the exact same code*.

It can connect to local or remote Large Language Model (LLM) backends, and also and comes with its own light backend so you can run local GGUF models directly without relying on external servers.

### No Python Dependencies
Pure .NET 10 C# implementation. No Python runtime, no conda environments, no pip hell.

### Self-Contained
Built-in LlamaSharp backend means you can distribute a **single executable** 
that runs LLMs locally. No external server required, but external servers are supported too (KoboldAPI, Llama.cpp, OpenAI Compatible, ...)

## 🎯 Use Cases
- **Chatbots** - Build context-aware assistants with long term memory and dynamic behaviors
- **Agent** - Give tool access and tasks to personalized semi-autonomous agents
- **Research Tools** - Combine web search with LLM analysis
- **Content Generation** - Structured output for automation pipelines

## 🔥 Minimal Example

```csharp
// 1. Setup (choose backend style)
LLMEngine.Setup("http://localhost:1234", BackendAPI.OpenAI);

// 2. Connect
await LLMEngine.Connect();
if (LLMEngine.Status != SystemStatus.Ready)
    throw new Exception("Backend not ready");

// 3. One-shot generation
var pb = LLMEngine.GetPromptBuilder();
pb.AddMessage(AuthorRole.System, "You're an helpful and friendly bot!");
pb.AddMessage(AuthorRole.User, "Explain gravity in one friendly paragraph.");
var query = pb.PromptToQuery();
var reply = await LLMEngine.SimpleQuery(query);
Console.WriteLine(reply.Text);

// 4. Streaming variant (with cancellation)
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
LLMEngine.OnInferenceStreamed += (_, token) => Console.Write(token);
await LLMEngine.SimpleQueryStreaming(query, cts.Token);
```

## 🧩 Compatible Backends
- **Llama.cpp:** Gold standard for local inference. [Llama.cpp](https://github.com/ggml-org/llama.cpp) offers the most complete feature-set.
- **Kobold API:** Powerful text completion API, used by [KoboldCpp](https://github.com/LostRuins/koboldcpp).
- **OpenAI API:** Industry standard chat completion API, used by [LM Studio](https://lmstudio.ai/), [Text Gen WebUI](https://github.com/oobabooga/text-generation-webui), and many other backends.

Remote endpoints should work but primary focus remains local / LAN latency. 

Alternatively, if running an external backend is too much, **Lethe AI** also comes with its internal "backend" to load local models (in the GGUF format) directly from your application. It uses [LLamaSharp](https://github.com/SciSharp/LLamaSharp) as a base, a C# port of LLama.cpp.


| Capability | Kobold API | Llama.cpp | OpenAI API | Internal |
|------------|------------|-----------|------------|----------|
| Text generation mode | ✅ Text | ✅ Chat / Text | ✅ Chat | ✅ Text |
| Streaming | ✅ | ✅ | ✅ | ✅ |
| Structured output | ✅ GBNF | ✅ Schema / GBNF | ✅ Schema | ✅ GBNF |
| Chain of thoughts | ✅ | ✅ | ✅ | ✅ |
| Personas & chat sessions | ✅ | ✅ | ✅ | ✅ |
| Memory integration | ✅ | ✅ | ✅ | ✅ |
| Samplers | ✅ Advanced  | ✅ Advanced |  ✅ Basics  | ✅ Advanced |
| Token management | ✅ Exact | ✅ Exact | ⚠️ Estimated  | ✅ Exact |
| Web search | ✅ | ✅ | ✅ | ✅ |
| Tool-calling *(1)* | ❌ | ✅ (Chat) | ✅  | ❌ |
| Text-to-speech | ✅ (if loaded) | ❌ | ❌ | ❌ |
| Image input *(2)* | ⚠️ In theory | ✅ (Chat) | ✅ | ❌ |

1) Function calling support depends largely on the LLM's capabilities.
2) VLM support depends entirely on underlying server and LLM capabilities. KoboldCpp has notoriously bad image input support, so if you need image support, load it as a OpenAI-compatible backend instead and make sure it's in Jinja template mode, then it will behave properly.

## ⭐ Core Features
- Persona system (bot & user role objects, custom prompts, instruction formats)
- Session-based chatlog with automated summarization
- LLM message streaming support
- Long‑term memory system + world info triggers
- RAG with vector search (HNSW) + embeddings
- Extensible background “agentic tasks” (search the web, summarization)
- Structured output (GBNF / JSON schema) for tool pipelines
- Web search integration (DuckDuckGo, Brave API)
- Useful LLM related tools (token counting, GBNF grammar, text manipulation helpers)
- Visual language model support
- Framework for group chat functionalities (one user and multiple AI characters)

## 📝 Long Term Memory and RAG
- United MemoryUnit format used by all long term memory systems
- Summaries of recent chat sessions into the system prompt
- Keyword-triggered text insertions (also known as "world info" in many frontends)
- Automatic and configurable insertion of relevant chat summaries into the context
- Customizable RAG system using the Small World implementation
- Fact-based discovery: the bot learns about the user over time, helping with the recall of other memory units.

## 🧠 Agentic System
- Extensible tool-calling functionalities, make custom toolsets that can be mixed and matched
- Customizable tasks can run in the background while the user is AFK
- Includes 2 default tasks that run relevant web searches and mention results in following chat session
- Write your own tasks and tools easily to boost your bot's abilities, can be imported from external dll too

## 👀 See it in action

<img width="1920" height="1032" alt="LetheChat_QrSGcp5ZBb" src="https://github.com/user-attachments/assets/7a81b84c-4c64-4249-9dfc-e5e210213787" />

To demonstrate how powerful **Lethe AI** can be, check out [Lethe AI Chat](https://github.com/SerialKicked/ChatAI/). This is a powerful AI chat program for _Windows_ that uses most of the features present in the library. It comes with its own integrated editors, extended agentic tasks, and extensive settings. It can rival with most of the dedicated AI chat programs currently available.

## 📦 Installation

Right now, the best way to use the library is to add this repo as a submodule or project reference in your C# solution. NuGet package coming soon.

### Install via Git Submodule
```bash
git submodule add https://github.com/SerialKicked/Lethe-AI-Sharp.git
````

### Manual Install
git clone, or download the project files in a new folder, and add it directly to your solution's project list. This gives you more control and full access to the source code.

### Optional Model
Place it into `data/classifiers/` (configure their *build action* to “Copy if newer”):
| File | Purpose | Required? |
|---------|------|-----------|
| [gte-large.Q6_K.gguf](https://huggingface.co/SerialKicked/Lethe-AI-Repo/resolve/main/gte-large.Q6_K.gguf?download=true) | Embeddings for RAG & Memory similarity | Yes for everything memory or RAG related |

## 🔎 Usage and Documentation

**New users**: Start with the [Quick Start Guide](Docs/QUICKSTART.md) to get running in 5 minutes!

### Learning Pathways

For comprehensive documentation, check the `Docs/` folder:
- [LLM System Documentation](Docs/LLMSYSTEM.md) - Core LLMEngine functionality, personas, and chat management
- [Instruction Format Guide](Docs/INSTRUCTFORMAT.md) - Configuring message formatting for different models
- [Personas](Docs/PERSONAS.md) - Create and customize personas
- [Persona Cookbook](Docs/PERSONA_COOKBOOK.md) - Side-by-side persona designs and runtime switching patterns
- [Memory System](Docs/MEMORY.md) - Understand the various memory systems and how they interact
- [Examples](Docs/Examples/) - Working code samples and tutorials

## 🤝 Third Party Libraries

*Lethe AI Sharp* relies on the following libraries and tools to work.
- [LlamaSharp](https://github.com/SciSharp/LLamaSharp/) - Used as a backend-agnostic embedding system
- [General Text Embedding - Large](https://huggingface.co/thenlper/gte-large) - Embedding model used as our default (works best in english)
- [HNSW.NET](https://github.com/curiosity-ai/hnsw-sharp) - Used for everything related to RAG / Vector Search
- [Newtonsoft Json](https://www.newtonsoft.com/json) - Practically all the classes can be imported and exported in Json
- [OpenAI-DotNet](https://github.com/RageAgainstThePixel/OpenAI-DotNet) - Used for OpenAI API backend compatibility
