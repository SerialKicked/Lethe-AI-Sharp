using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using AIToolkit.Files;
using AIToolkit.API;
using Newtonsoft.Json;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Globalization;
using static LLama.Common.ChatHistory;
using System;
using System.Threading.Tasks;
using Microsoft.VisualBasic;
using System.Collections.ObjectModel;
using System.Drawing;
using OpenAI.Chat;
using Message = OpenAI.Chat.Message;
using System.Drawing.Drawing2D;

namespace AIToolkit.LLM
{
    public enum SystemStatus { NotInit, Ready, Busy }
    public enum BackendAPI { KoboldAPI, OpenAI }

    /// <summary>
    /// System to handle communications with language models. 
    /// Handles the connection to the server and the generation of prompts. Manages the chat history, personas, inference settings, and instruction formats
    /// </summary>
    public static class LLMSystem
    {
        /// <summary> URL of the backend server </summary>
        public static string BackendUrl { get; set; } = "http://localhost:5001";

        /// <summary> API of the backend server, KoboldAPI (text completion) and OpenAI (chat completion) are both handled </summary>
        public static BackendAPI BackendAPI { get; set; } = BackendAPI.KoboldAPI;

        public static string OpenAIKey { get; set; } = "123";

        /// <summary> Reserved token space for summaries of previous sessions (0 to disable) </summary>
        public static int ReservedSessionTokens { get; set; } = 2048;

        /// <summary> Max length for the bot's reply. </summary>
        public static int MaxReplyLength { get; set; } = 512;

        /// <summary> Total token context window the model can handle </summary>
        public static int MaxContextLength { 
            get => maxContextLength;
            set 
            {
                if (value != maxContextLength) 
                    InvalidatePromptCache();
                maxContextLength = value;
            }
        }

        /// <summary> Name of the currently loaded model </summary>
        public static string CurrentModel { get; private set; } = string.Empty;

        /// <summary> Name of the current backend </summary>
        public static string Backend { get; private set; } = string.Empty;

        /// <summary> If >= 0 it'll override the selected sampler's temperature setting. </summary>
        public static double ForceTemperature { get; set; } = 0.7;

        /// <summary> Overrides the scenario field of the currently loaded character </summary>
        public static string ScenarioOverride { get; set; } = string.Empty;

        /// <summary> Override the Instruct Format setting deciding if character names should be inserted into the prompts (null to disable) </summary>
        public static bool? NamesInPromptOverride { get; set; } = null;

        /// <summary> Should the prompt format the memories and RAG entries into markdown. Some models like it better than others. </summary>
        public static bool MarkdownMemoryFormating { get; set; } = false;

        /// <summary> Should we stop the generation after the first paragraph? </summary>
        public static bool StopGenerationOnFirstParagraph { get; set; } = false;

        /// <summary> Allow keyword-activated snippets to be inserted in the prompt (see WorldInfo and BasePersona) </summary>
        public static bool WorldInfo { get; set; } = true;

        /// <summary> Should the prompt contains only the latest chat session or as much dialog as we can fit? </summary>
        public static SessionHandling SessionHandling { get; set; } = SessionHandling.FitAll;

        internal static Dictionary<string, BasePersona> LoadedPersonas = [];

        /// <summary> Called when the non streaming inference has completed, returns the raw and complete response from the model </summary>
        public static event EventHandler<string>? OnQuickInferenceEnded;
        /// <summary> Called when this library has generated the full prompt, returns full prompt </summary>
        public static event EventHandler<string>? OnFullPromptReady;
        /// <summary> Called during inference each time the LLM outputs a new token, returns the generated token </summary>
        public static event EventHandler<string>? OnInferenceStreamed;
        /// <summary> Called once the inference has ended, returns the full string </summary>
        public static event EventHandler<string>? OnInferenceEnded;
        /// <summary> Called when the system changes states (no init, busy, ready) </summary>
        public static event EventHandler<SystemStatus>? OnStatusChanged;

        /// <summary> Set to true if the backend supports text-to-speech </summary>
        public static bool SupportsTTS => Client?.SupportsTTS ?? false;

        /// <summary> Set to true if the backend supports web search </summary>
        public static bool SupportsWebSearch => Client?.SupportsWebSearch ?? false;

