using Newtonsoft.Json;
using System.Text;
using AIToolkit.LLM;
using System.Reflection.PortableExecutable;

namespace AIToolkit.Files
{

    public class PersonaAttribute(string name = "", string content = "", bool mutable = false, int updatefreq = 1, int lastupdated = 0, int stability = 0)
    {
        public string Name { get; set; } = name;
        public string Content { get; set; } = content;
        public bool Mutable { get; set; } = mutable;
        public int UpdateFrequency { get; set; } = updatefreq;
        public int LastUpdated { get; set; } = lastupdated;
        public int StabilityFactor { get; set; } = stability;
    }

    public class BasePersona : BaseFile
    {
        public enum AttributeCategory { Core, Physical, Personality, Tastes, Sexual, Relationships, Mood }

        /// <summary> Character's name (used by LLM) </summary>
        public string Name { get; set; } = string.Empty;
        public bool IsUser { get; set; } = false;
        /// <summary> Character's bio (used by LLM) </summary>
        public string Bio { get; set; } = string.Empty;

        public List<PersonaAttribute> Attributes { get; set; } = [];

        /// <summary> Character's long form history (used by LLM persona generator, not visible to 3rd party in group chat) </summary>
        public string HistoryBio { get; set; } = string.Empty;
        /// <summary> Self editable field for the character (updated on new summary) </summary>
        public string SelfEditField { get; set; } = string.Empty;
        /// <summary> Character's default scenario (used by LLM) </summary>
        public string Scenario { get; set; } = string.Empty;
        /// <summary> Character Notes (UI) </summary>
        public string Notes { get; set; } = string.Empty;
        /// <summary> Icon to be displayed in chat </summary>
        public string Icon { get; set; } = string.Empty;
        /// <summary> First message the character will send when starting a new session </summary>
        public List<string> FirstMessage { get; set; } = [];
        /// <summary> Examples of dialogs from the character to get a more consistent tone </summary>
        public List<string> ExampleDialogs { get; set; } = [];
        /// <summary>
        /// A list of whims that the character may have. 
        /// These are used to determine the character's mood at the start of a session (or after long AFK periods). 
        /// They can be used to influence the character's responses to create a more dynamic conversation.
        /// </summary>
        public List<string> Whims { get; set; } = [];
        /// <summary>
        /// The chance for a whim to change during the session. A percentage chance that the whim will change after each message pair.
        /// If set to 0, the whim will only change at the start of a session, or after a long AFK period.
        /// </summary>
        public float WhimChangeRate { get; set; } = 0.05f;
        /// <summary> Custom system prompt for this character </summary>
        public string SystemPrompt { get; set; } = string.Empty;
        /// <summary> WorldInfo applied to this character </summary>
        public List<string> Worlds { get; set; } = [];
        /// <summary> Optional world info being used for the Location plugin </summary>
        public List<string> Plugins { get; set; } = [];
        /// <summary> If set to true, older chat sessions will be summarized, allowing for a advanced form of memory </summary>
        public bool SessionMemorySystem { get; set; } = false;
        /// <summary> If set to true, this bot will stay informed about the spacing between user messages </summary>
        public bool SenseOfTime { get; set; } = false;
        /// <summary> If set above 0, this character will be allowed to write this amount of tokens in its system prompt. Altered each new session. </summary>
        public int SelfEditTokens { get; set; } = 0;

        [JsonIgnore] public List<WorldInfo> MyWorlds { get; protected set; } = [];
        [JsonIgnore] public Chatlog History { get; protected set; } = new();
        public string GetBio(string othername) => IsUser ? 
            Bio.Replace("{{user}}", Name).Replace("{{char}}", othername) :
            Bio.Replace("{{char}}", Name).Replace("{{user}}", othername);

        public string GetScenario(string othername) => IsUser ?
            Scenario.Replace("{{user}}", Name).Replace("{{char}}", othername) :
            Scenario.Replace("{{char}}", Name).Replace("{{user}}", othername);

        public string GetDialogExamples(string othername)
        {
            if (ExampleDialogs.Count == 0)
                return string.Empty;
            var str = new StringBuilder();
            str.AppendLinuxLine($"Here are some examples of {Name}'s writing style:");
            foreach (var item in ExampleDialogs)
                str.AppendLinuxLine("- " + item.Replace("{{user}}", othername).Replace("{{char}}", Name));
            return str.ToString();
        }

        public string GetWelcomeLine(string othername)
        {
            if (FirstMessage.Count == 0)
                return string.Empty;
            // select a random welcome line
            var index = LLMSystem.RNG.Next(FirstMessage.Count);
            return FirstMessage[index].Replace("{{user}}", othername).Replace("{{char}}", Name);
        }

