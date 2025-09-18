using LetheAISharp.LLM;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

namespace LetheAISharp.Files
{
    /// <summary>
    /// Represents a group persona that manages multiple bot personas for group chat scenarios.
    /// Extends BasePersona to support group conversations with multiple characters.
    /// </summary>
    /// <remarks>
    /// The GroupPersona class serves as a container and coordinator for multiple bot personas in group conversations.
    /// Key features:
    /// - Manages a collection of bot personas
    /// - Provides group-specific macros like {{group}} for formatted persona lists
    /// - Supports context-aware macro replacement where {{char}} and {{charbio}} refer to the current speaking bot
    /// - Maintains group scenario that applies to all participants
    /// - Handles persona switching and management for group interactions
    /// </remarks>
    public class GroupPersona : BasePersona
    {
        /// <summary>
        /// List of unique names of bot personas participating in the group conversation.
        /// Used for serialization to avoid nested persona objects in JSON.
        /// </summary>
        public List<string> BotPersonaNames { get; set; } = [];

        /// <summary>
        /// List of bot personas participating in the group conversation.
        /// Does not include the user persona. This is populated dynamically during BeginChat()
        /// from LLMSystem.LoadedPersonas using BotPersonaNames.
        /// </summary>
        [JsonIgnore]
        public List<BasePersona> BotPersonas { get; set; } = [];

        /// <summary>
        /// The currently active/speaking bot persona in the group conversation.
        /// This determines which persona responds to user messages and affects 
        /// {{char}} and {{charbio}} macro resolution.
        /// </summary>
        [JsonIgnore]
        public BasePersona? CurrentBot { get; set; }

        /// <summary>
        /// Unique identifier of the currently active bot persona.
        /// Used for serialization/deserialization of CurrentBot.
        /// </summary>
        public string CurrentBotId { get; set; } = string.Empty;

        public GroupPersona()
        {
            IsUser = false; // Group personas are always bot-type containers
            Name = "Group Chat";
            Bio = "A group conversation with multiple AI personas";
        }

        /// <summary>
        /// Adds a bot persona to the group conversation.
        /// </summary>
        /// <param name="persona">The bot persona to add. Must not be a user persona.</param>
        public void AddBotPersona(BasePersona persona)
        {
            if (persona.IsUser)
                throw new ArgumentException("Cannot add user personas to group chat. Only bot personas are allowed.");

            if (string.IsNullOrEmpty(persona.UniqueName))
                throw new ArgumentException("Persona must have a valid UniqueName.");

            if (!BotPersonaNames.Contains(persona.UniqueName))
            {
                BotPersonaNames.Add(persona.UniqueName);
                BotPersonas.Add(persona);
                
                // Set as current bot if this is the first one added
                if (CurrentBot == null)
                {
                    SetCurrentBot(persona);
                }
            }
        }