        /// <summary> Set to true if the backend supports vision </summary>
        public static bool SupportsVision => Client?.SupportsVision ?? false;

        public static CompletionType CompletionAPIType => Client?.CompletionType ?? CompletionType.Text;


        private static void RaiseOnFullPromptReady(string fullprompt) => OnFullPromptReady?.Invoke(null, fullprompt);
        private static void RaiseOnStatusChange(SystemStatus newStatus) => OnStatusChanged?.Invoke(null, newStatus);
        private static void RaiseOnInferenceStreamed(string addedString) => OnInferenceStreamed?.Invoke(null, addedString);
        private static void RaiseOnInferenceEnded(string fullString) => OnInferenceEnded?.Invoke(null, fullString);
        private static void RaiseOnQuickInferenceEnded(string fullprompt) => OnQuickInferenceEnded?.Invoke(null, fullprompt);

        /// <summary> List of loaded plugins </summary>
        public static List<IContextPlugin> ContextPlugins { get; set; } = [];

        /// <summary>
        /// Current status of the system. NoInit = not initialized, Ready = ready to use, Busy = working or generating a response.
        /// </summary>
        public static SystemStatus Status
        {
            get => status;
            private set
            {
                status = value;
                RaiseOnStatusChange(value);
            }
        }

        /// <summary> The currently loaded bot persona. You can change it here. </summary>
        /// <seealso cref="BasePersona"/>"
        public static BasePersona Bot { get => bot; set => ChangeBot(value); }

        /// <summary> The currently loaded user persona. You can change it here. </summary>
        /// <seealso cref="BasePersona"/>"
        public static BasePersona User { get => user; set => user = value; }

        /// <summary> Basic logging system to hook into </summary>
        public static ILogger? Logger
        {
            get => logger;
            set => logger = value;
        }

        /// <summary> Instruction format (important for KoboldAPI as it determines how to format the text in a way the model understands) </summary>
        /// <seealso cref="InstructFormat"/>"
        public static InstructFormat Instruct { 
            get => instruct; 
            set
            {
                instruct = value;
                InvalidatePromptCache();
            } 
        }

        /// <summary> Inference settings (The Kobold API handles more settings than OpenAI one).</summary>
        /// <seealso cref="SamplerSettings"/>
        public static SamplerSettings Sampler { get; set; } = new();

        /// <summary> Customer system prompt that will be used. See SysPrompt </summary>
        /// <seealso cref="SystemPrompt"/>"
        public static SystemPrompt SystemPrompt { get; set; } = new();

        /// <summary> Shortcut to the chat history of the currently loaded bot. </summary>
        public static Chatlog History => Bot.History;

        /// <summary> Language models use this character to mark a new line which is different than the one used on Windows.</summary>
        public static readonly string NewLine = "\n";

        private static SystemStatus status = SystemStatus.NotInit;
        private static string StreamingTextProgress = string.Empty;
        private static int maxContextLength = 4096;
        private static InstructFormat instruct = new();
        private static ILogger? logger = null;
        private static BasePersona bot = new() { IsUser = false, Name = "Bot", Bio = "You are an helpful AI assistant whose goal is to answer questions and complete tasks.", UniqueName = string.Empty };
        private static BasePersona user = new() { IsUser = true, Name = "User", UniqueName = string.Empty };

        internal static List<string> vlm_pictures = [];

        public static ILLMServiceClient? Client { get; private set; }
        public static IPromptBuilder? PromptBuilder { get; private set; }

        internal static HashSet<Guid> usedGuidInSession = [];
        internal static PromptInserts dataInserts = [];
        internal static readonly Random RNG = new();

        public static void Init()
        {
            if (Status != SystemStatus.NotInit)
                return;
            // Create the appropriate client based on the selected backend
            var httpClient = new HttpClient();
            Client = BackendAPI switch
            {
                BackendAPI.KoboldAPI => new KoboldCppAdapter(httpClient),
                BackendAPI.OpenAI => new OpenAIAdapter(httpClient),
                _ => throw new NotSupportedException($"Backend {BackendAPI} is not supported")
            };
            // Subscribe to the TokenReceived event
            Client.BaseUrl = BackendUrl;
            Client.TokenReceived += Client_StreamingMessageReceived;

            PromptBuilder = Client.GetPromptBuilder();

            Status = SystemStatus.Ready;
        }

