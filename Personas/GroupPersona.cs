using LetheAISharp.Agent;
using LetheAISharp.Files;
using LetheAISharp.Memory;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

namespace LetheAISharp.LLM
{

    public abstract class GroupPersonaBase : BasePersona 
    {
        [JsonIgnore] 
        public override string Name { get => GetCurrentPersona()?.Name ?? string.Empty; set => GetCurrentPersona()!.Name = value; }
        [JsonIgnore] 
        public override string Bio { get => GetCurrentPersona()?.Bio ?? string.Empty; set => GetCurrentPersona()!.Bio = value; }

        /// <summary>
        /// Unique name of the primary persona who owns the chatlog and brain.
        /// This persona is the "main actor" and always exists.
        /// </summary>
        public string PrimaryPersonaName { get; set; } = string.Empty;

        /// <summary>
        /// List of unique names of secondary bot personas participating in the group conversation.
        /// Used for serialization to avoid nested persona objects in JSON.
        /// </summary>
        public List<string> SecondaryPersonaNames { get; set; } = [];

        /// <summary>
        /// Unique identifier of the currently active bot persona.
        /// Used for serialization/deserialization of CurrentBot.
        /// </summary>
        public string CurrentBotId { get; set; } = string.Empty;

        public virtual BasePersona? GetCurrentPersona()
        {
            var id = string.IsNullOrEmpty(CurrentBotId) ? PrimaryPersonaName : CurrentBotId;
            return LLMEngine.LoadedPersonas.TryGetValue(id, out var basePersona) ? basePersona : null;
        }

        public override string GetIdentifier()
        {
            return GetCurrentPersona()?.UniqueName ?? string.Empty;
        }

    }

    /// <summary>
    /// Represents a group persona that manages multiple bot personas for group chat scenarios.
    /// Uses a primary persona as the main actor and owner of the chatlog, with secondary personas as additional participants.
    /// </summary>
    /// <remarks>
    /// The GroupPersona class serves as a container and coordinator for group conversations with one primary and multiple secondary personas.
    /// Key features:
    /// - Primary persona owns the chatlog and agent system
    /// - All History and Brain properties redirect to the primary persona
    /// - Secondary personas share the chatlog but use their own brains for context
    /// - All personas go through full BeginChat()/EndChat() cycles
    /// - Provides group-specific macros like {{group}} for formatted persona lists
    /// </remarks>
    public class GroupPersona<TPersona> : GroupPersonaBase where TPersona : BasePersona
    {
        [JsonIgnore]
        public override string Name { get => CurrentBot?.Name ?? string.Empty; set => CurrentBot!.Name = value; }
        [JsonIgnore]
        public override string Bio { get => CurrentBot?.Bio ?? string.Empty; set => CurrentBot!.Bio = value; }
        [JsonIgnore]
        public override bool AgentMode { get => PrimaryBot?.AgentMode ?? false; set => PrimaryBot!.AgentMode = value; }
        [JsonIgnore]
        public override string SelfEditField { get => PrimaryBot?.SelfEditField ?? string.Empty; set => PrimaryBot!.SelfEditField = value; }
        [JsonIgnore]
        public override int SelfEditTokens { get => PrimaryBot?.SelfEditTokens ?? 0; set => PrimaryBot!.SelfEditTokens = value; }
        [JsonIgnore]
        public override bool SenseOfTime { get => PrimaryBot?.SenseOfTime ?? false; set => PrimaryBot!.SenseOfTime = value; }

        [JsonIgnore] 
        public override List<string> ExampleDialogs { get => CurrentBot?.ExampleDialogs ?? []; set => CurrentBot!.ExampleDialogs = value; }

        [JsonIgnore] 
        public override string Scenario { get => PrimaryBot?.Scenario ?? string.Empty; set => PrimaryBot!.Scenario = value; }


        /// <summary>
        /// The primary bot persona who owns the chatlog and acts as the main participant.
        /// This is populated dynamically during BeginChat() from LLMEngine.LoadedPersonas.
        /// </summary>
        [JsonIgnore]
        public TPersona? PrimaryBot { get; set; }

        /// <summary>
        /// List of secondary bot personas participating in the group conversation.
        /// These personas share the primary's chatlog but maintain their own brains.
        /// This is populated dynamically during BeginChat() from LLMEngine.LoadedPersonas.
        /// </summary>
        [JsonIgnore]
        public List<TPersona> SecondaryBots { get; set; } = [];

