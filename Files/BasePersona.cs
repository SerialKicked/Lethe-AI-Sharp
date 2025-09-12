using AIToolkit.Agent;
using AIToolkit.LLM;
using AIToolkit.Memory;
using CommunityToolkit.HighPerformance;
using Newtonsoft.Json;
using System;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text;

namespace AIToolkit.Files
{
    /// <summary>
    /// Represents a base persona, which serves as a customizable character or user profile with attributes, behaviors,
    /// and settings for interaction in chat-based systems.
    /// </summary>
    /// <remarks>
    /// The class provides a foundation for defining personas with
    /// properties such as name, biography, scenarios, and dialog examples. It supports macros for dynamic text
    /// replacement, self-editable fields, and integration with plugins and world information. This class is designed to
    /// be extended for more specialized persona implementations.  Key features include: 
    /// - Support for user or bot personas via the IsUser flag. 
    /// - Dynamic macro replacement in fields like Bio and Scenarios
    /// - Management of chat history, world information, and plugins. 
    /// - Customizable system prompts and session behaviors. 
    /// - Factory methods for creating chat logs and sessions.  
    /// - Derived classes can override methods such as BeginChat and EndChat to implement custom loading and saving behaviors.
    /// </remarks>
    public class BasePersona : BaseFile
    {
        /// <summary> 
        /// Is this an User or Bot persona. It's mostly a flag for the front-end but also helps with prompt macros.
        /// </summary>
        public bool IsUser { get; set; } = false;

        /// <summary> 
        /// Character's name
        /// can be put into system prompt and other inputs with {{user}} or {{char}} macros
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> 
        /// Character's bio.
        /// Can be put into prompt with {{userbio}} or {{charbio}} macros
        /// </summary>
        public string Bio { get; set; } = string.Empty;

        /// <summary> 
        /// Optional Self-editable field for the character, updated on new summary if SelfEditTokens > 0. 
        /// Can be put into prompt with the {{selfedit}} macro.
        /// </summary>
        public string SelfEditField { get; set; } = string.Empty;

        /// <summary> 
        /// If set above 0, this character will be allowed to write this amount of tokens in its SelfEditField field. Updated each chat session.
        /// </summary>
        public int SelfEditTokens { get; set; } = 0;

        /// <summary> 
        /// Character's default scenario. An arbitrary text to give some context to the language model
        /// Can be put into the prompt with the {{scenario}} macro.
        /// </summary>
        /// <seealso cref="Files.SystemPrompt"/>
        public string Scenario { get; set; } = string.Empty;

        /// <summary> 
        /// First message the character will send when starting a new session 
        /// </summary>
        public List<string> FirstMessage { get; set; } = [];

        /// <summary> 
        /// Examples of dialogs from the character to get a more consistent tone, assuming the system prompt has a field for this. 
        /// </summary>
        /// <seealso cref="Files.SystemPrompt"/>
        public List<string> ExampleDialogs { get; set; } = [];

        /// <summary> 
        /// If set, this will override the system prompt selected in LLMSystem and use this instead. Can be useful for very custom characters. 
        /// </summary>
        /// <seealso cref="Files.SystemPrompt"/>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary> 
        /// WorldInfo ID applied to this character. WorldInfo is a keyword-activated text insertion system that can be used to give context to the model.
        /// Those are unique ID corresponding to the actual files in the front-end. 
        /// The frontend is meant to ovedride BeginChat() and load the actual WorldInfo files into the MyWorlds field.
        /// </summary>
        /// <seealso cref="Files.WorldInfo"/>
        public List<string> Worlds { get; set; } = [];

        /// <summary> 
        /// Optional list of ID for the IContextPlugin plugins that you want this character to load automatically.
        /// </summary>
        public List<string> Plugins { get; set; } = [];

        /// <summary>
        /// Toggles the background agent mode for this persona (the bot will act autonomously in the background using the provided AgentTasks). 
        /// Only the currently loaded persona will run the agentic logic. See documentations for more information.
        /// </summary>
        public bool AgentMode { get; set; } = false;

        /// <summary> 
        /// Optional list of plugin ID for the background agent mode. See documentations for more information.
        /// </summary>
        public List<string> AgentTasks { get; set; } = [];