        public static void LoadPersona(List<BasePersona> toload)
        {
            LoadedPersonas = [];
            foreach (var item in toload)
                LoadedPersonas.Add(item.UniqueName, item);
        }

        private static void Client_StreamingMessageReceived(object? sender, LLMTokenStreamingEventArgs e)
        {
            // "null", "stop", "length"
            if (e.IsComplete)
            {
                if (!string.IsNullOrEmpty(e.Token))
                    StreamingTextProgress += e.Token;
                var response = StreamingTextProgress.Trim();
                if (e.FinishReason == "length")
                {
                    var removelist = Instruct.GetStoppingStrings(User, Bot);
                    // look at response string for the stop string, if found, and not in first position of the string, remove the stop string and everything beyond.
                    foreach (var tocheck in removelist)
                    {
                        var index = response.LastIndexOf(tocheck);
                        if (index > 1)
                        {
                            response = response[..index];
                        }
                    }
                }
                foreach (var ctxplug in ContextPlugins)
                {
                    if (ctxplug.Enabled && ctxplug.ReplaceOutput(ReplaceMacros(response), History, out var editedresponse))
                        response = editedresponse;
                }
                Status = SystemStatus.Ready;
                RaiseOnInferenceEnded(response);
            }
            else
            {
                StreamingTextProgress += e.Token;
                RaiseOnInferenceStreamed(e.Token);
            }
        }

        public static string ReplaceMacros(string inputText)
        {
            return ReplaceMacros(inputText, User, Bot);
        }