        /// <summary>
        /// The currently active/speaking bot persona in the group conversation.
        /// This determines which persona's brain/context is used for responses.
        /// Always falls back to PrimaryPersona if null or invalid.
        /// </summary>
        [JsonIgnore]
        public TPersona? CurrentBot
        {
            get => _currentBot ?? PrimaryBot;
            set => SetCurrentBot(value ?? throw new ArgumentException("CurrentBot cannot be null"));
        }
        private TPersona? _currentBot;

        /// <summary>
        /// Redirects to the primary persona's brain. The GroupPersona itself doesn't use its own brain.
        /// </summary>
        [JsonIgnore]
        public override Brain Brain
        {
            get => PrimaryBot?.Brain ?? base.Brain;
            set
            {
                if (PrimaryBot != null)
                    PrimaryBot.Brain = value;
                else
                    base.Brain = value;
            }
        }

        /// <summary>
        /// Redirects to the primary persona's chatlog. The GroupPersona itself doesn't maintain its own history.
        /// </summary>
        [JsonIgnore]
        public override Chatlog History
        {
            get => PrimaryBot?.History ?? base.History;
            set
            {
                if (PrimaryBot != null)
                    PrimaryBot.History = value;
                else
                    base.History = value;
            }
        }

        /// <summary>
        /// Redirects to the primary persona's agent system. The GroupPersona itself doesn't run its own agent.
        /// </summary>
        [JsonIgnore]
        public new AgentRuntime? AgentSystem
        {
            get => PrimaryBot?.AgentSystem;
            set
            {
                if (PrimaryBot != null)
                    PrimaryBot.AgentSystem = value;
                else
                    base.AgentSystem = value;
            }
        }

        /// <summary>
        /// Gets all personas in the group (primary + secondaries) as a unified list.
        /// </summary>
        [JsonIgnore]
        public List<TPersona> AllPersonas
        {
            get
            {
                var all = new List<TPersona>();
                if (PrimaryBot != null)
                    all.Add(PrimaryBot);
                all.AddRange(SecondaryBots);
                return all;
            }
        }

        public GroupPersona()
        {
            IsUser = false;
        }

        /// <summary>
        /// Sets the primary persona for the group. This persona will own the chatlog and brain.
        /// </summary>
        /// <param name="persona">The bot persona to set as primary. Must not be a user persona.</param>
        public virtual void SetPrimaryPersona(TPersona persona)
        {
            if (persona.IsUser)
                throw new ArgumentException("Cannot use a user persona as primary. Only bot personas are allowed.");

            if (string.IsNullOrEmpty(persona.UniqueName))
                throw new ArgumentException("Persona must have a valid UniqueName.");

            PrimaryPersonaName = persona.UniqueName;
            PrimaryBot = persona;

            // Set as current bot by default
            SetCurrentBot(persona);
        }

        /// <summary>
        /// Adds a secondary bot persona to the group conversation.
        /// </summary>
        /// <param name="persona">The bot persona to add. Must not be a user persona.</param>
        public virtual void AddSecondaryPersona(TPersona persona)
        {
            if (persona.IsUser)
                throw new ArgumentException("Cannot add user personas to group chat. Only bot personas are allowed.");

            if (string.IsNullOrEmpty(persona.UniqueName))
                throw new ArgumentException("Persona must have a valid UniqueName.");

            if (PrimaryPersonaName == persona.UniqueName)
                throw new ArgumentException("Cannot add the primary persona as a secondary persona.");

            if (!SecondaryPersonaNames.Contains(persona.UniqueName))
            {
                SecondaryPersonaNames.Add(persona.UniqueName);
                SecondaryBots.Add(persona);
            }
        }