        public BasePersona() 
        {
            if (Attributes.Count == 0)
            {
                foreach (var cat in Enum.GetValues(typeof(AttributeCategory)))
                    Attributes.Add(new PersonaAttribute(cat.ToString()!));
            }
        }

        public virtual void BeginChat()
        {
        }

        public virtual void EndChat(bool backup = false)
        {
        }

        protected void SaveChatHistory(string path, bool backup = false)
        {
            if (string.IsNullOrEmpty(UniqueName))
                return;
            if (backup && File.Exists(path + UniqueName + ".json"))
            {
                if (File.Exists(path + UniqueName + ".bak"))
                    File.Delete(path + UniqueName + ".bak");
                File.Move(path + UniqueName + ".json", path + UniqueName + ".bak");
            }

            History.SaveToFile(path + UniqueName + ".json");
        }

        public void LoadChatHistory(string path)
        {
            if (string.IsNullOrEmpty(UniqueName))
            {
                History = new Chatlog();
                return;
            }
            var f = path + UniqueName + ".json";
            History = File.Exists(f) ? JsonConvert.DeserializeObject<Chatlog>(File.ReadAllText(f))! : new Chatlog();
        }

        public void ClearChatHistory(string path, bool deletefile = true)
        {
            History.ClearHistory();
            if (!deletefile)
                return;
            var f = path + UniqueName;
            if (File.Exists(f + ".json")) File.Delete(f + ".json");
        }

        public WorldEntry? GetWIEntryByGUID(Guid id)
        {
            if (MyWorlds.Count == 0)
                return null;
            foreach (var world in MyWorlds)
            {
                var res = world.Entries.Find(e => e.Guid == id);
                if (res != null)
                    return res; 
            }
            return null;
        }

        #region Attribute Handling and Self Editing

        public async Task UpdateSelfEditSection()
        {
            if (SelfEditTokens == 0 || History.Sessions.Count < 3)
                return;
            LLMSystem.NamesInPromptOverride = false;
            var availtks = LLMSystem.MaxContextLength - SelfEditTokens - LLMSystem.GetTokenCount(SelfEditField) - 1024;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to write a character's personal thoughts based on its chat history and information.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine(Bio);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Memories");
            sysprompt.AppendLinuxLine();

            var sizeeval = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            availtks -= LLMSystem.GetTokenCount(sizeeval);
            var maxcount = 99;
            var entries = new List<string>();
            for (int i = History.Sessions.Count - 2; i >= 0; i--)
            {
                var session = History.Sessions[i];
                var details = $"Between {session.StartTime.DayOfWeek} {StringExtensions.DateToHumanString(session.StartTime)} and {session.EndTime.DayOfWeek} {StringExtensions.DateToHumanString(session.EndTime)}: {session.Summary.RemoveNewLines()}" + LLMSystem.NewLine;
                var size = LLMSystem.GetTokenCount(details);
                availtks -= size;
                maxcount--;
                if (availtks <= 0 || maxcount < 0)
                    break;
                entries.Insert(0, details);
            }
            if (entries.Count == 0)
            {
                LLMSystem.NamesInPromptOverride = null;
                return;
            }

            foreach (var entry in entries)
                sysprompt.AppendLinuxLine(entry);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {Name}'s personal thoughts");
            sysprompt.AppendLinuxLine();
            if (SelfEditField.Length > 0)
                sysprompt.AppendLinuxLine(SelfEditField);
            else
                sysprompt.AppendLinuxLine("This space is empty for now.");

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this, $"Using {Name}'s memories alongside their biography, edit their personal thoughts section accordingly. Write two to three short paragraphs from {Name}'s perspective, in the first person. Focus on important events, life changing experiences, and promises, that {Name} would want to keep in mind. Don't include a title.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = SelfEditTokens;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                llmparams.Max_length += 1024;

            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            LLMSystem.NamesInPromptOverride = null;
            SelfEditField = finalstr.RemoveUnfinishedSentence().RemoveNewLines().CleanupAndTrim();
        }

        public async Task UpdatePersonaAttributes()
        {
            if (SelfEditTokens == 0 || History.Sessions.Count < 3)
                return;

            LLMSystem.NamesInPromptOverride = false;

            // Count this as a session for update frequency tracking
            var sessionCounter = History.Sessions.Count;

            foreach (var attribute in Attributes)
            {
                // Skip if not due for update based on frequency
                if (!attribute.Mutable || sessionCounter - attribute.LastUpdated < attribute.UpdateFrequency)
                    continue;

                // First check if update is needed
                if (await ShouldUpdateAttribute(attribute))
                {
                    var updatedContent = await UpdateAttributeContent(attribute);
                    if (!string.IsNullOrEmpty(updatedContent))
                    {
                        attribute.Content = updatedContent;
                        attribute.LastUpdated = sessionCounter;
                    }
                }
            }

            LLMSystem.NamesInPromptOverride = null;
        }

