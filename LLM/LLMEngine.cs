using LetheAISharp.Agent;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.Memory;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using static LetheAISharp.SearchAPI.WebSearchAPI;
using Message = OpenAI.Chat.Message;

namespace LetheAISharp.LLM
{
    public enum SystemStatus { NotInit, Ready, Busy }
    public enum BackendAPI { KoboldAPI, OpenAI }

    /// <summary>
    /// System to handle communications with language models. 
    /// Handles the connection to the server and the generation of prompts. 
    /// Manages the chat history, personas, inference settings, and instruction formats
    /// </summary>
    public static class LLMEngine
    {
        /// <summary> All settings for the LLM system. </summary>
        public static LLMSettings Settings { get; set; } = new();

        /// <summary>
        /// Client to communicate with the LLM backend (KoboldAPI, OpenAI API). 
        /// </summary>
        public static ILLMServiceClient? Client { get; private set; }

        /// <summary>
        /// Unified prompt builder to create prompts for the currently loaded backend. Used internally for the full chat system.
        /// </summary>
        internal static IPromptBuilder? PromptBuilder { get; private set; }

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

        /// <summary> Set to true if the backend supports GBNF grammar output </summary>
        public static bool SupportsSchema => Client?.SupportsSchema ?? false;

        public static CompletionType CompletionAPIType => Client?.CompletionType ?? CompletionType.Text;

        private static void RaiseOnFullPromptReady(string fullprompt) => OnFullPromptReady?.Invoke(Bot, fullprompt);
        private static void RaiseOnStatusChange(SystemStatus newStatus) => OnStatusChanged?.Invoke(Bot, newStatus);
        private static void RaiseOnInferenceStreamed(string addedString) => OnInferenceStreamed?.Invoke(Bot, addedString);
        private static void RaiseOnInferenceEnded(string fullString) => OnInferenceEnded?.Invoke(Bot, fullString);
        private static void RaiseOnQuickInferenceEnded(string fullprompt) => OnQuickInferenceEnded?.Invoke(Bot, fullprompt);

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
        private static int maxContextLength = 8192;
        private static InstructFormat instruct = new();
        private static ILogger? logger = null;
        private static BasePersona bot = new() { IsUser = false, Name = "Bot", Bio = "You are an helpful AI assistant whose goal is to answer questions and complete tasks.", UniqueName = string.Empty };
        private static BasePersona user = new() { IsUser = true, Name = "User", UniqueName = string.Empty };

        internal static List<string> vlm_pictures = [];
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
            await ModelSemaphore.WaitAsync(ct).ConfigureAwait(false);
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

            PromptBuilder = GetPromptBuilder();
            AgentRuntime.LoadDefaultActions();

            if (LoadedPersonas.Count == 0)
            {
                // Check Settings.DataPath for json files and load them as personas
                var personaFiles = Directory.GetFiles(Settings.DataPath, "*.json");
                foreach (var file in personaFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var persona = JsonConvert.DeserializeObject<BasePersona>(json);
                        if (persona != null)
                        {
                            // set UniqueName as filename without extention
                            persona.UniqueName = Path.GetFileNameWithoutExtension(file);
                            if (!string.IsNullOrEmpty(persona.UniqueName) && !LoadedPersonas.ContainsKey(persona.UniqueName))
                            {
                                LoadedPersonas.Add(persona.UniqueName, persona);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to load persona from file {File}: {Message}", file, ex.Message);
                    }
                }
            }

            Status = SystemStatus.Ready;
        }

