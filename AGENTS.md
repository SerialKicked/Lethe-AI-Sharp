# LetheAISharp — Agent guidance

This is a **C# library** (not an app) — a middleware DLL connecting LLM backends to UI frontends.
See `copilot-instructions.md` (repo root) for structure and style guidelines that must be preserved.

## Build & test

- **Build**: `dotnet build` (targets `net10.0`, single `.csproj`, single `.sln`)
- **Test**: No test framework exists. Tests are manual static classes with a `RunAllTests()` entry point (see `Files/Tests/GbnfConverterTest.cs`). Run via a throwaway console project or similar. No CI pipeline.
- **Lint/typecheck**: No configured tooling. Standard `dotnet build` compiler warnings are the only check.

## Code conventions

- **JSON**: Always use **Newtonsoft.Json** (`JsonConvert`, `JsonConvert.DeserializeObject<T>`, `[JsonProperty]`). Never use `System.Text.Json`.
- **Namespaces**: Do not enforce folder-namespace matching (IDE0130 suppressed in `.editorconfig`).
- **Naming rules**: IDE1006 (naming rule violations) is suppressed — don't flag or "fix" naming that already exists.
- **Nullable**: enabled (`<Nullable>enable</Nullable>`).

## Architecture

- **Single project** — no multi-package boundaries. All source lives under root directories: `LLM/`, `Adapters/`, `Agent/`, `Memory/`, `Personas/`, `Chatlog/`, `PromptBuilders/`, `SearchAPI/`, `Tools/`, `Files/`, `API/`.
- **Main entrypoint**: `LLM.LLMEngine` (static class). Everything flows through it: `Setup()` → `Connect()` → `SendMessageToBot()` / `SimpleQuery()`.
- **Backend adapters** in `Adapters/`: `KoboldCppAdapter`, `OpenAIAdapter`, `LlamaCppAdapter`, `LlamaSharpAdapter`. Picked via `BackendAPI` enum.
- **Persona persistence**: `.agent`, `.brain`, `.log` files in `LLMSettings.DataPath` ("data/chars/" by default), keyed by `BasePersona.UniqueName`.
- **Plugin DLLs**: Loaded at runtime via `LLMEngine.RegisterPlugin(path)` or `RegisterPluginsFromDirectory(dir)`. Scans for `IAgentTask`, `IAgentAction<,>`, `IToolList`, `IMoodlet`, and `IPluginEntry`.

## Documentation note

`Docs/AGENTS.md` is **library feature documentation** about the background agent system (not an AI assistant instruction file). Keep it as-is.

## Style

- Keep public API documented (`///` comments on public members).
- No emojis in source or docs.
- Use `PromptInserts`, `BasePersona`, `InstructFormat` from `Files/` namespace for file-format classes.
