using Newtonsoft.Json;
using System.Text;
using AIToolkit.LLM;
using System.Reflection.PortableExecutable;

namespace AIToolkit.Files
{

    public class AttributeChangeRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Content { get; set; } = string.Empty;

        public AttributeChangeRecord() { }

        public AttributeChangeRecord(string content)
        {
            Content = content;
        }
    }

    public class PersonaAttribute(string name = "", string content = "", bool mutable = false, int updatefreq = 1, int lastupdated = 0, int stability = 0)
    {
        public string Name { get; set; } = name;
        public string Content { get; set; } = content;
        public bool Mutable { get; set; } = mutable;
        public int UpdateFrequency { get; set; } = updatefreq;
        public int LastUpdated { get; set; } = lastupdated;
        public int StabilityFactor { get; set; } = stability;

        // Track history of changes, limited to 10 most recent
        public List<AttributeChangeRecord> History { get; set; } = [];

        public void RecordChange()
        {
            // Add current content to history before it gets changed
            History.Add(new AttributeChangeRecord(Content));
            // Keep only the 10 most recent changes
            if (History.Count > 10)
                History.RemoveAt(0);
        }
    }

    public class BasePersona : BaseFile
    {
        public enum AttributeCategory { Core, Physical, Personality, Tastes, Sexuality, Relationships, Mood, End }

        /// <summary> Character's name (used by LLM) </summary>
        public string Name { get; set; } = string.Empty;
        public bool IsUser { get; set; } = false;
        /// <summary> Character's bio (used by LLM) </summary>
        public string Bio { get; set; } = string.Empty;

        /// <summary> If set to true, the character's bio will be updated dynamically based on recent interactions. </summary>
        public bool DynamicBio { get; set; } = false;
        /// <summary> How many do we go back when updating bio </summary>
        public int DynamicBioHistoryDepth { get; set; } = 5;
        /// <summary> A list of attributes that can be used to dynamically update the character's information based on recent interactions. </summary>
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
        public bool FirstPersonSummary { get; set; } = true;
        /// <summary> If set above 0, this character will be allowed to write this amount of tokens in its system prompt. Altered each new session. </summary>
        public int SelfEditTokens { get; set; } = 0;

        [JsonIgnore] public List<WorldInfo> MyWorlds { get; protected set; } = [];
        [JsonIgnore] public Chatlog History { get; protected set; } = new();
        public string GetBio(string othername)
        {
            if (IsUser)
            {
                return Bio.Replace("{{user}}", Name).Replace("{{char}}", othername);
            }
            else if (DynamicBio)
            {
                var str = new StringBuilder();
                foreach (var attribute in Attributes)
                {
                    str.AppendLinuxLine(attribute.Content);
                }
                return str.ToString().CleanupAndTrim().Replace("{{char}}", Name).Replace("{{user}}", othername);
            }
            else
            {
                return Bio.Replace("{{char}}", Name).Replace("{{user}}", othername);
            }
        }

        public string GetScenario(string othername) => IsUser ?
            Scenario.Replace("{{user}}", Name).Replace("{{char}}", othername) :
            Scenario.Replace("{{char}}", Name).Replace("{{user}}", othername);

        public string GetDialogExamples(string othername)
        {
            if (ExampleDialogs.Count == 0)
                return string.Empty;
            var str = new StringBuilder();
            str.AppendLinuxLine($"Here are some guidelines for {Name}'s writing style:");
            foreach (var item in ExampleDialogs)
                str.AppendLinuxLine("- " + item.Replace("{{user}}", othername).Replace("{{char}}", Name));
            return str.ToString().CleanupAndTrim();
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
            SelfEditField = finalstr.RemoveUnfinishedSentence().RemoveNewLines().CleanupAndTrim().RemoveTitle();
        }

        /// <summary> Update the character's attributes based on recent interactions. </summary>
        public async Task UpdatePersonaAttributes()
        {
            if (History.Sessions.Count == 0 || !DynamicBio)
                return;
            if (History.Sessions.Count == 1 && History.CurrentSession.Messages.Count == 0)
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
                if (!string.IsNullOrWhiteSpace(attribute.Content) || await ShouldUpdateAttribute(attribute))
                {
                    var updatedContent = await UpdateAttributeContent(attribute);
                    updatedContent = updatedContent.CleanupAndTrim().RemoveTitle();
                    if (!string.IsNullOrEmpty(updatedContent))
                    {
                        attribute.RecordChange();
                        if (attribute.StabilityFactor > 0 && !string.IsNullOrWhiteSpace(attribute.Content))
                        {
                            // Blend the changes rather than directly applying them
                            var blendedContent = await BlendAttributeChanges(attribute.Name, attribute.Content, updatedContent);
                            if (!string.IsNullOrWhiteSpace(blendedContent))
                                updatedContent = blendedContent;
                            updatedContent = updatedContent.CleanupAndTrim().RemoveTitle();
                        }
                        attribute.Content = updatedContent;
                        attribute.LastUpdated = sessionCounter;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(SystemPrompt))
            {
                var newprompt = await RebuildBio($"Rewrite the text provided above by changing it from the third-person (he, she) to the second-person (you, your). Maintain consistency. Do not change the text's meaning. Fix potential spelling mistakes.");
                var fullsys = new StringBuilder();
                fullsys.AppendLinuxLine($"You are {Name}. {newprompt}");
                fullsys.AppendLinuxLine();
                fullsys.AppendLinuxLine("# {{user}} (user)");
                fullsys.AppendLinuxLine("{{userbio}}");
                SystemPrompt = fullsys.ToString();
            }

            LLMSystem.NamesInPromptOverride = null;
        }

        private async Task<bool> ShouldUpdateAttribute(PersonaAttribute attribute)
        {
            var availtks = LLMSystem.MaxContextLength - 1024;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                availtks -= 1024;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to determine if a character's attribute needs updating based on recent interactions.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine(Bio);
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
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {Name}'s Current {attribute.Name}");
            sysprompt.AppendLinuxLine(attribute.Content);

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this,
                $"Based on {Name}'s recent interactions, does their {attribute.Name.ToLower()} need to be updated? " +
                $"Answer with only 'Yes' or 'No'. Consider whether there have been meaningful experiences that would affect their {attribute.Name.ToLower()} significantly, and only this part of their character.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 512;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                llmparams.Max_length += 1024;
            llmparams.Temperature = 0.2f; // Lower temperature for more deterministic response
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var response = await LLMSystem.SimpleQuery(llmparams);
            response = response.RemoveThinkingBlocks().Trim();

            return response.StartsWith("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string> UpdateAttributeContent(PersonaAttribute attribute)
        {
            var availtks = LLMSystem.MaxContextLength - SelfEditTokens - 1024;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                availtks -= 1024;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to update a character's information based on its chat history and memories.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine(Bio);
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

            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {attribute.Name} information to update");
            if (!string.IsNullOrWhiteSpace(attribute.Content))
                sysprompt.AppendLinuxLine(attribute.Content);
            else
                sysprompt.AppendLinuxLine("No information about Emily's mood has been provided yet. First entry shouldn't be longer than a short paragraph.");

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());

            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this,
                $"Update {Name}'s {attribute.Name.ToLower()} information based on recent experiences. Make only small, incremental changes to the original version that would realistically reflect how it might have evolved from these interactions. Maintain the same writing style. Keep the same length. Focus on consistency with their general information while allowing gradual evolution. Don't add any commentary. Don't add a title. Don't explain your changes.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 512;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                llmparams.Max_length += 1024;
            if (llmparams.Temperature > 0.5f)
                llmparams.Temperature = 0.5f;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);

            return finalstr.RemoveUnfinishedSentence().RemoveNewLines().CleanupAndTrim();
        }

        private List<string> GetRecentSessionSummaries(int availableTokens)
        {
            var tempEntries = new List<(int index, string content, int tokenCount)>();
            var availtks = availableTokens;
            var maxcount = DynamicBioHistoryDepth;

            // First, collect all potential entries with their token counts and index
            if (History.Sessions.Count > 0)
            {
                // Handle most recent session first (we'll reorder later)
                var mostRecentSession = History.Sessions[^1];
                if (!string.IsNullOrEmpty(mostRecentSession.Summary))
                {
                    var mostRecentDetails = $"# Most Recent Session: {mostRecentSession.Title} ({StringExtensions.DateToHumanString(mostRecentSession.StartTime)}):\n{mostRecentSession.Summary}" + LLMSystem.NewLine;
                    var mostRecentSize = LLMSystem.GetTokenCount(mostRecentDetails);

                    if (mostRecentSize <= availtks)
                    {
                        tempEntries.Add((History.Sessions.Count - 1, mostRecentDetails, mostRecentSize));
                        availtks -= mostRecentSize;
                    }
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

        /// <summary>
        /// Blend the changes between the old and new content for a given attribute.
        /// </summary>
        /// <param name="attributeName">attribute's name</param>
        /// <param name="oldContent">old content</param>
        /// <param name="newContent">new content</param>
        /// <returns>blend between the two</returns>
        private async Task<string> BlendAttributeChanges(string attributeName, string oldContent, string newContent)
        {
            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to blend two versions of a text together.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# {Name}'s {attributeName}");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("## Current version");
            sysprompt.AppendLinuxLine(oldContent);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("## Suggested version");
            sysprompt.AppendLinuxLine(newContent);

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this,
                $"Write an intermediary version between the current and the suggestion versions of the text. Make modest changes that move in the direction of the update while preserving key elements of the original. The result should feel like a small step in character development rather than a complete change. Don't add any commentary, don't explain your changes.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 512;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                llmparams.Max_length += 1024;
            if (llmparams.Temperature > 0.5f)
                llmparams.Temperature = 0.5f;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var blendedContent = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                blendedContent = blendedContent.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);

            return blendedContent.RemoveUnfinishedSentence().RemoveNewLines().Trim();
        }

        private async Task<string> RebuildBio(string instruction)
        {
            LLMSystem.NamesInPromptOverride = false;
            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored AI system used for text editing purposes.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine("# Text to edit:");
            sysprompt.AppendLinuxLine();

            foreach (var attribute in Attributes)
            {
                sysprompt.AppendLinuxLine(attribute.Content);
            }

            var totalprompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString().CleanupAndTrim());
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this, instruction);

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = 3070;
            if (llmparams.Temperature > 0.5f)
                llmparams.Temperature = 0.5f;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;

            var blendedContent = await LLMSystem.SimpleQuery(llmparams);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                blendedContent = blendedContent.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            LLMSystem.NamesInPromptOverride = null;

            return blendedContent.RemoveUnfinishedSentence().CleanupAndTrim();
        }

        #endregion
    }
}
