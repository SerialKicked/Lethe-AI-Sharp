using LetheAISharp.Files;
using LetheAISharp.LLM;
using LetheAISharp.Skills;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OpenAI;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    /// <summary>
    /// Toolset giving the model access to the skill library: it can list (through the system prompt), search and load skills,
    /// and optionally create or update them when <see cref="SkillSettings.AllowAgentWrites"/> is enabled.
    /// </summary>
    public class SkillTools : IToolList
    {
        public string Id => "Skills";

        public string Description => "Reusable markdown instructions for specific tasks. Skills are listed in the prompt and loaded on demand; the model can also write new ones when allowed.";

        public string SystemPromptInstruction
        {
            get
            {
                var manager = LLMEngine.SkillManager;
                var index = manager.BuildIndex();
                var writes = LLMEngine.Settings.SkillOptions.AllowAgentWrites;
                var sb = new StringBuilder();
                sb.Append("You have access to a library of skills: reusable, step-by-step instructions for particular tasks. ");
                if (index.Length > 0)
                {
                    sb.Append("When the user's request matches one of the skills below, call Skill_Load with the skill's name BEFORE starting the task, then follow its instructions. ");
                    sb.Append("If you are unsure whether a relevant skill exists, call Skill_Search. Do not load skills that are unrelated to the current request. A loaded skill stays in your context, so do not load the same skill again unless its instructions are no longer visible.");
                }
                else
                {
                    sb.Append("The library is currently empty.");
                }
                if (writes)
                {
                    sb.Append(" When the user asks you to remember how to do something, or when you worked out a reliable procedure worth reusing, you can save it with Skill_Create (or improve an existing one with Skill_Update). The user will be asked to approve the change.");
                }
                if (index.Length > 0)
                {
                    sb.Append("\n\nAvailable skills:\n").Append(index);
                }
                return sb.ToString();
            }
        }

        private readonly List<Tool> readTools = [];
        private readonly List<Tool> writeTools = [];

        public IReadOnlyList<Tool> GetToolList()
        {
            if (LLMEngine.Settings.SkillOptions.AllowAgentWrites)
                return [.. readTools, .. writeTools];
            return readTools;
        }

        public bool RequiresConfirmation(string functionName)
        {
            return functionName == nameof(Skill_Create) || functionName == nameof(Skill_Update);
        }

        public void LoadTools(bool clearExisting = false)
        {
            readTools.Clear();
            writeTools.Clear();
            if (clearExisting)
            {
                Tool.ClearRegisteredTools();
            }
            readTools.Add(Tool.GetOrCreateTool(this, nameof(Skill_Load), "[Skills] Load the full instructions of a skill by name. Use this before starting a task that matches one of the available skills, then follow the instructions it contains. Do not reload a skill that is already loaded earlier in the conversation."));
            readTools.Add(Tool.GetOrCreateTool(this, nameof(Skill_Search), "[Skills] Search the skill library for skills relevant to a task. Provide a short description of the task or a few keywords. Returns skill names and descriptions, use Skill_Load to read one."));
            writeTools.Add(Tool.GetOrCreateTool(this, nameof(Skill_Create), "[Skills] Save a new reusable skill. Provide a short lowercase hyphenated name, a description explaining what it does and when to use it, and the full instructions in markdown (sections like 'When to use', 'Steps', 'Notes and pitfalls'). The user must approve it."));
            writeTools.Add(Tool.GetOrCreateTool(this, nameof(Skill_Update), "[Skills] Replace the instructions of an existing skill with an improved version. Provide the skill name and the complete new content in markdown. Optionally provide a new description. The user must approve it."));
        }

        public void UnloadTools()
        {
            foreach (var tool in readTools.Concat(writeTools))
            {
                Tool.TryUnregisterTool(tool);
            }
            readTools.Clear();
            writeTools.Clear();
        }

        #region *** Duplicate load protection ***

        // Remembers, for the current generation, which skills were already loaded (keyed by the last history message at that time).
        // Tool results are only written to the chatlog when a generation ends, so this is what catches repeated loads inside one response.
        private readonly Dictionary<string, Guid> _loadedThisResponse = new(StringComparer.OrdinalIgnoreCase);
        private readonly Lock _loadLock = new();

        private const string SupersededMarker = "[Older copy removed:";

        /// <summary>
        /// Finds the earlier Skill_Load results for a skill in the current chat session, and estimates if the most recent one is still part of the prompt.
        /// </summary>
        private static List<SingleMessage> FindPriorLoads(string skillName, out bool likelyVisible)
        {
            likelyVisible = false;
            var found = new List<SingleMessage>();
            try
            {
                var messages = LLMEngine.History.CurrentSession.Messages;
                var limit = LLMEngine.Settings.ToolCallChainLimit;
                int chainsAfter = 0;
                int tokensAfter = 0;
                bool newestFound = false;
                for (int i = messages.Count - 1; i >= 0; i--)
                {
                    var m = messages[i];
                    if (m.Role == AuthorRole.Assistant && m.ToolCalls.Count > 0)
                    {
                        chainsAfter++;
                        continue;
                    }
                    if (m.Role == AuthorRole.Tool && m.ToolCalls.Count > 0 && m.ToolCalls[0].FunctionName == nameof(Skill_Load)
                        && !m.Message.StartsWith(SupersededMarker, StringComparison.Ordinal) && !m.Message.Contains(SupersededMarker, StringComparison.Ordinal)
                        && ReferencesSkill(m.ToolCalls[0], skillName))
                    {
                        found.Add(m);
                        if (!newestFound)
                        {
                            newestFound = true;
                            // Conservative estimate: the chain limit and the context budget both drop old messages first.
                            var withinChains = limit <= 0 || chainsAfter + 3 < limit;
                            var withinContext = tokensAfter < LLMEngine.MaxContextLength * 0.4;
                            likelyVisible = withinChains && withinContext;
                        }
                        continue;
                    }
                    if (!newestFound && m.Message.Length > 0)
                        tokensAfter += TokenTools.CountTokensApprox(m.Message);
                }
            }
            catch
            {
                // no active chat (or a malformed record): behave as if nothing was loaded before
                found.Clear();
                likelyVisible = false;
            }
            return found;
        }

        private static bool ReferencesSkill(ToolCallRecord record, string skillName)
        {
            try
            {
                var args = JObject.Parse(record.GetArgumentsText());
                var requested = args["name"]?.ToString();
                var resolved = LLMEngine.SkillManager.Get(requested);
                return resolved != null && resolved.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static Guid CurrentHistoryMarker()
        {
            try
            {
                return LLMEngine.History.LastMessage()?.Guid ?? Guid.Empty;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        #endregion

        /// <summary>
        /// Returns the full instructions of a skill.
        /// </summary>
        /// <param name="name">Name of the skill to load, as shown in the list of available skills.</param>
        /// <param name="reload">Set to true to load the skill again even if it was already loaded earlier in the conversation.</param>
        public async Task<string> Skill_Load(
            [FunctionParameter("Name of the skill to load, exactly as shown in the list of available skills.")] string name,
            [FunctionParameter("Only set to true if the skill was loaded earlier but its instructions are no longer visible to you.")] bool reload = false)
        {
            await Task.Yield();
            var skill = LLMEngine.SkillManager.Get(name);
            if (skill == null || !skill.Enabled)
            {
                var names = string.Join(", ", LLMEngine.SkillManager.Enabled.Take(30).Select(s => s.Name));
                return $"Error: skill '{name}' not found. " + (names.Length > 0 ? "Available skills: " + names : "The skill library is empty.");
            }

            var marker = CurrentHistoryMarker();
            var already = $"Skill '{skill.Name}' is already loaded in this conversation. Keep following the instructions from its earlier Skill_Load result. " +
                "If you can no longer see them, call Skill_Load again with reload set to true.";

            if (!reload)
            {
                lock (_loadLock)
                {
                    if (_loadedThisResponse.TryGetValue(skill.Name, out var g) && g == marker)
                        return already;
                }
            }

            var prior = FindPriorLoads(skill.Name, out var likelyVisible);
            if (!reload && likelyVisible)
                return already;

            // Full load. Any older copy still stored in the chat is replaced by a short note so that only the newest copy takes space.
            foreach (var old in prior)
            {
                old.Message = $"# Skill: {skill.Name}\n{SupersededMarker} these instructions were loaded again later in the conversation, use the most recent copy.]";
            }
            lock (_loadLock)
            {
                _loadedThisResponse[skill.Name] = marker;
            }
            LLMEngine.Logger?.LogInformation("[Skills] Skill loaded by the model: {name}", skill.Name);
            return $"# Skill: {skill.Name}\n\n{skill.Body}";
        }
        /// <summary>
        /// Searches the skill library.
        /// </summary>
        /// <param name="query">Short description of the task or a few keywords.</param>
        public async Task<string> Skill_Search([FunctionParameter("A short description of the task, or a few keywords.")] string query)
        {
            var results = await LLMEngine.SkillManager.SearchAsync(query, 5).ConfigureAwait(false);
            if (results.Count == 0)
                return $"No skill matches '{query}'.";
            var sb = new StringBuilder();
            sb.AppendLine($"Skills matching '{query}' (use Skill_Load to read one):").AppendLine();
            foreach (var r in results)
            {
                sb.AppendLine($"- {r.Skill.Name}: {r.Skill.Description}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Creates a new skill.
        /// </summary>
        public async Task<string> Skill_Create(
            [FunctionParameter("Short identifier in lowercase letters, digits and hyphens (for example: 'write-commit-messages').")] string name,
            [FunctionParameter("One or two sentences explaining what the skill does AND when it should be used.")] string description,
            [FunctionParameter("The full instructions of the skill, in markdown.")] string content,
            [FunctionParameter("Optional comma-separated keywords that someone would use when asking for this task.")] string keywords = "")
        {
            await Task.Yield();
            if (!LLMEngine.Settings.SkillOptions.AllowAgentWrites)
                return "Error: creating skills is not allowed.";
            var manager = LLMEngine.SkillManager;
            var slug = Skill.Slugify(name);
            if (slug.Length == 0)
                return "Error: invalid skill name, use lowercase letters, digits and hyphens.";
            if (manager.Get(slug) != null)
                return $"Error: a skill named '{slug}' already exists. Use Skill_Update to change it.";

            // If the host application can't ask the user for approval, the skill is saved disabled so that it gets reviewed first.
            var approved = LLMEngine.ToolCallConfirmation != null;
            var skill = new Skill
            {
                Name = slug,
                Description = description,
                Keywords = [.. (keywords ?? string.Empty).Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).Take(8)],
                Body = content,
                Source = SkillSource.Agent,
                Enabled = approved
            };
            var err = manager.Save(skill);
            if (err.Length > 0)
                return "Error: " + err;
            return approved
                ? $"Skill '{slug}' created and ready to be used."
                : $"Skill '{slug}' was saved but is disabled until the user reviews and enables it.";
        }

        /// <summary>
        /// Replaces the content of an existing skill.
        /// </summary>
        public async Task<string> Skill_Update(
            [FunctionParameter("Name of the existing skill to update.")] string name,
            [FunctionParameter("The complete new instructions of the skill, in markdown.")] string content,
            [FunctionParameter("Optional new description. Leave empty to keep the current one.")] string description = "")
        {
            await Task.Yield();
            if (!LLMEngine.Settings.SkillOptions.AllowAgentWrites)
                return "Error: editing skills is not allowed.";
            var manager = LLMEngine.SkillManager;
            var existing = manager.Get(name);
            if (existing == null)
                return $"Error: skill '{name}' not found. Use Skill_Create to make a new one.";

            var updated = new Skill
            {
                Name = existing.Name,
                Description = string.IsNullOrWhiteSpace(description) ? existing.Description : description,
                Keywords = [.. existing.Keywords],
                Body = content,
                Source = existing.Source,
                Enabled = existing.Enabled,
                FolderPath = existing.FolderPath,
                ExtraFrontmatter = [.. existing.ExtraFrontmatter]
            };
            var err = manager.Save(updated);
            if (err.Length > 0)
                return "Error: " + err;
            return $"Skill '{existing.Name}' updated.";
        }
    }
}
