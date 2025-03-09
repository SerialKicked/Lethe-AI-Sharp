using Newtonsoft.Json;
using System.Text;
using AIToolkit.LLM;
using System.Reflection.PortableExecutable;

namespace AIToolkit.Files
{
    public class BasePersona : BaseFile
    {
        /// <summary> Character's name (used by LLM) </summary>
        public string Name { get; set; } = string.Empty;
        public bool IsUser { get; set; } = false;
        /// <summary> Character's bio (used by LLM) </summary>
        public string Bio { get; set; } = string.Empty;
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

        public BasePersona() { }

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

        protected void LoadChatHistory(string path)
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

        public async Task UpdateSelfEditSection()
        {
            if (SelfEditTokens == 0 || History.Sessions.Count < 3)
                return;
            LLMSystem.NamesInPromptOverride = false;
            var availtks = LLMSystem.MaxContextLength - SelfEditTokens - LLMSystem.GetTokenCount(SelfEditField) - 400;

            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to complete a character's personal memories based on its chat history.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine(Bio);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Chat History");
            sysprompt.AppendLinuxLine();

            var sizeeval = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, this, sysprompt.ToString());
            availtks -= LLMSystem.GetTokenCount(sizeeval);
            var maxcount = 30;
            var entries = new List<string>();
            for (int i = History.Sessions.Count - 2; i >= 0; i--)
            {
                var session = History.Sessions[i];
                var details = $"Between {session.StartTime.DayOfWeek} {StringExtensions.DateToHumanString(session.StartTime)} and {session.EndTime.DayOfWeek} {StringExtensions.DateToHumanString(session.EndTime)}, the following events took places from {LLMSystem.Bot.Name}'s perspective: {session.Summary.RemoveNewLines()}" + LLMSystem.NewLine;
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
            var query = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.User, LLMSystem.User, this, $"Using {Name}'s memories alongside their biography, edit their personal thoughts section accordingly. Write two to three short paragraphs from {Name}'s perspective. Focus on important events, life changing experiences, and promises, that {Name} would want to keep in mind.");

            totalprompt += query + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = totalprompt;
            llmparams.Max_length = SelfEditTokens;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            LLMSystem.NamesInPromptOverride = null;
            SelfEditField = finalstr.RemoveNewLines().CleanupAndTrim();
        }
    }
}