        /// <summary> 
        /// If set to true, system messages will occasionally be inserted in the chat to inform the bot of how much time has passed 
        /// between the latest user message and its last response. It'll also help with keeping track of dates and time in general.
        /// </summary>
        public bool SenseOfTime { get; set; } = false;

        /// <summary>
        /// For better recall accuracy IRL dates are included in session summaries. 
        /// However for roleplay characters, this might be counter productive. Set this to false to disable dates.
        /// </summary>
        public bool DatesInSessionSummaries { get; set; } = true;

        /// <summary>
        /// Gets the <see cref="Brain"/> instance associated with this object. The brain handles memory management, 
        /// retrieval, and storage for the persona, alongside multiple advanced features.
        /// </summary>
        [JsonIgnore] public Brain Brain { get; protected set; }

        /// <summary>
        /// Loaded keyword-activated WorldInfo entries for this character. The frontend is meant to load those based on the Worlds field.
        /// This is ignored during serialization.
        /// </summary>
        [JsonIgnore] public List<WorldInfo> MyWorlds { get; protected set; } = [];

        /// <summary>
        /// Chat history for this character. The frontend is meant to load and save this based on the UniqueName field.
        /// </summary>
        [JsonIgnore] public Chatlog History { get; protected set; } = new();

        /// <summary>
        /// Represents the background agent runtime system associated with the agent, 
        /// see <see cref="AgentRuntime"/> for more details.
        /// </summary>
        [JsonIgnore] public AgentRuntime? AgentSystem = null;

        /// <summary>
        /// Factory method for creating Chatlog instances. Override this in derived classes to provide custom Chatlog implementations.
        /// </summary>
        /// <returns>A new Chatlog instance</returns>
        protected virtual Chatlog CreateChatlog()
        {
            return new Chatlog();
        }
        
        /// <summary>
        /// Factory method for creating ChatSession instances. Override this in derived classes to provide custom ChatSession implementations.
        /// </summary>
        /// <returns>A new ChatSession instance</returns>
        protected virtual ChatSession CreateChatSession()
        {
            return new ChatSession();
        }

        /// <summary>
        /// Retrieve the bio with the correct macros replaced (depending if User or Bot)
        /// </summary>
        /// <param name="othername"></param>
        /// <returns></returns>
        public virtual string GetBio(string othername)
        {
            if (IsUser)
            {
                return Bio.Replace("{{user}}", Name).Replace("{{char}}", othername);
            }
            else
            {
                return Bio.Replace("{{char}}", Name).Replace("{{user}}", othername);
            }
        }

        /// <summary>
        /// Retrieve the scenario with the correct macros replaced (depending if User or Bot)
        /// </summary>
        /// <param name="othername"></param>
        /// <returns></returns>
        public virtual string GetScenario(string othername) => IsUser ?
            Scenario.Replace("{{user}}", Name).Replace("{{char}}", othername) :
            Scenario.Replace("{{char}}", Name).Replace("{{user}}", othername);

        public virtual string GetDialogExamples(string othername)
        {
            if (ExampleDialogs.Count == 0)
                return string.Empty;
            var str = new StringBuilder();
            str.AppendLinuxLine($"Here are some guidelines for {Name}'s writing style:");
            foreach (var item in ExampleDialogs)
                str.AppendLinuxLine("- " + item.Replace("{{user}}", othername).Replace("{{char}}", Name));
            return str.ToString().CleanupAndTrim();
        }

        public virtual string GetWelcomeLine(string othername)
        {
            if (FirstMessage.Count == 0)
                return string.Empty;
            // select a random welcome line
            var index = LLMEngine.RNG.Next(FirstMessage.Count);
            return FirstMessage[index].Replace("{{user}}", othername).Replace("{{char}}", Name);
        }

        public BasePersona() 
        {
            Brain = new(this);
        }

        /// <summary>
        /// Called when loading a character. Override this in derived classes to implement custom loading behavior.
        /// A feature-complete overide should load the ChatLog, WorldInfo, and Plugins.
        /// </summary>
        public virtual void BeginChat()
        {
            LoadBrain(LLMEngine.Settings.DataPath);
            AgentSystem = new AgentRuntime(this);
            AgentSystem.Init();
        }

