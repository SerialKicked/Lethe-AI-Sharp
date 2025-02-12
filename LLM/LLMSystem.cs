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

namespace AIToolkit.LLM
{
    public enum SystemStatus { NotInit, Ready, Busy }
    public enum SystemPromptSection { MainPrompt, BotBio, UserBio, Scenario, Memory, ContextInfo }

    public delegate void BasicDelegateFunction();
    public delegate void UpdateMessageFunction(string update);

    public static class LLMSystem
    {
        public static int MaxRAGEntries { get; set; } = 3;
        public static int RAGIndex { get; set; } = 3;
        public static int ReservedSessionTokens { get; set; } = 2048;
        public static int MaxReplyLength { get; set; } = 512;
        public static int MaxContextLength { 
            get => maxContextLength;
            set 
            {
                if (value != maxContextLength) 
                    InvalidatePromptCache();
                maxContextLength = value;
            }
        }
        public static string CurrentModel { get; private set; } = string.Empty;
        public static string Backend { get; private set; } = string.Empty;
        public static double ForceTemperature { get; set; } = 0.7;
        public static string ScenarioOverride { get; set; } = string.Empty;
        public static bool? NamesInPromptOverride { get; set; } = null;
        public static bool WebBrowsingPlugin { get; set; } = false;
        public static bool MarkdownMemoryFormating { get; set; } = false;
        public static bool WorldInfo { get; set; } = true;
        public static SessionHandling SessionHandling { get; set; } = SessionHandling.FitAll;

        internal static Dictionary<string, BasePersona> LoadedPersonas = [];

        public static event EventHandler<string>? OnFullPromptReady;
        /// <summary> Called during inference each time the LLM outputs a new token </summary>
        public static event EventHandler<string>? OnInferenceStreamed;
        /// <summary> Called once the inference has ended, returns the full string </summary>
        public static event EventHandler<string>? OnInferenceEnded;
        /// <summary> Called when the system changes states (no init, busy, ready) </summary>
        public static event EventHandler<SystemStatus>? OnStatusChanged;

        private static void RaiseOnFullPromptReady(string fullprompt) => OnFullPromptReady?.Invoke(null, fullprompt);
        private static void RaiseOnStatusChange(SystemStatus newStatus) => OnStatusChanged?.Invoke(null, newStatus);
        private static void RaiseOnInferenceStreamed(string addedString) => OnInferenceStreamed?.Invoke(null, addedString);
        private static void RaiseOnInferenceEnded(string fullString) => OnInferenceEnded?.Invoke(null, fullString);


        public static List<IContextPlugin> ContextPlugins { get; set; } = [];

        public static readonly Random RNG = new();

        public static SystemStatus Status
        {
            get => status;
            private set
            {
                status = value;
                RaiseOnStatusChange(value);
            }
        }

        public static BasePersona Bot { get => bot; set => ChangeBot(value); }
        public static BasePersona User { get => user; set => user = value; }
        public static ILogger? Logger
        {
            get => logger;
            set => logger = value;
        }
        public static InstructFormat Instruct { 
            get => instruct; 
            set
            {
                instruct = value;
                InvalidatePromptCache();
            } 
        }
        public static SamplerSettings Sampler { get; set; } = new();
        public static SystemPrompt SystemPrompt { get; set; } = new();
        public static Chatlog History => Bot.History;

        public static readonly string NewLine = "\n";

        private static SystemStatus status = SystemStatus.NotInit;
        private static int systemPromptSize = 0;
        private static string StreamingTextProgress = string.Empty;
        private static string _LastGeneratedPrompt = string.Empty;
        private static readonly HttpClient _httpclient = new();
        private static readonly KoboldCppClient Client = new(_httpclient);
        private static int maxContextLength = 4096;
        private static InstructFormat instruct = new();
        private static ILogger? logger = null;
        private static BasePersona bot = new() { IsUser = false, Name = "Bot", Bio = "You are an helpful AI assistant whose goal is to answer questions and complete tasks.", UniqueName = string.Empty };
        private static BasePersona user = new() { IsUser = true, Name = "User", UniqueName = string.Empty };

        internal static HashSet<Guid> usedGuidInSession = [];
        internal static PromptInserts dataInserts = [];

        public static void Init()
        {
            if (Status != SystemStatus.NotInit)
                return;
            Client.BaseUrl = "http://localhost:5001";
            Client.ReadResponseAsString = true;
            Client.StreamingMessageReceived += Client_StreamingMessageReceived;
            // Load plugins
            Status = SystemStatus.Ready;
        }

