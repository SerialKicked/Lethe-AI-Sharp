# EsKa's AI Toolkit - A C# Middleware LLM Library

Powerful, highly configurable, general purpose library used to connect a local backend running a Large Language Model (LLM) to a front-end.

This is the main library used by [wAIfu.Net](https://github.com/SerialKicked/ChatAI), a feature-complete AI chat application for Windows.

## What is This?

Simply put, this library offers a bunch of tools for those who want to code their own C# front-end or LLM-powered tools without having to do all the heavy lifting. It can  happily connect itself to the most popular backends (the program loading the LLM proper) and allows your code to "speak" with the LLM is a few function calls. 

It includes easy-to-use (and easy to build upon) systems to handle most of the operations you'd wish to do with a LLM, alonside a bunch of related functions like RAG, web search, text to speech, or string manipulation.

## Compatible Backends
- **Kobold API:** Used by [KoboldCpp](https://github.com/LostRuins/koboldcpp). This is the recommanded backend with the most features.
- **OpenAI API:** Used by [LM Studio](https://lmstudio.ai/), [Text Generation WebUI](https://github.com/oobabooga/text-generation-webui), and others. Less features.

## Main Features
- Easy to use classes for bot personas, system prompts, instruction formats, and inference settings
- Session-based chatlog with automated summaries for past sessions
- Streamed (or not) inference / reroll / and impersonate functions
- Support CoT / "thinking" models out of the box
- Many useful tools to manipulate text, count tokens
- GBNF grammar generation directly from a class's structure
- Tools for reliable Web Search (DuckDuckGo and Brave)
- Text To Speech support (through the *Kobold API* only)

## Long Term Memory System
- Keyword-triggered text insertions (also known as "world info" in many frontends)
- Customizable RAG System using the Small World implementation
- Automatic (optional) and configurable insertion of relevant past chat sessions into the context

## Advanced Features / WIP
- Basic support for VLM (visual language models) depending on the backend
- Background Agent system (bot can run tasks in the background)
- Group chat functionalities (one user and multiple AI characters)

## Usage and Documentations

Check the `Docs/` folder for detailed instructions on how to use this library. The [LLM system docs](Docs/LLMSYSTEM.md) to get you started. You can go further with this doc about [Extending the Chatlog and Persona](Docs/LLMSYSTEM.md) functionalities.

## Third Party Libraries

*AI Toolkit* relies on the following libraries and tools to work.
- [LlamaSharp](https://github.com/SciSharp/LLamaSharp/) - Used as a backend-agnostic embedding system
- [General Text Embedding - Large](https://huggingface.co/thenlper/gte-large) - Embedding model used as our default (works best in english)
- [HNSW.NET](https://github.com/curiosity-ai/hnsw-sharp) - Used for everything related to RAG / Vector Search
- [Newtonsoft Json](https://www.newtonsoft.com/json) - Practically all the classes can be imported and exported in Json
- [OpenAI .Net API Library](https://github.com/openai/openai-dotnet) - Used for OpenAI API backend compatibility