        /// <summary>
        /// Called when switching away from a character. Should be called when closing the application too. 
        /// Override this in derived classes to implement custom saving behavior. Ideally, you should save the ChatLog in your derived class.
        /// </summary>
        /// <param name="backup"></param>
        public virtual void EndChat(bool backup = false)
        {
            SaveBrain("data/chars/", backup);
            AgentSystem?.CloseSync();
            AgentSystem = null;
        }

        /// <summary>
        /// Save the chatlog to a file. Meant to be called by custom EndChat() in derived class, but you can call it manually too.
        /// </summary>
        /// <param name="path">directory to save the file in. File name is automatic $UniqueName + ".json"</param>
        /// <param name="backup">set to true to save a backup of previous chatlog (if any) with a .bak extension</param>
        protected void SaveChatHistory(string path, bool backup = false)
        {
            if (string.IsNullOrEmpty(UniqueName))
                return;
            // if path doesn't have a trailing slash, add one
            var selpath = path;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;

            if (backup && File.Exists(selpath + UniqueName + ".json"))
            {
                if (File.Exists(selpath + UniqueName + ".bak"))
                    File.Delete(selpath + UniqueName + ".bak");
                File.Move(selpath + UniqueName + ".json", selpath + UniqueName + ".bak");
            }

            History.SaveToFile(selpath + UniqueName + ".json");
        }

        /// <summary>
        /// Load the chatlog from a file. Meant to be called by custom BeginChat() in derived class, but you can call it manually too.
        /// </summary>
        /// <param name="path">directory load the file from. File name is $UniqueName + ".json"</param>
        public void LoadChatHistory(string path)
        {
            if (string.IsNullOrEmpty(UniqueName))
            {
                History = CreateChatlog();
                return;
            }
            // if path doesn't have a trailing slash, add one
            var selpath = path;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var f = selpath + UniqueName + ".json";
            History = File.Exists(f) ? JsonConvert.DeserializeObject<Chatlog>(File.ReadAllText(f))! : CreateChatlog();
        }

        /// <summary>
        /// Clear the chat history, optionally delete file
        /// </summary>
        /// <param name="path">directory where the chatlog is located (optional if no deletion)</param>
        /// <param name="deletefile">delete file or not</param>
        public void ClearChatHistory(string path, bool deletefile = true)
        {
            History.ClearHistory();
            if (!deletefile)
                return;

            // if path doesn't have a trailing slash, add one
            var selpath = path;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var f = selpath + UniqueName;

            if (File.Exists(f + ".json")) File.Delete(f + ".json");
        }