        /// <summary>
        /// Replaces the macros in a string with the actual values.
        /// </summary>
        /// <param name="inputText"></param>
        /// <param name="user"></param>
        /// <param name="character"></param>
        /// <returns></returns>
        public static string ReplaceMacros(string inputText, BasePersona user, BasePersona character)
        {
            if (string.IsNullOrEmpty(inputText))
                return string.Empty;
            StringBuilder res = new(inputText);
            res.Replace("{{user}}", user.Name)
               .Replace("{{userbio}}", user.GetBio(character.Name))
               .Replace("{{char}}", character.Name)
               .Replace("{{charbio}}", character.GetBio(user.Name))
               .Replace("{{examples}}", character.GetDialogExamples(user.Name))
               .Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToShortTimeString())
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString())
               .Replace("{{selfedit}}", character.SelfEditField)
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(ScenarioOverride) ? character.GetScenario(user.Name) : ScenarioOverride);
            return res.ToString();
        }

        public static string ReplaceMacros(string inputText, string userName, BasePersona character)
        {
            StringBuilder res = new(inputText);
            res.Replace("{{user}}", userName)
               .Replace("{{userbio}}", "This is the user interacting with you.")
               .Replace("{{char}}", character.Name)
               .Replace("{{charbio}}", character.GetBio(userName))
               .Replace("{{examples}}", character.GetDialogExamples(userName))
               .Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToShortTimeString())
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString())
               .Replace("{{selfedit}}", character.SelfEditField)
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(ScenarioOverride) ? character.GetScenario(userName) : ScenarioOverride);
            return res.ToString().CleanupAndTrim();
        }

        /// <summary>
        /// Change the current bot persona.
        /// </summary>
        /// <param name="newbot"></param>
        private static void ChangeBot(BasePersona newbot)
        {
            InvalidatePromptCache();
            bot.EndChat(backup: true);
            if (!string.IsNullOrEmpty(bot.UniqueName))
                (bot as IFile).SaveToFile("data/chars/" + bot.UniqueName + ".json");
            bot = newbot;
            bot.BeginChat();
            RAGSystem.VectorizeChatBot(Bot);
            // if first time interaction, display welcome message from bot
            if (History.Sessions.Count == 0)
            {
                History.Sessions.Add(new ChatSession());
            }
            if (History.CurrentSession.Messages.Count == 0 && History.Sessions.Count == 1)
            {
                var message = new SingleMessage(AuthorRole.Assistant, DateTime.Now, bot.GetWelcomeLine(User.Name), bot.UniqueName, User.UniqueName);
                History.LogMessage(message);
            }
        }

        /// <summary>
        /// Returns the current token count of a string.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static int GetTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text) || Client == null)
                return 0;
            else if (Status == SystemStatus.NotInit || text.Length > MaxContextLength * 10)
                return TokenTools.CountTokens(text);
            try
            {
                return Client.CountTokensSync(text);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to count tokens. Falling back to failsafe");
                return TokenTools.CountTokens(text);
            }
        }

        public static bool CancelGeneration()
        {
            if (Client == null)
                return true;
            try
            {
                var mparams = new GenkeyData() { };
                var success = Client.AbortGenerationSync();
                if (success)
                    Status = SystemStatus.Ready;
                return success;
            }
            catch (Exception ex)
            {
                //MessageBox.Show($"An error occured while counting tokens, estimate used instead. {ex.Message}");
                logger?.LogError(ex, "Failed to cancel generation");
                return false;
            }
        }

        public static void Setup(string url, BackendAPI backend, string? key = null)
        {
            Status = SystemStatus.NotInit;
            BackendUrl = url;
            BackendAPI = backend;
            OpenAIKey = key ?? "123";
            Init();
        }

        /// <summary>
        /// Check if the backend is working
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> CheckBackend()
        {
            if (Client == null)
                return false;
            return await Client.CheckBackend();
        }

        /// <summary>
        /// Connects to the LLM server and retrieves the needed info.
        /// </summary>
        public static async Task Connect()
        {
            Init();
            if (Client == null)
            {
                MaxContextLength = 4096;
                CurrentModel = "Nothing Loaded";
                Backend = "No backend";
                return;
            }
            try
            {
                MaxContextLength = await Client.GetMaxContextLength();
                CurrentModel = await Client.GetModelInfo();
                Backend = await Client.GetBackendInfo();
                Status = SystemStatus.Ready;
            }
            catch (Exception ex)
            {
                MaxContextLength = 4096;
                CurrentModel = "Error";
                Backend = "Error";
                LLMSystem.Logger?.LogError(ex, "Failed to connect to LLM server: " +ex.Message);
            }
        }

        /// <summary>
        /// Generates the system prompt content.
        /// </summary>
        /// <param name="MsgSender"></param>
        /// <param name="newMessage"></param>
        /// <returns></returns>
        private static string GenerateSystemPromptContent(AuthorRole MsgSender, string newMessage)
        {
            var searchmessage = string.IsNullOrWhiteSpace(newMessage) ? History.GetLastUserMessageContent() : newMessage;
            var rawprompt = new StringBuilder(SystemPrompt.GetSystemPromptRaw(Bot));

            // Check if the plugin has anything to add to system prompts
            foreach (var ctxplug in ContextPlugins)
            {
                if (ctxplug.Enabled && ctxplug.AddToSystemPrompt(searchmessage, History, out var ctxinfo))
                    rawprompt.AppendLinuxLine(ctxinfo);
            }

            // Now add the system prompt entries we gathered
            var syspromptentries = dataInserts.GetEntriesByPosition(-1);
            if (syspromptentries.Count > 0)
            {
                rawprompt.AppendLinuxLine().AppendLinuxLine(SystemPrompt.WorldInfoTitle);
                foreach (var item in syspromptentries)
                    rawprompt.AppendLinuxLine(item.Content);
            }

            if (Bot.SessionMemorySystem && ReservedSessionTokens > 0 && History.Sessions.Count > 1)
            {
                usedGuidInSession = dataInserts.GetGuids();
                var shistory = History.GetPreviousSummaries(ReservedSessionTokens - GetTokenCount(ReplaceMacros(SystemPrompt.SessionHistoryTitle)) - 3, SystemPrompt.SubCategorySeparator);
                if (!string.IsNullOrEmpty(shistory))
                {
                    rawprompt.AppendLinuxLine(NewLine + ReplaceMacros(SystemPrompt.SessionHistoryTitle) + NewLine);
                    rawprompt.AppendLinuxLine(shistory);
                }
            }

            return ReplaceMacros(rawprompt.ToString()).CleanupAndTrim();
        }

        /// <summary>
        /// Checks for RAG entries and refreshes the textual inserts.
        /// </summary>
        /// <param name="MsgSender"></param>
        /// <param name="newMessage"></param>
        /// <returns></returns>
        private static async Task UpdateRagAndInserts(AuthorRole MsgSender, string newMessage)
        {
            // Check for RAG entries and refresh the textual inserts
            dataInserts.DecreaseDuration();
            var searchmessage = string.IsNullOrWhiteSpace(newMessage) ? History.GetLastUserMessageContent() : newMessage;
            if (RAGSystem.Enabled)
            {
                var search = await RAGSystem.Search(ReplaceMacros(searchmessage));
                search.RemoveAll(search => usedGuidInSession.Contains(search.session.Guid));
                dataInserts.AddMemories(search);
            }
            // Check for keyword-activated world info entries
            if (WorldInfo && Bot.MyWorlds.Count > 0)
            {
                var _currentWorldEntries = new List<WorldEntry>();
                foreach (var world in Bot.MyWorlds)
                {
                    _currentWorldEntries.AddRange(world.FindEntries(History, searchmessage));
                }
                foreach (var entry in _currentWorldEntries)
                {
                    dataInserts.AddInsert(new PromptInsert(
                        entry.Guid, entry.Message, entry.Position == WEPosition.SystemPrompt ? -1 : entry.PositionIndex, entry.Duration)
                        );
                }
            }
            // Check all sessions for sticky entries
            foreach (var session in History.Sessions)
            {
                if (session.Sticky && session != History.CurrentSession)
                {
                    var rawmem = session.GetRawMemory(!MarkdownMemoryFormating);
                    dataInserts.AddInsert(new PromptInsert(session.Guid, rawmem, RAGSystem.RAGIndex, 1));
                }
            }
        }

        /// <summary>
        /// Generates a full prompt for the LLM to use
        /// </summary>
        /// <param name="newMessage">Added message from the user</param>
        /// <returns></returns>
        private static async Task<object> GenerateFullPrompt(AuthorRole MsgSender, string newMessage, string? pluginMessage = null, int imgpadding = 0)
        {
            var availtokens = MaxContextLength - MaxReplyLength - imgpadding;
            PromptBuilder!.ResetPrompt();

            // setup user message (+ optional plugin message) and count tokens used
            var msg = string.IsNullOrEmpty(newMessage) ? string.Empty : Instruct.FormatSinglePrompt(MsgSender, User, Bot, newMessage);
            var pluginmsg = string.IsNullOrEmpty(pluginMessage) ? string.Empty : Instruct.FormatSinglePrompt(AuthorRole.System, User, Bot, pluginMessage);
            if (!string.IsNullOrEmpty(newMessage))
            {
                availtokens -= PromptBuilder.GetTokenCount(MsgSender, newMessage);
            }
            if (!string.IsNullOrEmpty(pluginMessage))
            {
                availtokens -= PromptBuilder.GetTokenCount(AuthorRole.System, pluginMessage);
            }

            // update the RAG, world info, and summary stuff
            await UpdateRagAndInserts(MsgSender, newMessage);
            // Prepare the full system prompt and count the tokens used
            var rawprompt = GenerateSystemPromptContent(MsgSender, newMessage);
            availtokens -= PromptBuilder.AddMessage(AuthorRole.SysPrompt, rawprompt);

            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
                availtokens -= GetTokenCount(Instruct.GetResponseStart(User));
            else
                availtokens -= GetTokenCount(Instruct.GetResponseStart(Bot));

            // get the full, formated chat history complemented by the data inserts
            History.AddHistoryToPrompt(SessionHandling, availtokens, dataInserts);
            if (!string.IsNullOrEmpty(newMessage))
            {
                PromptBuilder.AddMessage(MsgSender, newMessage);
            }
            if (!string.IsNullOrEmpty(newMessage) || MsgSender != AuthorRole.User)
            {
                if (!string.IsNullOrEmpty(pluginmsg))
                {
                    PromptBuilder.AddMessage(AuthorRole.System, pluginmsg);
                }
            }

            var final = PromptBuilder.GetTokenUsage();
            if (final > (MaxContextLength - MaxReplyLength))
            {
                var diff = final - (MaxContextLength - MaxReplyLength);
                logger?.LogWarning("The prompt is {Diff} tokens over the limit.", diff);
            }
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
                return PromptBuilder.PromptToQuery(AuthorRole.User);
            else
                return PromptBuilder.PromptToQuery(AuthorRole.Assistant);
        }

        internal static Message FormatSingleMessage(AuthorRole role, BasePersona user, BasePersona bot, string prompt)
        {
            var realprompt = prompt;
            var addname = NamesInPromptOverride ?? Instruct.AddNamesToPrompt;
            if (role != AuthorRole.Assistant && role != AuthorRole.User)
                addname = false;
            string? selname = null;
            if (addname)
            {
                if (role == AuthorRole.Assistant)
                {
                    realprompt = string.Format("{0}: {1}", bot.Name, prompt);
                    selname = bot.Name;
                }
                else if (role == AuthorRole.User)
                {
                    realprompt = string.Format("{0}: {1}", user.Name, prompt);
                    selname = user.Name;
                }
            }
            return new Message(TokenTools.InternalRoleToChatRole(role), ReplaceMacros(realprompt, user, bot), selname);
        }

        public static async Task AddBotMessage()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.Assistant, string.Empty);
        }

        public static async Task ImpersonateUser()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.User, string.Empty);
        }

        /// <summary>
        /// Sends a message to the bot and logs it to the chat history. Response done through the RaiseOnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <param name="MsgSender"></param>
        /// <param name="userInput"></param>
        /// <param name="logtohistory"></param>
        /// <returns></returns>
        public static async Task SendMessageToBot(SingleMessage message)
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(message.Role, message.Message);
        }

        /// <summary>
        /// Rerolls the last response from the bot.
        /// </summary>
        /// <returns></returns>
        public static async Task RerollLastMessage()
        {
            if (Status != SystemStatus.Ready || History.CurrentSession.Messages.Count == 0 || History.LastMessage()?.Role != AuthorRole.Assistant || Client == null || PromptBuilder == null)
                return;
            History.RemoveLast();
            if (PromptBuilder.Count == 0)
            {
                await StartGeneration(AuthorRole.Assistant, string.Empty);
            }
            else
            {
                Status = SystemStatus.Busy;
                StreamingTextProgress = string.Empty;
                if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
                {
                    StreamingTextProgress = Instruct.ThinkingStart + Instruct.ThinkingForcedThought;
                    RaiseOnInferenceStreamed(StreamingTextProgress);
                }
                RaiseOnFullPromptReady(PromptBuilder.PromptToText());
                await Client.GenerateTextStreaming(PromptBuilder.PromptToQuery(AuthorRole.Assistant));
            }
        }

        /// <summary>
        /// Send a system message to the bot and wait for a response. Message log is optional.
        /// </summary>
        /// <param name="systemMessage">Message from sender</param>
        /// <param name="logSystemPrompt">Log the message to the chat history</param>
        /// <returns></returns>
        public static async Task<string> QuickInferenceForSystemPrompt(string systemMessage, bool logSystemPrompt)
        {
            if (Status == SystemStatus.Busy || Client == null)
                return string.Empty;
            var inputText = systemMessage;
            StreamingTextProgress = string.Empty;
            if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
                StreamingTextProgress = Instruct.ThinkingStart + Instruct.ThinkingForcedThought;

            var genparams = await GenerateFullPrompt(AuthorRole.System, inputText, null, 0);
            if (!string.IsNullOrEmpty(systemMessage) && logSystemPrompt)
                Bot.History.LogMessage(AuthorRole.System, systemMessage, User, Bot);
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(genparams);
            Status = SystemStatus.Ready;
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        /// <summary>
        /// Starts the generation process for the bot.
        /// </summary>
        /// <param name="MsgSender">Role of the sender</param>
        /// <param name="userInput">Message from sender</param>
        /// <returns></returns>
        private static async Task StartGeneration(AuthorRole MsgSender, string userInput)
        {
            if (Status == SystemStatus.Busy || Client == null || PromptBuilder == null)
                return;
            Status = SystemStatus.Busy;
            var inputText = userInput;
            var lastuserinput = string.IsNullOrEmpty(userInput) ? History.GetLastUserMessageContent() : userInput;
            var insertmessages = new List<string>();
            if (!string.IsNullOrEmpty(lastuserinput))
                foreach (var ctxplug in ContextPlugins)
                {
                    if (!ctxplug.Enabled)
                        continue;
                    var plugres = await ctxplug.ReplaceUserInput(ReplaceMacros(lastuserinput));
                    if (plugres.IsHandled && !string.IsNullOrEmpty(plugres.Response))
                    {
                        if (plugres.Replace)
                            lastuserinput = plugres.Response;
                        else
                            insertmessages.Add(plugres.Response);
                    }
                }
            var pluginmessage = string.Empty;
            foreach (var item in insertmessages)
                pluginmessage += item + NewLine;
            pluginmessage = pluginmessage.Trim('\n');

            StreamingTextProgress = string.Empty;

            if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
            {
                StreamingTextProgress = Instruct.ThinkingStart + Instruct.ThinkingForcedThought;
                RaiseOnInferenceStreamed(StreamingTextProgress);
            }
            var genparams = await GenerateFullPrompt(MsgSender, inputText, pluginmessage, vlm_pictures.Count > 0 ? vlm_pictures.Count * 1024 : 0);
            if (!string.IsNullOrEmpty(userInput))
                Bot.History.LogMessage(MsgSender, userInput, User, Bot);
            RaiseOnFullPromptReady(PromptBuilder.PromptToText());
            await Client.GenerateTextStreaming(genparams);
        }

        /// <summary>
        /// Returns an away string depending on the last chat's date.
        /// </summary>
        /// <returns></returns>
        public static string GetAwayString()
        {
            if (History.CurrentSession.Messages.Count == 0 || !Bot.SenseOfTime || History.CurrentSession != History.Sessions.Last())
                return string.Empty;

            var timespan = DateTime.Now - History.CurrentSession.Messages.Last().Date;
            if (timespan <= new TimeSpan(2, 0, 0))
                return string.Empty;

            var msgtxt = (DateTime.Now.Date != History.CurrentSession.Messages.Last().Date.Date) || (timespan > new TimeSpan(12, 0, 0)) ? 
                $"We're {DateTime.Now.DayOfWeek} {StringExtensions.DateToHumanString(DateTime.Now)}." : string.Empty;
            if (timespan.Days > 1)
                msgtxt += $" Your last chat was {timespan.Days} days ago. " + "It is {{time}} now.";
            else if (timespan.Days == 1)
                msgtxt += " The last chat happened yesterday. It is {{time}} now.";
            else
                msgtxt += $" The last chat was about {timespan.Hours} hours ago. " + "It is {{time}} now.";
            msgtxt = "*" + msgtxt.Trim() + "*" + NewLine;
            return ReplaceMacros(msgtxt);
        }

        public static void InvalidatePromptCache()
        {
            if (PromptBuilder != null)
                PromptBuilder.ResetPrompt();
            dataInserts.Clear();
            usedGuidInSession.Clear();
        }

        public static async Task<string> SimpleQuery(object chatlog)
        {
            if (Client == null)
                return string.Empty;
            var oldst = status;
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(chatlog);
            Status = oldst;
            RaiseOnQuickInferenceEnded(result);
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        public static async Task<WebQueryFullResponse> WebSearch(string query)
        {
            if (Client == null || !SupportsWebSearch)
                return [];
            var res = await Client.WebSearch(query);
            var webres = JsonConvert.DeserializeObject<WebQueryFullResponse>(res);
            if (webres is null)
            {
                logger?.LogError("Failed to parse web search response");
                return [];
            }
            return webres;
        }

        public static async Task<byte[]> GenerateTTS(string input, string voiceID)
        {
            // female: "Tina", "super chariot of death", "super chariot in death"
            // male: "Lor_ Merciless", "kobo", "chatty"
            if (Client?.SupportsTTS != true)
            {
                logger?.LogError("TTS is not supported by the current backend.");
                return [];
            }
            var audioData = await Client.TextToSpeech(input, voiceID);
            return audioData;
        }

        public static void RemoveQuickInferenceEventHandler()
        {
            OnQuickInferenceEnded = null;
        }

        #region *** Visual Language Model Management ***

        public static void VLM_ClearImages()
        {
            vlm_pictures = [];
        }

        public static void VLM_AddB64Image(string base64)
        {
            vlm_pictures.Add(base64);
        }

        public static void VLM_AddImage(Image image, int size = 1024)
        {
            var res = ImageUtils.ImageToBase64(image, size);
            if (!string.IsNullOrEmpty(res))
                vlm_pictures.Add(res);
        }

        #endregion

    }
}