        public static void LoadPersona(List<BasePersona> toload)
        {
            LoadedPersonas = [];
            foreach (var item in toload)
                LoadedPersonas.Add(item.UniqueName, item);
        }

        private static void Client_StreamingMessageReceived(object? sender, TextStreamingEvenArg e)
        {
            // "null", "stop", "length"
            if (e.Data.finish_reason != "null")
            {
                if (!string.IsNullOrEmpty(e.Data.token))
                    StreamingTextProgress += e.Data.token;
                var response = StreamingTextProgress.Trim();
                if (e.Data.finish_reason == "length")
                {
                    var removelist = Instruct.GetStoppingStrings(User, Bot);
                    // look at response string for the stop string, if found, and not in first position of the string, remove the stop string and everything beyond.
                    foreach (var tocheck in removelist)
                    {
                        var index = response.LastIndexOf(tocheck);
                        if (index > 1)
                        {
                            response = response.Remove(index);
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
                StreamingTextProgress += e.Data.token;
                RaiseOnInferenceStreamed(e.Data.token);
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
            StringBuilder res = new(inputText);
            res.Replace("{{user}}", user.Name)
               .Replace("{{userbio}}", user.GetBio(character.Name))
               .Replace("{{char}}", character.Name)
               .Replace("{{charbio}}", character.GetBio(user.Name))
               .Replace("{{examples}}", character.GetDialogExamples(user.Name))
               .Replace("{{date}}", StringExtensions.DateToHumanString(DateTime.Now))
               .Replace("{{time}}", DateTime.Now.ToShortTimeString())
               .Replace("{{day}}", DateTime.Now.DayOfWeek.ToString())
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
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(ScenarioOverride) ? character.GetScenario(userName) : ScenarioOverride);
            return res.ToString();
        }

        /// <summary>
        /// Change the current bot persona.
        /// </summary>
        /// <param name="newbot"></param>
        private static void ChangeBot(BasePersona newbot)
        {
            InvalidatePromptCache();
            bot.EndChat(backup: true);
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
            if (string.IsNullOrEmpty(text))
                return 0;
            else if (text.Length > MaxContextLength * 10)
                return text.Length / 5;
            try
            {
                var mparams = new KcppPrompt() { Prompt = text };
                var res = Client.TokencountAsync(mparams).GetAwaiter().GetResult();
                return res.Value;
            }
            catch (Exception)
            {
                //MessageBox.Show($"An error occured while counting tokens, estimate used instead. {ex.Message}");
                return text.Length / 5; // or any default value you want to return in case of an error
            }
        }

        public static bool CancelGeneration()
        {
            try
            {
                var mparams = new Body3() { };
                var res = Client.AbortAsync(mparams).GetAwaiter().GetResult();
                if (res.Success)
                    Status = SystemStatus.Ready;
                return res.Success;
            }
            catch (Exception)
            {
                //MessageBox.Show($"An error occured while counting tokens, estimate used instead. {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Connects to the LLM server and retrieves the needed info.
        /// </summary>
        public static async Task Connect()
        {
            Init();
            try
            {
                var result = await Client.TrueMaxContextLengthAsync();
                MaxContextLength = result.Value;
                var info = await Client.ModelAsync();
                var index = info.Result.IndexOf('/');
                if (index > 0)
                    info.Result = info.Result[(index + 1)..];
                CurrentModel = info.Result;
                var engine = await Client.ExtraVersionAsync();
                Backend = engine.result + " " + engine.version;
            }
            catch (Exception ex)
            {
                throw new Exception("An error occured while connecting to the LLM server. " + ex.Message);
            }
        }

        /// <summary>
        /// Generates a full prompt for the LLM to use
        /// </summary>
        /// <param name="newMessage">Added message from the user</param>
        /// <returns></returns>
        private static async Task<string> GenerateFullPrompt(AuthorRole MsgSender, string newMessage, string? pluginMessage = null)
        {
            // setup user message (+ optional plugin message) and count tokens used
            var msg = string.IsNullOrEmpty(newMessage) ? string.Empty : Instruct.FormatSinglePrompt(MsgSender, User, Bot, newMessage);
            var pluginmsg = string.IsNullOrEmpty(pluginMessage) ? string.Empty : Instruct.FormatSinglePrompt(AuthorRole.System, User, Bot, pluginMessage);
            var tokensused = string.IsNullOrEmpty(msg) ? 0 :GetTokenCount(msg);
            tokensused += string.IsNullOrEmpty(pluginmsg) ? 0 : GetTokenCount(pluginmsg);
            var rawprompt = new StringBuilder(SystemPrompt.GetSystemPromptRaw(Bot));
            var searchmessage = string.IsNullOrWhiteSpace(newMessage) ? History.GetLastUserMessageContent() : newMessage;
            // refresh the textual inserts
            dataInserts.DecreaseDuration();
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
                    dataInserts.AddInsert(new PromptInsert(entry.Guid, entry.Message, entry.Position == WEPosition.SystemPrompt ? -1 : entry.PositionIndex, entry.Duration));
                }
            }
            // Check all sessions for sticky entries
            foreach (var session in History.Sessions)
            {
                if (session.Sticky && session != History.CurrentSession)
                {
                    var rawmem = session.GetRawMemory(!MarkdownMemoryFormating);
                    dataInserts.AddInsert(new PromptInsert(session.Guid, rawmem, RAGIndex , 1));
                }
            }
            // Check if the plugin has anything to add to system prompts
            foreach (var ctxplug in ContextPlugins)
            {
                if (ctxplug.Enabled && ctxplug.AddToSystemPrompt(searchmessage, History, out var ctxinfo))
                    rawprompt.AppendLinuxLine(ctxinfo);
            }

            usedGuidInSession = dataInserts.GetGuids();
            // Check for RAG entries
            if (RAGSystem.Enabled)
            {
                var search = await RAGSystem.Search(ReplaceMacros(searchmessage), MaxRAGEntries);
                search.RemoveAll(search => usedGuidInSession.Contains(search.session.Guid));
                dataInserts.AddMemories(search);
            }
            // Now add the system prompt entries we gathered
            var syspromptentries = dataInserts.GetEntriesByPosition(-1);
            if (syspromptentries.Count > 0)
            {
                rawprompt.AppendLinuxLine().AppendLinuxLine(SystemPrompt.WorldInfoTitle);
                foreach (var item in syspromptentries)
                    rawprompt.AppendLinuxLine(item.Content);
            }
            // Prepare the full system prompt and count the tokens used
            var sysprompt = Instruct.BoSToken + Instruct.FormatSinglePrompt(AuthorRole.SysPrompt, User, Bot, rawprompt.ToString().CleanupAndTrim());
            systemPromptSize = GetTokenCount(sysprompt);
            tokensused += systemPromptSize;
            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
                tokensused += GetTokenCount(Instruct.GetResponseStart(User));
            else
                tokensused += GetTokenCount(Instruct.GetResponseStart(Bot));
            var availtokens = (int)(MaxContextLength) - tokensused - MaxReplyLength;
            // If we have a session memory system (and previous available sessions), reserve more tokens
            if (Bot.SessionMemorySystem && History.Sessions.Count > 1)
                availtokens -= ReservedSessionTokens;
            // get the full, formated chat history complemented by the data inserts
            var history = History.GetMessageHistory(SessionHandling, availtokens, dataInserts);

            if (Bot.SessionMemorySystem && ReservedSessionTokens > 0 && History.Sessions.Count > 1)
            {
                usedGuidInSession = dataInserts.GetGuids();
                var shistory = History.GetPreviousSummaries(ReservedSessionTokens - GetTokenCount(ReplaceMacros(SystemPrompt.SessionHistoryTitle)) - 3, SystemPrompt.SubCategorySeparator);
                if (!string.IsNullOrEmpty(shistory))
                {
                    rawprompt.AppendLinuxLine(NewLine + ReplaceMacros(SystemPrompt.SessionHistoryTitle) + NewLine);
                    rawprompt.AppendLinuxLine(shistory);
                    sysprompt = Instruct.BoSToken + Instruct.FormatSinglePrompt(AuthorRole.SysPrompt, User, Bot, rawprompt.ToString().CleanupAndTrim());
                }
            }
            string res = string.Empty;
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
            {
                res = sysprompt + history + msg + Instruct.GetUserStart(User).TrimEnd();
            }
            else
            {
                res = sysprompt + history + msg + pluginmsg + Instruct.GetResponseStart(Bot);
            }
            var final = GetTokenCount(res);
            if (final > MaxContextLength + MaxReplyLength)
            {
                var diff = final - (MaxContextLength + MaxReplyLength);
                logger?.LogWarning("The prompt is {Diff} tokens over the limit.", diff);
            }
            return res;
        }

        public static async Task AddBotMessage()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.Assistant, "");
        }

        public static async Task ImpersonateUser()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.User, "");
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
            if (Status != SystemStatus.Ready || History.CurrentSession.Messages.Count == 0 || History.LastMessage()?.Role != AuthorRole.Assistant)
                return;
            History.RemoveLast();
            if (string.IsNullOrEmpty(_LastGeneratedPrompt))
            {
                await StartGeneration(AuthorRole.Assistant, string.Empty);
            }
            else
            {
                Status = SystemStatus.Busy;
                StreamingTextProgress = string.Empty;
                GenerationInput genparams = Sampler.GetCopy();
                if (ForceTemperature >= 0)
                    genparams.Temperature = ForceTemperature;
                genparams.Max_context_length = MaxContextLength;
                genparams.Max_length = MaxReplyLength;
                genparams.Stop_sequence = Instruct.GetStoppingStrings(User, Bot);
                genparams.Prompt = _LastGeneratedPrompt;
                RaiseOnFullPromptReady(genparams.Prompt);
                await Client.GenerateTextStreamAsync(genparams);
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
            if (Status == SystemStatus.Busy)
                return string.Empty;

            var inputText = systemMessage;
            StreamingTextProgress = string.Empty;
            GenerationInput genparams = Sampler.GetCopy();
            _LastGeneratedPrompt = await GenerateFullPrompt(AuthorRole.System, inputText);
            if (ForceTemperature >= 0)
                genparams.Temperature = ForceTemperature;
            genparams.Max_context_length = MaxContextLength;
            genparams.Max_length = MaxReplyLength;
            genparams.Stop_sequence = Instruct.GetStoppingStrings(User, Bot);
            genparams.Prompt = _LastGeneratedPrompt;
            if (!string.IsNullOrEmpty(systemMessage) && logSystemPrompt)
                Bot.History.LogMessage(AuthorRole.System, systemMessage, User, Bot);

            Status = SystemStatus.Busy;
            var result = await Client.GenerateAsync(genparams);
            string finalstr = string.Empty;
            foreach (var item in result.Results)
            {
                finalstr += item.Text;
            }
            Status = SystemStatus.Ready;
            return string.IsNullOrEmpty(finalstr) ? string.Empty : finalstr;
        }

