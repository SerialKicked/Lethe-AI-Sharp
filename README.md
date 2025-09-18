# Lethe AI - A C# Middleware LLM Library

Powerful, object-oriented, and highly configurable, general purpose library used to connect a local back-end running a Large Language Model (LLM) to a front-end.

## ℹ️ What is This?

Simply put, this library offers a bunch of tools for those who want to code their own C# front-end or LLM-powered tools without having to do all the heavy lifting. It can  happily connect itself to the most popular backends (the program loading the LLM proper) and allows your code to "speak" with the LLM is a few function calls. 

It includes easy-to-use (and easy to build upon) systems to handle most of the operations you'd wish to do with a LLM, alongside many related features like RAG, web search, text to speech, semantic similarity testing, and string manipulation.

## 🧩 Compatible Backends
- **Kobold API:** Used by [KoboldCpp](https://github.com/LostRuins/koboldcpp). This is the recommended backend with the most features.
- **OpenAI API:** Used by [LM Studio](https://lmstudio.ai/), [Text Generation WebUI](https://github.com/oobabooga/text-generation-webui), and others. Less features.

## ⭐ Main Features
- Easy to use classes for bot personas, system prompts, instruction formats, and inference settings
- Session-based chatlog with automated summaries for past sessions
- Streamed (or not) inference / reroll / and impersonate functions
- Support CoT / "thinking" models out of the box
- GBNF grammar generation directly from a class's structure for structured output
- Basic support for VLM (visual language models) depending on the back-end
- Tools for reliable Web Search (DuckDuckGo and Brave)
- Text To Speech support (through the *Kobold API* only)
- Many useful tools to manipulate text, count tokens, and more

## 📝 Long Term Memory System
- Keyword-triggered text insertions (also known as "world info" in many frontends)
- Customizable RAG System using the Small World implementation
- Automatic (optional) and configurable insertion of relevant past chat sessions into the context

## 🧠 Agentic and Brain Module for personas
- Analyze past chat sessions, run relevant web searches and mention results in next session
- Mood tracking + drift system (personality coloring over time)
- Goal‑driven behaviors (long‑term projects, self‑seeding topics of interest)

## 🛠️ Advanced Features
- Background agent system (bot can run tasks in the background)
- Group chat functionalities (one user and multiple AI characters)

## 🔎 Installation

Right now, the best way to use that library is to add it to your C# solution as a library directly. 

NuGet packages are not yet available, but will be coming at some point.

## 🔎 Usage and Documentation

**New users**: Start with the [Quick Start Guide](Docs/QUICKSTART.md) to get running in 5 minutes!

For comprehensive documentation, check the `Docs/` folder:
- [LLM System Documentation](Docs/LLMSYSTEM.md) - Core LLMEngine functionality, personas, and chat management
- [Instruction Format Guide](Docs/INSTRUCTFORMAT.md) - Configuring message formatting for different models
- [Examples](Docs/Examples/) - Working code samples and tutorials

## 🤝 Third Party Libraries

*AI Toolkit* relies on the following libraries and tools to work.
- [LlamaSharp](https://github.com/SciSharp/LLamaSharp/) - Used as a backend-agnostic embedding system
- [General Text Embedding - Large](https://huggingface.co/thenlper/gte-large) - Embedding model used as our default (works best in english)
- [HNSW.NET](https://github.com/curiosity-ai/hnsw-sharp) - Used for everything related to RAG / Vector Search
- [Newtonsoft Json](https://www.newtonsoft.com/json) - Practically all the classes can be imported and exported in Json
- [OpenAI .Net API Library](https://github.com/openai/openai-dotnet) - Used for OpenAI API backend compatibility