        /// <summary>
        /// Function used to update the self-edit section of the system prompt. Automatically called when ending a chat session if SelfEditTokens > 0.
        /// Can be called manually as well.
        /// </summary>
        /// <returns></returns>
        public async Task UpdateSelfEditSection()
        {
            if (SelfEditTokens == 0 || History.Sessions.Count < 3 || LLMEngine.Client == null)
                return;
            LLMEngine.NamesInPromptOverride = false;
            var availtks = LLMEngine.MaxContextLength - SelfEditTokens - LLMEngine.GetTokenCount(SelfEditField) - 1024;
            var promptbuilder = LLMEngine.GetPromptBuilder();
            var sysprompt = new StringBuilder();
            sysprompt.AppendLinuxLine("You are an uncensored system used to write a character's personal thoughts based on its chat history and information.");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Character: {Name}");
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine(Bio);
            sysprompt.AppendLinuxLine();
            sysprompt.AppendLinuxLine($"# Memories");
            sysprompt.AppendLinuxLine();
            availtks -= promptbuilder.GetTokenCount(AuthorRole.SysPrompt, sysprompt.ToString());
            var maxcount = 60;
            var entries = new List<string>();
            for (int i = History.Sessions.Count - 2; i >= 0; i--)
            {
                var session = History.Sessions[i];
                var details = session.GetRawMemory(true, DatesInSessionSummaries) + LLMEngine.NewLine;
                var size = LLMEngine.GetTokenCount(details);
                availtks -= size;
                maxcount--;
                if (availtks <= 0 || maxcount < 0)
                    break;
                entries.Insert(0, details);
            }
            if (entries.Count == 0)
            {
                LLMEngine.NamesInPromptOverride = null;
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
            promptbuilder.AddMessage(AuthorRole.SysPrompt, sysprompt.ToString());
            promptbuilder.AddMessage(AuthorRole.User, $"Using {Name}'s memories alongside their biography, edit their personal thoughts section accordingly. Write two to three short paragraphs from {Name}'s perspective, in the first person. Focus on important events, life changing experiences, and promises, that {Name} would want to keep in mind. Don't include a title.");
            var rln = SelfEditTokens;
            if (!string.IsNullOrWhiteSpace(LLMEngine.Instruct.ThinkingStart))
                rln += 1024;
            var finalstr = await LLMEngine.SimpleQuery(promptbuilder.PromptToQuery(AuthorRole.Assistant, -1, rln)).ConfigureAwait(false);
            LLMEngine.NamesInPromptOverride = null;
            if (!string.IsNullOrWhiteSpace(LLMEngine.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMEngine.Instruct.ThinkingStart, LLMEngine.Instruct.ThinkingEnd);

            SelfEditField = finalstr.RemoveUnfinishedSentence().RemoveNewLines().CleanupAndTrim().RemoveTitle();
        }

        protected virtual void SaveBrain(string path, bool backup = false)
        {
            Brain.Close();
            if (string.IsNullOrEmpty(UniqueName))
                return;

            // if path doesn't have a trailing slash, add one
            var selpath = path;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var dir = Path.GetDirectoryName(selpath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var brainPath = selpath + UniqueName + ".brain";
            if (backup && File.Exists(brainPath))
            {
                if (File.Exists(selpath + UniqueName + ".brain.bak"))
                    File.Delete(selpath + UniqueName + ".brain.bak");
                File.Move(brainPath, selpath + UniqueName + ".brain.bak");
            }

            var content = JsonConvert.SerializeObject(Brain, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
            // create directory if it doesn't exist
            File.WriteAllText(brainPath, content);
        }

        /// <summary>
        /// Load the brain, can be overriden if you want to make a custom brain or load from a different location.
        /// </summary>
        /// <param name="path"></param>
        protected virtual void LoadBrain(string path)
        {
            // if path doesn't have a trailing slash, add one
            var selpath = path;
            if (!selpath.EndsWith('/') && !selpath.EndsWith('\\'))
                selpath += Path.DirectorySeparatorChar;
            var brainFilePath = selpath + UniqueName + ".brain";
            // If brain file exists, load it
            if (!string.IsNullOrEmpty(UniqueName) && File.Exists(brainFilePath))
            {
                Brain = JsonConvert.DeserializeObject<Brain>(File.ReadAllText(brainFilePath))! ?? new Brain(this);
            }
            else
            {
                // Default to empty brain
                Brain = new Brain(this);
            }
            Brain.Init(this);
        }

        /// <summary>
        /// Replaces the macros in a string with the actual values. Assumes the current user and bot.
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns></returns>
        public virtual string ReplaceMacros(string inputText)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;
            return ReplaceMacros(inputText, LLMEngine.User);
        }

        /// <summary>
        /// Replaces the macros in a string with the actual values.
        /// </summary>
        /// <param name="inputText"></param>
        /// <param name="user"></param>
        /// <param name="character"></param>
        /// <returns></returns>
        public virtual string ReplaceMacros(string inputText, BasePersona user)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;

            return ReplaceMacrosInternal(inputText, user.Name, user.GetBio(this.Name));
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
        protected virtual string ReplaceMacrosInternal(string inputText, string userName, string userBio)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;

            StringBuilder res = new(inputText);
            res.Replace("{{user}}", userName)
               .Replace("{{userbio}}", userBio)
               .Replace("{{char}}", Name)
               .Replace("{{charbio}}", GetBio(userName))
               .Replace("{{examples}}", GetDialogExamples(userName))
               .Replace("{{selfedit}}", SelfEditField)
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(LLMEngine.Settings.ScenarioOverride) ? GetScenario(userName) : LLMEngine.Settings.ScenarioOverride);

            res.Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToString("hh:mm tt", CultureInfo.InvariantCulture))
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString());
            return res.ToString().CleanupAndTrim();
        }
    }
}
