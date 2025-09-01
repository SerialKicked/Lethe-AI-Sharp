This is a C# based repository for a middleware DLL used between a LLM backend and a UI Frontend. It is primarily used to simplify the heavy duty operations, offering powerful functions and premade file formats to the end developer. Please follow these guidelines when contributing:

## Code Standards

### Base Requirements
- Never change the target platform or NET version

## Repository Structure
- `Adapters/`: Handles the communication between the backend server and the DLL
- `Agent/`: Background agent system (run tasks in the background while the user is not active)
- `API/`: Core clients for the various backends
- `Chatlog/`: All classes related to the chat log (log itself, sessions, individual messages)
- `Files/`: Main file formats used to communicate with the LLM some of which (like LLMSettings and BasePersona) are intended to serve as virtual class to be built upon by the actual application
- `LLM/`: Core functionalities like LLM communications and RAG functionalities
- `Memory/`: Work in progress unified long term memory system
- `PromptBuilder/`: Used to convert from the library's internal chatlog format to Chat Completion and Text Completion formats that can be digested by a LLM
- `SearchAPI/`: Web search API integration
- `Tools/`: General purpose tools for string manipulation, token counter, and more

## Key Guidelines
1. Follow C# best practices and idiomatic patterns
2. Maintain existing code structure and organization
3. When using JSON, always favor using Newtonsoft JSON (already available) over the native json tools
4. Document public APIs and complex logic.
