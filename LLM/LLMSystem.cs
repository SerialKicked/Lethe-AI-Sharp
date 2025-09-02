using AIToolkit.Agent;
using AIToolkit.API;
using AIToolkit.Files;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI.Chat;
using System;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static AIToolkit.SearchAPI.WebSearchAPI;
using static LLama.Common.ChatHistory;
using Message = OpenAI.Chat.Message;

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
        /// <summary> All settings for the LLM system. </summary>
        public static LLMSettings Settings { get; set; } = new();

        /// <summary>
        /// Client to communicate with the LLM backend (KoboldAPI, OpenAI API). 
        /// </summary>
        public static ILLMServiceClient? Client { get; private set; }

        /// <summary>
        /// Unified prompt builder to create prompts for the currently loaded backend.
        /// </summary>
        public static IPromptBuilder? PromptBuilder { get; private set; }

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

        /// <summary> If >= 0 it'll override the selected sampler's temperature setting.</summary>
        public static double ForceTemperature { get; set; } = 0.7;

        /// <summary> 
        /// Override the Instruct Format setting deciding if character names should be inserted into the prompts (null to disable) 
        /// </summary>
        public static bool? NamesInPromptOverride { get; set; } = null;

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
        /// <summary> Called when the bot persona is changed, returns the new bot </summary>
        public static event EventHandler<BasePersona>? OnBotChanged;

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

        /// <summary> 
        /// System prompt to be used when communicating with the LLM.
        /// </summary>
        /// <seealso cref="Files.SystemPrompt"/>"
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
        internal static HashSet<Guid> usedGuidInSession = [];
        internal static PromptInserts dataInserts = [];
        internal static readonly Random RNG = new();

        #region *** Semaphore for model access control (Internal) ***

        private static readonly SemaphoreSlim ModelSemaphore = new(1, 1);

        internal sealed class ModelSlotGuard : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                ModelSemaphore.Release();
            }
        }

        internal static async Task<ModelSlotGuard> AcquireModelSlotAsync(CancellationToken ct)
        {
            await ModelSemaphore.WaitAsync(ct);
            return new ModelSlotGuard();
        }

        internal static async Task<ModelSlotGuard?> TryAcquireModelSlotAsync(TimeSpan timeout, CancellationToken ct)
        {
            var ok = await ModelSemaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
            return ok ? new ModelSlotGuard() : null;
        }

        #endregion

        #region *** Initialization and Loading ***

        public static void Init()
        {
            if (Status != SystemStatus.NotInit)
                return;
            // Create the appropriate client based on the selected backend
            var httpClient = new HttpClient();
            Client = Settings.BackendAPI switch
            {
                BackendAPI.KoboldAPI => new KoboldCppAdapter(httpClient),
                BackendAPI.OpenAI => new OpenAIAdapter(httpClient),
                _ => throw new NotSupportedException($"Backend {Settings.BackendAPI} is not supported")
            };
            // Subscribe to the TokenReceived event
            Client.BaseUrl = Settings.BackendUrl;
            Client.TokenReceived += Client_StreamingMessageReceived;

            PromptBuilder = Client.GetPromptBuilder();

            Status = SystemStatus.Ready;
        }

        /// <summary>
        /// Preload personas available in the application, so the system can interpret chatlogs from personas that aren't the currently loaded ones.
        /// </summary>
        /// <param name="toload"></param>
        public static void LoadPersona(List<BasePersona> toload)
        {
            LoadedPersonas = [];
            foreach (var item in toload)
                LoadedPersonas.Add(item.UniqueName, item);
        }

        /// <summary>
        /// Sets up the backend connection settings and initializes the system.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="backend"></param>
        /// <param name="key"></param>
        public static void Setup(string url, BackendAPI backend, string? key = null)
        {
            Status = SystemStatus.NotInit;
            Settings.BackendUrl = url;
            Settings.BackendAPI = backend;
            Settings.OpenAIKey = key ?? "123";
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
                LLMSystem.Logger?.LogError(ex, "Failed to connect to LLM server: {Message}", ex.Message);
            }
        }

        #endregion

        #region *** Communications with LLM ***

        /// <summary>
        /// Asks the model to generate a message based on the the chat history. 
        /// Response done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <returns>It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceEnded event </returns>
        public static async Task AddBotMessage()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.Assistant, string.Empty);
        }

        /// <summary>
        /// Ask the model to impersonate the user based on the chat history. 
        /// Response done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <returns>It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceEnded event </returns>
        public static async Task ImpersonateUser()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.User, string.Empty);
        }

        /// <summary>
        /// Sends a message to the LLM. Message is logged to chat history. 
        /// Response is done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <param name="message">Fully fledged message</param>
        /// <returns></returns>
        public static async Task SendMessageToBot(SingleMessage message)
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(message.Role, message.Message, message.Guid);
        }

        /// <summary>
        /// Sends a message to the LLM. Message is logged to chat history. 
        /// Response done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <param name="role">Role of the sender (User, Bot or System)</param>
        /// <param name="message">Message to send </param>
        /// <returns>It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceEnded event </returns>
        public static async Task SendMessageToBot(AuthorRole role, string message)
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(role, message);
        }

        /// <summary>
        /// Rerolls the last response from the bot. It will automatically remove the last message from the chat history (if it's from the bot).
        /// Response done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <returns>It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceEnded event </returns>
        public static async Task RerollLastMessage()
        {
            if (History.CurrentSession.Messages.Count == 0 || History.LastMessage()?.Role != AuthorRole.Assistant || Client == null || PromptBuilder == null)
                return;
            History.RemoveLast();
            if (PromptBuilder.Count == 0)
            {
                await StartGeneration(AuthorRole.Assistant, string.Empty);
            }
            else
            {
                using var _ = await AcquireModelSlotAsync(CancellationToken.None);
                if (Status == SystemStatus.Busy)
                    return;
                Status = SystemStatus.Busy;
                StreamingTextProgress = Instruct.GetThinkPrefill();
                if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
                {
                    RaiseOnInferenceStreamed(StreamingTextProgress);
                }
                RaiseOnFullPromptReady(PromptBuilder.PromptToText());
                await Client.GenerateTextStreaming(PromptBuilder.PromptToQuery(AuthorRole.Assistant));
            }
        }

        /// <summary>
        /// Cancel the current generation
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Submit a chatlog, get a response from the LLM. No text streaming.
        /// </summary>
        /// <param name="chatlog">Full chatlog to sent to the LLM. GenerationInput for Text Completion API or ChatRequest for Chat Completion API. Use PromptBuilder to generate proper format for the currently loaded API automatically.</param>
        /// <returns>LLM's Response</returns>
        public static async Task<string> SimpleQuery(object chatlog)
        {
            if (Client == null)
                return string.Empty;
            using var _ = await AcquireModelSlotAsync(CancellationToken.None);
            var oldst = status;
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(chatlog);
            Status = oldst;
            RaiseOnQuickInferenceEnded(result);
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        /// <summary>
        /// Send a system message to the bot and wait for a response. Message log is optional. No text streaming. This can be useful when the program needs to ask the LLM for something between user inputs.
        /// </summary>
        /// <param name="systemMessage">Message from sender</param>
        /// <param name="logSystemPrompt">Log the message to the chat history or not</param>
        /// <returns></returns>
        public static async Task<string> QuickInferenceForSystemPrompt(string systemMessage, bool logSystemPrompt, CancellationToken ct = default)
        {
            if (Client == null)
                return string.Empty;
            using var _ = await AcquireModelSlotAsync(ct);
            if (Status == SystemStatus.Busy)
                return string.Empty;

            var inputText = systemMessage;
            StreamingTextProgress = Instruct.GetThinkPrefill();
            var genparams = await GenerateFullPrompt(AuthorRole.System, inputText, null, 0);
            if (!string.IsNullOrEmpty(systemMessage) && logSystemPrompt)
                Bot.History.LogMessage(AuthorRole.System, systemMessage, User, Bot);
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(genparams);
            Status = SystemStatus.Ready;
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        #endregion

        #region *** Macros and Secondary Functions ***

        /// <summary>
        /// Replaces the macros in a string with the actual values. Assumes the current user and bot.
        /// </summary>
        /// <param name="inputText"></param>
        /// <returns></returns>
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
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(Settings.ScenarioOverride) ? character.GetScenario(user.Name) : Settings.ScenarioOverride);
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
               .Replace("{{scenario}}", string.IsNullOrWhiteSpace(Settings.ScenarioOverride) ? character.GetScenario(userName) : Settings.ScenarioOverride);
            return res.ToString().CleanupAndTrim();
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
            return ReplaceMacros(msgtxt + NewLine);
        }

        /// <summary>
        /// Called to clear the prompt cache and force a rebuild of the prompt on next generation. Must be called when changing any setting that affects the prompt.
        /// </summary>
        public static void InvalidatePromptCache()
        {
            PromptBuilder?.ResetPrompt();
            dataInserts.Clear();
            usedGuidInSession.Clear();
        }

        /// <summary>
        /// Performs a web search using the backend's web search capabilities.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public static async Task<List<EnrichedSearchResult>> WebSearch(string query)
        {
            if (Client == null || !SupportsWebSearch)
                return [];
            var res = await Client.WebSearch(query);
            var webres = JsonConvert.DeserializeObject<List<EnrichedSearchResult>>(res);
            if (webres is null)
            {
                logger?.LogError("Failed to parse web search response");
                return [];
            }
            return webres;
        }

        /// <summary>
        /// Generates speech audio from text using the backend's TTS capabilities (if available).
        /// </summary>
        /// <param name="input">text to convert into audio</param>
        /// <param name="voiceID">Voice ID for the TTS model</param>
        /// <returns>byte array of audio data (can be loaded into memory stream and played with SoundPlayer)</returns>
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

        /// <summary>
        /// Clears the quick inference event handler.
        /// </summary>
        public static void RemoveQuickInferenceEventHandler()
        {
            OnQuickInferenceEnded = null;
        }

        #endregion

        #region *** Visual Language Model Management (for supported API) ***

        /// <summary>
        /// Clears the list of images to be sent to the backend.
        /// </summary>
        public static void VLM_ClearImages()
        {
            vlm_pictures = [];
        }

        /// <summary>
        /// Provide a base64 encoded image to be sent to the backend with the next prompt.
        /// </summary>
        /// <param name="base64"></param>
        public static void VLM_AddB64Image(string base64)
        {
            vlm_pictures.Add(base64);
        }

        /// <summary>
        /// Provide an image to be sent to the backend with the next prompt. The image will be resized to fit within the specified size (default 1024px).
        /// </summary>
        /// <param name="image">image</param>
        /// <param name="size">dimension</param>
        public static void VLM_AddImage(Image image, int size = 1024)
        {
            var res = ImageUtils.ImageToBase64(image, size);
            if (!string.IsNullOrEmpty(res))
                vlm_pictures.Add(res);
        }

        #endregion

        #region *** Private and Internal Methods ***

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

            OnBotChanged?.Invoke(null, bot);
            EventBus.Publish(new BotChangedEvent(bot.UniqueName));

            bot.BeginChat();

            RAGSystem.VectorizeChatBot(Bot);
            // if first time interaction, display welcome message from bot
            if (History.Sessions.Count == 0)
            {
                // Access CurrentSession to trigger automatic session creation via factory method
                _ = History.CurrentSession;
            }
            if (History.CurrentSession.Messages.Count == 0 && History.Sessions.Count == 1)
            {
                var message = new SingleMessage(AuthorRole.Assistant, DateTime.Now, bot.GetWelcomeLine(User.Name), bot.UniqueName, User.UniqueName);
                History.LogMessage(message);
            }
        }

        /// <summary>
        /// Generates the system prompt content.
        /// </summary>
        /// <param name="MsgSender"></param>
        /// <param name="newMessage"></param>
        /// <returns></returns>
        private static string GenerateSystemPromptContent(string newMessage)
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
            var syspromptentries = Settings.MoveAllInsertsToSysPrompt ? dataInserts : dataInserts.GetEntriesByPosition(-1);
            if (syspromptentries.Count > 0)
            {
                rawprompt.AppendLinuxLine().AppendLinuxLine(SystemPrompt.WorldInfoTitle).AppendLinuxLine();
                foreach (var item in syspromptentries)
                    rawprompt.AppendLinuxLine(item.Content);
            }

            if (Settings.SessionMemorySystem && Settings.SessionReservedTokens > 0 && History.Sessions.Count > 1)
            {
                usedGuidInSession = dataInserts.GetGuids();
                var shistory = History.GetPreviousSummaries(Settings.SessionReservedTokens - GetTokenCount(ReplaceMacros(SystemPrompt.SessionHistoryTitle)) - 3, SystemPrompt.SubCategorySeparator);
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
        private static async Task UpdateRagAndInserts(string newMessage)
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
            if (Settings.AllowWorldInfo && Bot.MyWorlds.Count > 0)
            {
                var _currentWorldEntries = new List<WorldEntry>();
                foreach (var world in Bot.MyWorlds)
                {
                    _currentWorldEntries.AddRange(world.FindEntries(History, searchmessage));
                }
                foreach (var entry in _currentWorldEntries)
                {
                    dataInserts.AddInsert(new PromptInsert(
                        entry.Guid, entry.Content, entry.Position == WEPosition.SystemPrompt ? -1 : entry.PositionIndex, entry.Duration)
                        );
                }
            }
            // Check all sessions for sticky entries
            foreach (var session in History.Sessions)
            {
                if (session.Sticky && session != History.CurrentSession)
                {
                    var rawmem = session.GetRawMemory(true, Bot.DatesInSessionSummaries);
                    dataInserts.AddInsert(new PromptInsert(session.Guid, rawmem, Settings.RAGIndex, 1));
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
            var availtokens = MaxContextLength - Settings.MaxReplyLength - imgpadding;
            PromptBuilder!.ResetPrompt();

            // setup user message (+ optional plugin message) and count tokens used
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
            await UpdateRagAndInserts(newMessage);
            // Prepare the full system prompt and count the tokens used
            var rawprompt = GenerateSystemPromptContent(newMessage);
            availtokens -= PromptBuilder.AddMessage(AuthorRole.SysPrompt, rawprompt);

            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
                availtokens -= GetTokenCount(Instruct.GetResponseStart(User));
            else
                availtokens -= GetTokenCount(Instruct.GetResponseStart(Bot));

            // get the full, formated chat history complemented by the data inserts
            var addinserts = string.IsNullOrEmpty(Instruct.ThinkingStart) || !Settings.RAGMoveToThinkBlock;
            History.AddHistoryToPrompt(Settings.SessionHandling, availtokens, addinserts ? dataInserts : null);
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
            if (final > (MaxContextLength - Settings.MaxReplyLength))
            {
                var diff = final - (MaxContextLength - Settings.MaxReplyLength);
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

        /// <summary>
        /// Plugin Handler
        /// </summary>
        /// <param name="lastuserinput"></param>
        /// <returns></returns>
        private static async Task<string> BuildPluginSystemInsertAsync(string? lastuserinput)
        {
            if (string.IsNullOrWhiteSpace(lastuserinput))
                return string.Empty;

            var insertmessages = new List<string>();
            foreach (var ctxplug in ContextPlugins)
            {
                if (!ctxplug.Enabled)
                    continue;
                // Plugins may call LLMSystem.SimpleQuery here. We are intentionally NOT
                // holding the model semaphore yet to avoid re-entrancy deadlocks.
                var plugres = await ctxplug.ReplaceUserInput(ReplaceMacros(lastuserinput));
                if (plugres.IsHandled && !string.IsNullOrEmpty(plugres.Response))
                {
                    if (plugres.Replace)
                        lastuserinput = plugres.Response; // preserve replacement for downstream if needed
                    else
                        insertmessages.Add(plugres.Response);
                }
            }
            return string.Join(NewLine, insertmessages).Trim();
        }

        /// <summary>
        /// Starts the generation process for the bot.
        /// </summary>
        /// <param name="MsgSender">Role of the sender</param>
        /// <param name="userInput">Message from sender</param>
        /// <returns></returns>
        private static async Task StartGeneration(AuthorRole MsgSender, string userInput, Guid? setGuid = null)
        {
            if (Client == null || PromptBuilder == null)
                return;

            // Plugin pre-pass OUTSIDE the model slot to avoid deadlocks
            var lastuserinput = string.IsNullOrEmpty(userInput) ? History.GetLastUserMessageContent() : userInput;
            var pluginmessage = await BuildPluginSystemInsertAsync(lastuserinput);
            if (string.IsNullOrWhiteSpace(pluginmessage) && !string.IsNullOrEmpty(userInput) && MsgSender == AuthorRole.User)
            {
                Bot.Brain.OnUserPost(userInput);
            }

            using var _ = await AcquireModelSlotAsync(CancellationToken.None);
            Status = SystemStatus.Busy;

            var inputText = userInput;
            var genparams = await GenerateFullPrompt(MsgSender, inputText, pluginmessage, vlm_pictures.Count > 0 ? vlm_pictures.Count * 1024 : 0);

            StreamingTextProgress = Instruct.GetThinkPrefill();
            if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
            {
                RaiseOnInferenceStreamed(StreamingTextProgress);
            }

            if (!string.IsNullOrEmpty(userInput))
            {
                var msg = Bot.History.LogMessage(MsgSender, userInput, User, Bot);
                if (setGuid is not null)
                    msg.Guid = (Guid)setGuid;
            }

            RaiseOnFullPromptReady(PromptBuilder.PromptToText());
            await Client.GenerateTextStreaming(genparams);
        }

        #endregion

    }
}