        private async Task<bool> ShouldUpdateAttribute(PersonaAttribute attribute)
        {
            var availtks = LLMSystem.MaxContextLength - 1024;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to determine if a character's attribute needs updating based on recent interactions.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine(Bio);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {Name}'s Current {attribute.Name}");
            sysprompt.AppendLinuxLine(attribute.Content);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("# Recent Sessions");

            // Get recent sessions (similar to UpdateSelfEditSection method)
            var sizeeval = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            availtks -= LLMSystem.GetTokenCount(sizeeval);

            var entries = GetRecentSessionSummaries(availtks);
            if (entries.Count == 0)
                return false;

            foreach (var entry in entries)
                sysprompt.AppendLinuxLine(entry);

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this,
                $"Based on {Name}'s recent interactions, does their {attribute.Name.ToLower()} need to be updated? " +
                $"Answer with only 'Yes' or 'No'. Consider whether there have been meaningful experiences that would affect this specific aspect of their personality.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 20; // Short response
            llmparams.Temperature = 0.2f; // Lower temperature for more deterministic response
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var response = await LLMSystem.SimpleQuery(llmparams);
            response = response.RemoveThinkingBlocks().Trim();

            return response.StartsWith("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> UpdateAttributeContent(PersonaAttribute attribute)
        {
            var availtks = LLMSystem.MaxContextLength - SelfEditTokens - 512;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to update a character's information based on its chat history and memories.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("# Basic Bio");
            sysprompt.AppendLinuxLine(Bio);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {attribute.Name} information to Update");
            sysprompt.AppendLinuxLine(attribute.Content);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("# Recent Sessions");

            // Same session gathering as above
            var sizeeval = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            availtks -= LLMSystem.GetTokenCount(sizeeval);

            var entries = GetRecentSessionSummaries(availtks);
            if (entries.Count == 0)
                return string.Empty;

            foreach (var entry in entries)
                sysprompt.AppendLinuxLine(entry);

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());

            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this,
                $"Update {Name}'s {attribute.Name.ToLower()} information based on recent experiences. Make only small, incremental changes that would realistically reflect how this aspect of their personality might evolve from these interactions. Maintain the same general writing style and length. Focus on consistency with their core personality while allowing gradual, meaningful evolution.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 1024;
            llmparams.Temperature = 0.7f;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);

            return finalstr.RemoveUnfinishedSentence().CleanupAndTrim();
        }

        private List<string> GetRecentSessionSummaries(int availableTokens)
        {
            var tempEntries = new List<(int index, string content, int tokenCount)>();
            var availtks = availableTokens;
            var maxcount = 5;

            // First, collect all potential entries with their token counts and index
            if (History.Sessions.Count > 0)
            {
                // Handle most recent session first (we'll reorder later)
                var mostRecentSession = History.Sessions[^1];
                var mostRecentDetails = $"# Most Recent Session: {mostRecentSession.Title} ({StringExtensions.DateToHumanString(mostRecentSession.StartTime)}):\n{mostRecentSession.Summary}" + LLMSystem.NewLine;
                var mostRecentSize = LLMSystem.GetTokenCount(mostRecentDetails);

                if (mostRecentSize <= availtks)
                {
                    tempEntries.Add((History.Sessions.Count - 1, mostRecentDetails, mostRecentSize));
                    availtks -= mostRecentSize;
                }

                // Add previous session summaries
                for (int i = History.Sessions.Count - 2; i >= 0 && tempEntries.Count < maxcount; i--)
                {
                    var session = History.Sessions[i];
                    var details = $"# Session: {session.Title}\nBetween {session.StartTime.DayOfWeek} {StringExtensions.DateToHumanString(session.StartTime)} and {session.EndTime.DayOfWeek} {StringExtensions.DateToHumanString(session.EndTime)}:\n{session.Summary.RemoveNewLines()}" + LLMSystem.NewLine;
                    var size = LLMSystem.GetTokenCount(details);

                    if (size <= availtks)
                    {
                        tempEntries.Add((i, details, size));
                        availtks -= size;
                    }
                    else
                    {
                        // If we can't fit this one, we likely can't fit earlier ones either
                        break;
                    }
                }
            }

            // Now sort by session index (oldest first) and extract just the content
            return [.. tempEntries.OrderBy(e => e.index).Select(e => e.content)];
        }

        #endregion
    }
}