        /// <summary>
        /// Removes a bot persona from the group conversation.
        /// </summary>
        /// <param name="uniqueName">The unique name of the persona to remove.</param>
        public void RemoveBotPersona(string uniqueName)
        {
            var persona = BotPersonas.FirstOrDefault(p => p.UniqueName == uniqueName);
            if (persona != null)
            {
                BotPersonaNames.Remove(uniqueName);
                BotPersonas.Remove(persona);
                
                // If we removed the current bot, switch to the first available one
                if (CurrentBot?.UniqueName == uniqueName)
                {
                    CurrentBot = BotPersonas.FirstOrDefault();
                    CurrentBotId = CurrentBot?.UniqueName ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Sets the currently active bot persona for the conversation.
        /// </summary>
        /// <param name="persona">The persona to set as current. Must be in the BotPersonas list.</param>
        public void SetCurrentBot(BasePersona persona)
        {
            if (!BotPersonas.Contains(persona))
                throw new ArgumentException("Persona must be added to the group before setting as current bot.");

            CurrentBot = persona;
            CurrentBotId = persona.UniqueName;
        }

        /// <summary>
        /// Sets the currently active bot persona by unique name.
        /// </summary>
        /// <param name="uniqueName">The unique name of the persona to set as current.</param>
        public void SetCurrentBot(string uniqueName)
        {
            var persona = BotPersonas.FirstOrDefault(p => p.UniqueName == uniqueName) ?? 
                throw new ArgumentException($"No persona found with unique name: {uniqueName}");
            SetCurrentBot(persona);
        }

        /// <summary>
        /// Gets a formatted list of all bot personas (Name + Bio) for use in system prompts.
        /// This is used by the {{group}} macro.
        /// </summary>
        /// <param name="userName">The user's name for bio formatting.</param>
        /// <returns>Formatted string containing all bot personas information.</returns>
        public string GetGroupPersonasList(string userName)
        {
            if (BotPersonas.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("=== Group Chat Participants ===");
            
            foreach (var persona in BotPersonas)
            {
                sb.AppendLine($"**{persona.Name}**");
                var bio = persona.GetBio(userName);
                if (!string.IsNullOrWhiteSpace(bio))
                {
                    sb.AppendLine(bio);
                }
                sb.AppendLine();
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets the bio for the group context, which includes information about all participants.
        /// </summary>
        /// <param name="otherName">The other participant's name (typically the user).</param>
        /// <returns>Group bio with participant information.</returns>
        public override string GetBio(string otherName)
        {
            var groupBio = base.GetBio(otherName);
            var participantsList = GetGroupPersonasList(otherName);
            
            if (!string.IsNullOrWhiteSpace(participantsList))
            {
                return $"{groupBio}\n\n{participantsList}";
            }
            
            return groupBio;
        }

        /// <summary>
        /// Gets the scenario for the group context, which applies to all participants.
        /// </summary>
        /// <param name="otherName">The other participant's name (typically the user).</param>
        /// <returns>Group scenario with macros replaced.</returns>
        public override string GetScenario(string otherName)
        {
            return base.GetScenario(otherName);
        }

        /// <summary>
        /// Gets dialog examples from the current active bot in the group.
        /// </summary>
        /// <param name="otherName">The other participant's name (typically the user).</param>
        /// <returns>Dialog examples from the current bot, or empty string if no current bot.</returns>
        public override string GetDialogExamples(string otherName)
        {
            var currentBot = CurrentBot ?? BotPersonas.FirstOrDefault();
            return currentBot?.GetDialogExamples(otherName) ?? string.Empty;
        }

        /// <summary>
        /// Gets welcome line from the current active bot in the group.
        /// </summary>
        /// <param name="otherName">The other participant's name (typically the user).</param>
        /// <returns>Welcome line from the current bot, or empty string if no current bot.</returns>
        public override string GetWelcomeLine(string otherName)
        {
            var currentBot = CurrentBot ?? BotPersonas.FirstOrDefault();
            return currentBot?.GetWelcomeLine(otherName) ?? string.Empty;
        }

        /// <summary>
        /// Override BeginChat to initialize all bot personas in the group.
        /// </summary>
        public override void BeginChat()
        {
            base.BeginChat();
            
            // Clear the current BotPersonas list and repopulate from LoadedPersonas
            BotPersonas.Clear();
            
            // Load personas from LLMSystem.LoadedPersonas based on BotPersonaNames
            foreach (var personaName in BotPersonaNames)
            {
                if (LLMEngine.LoadedPersonas.TryGetValue(personaName, out var persona))
                {
                    BotPersonas.Add(persona);
                    persona.BeginChat();
                }
            }
            
            // Restore current bot from saved ID
            if (!string.IsNullOrEmpty(CurrentBotId))
            {
                var savedBot = BotPersonas.FirstOrDefault(p => p.UniqueName == CurrentBotId);
                if (savedBot != null)
                {
                    CurrentBot = savedBot;
                }
            }
            
            // If no current bot is set but we have personas, set the first one
            if (CurrentBot == null && BotPersonas.Count > 0)
            {
                CurrentBot = BotPersonas.First();
                CurrentBotId = CurrentBot.UniqueName;
            }
        }

        /// <summary>
        /// Override EndChat to properly save all bot personas in the group.
        /// </summary>
        /// <param name="backup">Whether to create backup files.</param>
        public override void EndChat(bool backup = false)
        {
            // Save current bot ID for restoration
            CurrentBotId = CurrentBot?.UniqueName ?? string.Empty;
            
            // Ensure BotPersonaNames is synchronized with BotPersonas
            BotPersonaNames = [.. BotPersonas.Select(p => p.UniqueName)];
            
            // End chat for all bot personas
            foreach (var persona in BotPersonas)
            {
                persona.EndChat(backup);
            }
            
            base.EndChat(backup);
        }

        /// <summary>
        /// Internal method that performs the actual macro replacement logic.
        /// Handles both regular and group personas in a unified way.
        /// </summary>
        /// <param name="inputText">The text to process</param>
        /// <param name="userName">The user's name</param>
        /// <param name="userBio">The user's bio</param>
        /// <param name="character">The character persona (can be BasePersona or GroupPersona)</param>
        /// <returns>Text with macros replaced</returns>
        protected override string ReplaceMacrosInternal(string inputText, string userName, string userBio)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;

            StringBuilder res = new(inputText);

            var currentBot = CurrentBot ?? BotPersonas.FirstOrDefault();
            if (currentBot != null)
            {
                // In group context, {{char}} and {{charbio}} refer to current bot
                res.Replace("{{user}}", userName)
                    .Replace("{{userbio}}", userBio)
                    .Replace("{{char}}", currentBot.Name)
                    .Replace("{{charbio}}", currentBot.GetBio(userName))
                    .Replace("{{currentchar}}", currentBot.Name)
                    .Replace("{{currentcharbio}}", currentBot.GetBio(userName))
                    .Replace("{{examples}}", currentBot.GetDialogExamples(userName))
                    .Replace("{{group}}", GetGroupPersonasList(userName))
                    .Replace("{{selfedit}}", currentBot.SelfEditField);
            }
            else
            {
                // Fallback to group persona itself if no current bot
                res.Replace("{{user}}", userName)
                    .Replace("{{userbio}}", userBio)
                    .Replace("{{char}}", Name)
                    .Replace("{{charbio}}", GetBio(userName))
                    .Replace("{{currentchar}}", "[No character selected]")
                    .Replace("{{currentcharbio}}", "[No character selected]")
                    .Replace("{{examples}}", GetDialogExamples(userName))
                    .Replace("{{group}}", GetGroupPersonasList(userName))
                    .Replace("{{selfedit}}", SelfEditField);
            }
            // Common replacements for both group and single
            res.Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToString("hh:mm tt", CultureInfo.InvariantCulture))
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString())
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(LLMEngine.Settings.ScenarioOverride) ? GetScenario(userName) : LLMEngine.Settings.ScenarioOverride);

            return res.ToString().CleanupAndTrim();
        }

    }
}