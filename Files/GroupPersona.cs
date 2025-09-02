using AIToolkit.LLM;
using Newtonsoft.Json;
using System.Text;

namespace AIToolkit.Files
{
    /// <summary>
    /// Represents a group persona that manages multiple bot personas for group chat scenarios.
    /// This extends BasePersona to provide group chat functionality while maintaining compatibility
    /// with existing 1:1 conversation systems.
    /// </summary>
    public class GroupPersona : BasePersona
    {
        /// <summary>
        /// List of bot personas participating in the group chat.
        /// These should all have IsUser = false.
        /// </summary>
        public List<string> BotPersonaIds { get; set; } = [];

        /// <summary>
        /// ID of the currently active bot persona that should respond next.
        /// This can be set to control which bot responds in the group chat.
        /// </summary>
        public string? ActiveBotId { get; set; }

        /// <summary>
        /// When true, the group persona automatically selects which bot should respond
        /// based on the conversation context and bot personalities.
        /// </summary>
        public bool AutoSelectResponder { get; set; } = true;

        /// <summary>
        /// Instructions for the LLM on how to manage the group conversation.
        /// This is included in the system prompt for group chat scenarios.
        /// </summary>
        public string GroupInstructions { get; set; } = "This is a group conversation. Choose which character should respond based on their personality and the conversation context. Always respond as the most appropriate character.";

        /// <summary>
        /// Gets all loaded bot personas that are part of this group.
        /// </summary>
        [JsonIgnore]
        public List<BasePersona> BotPersonas
        {
            get
            {
                var bots = new List<BasePersona>();
                foreach (var botId in BotPersonaIds)
                {
                    if (LLMSystem.LoadedPersonas.TryGetValue(botId, out var persona) && !persona.IsUser)
                    {
                        bots.Add(persona);
                    }
                }
                return bots;
            }
        }

        /// <summary>
        /// Gets the currently active bot persona.
        /// </summary>
        [JsonIgnore]
        public BasePersona? ActiveBot
        {
            get
            {
                if (string.IsNullOrEmpty(ActiveBotId))
                    return BotPersonas.FirstOrDefault();
                return LLMSystem.LoadedPersonas.TryGetValue(ActiveBotId, out var persona) ? persona : null;
            }
        }

        public GroupPersona()
        {
            IsUser = false; // Group personas are always bot-type
            Name = "Group";
            Bio = "This is a group conversation with multiple characters.";
        }

        /// <summary>
        /// Gets the bio for group chat, including information about all participating characters.
        /// </summary>
        /// <param name="othername">Name of the user</param>
        /// <returns>Combined bio of all group participants</returns>
        public override string GetBio(string othername)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== GROUP CONVERSATION ===");
            sb.AppendLine($"User: {othername}");
            sb.AppendLine("Characters in this conversation:");
            
            foreach (var bot in BotPersonas)
            {
                sb.AppendLine($"- {bot.Name}: {bot.GetBio(othername)}");
            }
            
            if (!string.IsNullOrWhiteSpace(GroupInstructions))
            {
                sb.AppendLine();
                sb.AppendLine("Group Instructions:");
                sb.AppendLine(GroupInstructions);
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Gets the scenario for group chat, combining scenarios from all participating characters.
        /// </summary>
        /// <param name="othername">Name of the user</param>
        /// <returns>Combined scenario for the group</returns>
        public override string GetScenario(string othername)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Scenario))
            {
                sb.AppendLine(Scenario.Replace("{{user}}", othername).Replace("{{char}}", "the group"));
                sb.AppendLine();
            }

            foreach (var bot in BotPersonas)
            {
                var botScenario = bot.GetScenario(othername);
                if (!string.IsNullOrWhiteSpace(botScenario))
                {
                    sb.AppendLine($"[{bot.Name}'s context: {botScenario}]");
                }
            }
            
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets dialog examples from all participating characters.
        /// </summary>
        /// <param name="othername">Name of the user</param>
        /// <returns>Combined dialog examples</returns>
        public override string GetDialogExamples(string othername)
        {
            var sb = new StringBuilder();
            foreach (var bot in BotPersonas)
            {
                var examples = bot.GetDialogExamples(othername);
                if (!string.IsNullOrWhiteSpace(examples))
                {
                    sb.AppendLine($"=== {bot.Name} Examples ===");
                    sb.AppendLine(examples);
                    sb.AppendLine();
                }
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Gets a welcome message for the group, potentially from the active bot or a random one.
        /// </summary>
        /// <param name="othername">Name of the user</param>
        /// <returns>Welcome message</returns>
        public override string GetWelcomeLine(string othername)
        {
            var activeBot = ActiveBot ?? BotPersonas.FirstOrDefault();
            if (activeBot != null)
            {
                return activeBot.GetWelcomeLine(othername);
            }
            return $"Hello {othername}! Welcome to the group chat.";
        }

        /// <summary>
        /// Sets the active bot persona by ID.
        /// </summary>
        /// <param name="botId">ID of the bot persona to make active</param>
        /// <returns>True if successfully set, false if bot not found</returns>
        public bool SetActiveBot(string botId)
        {
            if (BotPersonaIds.Contains(botId) && LLMSystem.LoadedPersonas.ContainsKey(botId))
            {
                ActiveBotId = botId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Adds a bot persona to the group.
        /// </summary>
        /// <param name="botId">ID of the bot persona to add</param>
        /// <returns>True if successfully added, false if already exists or bot not found</returns>
        public bool AddBot(string botId)
        {
            if (string.IsNullOrWhiteSpace(botId) || BotPersonaIds.Contains(botId))
                return false;

            if (LLMSystem.LoadedPersonas.TryGetValue(botId, out var persona) && !persona.IsUser)
            {
                BotPersonaIds.Add(botId);
                if (string.IsNullOrEmpty(ActiveBotId))
                    ActiveBotId = botId;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes a bot persona from the group.
        /// </summary>
        /// <param name="botId">ID of the bot persona to remove</param>
        /// <returns>True if successfully removed</returns>
        public bool RemoveBot(string botId)
        {
            var removed = BotPersonaIds.Remove(botId);
            if (removed && ActiveBotId == botId)
            {
                ActiveBotId = BotPersonaIds.FirstOrDefault();
            }
            return removed;
        }
    }
}