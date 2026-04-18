# Persona Cookbook

This page is a **showcase** of what you can build with Lethe AI personas.  
If you need field-by-field API details, read [PERSONAS.md](PERSONAS.md).  
If you need tool/task APIs, read [AGENTS.md](AGENTS.md).  

---

Each `BasePersona` is a distinct AI entity with its own behavior, memory style, and capabilities.
You can switch active personas at runtime with `LLMEngine.Bot = ...`.
Setting `LLMEngine.Bot` ends the previous persona chat and starts the new one.

---

## One-time setup used by the examples

```csharp
using LetheAISharp.Agent.Tools;
using LetheAISharp.Files;
using LetheAISharp.LLM;

LLMEngine.Setup("http://localhost:5001", BackendAPI.KoboldAPI);
await LLMEngine.Connect();

// Register toolsets once (IDs come from each toolset's .Id property)
var memoryTools = new MemoryTools();
var webSearchTools = new WebSearchTools();
LLMEngine.ToolManager.RegisterToolList(memoryTools);
LLMEngine.ToolManager.RegisterToolList(webSearchTools);
```

---

## Persona A — Creative / Roleplay Character (tools/tasks intentionally OFF)

```csharp
var creativeRoleplayPersona = new BasePersona
{
    Name = "Nya",
    UniqueName = "roleplay_nya",
    Bio = "A playful catgirl who lives entirely in a cozy fantasy café world. She speaks in a warm, expressive style and stays in-character.",
    Scenario = "You are Nya, a roleplay companion in a fantasy café. Keep immersion strong, avoid real-world factual claims, and focus on atmosphere and character chemistry.",
    FirstMessage = new List<string>
    {
        "Nya stretches on the windowsill, tail swaying. \"Hehe~ welcome back, {{user}}. Tea, stories, or both?\""
    },
    ExampleDialogs = new List<string>
    {
        "Use sensory and emotional language to make scenes vivid.",
        "Stay in roleplay mode and avoid practical assistant behavior unless explicitly requested.",
        "React to emotional tone shifts naturally (playful, gentle, supportive)."
    },
    // Deliberately disable autonomous and tool behavior:
    AgentMode = false,
    AgentTasks = new List<string>(),
    OverrideDefaultToolset = true,
    Tools = new HashSet<string>(),

    // Keep guidance enabled so mood/context inserts can still shape tone:
    DisableBotGuidance = false,

    // Roleplay-focused memory style:
    SenseOfTime = false,
    DatesInSessionSummaries = false
};
```

Why this setup:
- No tools (`OverrideDefaultToolset = true`, `Tools = []`) to preserve immersion.
- No agent tasks to keep behavior reactive and in-session.
- Guidance remains enabled so memory/mood inserts can steer tone.

Mood setup for this persona:

```csharp
MoodManager.LoadDefaultMoods(); // registers MoodCheer, MoodCuriosity, MoodEnergy

creativeRoleplayPersona.Brain.MoodHandling = true;
creativeRoleplayPersona.Brain.StaticMood = false; // set true to freeze mood values
```

Moodlets are `0.0–1.0` axes tracked by `MoodManager`.
Each moodlet evolves over time (`OnTimePassed`), reacts to user input (`InterpretMessage`), and outputs adjectives at thresholds (`GetAdjective`).
`MoodManager.Describe()` builds a line such as `"{{mchar}} is currently feeling happy, curious."` (`{{mchar}}` is the character-name template placeholder), which is inserted into prompt context when mood handling is active.
Default moodlets are Cheer (`sad → moody → happy → joyful`), Curiosity (`disinterested → curious → inquisitive`), and Energy (similar low→high adjective range).
`IMoodlet` is an interface (`Id`, `Description`, `NaturalChangeRate`, `NaturalValue`, `StartingValue`, `GetAdjective`, `InterpretMessage`, `OnTimePassed`, `ProcessNewSession`), so you can add domain-specific mood axes.

---

## Persona B — Productivity / Research Assistant (capabilities intentionally ON)

```csharp
var researchAssistantPersona = new BasePersona
{
    Name = "Astra",
    UniqueName = "assistant_astra",
    Bio = "A practical research and planning assistant focused on accurate information, concise summaries, and dependable follow-through.",
    Scenario = "You are Astra, a productivity-oriented assistant. Use tools when useful, cite uncertainty, and help the user plan actionable next steps.",
    FirstMessage = new List<string>
    {
        "Hi {{user}} — I can research topics, keep long-term notes, and help you maintain reminders/schedules."
    },
    ExampleDialogs = new List<string>
    {
        "When information may be outdated, run web search before answering.",
        "Persist useful user preferences and commitments to memory.",
        "When asked about plans, propose explicit next actions and dates."
    },

    AgentMode = true,
    AgentTasks = new List<string> { "ActiveResearchTask", "ResearchTask" }, // Research while user is AFK

    // Persona-specific tool access:
    OverrideDefaultToolset = true,
    Tools = new HashSet<string> { memoryTools.Id, webSearchTools.Id },

    SenseOfTime = true,
    DatesInSessionSummaries = true
};
```

Companion settings typically used with this persona:

```csharp
// Tool-calling must be globally allowed:
LLMEngine.Settings.ToolCallsAllowed = true;

// Keep extracted-facts retrieval enabled for better long-term recall:
LLMEngine.Settings.FactRetrievalEnabled = true;
```

Why this setup:
- `MemoryTools` enables self-memory edits, reminders, and schedules.
- `WebSearchTools` + research tasks enable autonomous info gathering between turns.
- Time-aware settings improve reminder/planning quality.

Weekly schedule/reminder behavior is enabled through memory tools (for example, via `SetSchedule(DayOfWeek.Monday, "...")` and `SetReminder(...)` calls made by the model).

---

## Runtime switching pattern

```csharp
// User persona can stay the same
LLMEngine.User = new BasePersona
{
    Name = "User",
    UniqueName = "default_user",
    IsUser = true
};

// Start with roleplay
LLMEngine.Bot = creativeRoleplayPersona;

// ... later switch to productivity mode
LLMEngine.Bot = researchAssistantPersona;
```

You can switch whenever your app context changes (chat tab, command mode, time of day, user intent, etc.).

---

## Composition model: capabilities are per persona

Treat these systems as **composable persona capabilities**, not a single global mode:

- Persona A: roleplay-centric behavior, no tools/tasks, mood handling enabled.
- Persona B: memory + web tools, background research tasks, fact retrieval workflow.

In other words, you are not turning one monolithic “agent mode” on for the entire app.  
You are designing **multiple AI entities** and selecting the right one by setting `LLMEngine.Bot`.