        /// <summary>
        /// Removes a secondary bot persona from the group conversation.
        /// </summary>
        /// <param name="uniqueName">The unique name of the persona to remove.</param>
        public virtual void RemoveSecondaryPersona(string uniqueName)
        {
            var persona = SecondaryBots.FirstOrDefault(p => p.UniqueName == uniqueName);
            if (persona != null)
            {
                if (LLMEngine.Settings.CommitGroupSessionToSecondaryPersonaHistory)
                {
                    // Save chat history to this persona before removing
                    SaveSecondaryPersonaHistory(persona, true);
                }
                SecondaryPersonaNames.Remove(uniqueName);
                SecondaryBots.Remove(persona);

                // If we removed the current bot, switch back to primary
                if (_currentBot?.UniqueName == uniqueName)
                {
                    _currentBot = PrimaryBot;
                    CurrentBotId = PrimaryBot?.UniqueName ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Sets the currently active bot persona for the conversation.
        /// </summary>
        /// <param name="persona">The persona to set as current. Must be the primary or in the secondary list.</param>
        public virtual void SetCurrentBot(TPersona persona)
        {
            if (persona != PrimaryBot && !SecondaryBots.Contains(persona))
                throw new ArgumentException("Persona must be the primary or a secondary persona in the group.");

            _currentBot = persona;
            CurrentBotId = persona.UniqueName;
        }

        /// <summary>
        /// Sets the currently active bot persona by unique name.
        /// </summary>
        /// <param name="uniqueName">The unique name of the persona to set as current.</param>
        public virtual void SetCurrentBot(string uniqueName)
        {
            if (PrimaryBot?.UniqueName == uniqueName)
            {
                SetCurrentBot(PrimaryBot);
                return;
            }

            var persona = SecondaryBots.FirstOrDefault(p => p.UniqueName == uniqueName) ?? throw new ArgumentException($"No persona found with unique name: {uniqueName}");
            SetCurrentBot(persona);
        }

        /// <summary>
        /// Gets a formatted list of all bot personas (Name + Bio) for use in system prompts.
        /// This is used by the {{group}} macro.
        /// </summary>
        /// <param name="userName">The user's name for bio formatting.</param>
        /// <returns>Formatted string containing all bot personas information.</returns>
        protected override string GetGroupBio(string userName, bool mainfirst = false)
        {
            var all = AllPersonas;
            if (all.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();

            var firstone = mainfirst ? PrimaryBot : CurrentBot;

            if (firstone is not null)
            {
                if (mainfirst)
                    sb.AppendLinuxLine($"{LLMEngine.SystemPrompt.SubCategorySeparator} {firstone.Name}");
                else
                    sb.AppendLinuxLine($"{LLMEngine.SystemPrompt.SubCategorySeparator} {firstone.Name} (this is you)");
                sb.AppendLinuxLine();
                sb.AppendLinuxLine(firstone.GetBio(userName).CleanupAndTrim());
                sb.AppendLinuxLine();
            }

            foreach (var persona in all)
            {
                if (persona == firstone)
                    continue;
                sb.AppendLinuxLine($"## {persona.Name}");
                sb.AppendLinuxLine();
                sb.AppendLinuxLine(persona.GetBio(userName).CleanupAndTrim());
                sb.AppendLinuxLine();
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets the bio for the group context, which includes information about all participants.
        /// </summary>
        public override string GetBio(string otherName)
        {
            return CurrentBot?.GetBio(otherName) ?? PrimaryBot?.GetBio(otherName) ?? string.Empty;
        }

        /// <summary>
        /// Gets dialog examples from the current active bot in the group.
        /// </summary>
        public override string GetDialogExamples(string otherName)
        {
            return CurrentBot?.GetDialogExamples(otherName) ?? string.Empty;
        }

        /// <summary>
        /// Gets welcome line from the current active bot in the group.
        /// </summary>
        public override string GetWelcomeLine(string otherName)
        {
            return CurrentBot?.GetWelcomeLine(otherName) ?? string.Empty;
        }

        /// <summary>
        /// Override BeginChat to initialize the group with primary and secondary personas.
        /// All personas go through full BeginChat() to ensure proper initialization.
        /// The group redirects to the primary's chatlog and uses the primary's brain/agent system.
        /// </summary>
        public override void BeginChat()
        {
            // Validate primary persona exists
            if (string.IsNullOrEmpty(PrimaryPersonaName))
                throw new InvalidOperationException("GroupPersona must have a PrimaryPersonaName set.");

            if (!LLMEngine.LoadedPersonas.TryGetValue(PrimaryPersonaName, out var primaryPersona))
                throw new InvalidOperationException($"Primary persona '{PrimaryPersonaName}' not found in LoadedPersonas.");

            PrimaryBot = (TPersona?)primaryPersona;

            // Load personas from LoadedPersonas
            SecondaryBots.Clear();
            foreach (var secondaryName in SecondaryPersonaNames)
            {
                if (LLMEngine.LoadedPersonas.TryGetValue(secondaryName, out var secondaryPersona))
                {
                    SecondaryBots.Add((TPersona)secondaryPersona);
                }
            }

            // Call BeginChat() on primary persona FIRST - this loads its brain, agent, and chatlog
            PrimaryBot?.BeginChat();

            // Now call BeginChat() on all secondary personas - they'll load their own brains
            foreach (var secondary in SecondaryBots)
            {
                secondary.LoadBrain(LLMEngine.Settings.DataPath);
                secondary.AgentSystem = null;
            }

            // Restore current bot from saved ID
            if (!string.IsNullOrEmpty(CurrentBotId))
            {
                if (CurrentBotId == PrimaryPersonaName)
                {
                    _currentBot = PrimaryBot;
                }
                else
                {
                    _currentBot = SecondaryBots.FirstOrDefault(p => p.UniqueName == CurrentBotId);
                }
            }

            // Fallback to primary if current bot is still null
            if (_currentBot == null)
            {
                _currentBot = PrimaryBot;
                CurrentBotId = PrimaryBot?.UniqueName ?? string.Empty;
            }

            // Don't call base.BeginChat() as it would create duplicate brain/agent/history
        }

        /// <summary>
        /// Override EndChat to properly save all persona brains and the shared chatlog.
        /// All personas go through full EndChat() to ensure proper cleanup.
        /// </summary>
        /// <param name="backup">Whether to create backup files.</param>
        public override void EndChat(bool backup = false)
        {
            // Save current bot ID for restoration
            CurrentBotId = _currentBot?.UniqueName ?? PrimaryBot?.UniqueName ?? string.Empty;

            // Ensure persona name lists are synchronized
            if (PrimaryBot != null)
                PrimaryPersonaName = PrimaryBot.UniqueName;
            SecondaryPersonaNames = [.. SecondaryBots.Select(p => p.UniqueName)];

            // Call EndChat() on all secondary personas first
            foreach (var persona in SecondaryBots)
            {
                persona.SaveBrain(LLMEngine.Settings.DataPath);
                persona.AgentSystem?.CloseSync();
                persona.AgentSystem = null;
            }

            // Then call EndChat() on primary persona - this saves the shared chatlog, brain, and agent
            PrimaryBot?.EndChat(backup);
        }

        /// <summary>
        /// Redirects to the primary persona's LoadChatHistory.
        /// </summary>
        public override void LoadChatHistory()
        {
            PrimaryBot?.LoadChatHistory();
        }

        /// <summary>
        /// Redirects to the primary persona's SaveChatHistory.
        /// </summary>
        public override void SaveChatHistory(bool backup = false)
        {
            PrimaryBot?.SaveChatHistory(backup);
            if (LLMEngine.Settings.CommitGroupSessionToSecondaryPersonaHistory)
            {
                // Also save chat history to secondary personas
                foreach (var persona in SecondaryBots)
                {
                    SaveSecondaryPersonaHistory(persona, backup);
                }
            }
        }

        private void SaveSecondaryPersonaHistory(TPersona persona, bool backup)
        {
            // find sessions involving this persona and save them
            var sessions = LLMEngine.History.Sessions.FindAll(e => e.ContainsPersona(persona));
            if (sessions.Count > 0)
            {
                persona.LoadChatHistory();
                foreach (var session in sessions)
                {
                    if (persona.History.Sessions.Find(e => e.Guid == session.Guid) != null)
                        continue;
                    persona.History.Sessions.Add(session);
                }
                persona.History.Sessions.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
                persona.SaveChatHistory(backup);
            }
        }

        /// <summary>
        /// Redirects to the primary persona's LoadBrain.
        /// </summary>
        public override void LoadBrain(string path)
        {
            throw new Exception("GroupPersona does not support LoadBrain directly. Brains are loaded individually by each persona during BeginChat().");
        }

        /// <summary>
        /// Redirects to the primary persona's SaveBrain.
        /// </summary>
        public override void SaveBrain(string path, bool backup = false)
        {
            throw new Exception("GroupPersona does not support SaveBrain directly. Brains are saved individually by each persona during EndChat().");
        }

        /// <summary>
        /// Allows derived classes (e.g., GroupCharacter in Lethe AI Chat) to select which bot should respond next.
        /// Default implementation returns the current bot.
        /// </summary>
        /// <param name="userMessage">The user's message to analyze.</param>
        /// <returns>The persona that should respond next.</returns>
        public virtual TPersona SelectNextResponder(string userMessage)
        {
            // Override in derived class for custom logic (round-robin, keyword-based, LLM-assisted, etc.)
            return CurrentBot ?? PrimaryBot!;
        }

        public override string GetIdentifier()
        {
            return CurrentBot?.UniqueName ?? string.Empty;
        }



        /// <summary>
        /// Internal method that performs the actual macro replacement logic for group personas.
        /// </summary>
        protected override string ReplaceMacrosInternal(string inputText, string userName, string userBio)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;

            // Every macro starts with "{{": without one nothing below can change the text (see BasePersona).
            if (!inputText.Contains("{{", StringComparison.Ordinal) && inputText.IndexOf("{{memory:", StringComparison.InvariantCultureIgnoreCase) < 0)
                return inputText;

            StringBuilder res = new(inputText);

            var currentBot = CurrentBot; // Will never be null due to property fallback

            if (currentBot != null)
            {
                // In group context, {{char}} and {{charbio}} refer to current bot
                res.Replace("{{user}}",  userName)
                    .Replace("{{userbio}}", userBio)
                    .Replace("{{char}}", currentBot.Name)
                    .Replace("{{charbio}}", currentBot.GetBio(userName))
                    .Replace("{{currentchar}}", currentBot.Name)
                    .Replace("{{currentcharbio}}", currentBot.GetBio(userName))
                    .Replace("{{examples}}", currentBot.GetDialogExamples(userName))
                    .Replace("{{group}}", GetGroupBio(userName))
                    .Replace("{{mgroup}}", GetGroupBio(userName, true))
                    .Replace("{{selfedit}}", currentBot.SelfEditField)
                    .Replace("{{scenario}}", string.IsNullOrWhiteSpace(LLMEngine.Settings.ScenarioOverride) ? GetScenario(userName) : LLMEngine.Settings.ScenarioOverride);
            }
            else
            {
                // Extreme fallback (should never happen due to CurrentBot property logic)
                res.Replace("{{user}}", userName)
                    .Replace("{{userbio}}", userBio)
                    .Replace("{{char}}", Name)
                    .Replace("{{charbio}}", GetBio(userName))
                    .Replace("{{currentchar}}", "[No character selected]")
                    .Replace("{{currentcharbio}}", "[No character selected]")
                    .Replace("{{examples}}", string.Empty)
                    .Replace("{{group}}", GetGroupBio(userName))
                    .Replace("{{mgroup}}", GetGroupBio(userName, true))
                    .Replace("{{selfedit}}", SelfEditField)
                    .Replace("{{scenario}}", string.IsNullOrWhiteSpace(LLMEngine.Settings.ScenarioOverride) ? GetScenario(userName) : LLMEngine.Settings.ScenarioOverride);
            }

            // Common replacements
            res.Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToString("hh:mm tt", CultureInfo.InvariantCulture))
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString())
               .Replace("{{mchar}}", PrimaryBot!.Name)
               .Replace("{{mcharbio}}", PrimaryBot!.GetBio(userName));

            // Handle {{memory:<title>}} macros using current bot's brain
            if (currentBot != null)
            {
                var memstart = "{{memory:";
                var memend = "}}";
                int startindex = res.ToString().IndexOf(memstart, StringComparison.InvariantCultureIgnoreCase);
                while (startindex >= 0)
                {
                    var endindex = res.ToString().IndexOf(memend, startindex + memstart.Length, StringComparison.InvariantCultureIgnoreCase);
                    if (endindex < 0)
                        break;
                    var titlelength = endindex - (startindex + memstart.Length);
                    if (titlelength <= 0)
                        break;
                    var title = res.ToString().Substring(startindex + memstart.Length, titlelength).Trim();
                    var memories = currentBot.Brain.GetMemoriesByTitle(title);
                    res.Remove(startindex, (endindex + memend.Length) - startindex);
                    var memorycontent = string.Empty;
                    if (memories.Count > 0)
                    {
                        foreach (var mem in memories)
                        {
                            if (memorycontent.Length > 0)
                                memorycontent += LLMEngine.NewLine;
                            memorycontent += mem.ToSnippet(TitleInsertType.Simple, mem.Category == MemoryType.ChatSession, mem.Category == MemoryType.Goal, false).CleanupAndTrim() + LLMEngine.NewLine;
                        }
                        res.Insert(startindex, memorycontent.Trim());
                    }
                    if (startindex + memorycontent.Length > res.Length)
                        break;
                    startindex = res.ToString().IndexOf(memstart, startindex + memorycontent.Length, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            return res.ToString();
        }

        public override void SaveToFile(string pPath, string? fileName = null)
        {
            base.SaveToFile(pPath, fileName);
            // do persona files for all bots in the group
            foreach (var persona in AllPersonas)
            {
                if (string.IsNullOrEmpty(persona.UniqueName))
                    continue; // skip temporary or invalid personas
                var personaFileName = $"{persona.UniqueName}.json";
                persona.SaveToFile(pPath, personaFileName);
            }
        }
    }
}