        /// <summary>
        /// Preload personas available in the application, so the system can interpret chatlogs from personas that aren't the currently loaded ones.
        /// </summary>
        /// <param name="toload"></param>
        public static void LoadPersonas(List<BasePersona> toload)
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
            return await Client.CheckBackend().ConfigureAwait(false);
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
                MaxContextLength = await Client.GetMaxContextLength().ConfigureAwait(false);
                CurrentModel = await Client.GetModelInfo().ConfigureAwait(false);
                Backend = await Client.GetBackendInfo().ConfigureAwait(false);
                Status = SystemStatus.Ready;
            }
            catch (Exception ex)
            {
                MaxContextLength = 4096;
                CurrentModel = "Error";
                Backend = "Error";
                LLMEngine.Logger?.LogError(ex, "Failed to connect to LLM server: {Message}", ex.Message);
            }
        }

        #endregion

        #region *** Full Communications (Send/reroll messages using History, RAG, and all features) ***

        /// <summary>
        /// Asks the model to generate a message based on the the chat history. 
        /// Response done through the OnInferenceStreamed and OnInferenceEnded events.
        /// </summary>
        /// <returns>It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceEnded event </returns>
        public static async Task AddBotMessage()
        {
            if (Status == SystemStatus.Busy)
                return;
            await StartGeneration(AuthorRole.Assistant, string.Empty).ConfigureAwait(false);
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
            await StartGeneration(AuthorRole.User, string.Empty).ConfigureAwait(false);
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
            await StartGeneration(message.Role, message.Message, message.Guid).ConfigureAwait(false);
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
            await StartGeneration(role, message).ConfigureAwait(false);
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
                await StartGeneration(AuthorRole.Assistant, string.Empty).ConfigureAwait(false);
            }
            else
            {
                using var _ = await AcquireModelSlotAsync(CancellationToken.None).ConfigureAwait(false);
                if (Status == SystemStatus.Busy)
                    return;
                Status = SystemStatus.Busy;
                StreamingTextProgress = Instruct.GetThinkPrefill();
                if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
                {
                    RaiseOnInferenceStreamed(StreamingTextProgress);
                }
                RaiseOnFullPromptReady(PromptBuilder.PromptToText());
                await Client.GenerateTextStreaming(PromptBuilder.PromptToQuery(AuthorRole.Assistant)).ConfigureAwait(false);
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
        /// Send a system message to the bot and wait for a response. Message log is optional. No text streaming. This can be useful when the program needs to ask the LLM for something between user inputs.
        /// </summary>
        /// <param name="systemMessage">Message from sender</param>
        /// <param name="logSystemPrompt">Log the message to the chat history or not</param>
        /// <returns></returns>
        public static async Task<string> QuickInferenceForSystemPrompt(string systemMessage, bool logSystemPrompt, CancellationToken ct = default)
        {
            if (Client == null)
                return string.Empty;
            using var _ = await AcquireModelSlotAsync(ct).ConfigureAwait(false);
            if (Status == SystemStatus.Busy)
                return string.Empty;

            var inputText = systemMessage;
            StreamingTextProgress = Instruct.GetThinkPrefill();
            var genparams = await GenerateFullPrompt(AuthorRole.System, inputText, null, 0).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(systemMessage) && logSystemPrompt)
                Bot.History.LogMessage(AuthorRole.System, systemMessage, User, Bot);
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(genparams).ConfigureAwait(false);
            Status = SystemStatus.Ready;
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        #endregion

        #region *** Simple LLM Queries (provide full prompt, get response) ***

        /// <summary>
        /// Get a prompt builder for the currently loaded backend. Useful when you want to build your own prompts outside of the chat system.
        /// </summary>
        /// <returns>a new brompt builder instance </returns>
        /// <exception cref="InvalidOperationException">The client must be initialized first</exception>
        public static IPromptBuilder GetPromptBuilder()
        {
            if (Client == null)
                throw new InvalidOperationException("Client is not initialized");
            return Client.GetPromptBuilder();
        }

        /// <summary>
        /// Submit a chatlog, get a response from the LLM. No text streaming.
        /// </summary>
        /// <param name="chatlog">Full chatlog to sent to the LLM. GenerationInput for Text Completion API or ChatRequest for Chat Completion API. Use PromptBuilder to generate proper format for the currently loaded API automatically.</param>
        /// <returns>LLM's Response</returns>
        public static async Task<string> SimpleQuery(object chatlog, CancellationToken ctx = default)
        {
            if (Client == null)
                return string.Empty;
            using var _ = await AcquireModelSlotAsync(ctx).ConfigureAwait(false);
            var oldst = status;
            Status = SystemStatus.Busy;
            var result = await Client.GenerateText(chatlog).ConfigureAwait(false);
            Status = oldst;
            RaiseOnQuickInferenceEnded(result);
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        /// <summary>
        /// Submit a chatlog, get a response from the LLM. Streamed response through event system
        /// </summary>
        /// <param name="chatlog">Full chatlog to sent to the LLM. GenerationInput for Text Completion API or ChatRequest for Chat Completion API. Use PromptBuilder to generate proper format for the currently loaded API automatically.</param>
        public static async Task SimpleQueryStreaming(object chatlog, CancellationToken ctx = default)
        {
            if (Client == null)
                return;
            using var _ = await AcquireModelSlotAsync(ctx).ConfigureAwait(false);
            Status = SystemStatus.Busy;
            await Client.GenerateTextStreaming(chatlog).ConfigureAwait(false);
        }

        #endregion

        #region *** Utility Functions ***

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
        /// Called to clear the prompt cache and force a rebuild of the prompt on next generation. Must be called when changing any setting that affects the prompt.
        /// </summary>
        public static void InvalidatePromptCache()
        {
            PromptBuilder?.Clear();
            dataInserts.Clear();
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
            var res = await Client.WebSearch(query).ConfigureAwait(false);
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
            var audioData = await Client.TextToSpeech(input, voiceID).ConfigureAwait(false);
            return audioData;
        }

        /// <summary>
        /// Clears the quick inference event handler.
        /// </summary>
        public static void RemoveQuickInferenceEventHandler()
        {
            OnQuickInferenceEnded = null;
        }

        /// <summary>
        /// Asynchronously retrieves the grammar representation for the specified class type.
        /// </summary>
        /// <remarks>This method requires a valid backend client that supports grammar extraction. If the
        /// backend client is not initialized or does not support grammar extraction, the method logs an error and
        /// returns an empty string.</remarks>
        /// <typeparam name="ClassToConvert">The type of the class for which the grammar representation is to be generated.</typeparam>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains the grammar
        /// representation as a string. Returns an empty string if grammar extraction is not supported or if an error
        /// occurs.</returns>
        public static async Task<string> GetGrammar<ClassToConvert>()
        {
            var res = string.Empty;
            if (Client == null || !SupportsSchema)
            {
                Logger?.LogError("Grammar extraction is not supported by the current backend.");
                return res;
            }
            try
            {
                res = await Client!.SchemaToGrammar(typeof(ClassToConvert)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to get grammar: {Message}", ex.Message);
            }
            return res;
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

        #region *** Group Chat Management (WIP) ***

        /// <summary>
        /// Checks if the current bot is a group persona.
        /// </summary>
        /// <returns>True if the current bot is a GroupPersona, false otherwise.</returns>
        public static bool IsGroupConversation => Bot is GroupPersona;

        /// <summary>
        /// Gets the current GroupPersona if the bot is a group, null otherwise.
        /// </summary>
        /// <returns>The GroupPersona or null if not in group mode.</returns>
        public static GroupPersona? GetGroupPersona() => Bot as GroupPersona;

        /// <summary>
        /// Sets the current active bot in a group conversation.
        /// </summary>
        /// <param name="uniqueName">The unique name of the bot persona to set as current.</param>
        /// <exception cref="InvalidOperationException">Thrown when not in group conversation mode.</exception>
        /// <exception cref="ArgumentException">Thrown when the specified bot is not found in the group.</exception>
        public static void SetCurrentGroupBot(string uniqueName)
        {
            var groupPersona = GetGroupPersona() ?? throw new InvalidOperationException("Cannot set current group bot when not in group conversation mode.");
            groupPersona.SetCurrentBot(uniqueName);
            InvalidatePromptCache();

            // Trigger bot changed event for UI updates
            OnBotChanged?.Invoke(null, bot);
        }

        /// <summary>
        /// Gets the currently active bot in a group conversation.
        /// </summary>
        /// <returns>The current bot persona, or null if not in group mode or no bot is set.</returns>
        public static BasePersona? GetCurrentGroupBot()
        {
            return GetGroupPersona()?.CurrentBot;
        }

        /// <summary>
        /// Gets all bot personas in the group conversation.
        /// </summary>
        /// <returns>List of bot personas if in group mode, empty list otherwise.</returns>
        public static List<BasePersona> GetGroupBots()
        {
            return GetGroupPersona()?.BotPersonas ?? [];
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
                    if (ctxplug.Enabled && ctxplug.ReplaceOutput(Bot.ReplaceMacros(response), History, out var editedresponse))
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

            bot.BeginChat();

            RAGEngine.VectorizeChatBot(Bot);
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
            var searchmessage = string.IsNullOrWhiteSpace(newMessage) ? 
                History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty : 
                newMessage;

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

            if (Settings.SessionMemorySystem && History.Sessions.Count > 1)
            {
                var shistory = History.GetPreviousSummaries(Settings.SessionReservedTokens - GetTokenCount(Bot.ReplaceMacros(SystemPrompt.SessionHistoryTitle)) - 3, SystemPrompt.SubCategorySeparator, ignoreList: dataInserts.GetGuids());
                if (!string.IsNullOrEmpty(shistory))
                {
                    rawprompt.AppendLinuxLine(NewLine + Bot.ReplaceMacros(SystemPrompt.SessionHistoryTitle) + NewLine);
                    rawprompt.AppendLinuxLine(shistory);
                }
            }

            if (Settings.AntiHallucinationMemoryFormat && !Bot.Brain.DisableEurekas)
            { 
                var abilities = Bot.AgentSystem?.AbilitiesToString();
                if (!string.IsNullOrEmpty(abilities))
                {
                    rawprompt.AppendLinuxLine(NewLine + NewLine + "Note: Sometimes the system will insert events in the format <SystemEvent>[TYPE]: {content}.\nThese may include JOURNAL, WEBSEARCH, or GOAL.\nYou may acknowledge that you did one of the actions listed below when a system message says you did. However, you must not invent or describe the contents of those actions unless a <SystemEvent>[TYPE] has been explicitly provided:\n" + abilities);

                    rawprompt.AppendLinuxLine(abilities);
                }
            }

            return Bot.ReplaceMacros(rawprompt.ToString()).CleanupAndTrim();
        }

        /// <summary>
        /// Generates a full prompt for the LLM to use
        /// </summary>
        /// <param name="newMessage">Added message from the user</param>
        /// <returns></returns>
        private static async Task<object> GenerateFullPrompt(AuthorRole MsgSender, string newMessage, string? pluginMessage = null, int imgpadding = 0)
        {
            var availtokens = MaxContextLength - Settings.MaxReplyLength - imgpadding;
            PromptBuilder!.Clear();

            // setup user message (+ optional plugin message) and count tokens used
            if (!string.IsNullOrEmpty(newMessage))
            {
                availtokens -= PromptBuilder.GetTokenCount(MsgSender, newMessage);
            }
            if (!string.IsNullOrEmpty(pluginMessage))
            {
                availtokens -= PromptBuilder.GetTokenCount(AuthorRole.System, pluginMessage);
            }

            // update the RAG, world info, and summary stuff
            await Bot.Brain.UpdateRagAndInserts(dataInserts, newMessage, Settings.RAGMaxEntries, Settings.RAGDistanceCutOff).ConfigureAwait(false);

            // Prepare the full system prompt and count the tokens used
            var rawprompt = GenerateSystemPromptContent(newMessage);
            availtokens -= PromptBuilder.AddMessage(AuthorRole.SysPrompt, rawprompt);

            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(newMessage) && MsgSender == AuthorRole.User)
                availtokens -= PromptBuilder.GetResponseTokenCount(User);
            else
                availtokens -= PromptBuilder.GetResponseTokenCount(Bot);

            // get the full, formated chat history complemented by the data inserts
            var addinserts = string.IsNullOrEmpty(Instruct.ThinkingStart) || !Settings.RAGMoveToThinkBlock;
            History.AddHistoryToPrompt(Settings.SessionHandling, availtokens, addinserts ? dataInserts : null);
            if (!string.IsNullOrEmpty(newMessage) || MsgSender != AuthorRole.User)
            {
                if (!string.IsNullOrEmpty(pluginMessage))
                {
                    PromptBuilder.AddMessage(AuthorRole.System, pluginMessage);
                }
            }

            if (!string.IsNullOrEmpty(newMessage))
            {
                PromptBuilder.AddMessage(MsgSender, newMessage);
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
                var plugres = await ctxplug.ReplaceUserInput(Bot.ReplaceMacros(lastuserinput)).ConfigureAwait(false);
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
            var lastuserinput = string.IsNullOrWhiteSpace(userInput) ?
                History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty :
                userInput;
            
            var pluginmessage = await BuildPluginSystemInsertAsync(lastuserinput).ConfigureAwait(false);

            // build the message if relevant
            SingleMessage? singlemsg = null;
            if (!string.IsNullOrEmpty(userInput))
            {
                singlemsg = new SingleMessage(MsgSender, DateTime.Now, userInput, Bot.UniqueName, User.UniqueName);
                if (setGuid is not null)
                    singlemsg.Guid = (Guid)setGuid;
            }

            // call the brain if there's no plugin interfering
            if (singlemsg is not null && string.IsNullOrEmpty(pluginmessage))
            {
                await Bot.Brain.HandleMessages(singlemsg!).ConfigureAwait(false);
            }

            using var _ = await AcquireModelSlotAsync(CancellationToken.None).ConfigureAwait(false);
            Status = SystemStatus.Busy;

            var inputText = userInput;
            var genparams = await GenerateFullPrompt(MsgSender, inputText, pluginmessage, vlm_pictures.Count > 0 ? vlm_pictures.Count * 1024 : 0).ConfigureAwait(false);

            StreamingTextProgress = Instruct.GetThinkPrefill();
            if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
            {
                RaiseOnInferenceStreamed(StreamingTextProgress);
            }

            if (singlemsg is not null)
            {
                Bot.History.LogMessage(singlemsg!);
            }

            RaiseOnFullPromptReady(PromptBuilder.PromptToText());
            await Client.GenerateTextStreaming(genparams).ConfigureAwait(false);
        }

        #endregion

    }
}
