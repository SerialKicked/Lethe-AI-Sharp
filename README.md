# Lethe AI - A C# Middleware LLM Library

Powerful, object-oriented, and highly configurable, general purpose library used to connect a local back-end running a Large Language Model (LLM) to a front-end.

## ℹ️ What is This?

This library offers many tools and features for those who want to code their own C# front-end or LLM-powered tools without having to do all the heavy lifting. It can  happily connect itself to the most popular backends (the program loading the LLM proper) and allows your code to "speak" with the LLM is a few function calls. 

It includes easy-to-use (and easy to build upon) systems to handle most of the operations you'd wish to do with a LLM, alongside many advanced features like RAG, agentic systems, web search, text to speech, semantic similarity testing, and prompt manipulation.

## 🧩 Compatible Backends
- **Kobold API:** Used by [KoboldCpp](https://github.com/LostRuins/koboldcpp). This is the recommended backend with the most features.
- **OpenAI API:** Used by [LM Studio](https://lmstudio.ai/), [Text Generation WebUI](https://github.com/oobabooga/text-generation-webui), and others. Less features.

**Lethe AI** technically supports remote backends but this hasn't been tested, this library is mostly designed for local (or local network) LLM inference.

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
- Background agent system (bot can run tasks in the background)
- Analyze past chat sessions, run relevant web searches and mention results in next session
- Mood tracking + drift system (personality coloring over time)
- Goal‑driven behaviors (long‑term projects, self‑seeding topics of interest)

## 🛠️ Advanced Features (Work in progress / experimental)
- Group chat functionalities (one user and multiple AI characters)
- Sentiment analysis

## 👀 See it in action

To demonstrate how powerful **Lethe AI** can be, check out [Lethe AI Chat](https://github.com/SerialKicked/Lethe-AI-Chat/). This is a powerful AI chat program for _Windows_ that uses most of the features present in the library. It comes with its own integrated editors, extended agentic tasks, and extensive settings. It can rival with most of the dedicated AI chat programs currently available.

## 🔎 Installation

Right now, the best way to use that library is to add it to your C# solution as a library directly.

To take advantage of the RAG and Sentiment Analysis functionalities, you'll need to download 2 more files and place them into the `data/classifiers` folder of the library:
- [gte-large.Q6_K.gguf](https://huggingface.co/SerialKicked/Lethe-AI-Repo/resolve/main/gte-large.Q6_K.gguf?download=true) - Required for all RAG and Memory related operations
- [emotion-bert-classifier.gguf](https://huggingface.co/SerialKicked/Lethe-AI-Repo/resolve/main/emotion-bert-classifier.gguf?download=true) - Used for experimental and option Sentiment Analysis functions

Then, in Visual Studio (or whatever editor you're using) make sure to set the build action _"Copy to Output Directory"_ to _"Copy if newer"_ for both files.

## 🔎 Usage and Documentation

**New users**: Start with the [Quick Start Guide](Docs/QUICKSTART.md) to get running in 5 minutes!

For comprehensive documentation, check the `Docs/` folder:
- [LLM System Documentation](Docs/LLMSYSTEM.md) - Core LLMEngine functionality, personas, and chat management
- [Instruction Format Guide](Docs/INSTRUCTFORMAT.md) - Configuring message formatting for different models
- [Memory System](Docs/MEMORY.md) - Understand the various memory systems and how they interact
- [Examples](Docs/Examples/) - Working code samples and tutorials

## 🤝 Third Party Libraries

*AI Toolkit* relies on the following libraries and tools to work.
- [LlamaSharp](https://github.com/SciSharp/LLamaSharp/) - Used as a backend-agnostic embedding system
- [General Text Embedding - Large](https://huggingface.co/thenlper/gte-large) - Embedding model used as our default (works best in english)
- [HNSW.NET](https://github.com/curiosity-ai/hnsw-sharp) - Used for everything related to RAG / Vector Search
- [Newtonsoft Json](https://www.newtonsoft.com/json) - Practically all the classes can be imported and exported in Json
- [OpenAI .Net API Library](https://github.com/openai/openai-dotnet) - Used for OpenAI API backend compatibility