        /// <summary>
        /// Starts the generation process for the bot.
        /// </summary>
        /// <param name="MsgSender">Role of the sender</param>
        /// <param name="userInput">Message from sender</param>
        /// <returns></returns>
        private static async Task StartGeneration(AuthorRole MsgSender, string userInput)
        {
            if (Status == SystemStatus.Busy)
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
            GenerationInput genparams = Sampler.GetCopy();
            _LastGeneratedPrompt = await GenerateFullPrompt(MsgSender, inputText, pluginmessage);
            if (ForceTemperature >= 0)
                genparams.Temperature = ForceTemperature;
            genparams.Max_context_length = MaxContextLength;
            genparams.Max_length = MaxReplyLength;
            genparams.Stop_sequence = Instruct.GetStoppingStrings(User, Bot);
            genparams.Prompt = _LastGeneratedPrompt;
            if (!string.IsNullOrEmpty(userInput))
                Bot.History.LogMessage(MsgSender, userInput, User, Bot);

            RaiseOnFullPromptReady(genparams.Prompt);
            await Client.GenerateTextStreamAsync(genparams);
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
            _LastGeneratedPrompt = string.Empty;
            dataInserts.Clear();
            usedGuidInSession.Clear();
        }

        public static async Task<string> SimpleQuery(SamplerSettings llmparams)
        {
            var oldst = status;
            Status = SystemStatus.Busy;
            var result = await Client.GenerateAsync(llmparams);
            Status = oldst;
            string finalstr = string.Empty;
            foreach (var item in result.Results)
            {
                finalstr += item.Text;
            }
            return string.IsNullOrEmpty(finalstr) ? string.Empty : finalstr;
        }

        public static async Task<WebQueryFullResponse> WebSearch(string query)
        {
            return await Client.WebQueryAsync(new WebQuery() { q = query });
        }

        public static async Task<byte[]> GenerateTTS(string input, string voiceID)
        {
            // female: "Tina", "super chariot of death", "super chariot in death"
            // male: "Lor_ Merciless", "kobo", "chatty"
            var ttsinput = new TextToSpeechInput()
            {
                Input = input,
                Voice = voiceID,
            };
            var audioData = await Client.TextToSpeechAsync(ttsinput);
            return audioData;
        }

    }
}
