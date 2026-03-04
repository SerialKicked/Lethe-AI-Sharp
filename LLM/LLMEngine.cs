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


namespace LetheAISharp.LLM
{
    public enum SystemStatus { NotInit, Ready, Busy }
    public enum BackendAPI { KoboldAPI, OpenAI, LlamaSharp }

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
            get => Settings.MaxTotalTokens;
            set 
            {
                if (value != Settings.MaxTotalTokens) 
                    InvalidatePromptCache();
                Settings.MaxTotalTokens = value;
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

        /// <summary>
        /// This is a list of words that are banned during search queries. It's on the application to load and maintain this list.
        /// </summary>
        public static BanList BannedSearchWords { get; set; } = new();

        internal static Dictionary<string, BasePersona> LoadedPersonas = [];

        /// <summary> Called when the non streaming inference has completed, returns the raw and complete response from the model </summary>
        public static event EventHandler<string>? OnQuickInferenceEnded;
        /// <summary> Called when this library has generated the full prompt, returns full prompt </summary>
        public static event EventHandler<string>? OnFullPromptReady;
        /// <summary> Called during inference each time the LLM outputs a new token, returns the generated token </summary>
        [Obsolete("Use OnInferenceSegment for channel-aware streaming. This event only receives Text channel content.")]
        public static event EventHandler<string>? OnInferenceStreamed;
        /// <summary> Called once the inference has ended, returns the full string </summary>
        [Obsolete("Use OnInferenceCompleted for structured results including thinking content and tool calls.")]
        public static event EventHandler<string>? OnInferenceEnded;
        /// <summary> Called when the system changes states (no init, busy, ready) </summary>
        public static event EventHandler<SystemStatus>? OnStatusChanged;
        /// <summary> Called when the bot persona is changed, returns the new bot (sender is always null) </summary>
        public static event EventHandler<BasePersona>? OnBotChanged;

        /// <summary> Called during inference with typed, channel-tagged segments. Provides richer information than OnInferenceStreamed. </summary>
        public static event EventHandler<InferenceSegment>? OnInferenceSegment;
        /// <summary> Called when a complete inference cycle finishes. Provides structured results including thinking content and tool call records. </summary>
        public static event EventHandler<InferenceResult>? OnInferenceCompleted;

        /// <summary> Set to true if the backend supports text-to-speech </summary>
        public static bool SupportsTTS => Client?.SupportsTTS ?? false;

        /// <summary> Set to true if the backend supports web search </summary>
        public static bool SupportsWebSearch => Client?.SupportsWebSearch ?? false;

        /// <summary> Set to true if the backend supports vision </summary>
        public static bool SupportsVision => Client?.SupportsVision ?? false;

        /// <summary> Set to true if the backend supports GBNF grammar output </summary>
        public static bool SupportsSchema => Client?.SupportsSchema ?? false;

        /// <summary> Set to true if the backend supports tool calls (like function calling in OpenAI) </summary>
        public static bool SupportsToolCalls => Client?.SupportsToolCalls ?? false;

        public static CompletionType CompletionAPIType => Client?.CompletionType ?? CompletionType.Text;

        private static void RaiseOnFullPromptReady(string fullprompt) => OnFullPromptReady?.Invoke(Bot, fullprompt);
        private static void RaiseOnStatusChange(SystemStatus newStatus) => OnStatusChanged?.Invoke(Bot, newStatus);
#pragma warning disable CS0618 // backward-compat raise helpers for obsolete events
        private static void RaiseOnInferenceStreamed(string addedString) => OnInferenceStreamed?.Invoke(Bot, addedString);
        private static void RaiseOnInferenceEnded(string fullString) => OnInferenceEnded?.Invoke(Bot, fullString);
#pragma warning restore CS0618
        private static void RaiseOnQuickInferenceEnded(string fullprompt) => OnQuickInferenceEnded?.Invoke(Bot, fullprompt);
        private static void RaiseInferenceSegment(InferenceSegment segment) => OnInferenceSegment?.Invoke(Bot, segment);
        private static void RaiseInferenceCompleted(InferenceResult result) => OnInferenceCompleted?.Invoke(Bot, result);

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
        private static InferenceChannel _currentChannel = InferenceChannel.Text;
        private static readonly StringBuilder _thinkingBuffer = new();
        private static readonly StringBuilder _textBuffer = new();
        private static InstructFormat instruct = new() 
        { 
            AddNamesToPrompt = false,
            SysPromptStart = "### Instruction:" + NewLine,
            SysPromptEnd = "",
            SystemStart = "### Instruction:" + NewLine,
            SystemEnd = "",
            UserStart = "### Input:" + NewLine,
            UserEnd = "",
            BotStart = "### Response:" + NewLine,
            BotEnd = "",
            NewLinesBetweenMessages = true,
            StopStrings = [ "### Instruction:", "### Input:", "### Response:" ],
        };

        private static bool _isSimpleQuery = false;
        private static ILogger? logger = null;
        private static BasePersona bot = new() { IsUser = false, Name = "Bot", Bio = "You are a helpful AI assistant whose goal is to answer questions and complete tasks.", UniqueName = string.Empty };
        private static BasePersona user = new() { IsUser = true, Name = "User", UniqueName = string.Empty };
        public static PromptInserts dataInserts = [];
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

        private static void LoadBackendClient()
        {
            if (Status != SystemStatus.NotInit)
                return;
            // Create the appropriate client based on the selected backend
            var httpClient = new HttpClient();
            Client = Settings.BackendAPI switch
            {
                BackendAPI.KoboldAPI => new KoboldCppAdapter(httpClient),
                BackendAPI.OpenAI => new OpenAIAdapter(httpClient),
                BackendAPI.LlamaSharp => new LlamaSharpAdapter(Settings.BackendUrl),
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
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Backend URL cannot be null or empty", nameof(url));
            Status = SystemStatus.NotInit;
            Settings.BackendUrl = url;
            Settings.BackendAPI = backend;
            Settings.OpenAIKey = key ?? "123";
            LoadBackendClient();
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
            LoadBackendClient();
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
            await StartGeneration(new SingleMessage(AuthorRole.Assistant, string.Empty)).ConfigureAwait(false);
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
            await StartGeneration(new SingleMessage(AuthorRole.User, string.Empty)).ConfigureAwait(false);
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
            await StartGeneration(message).ConfigureAwait(false);
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
                await StartGeneration(new SingleMessage(AuthorRole.Assistant, string.Empty)).ConfigureAwait(false);
            }
            else
            {
                using var _ = await AcquireModelSlotAsync(CancellationToken.None).ConfigureAwait(false);
                if (Status == SystemStatus.Busy)
                    return;
                Status = SystemStatus.Busy;
                ResetStreamingState();
                StreamingTextProgress = Instruct.GetThinkPrefill();
                if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
                {
                    RaiseOnInferenceStreamed(StreamingTextProgress);
                }
                RaiseOnFullPromptReady(PromptBuilder.PromptToText());
                await Client.GenerateTextStreaming(PromptBuilder.PromptToQuery(AuthorRole.Assistant, forceAltRoles: IsGroupConversation && Settings.GroupInstructFormatAdapter)).ConfigureAwait(false);
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
                var success = Client.AbortGenerationSync();
                if (success)
                    Status = SystemStatus.Ready;
                return success;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to cancel generation");
                return false;
            }
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
                throw new InvalidOperationException("LLMEngine not initialized. Call Setup() and Connect() first.");

            using var _ = await AcquireModelSlotAsync(ctx).ConfigureAwait(false);
            var oldst = status;
            Status = SystemStatus.Busy;
            _isSimpleQuery = true;
            string result;
            try
            {
                result = await Client.GenerateText(chatlog).ConfigureAwait(false);
            }
            finally
            {
                _isSimpleQuery = false;
            }
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
                throw new InvalidOperationException("LLMEngine not initialized. Call Setup() and Connect() first.");

            using var _ = await AcquireModelSlotAsync(ctx).ConfigureAwait(false);
            Status = SystemStatus.Busy;
            ResetStreamingState();
            _isSimpleQuery = true;
            try
            {
                await Client.GenerateTextStreaming(chatlog).ConfigureAwait(false);
            }
            finally
            {
                _isSimpleQuery = false;
            }
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
            if (string.IsNullOrEmpty(text))
                return 0;
            else if (Client == null || Status != SystemStatus.Ready || text.Length > MaxContextLength * 8)
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

        public static int GetTokenCount(SingleMessage mess)
        {
            var realmessage = mess.ToTextCompletion();
            if (string.IsNullOrEmpty(realmessage))
                return 0;
            else if (Client == null || Status != SystemStatus.Ready || realmessage.Length > MaxContextLength * 8)
                return TokenTools.CountTokens(realmessage);
            try
            {
                return Client.CountTokensSync(realmessage);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to count tokens. Falling back to failsafe");
                return TokenTools.CountTokens(realmessage);
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
            if (Client == null)
                throw new InvalidOperationException("LLMEngine not initialized. Call Setup() and Connect() first.");
            var res = string.Empty;
            if (!SupportsSchema)
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

        public static bool SetTools(List<OpenAI.Tool> tools)
        {
            if (Client == null || !SupportsToolCalls || PromptBuilder == null)
            {
                logger?.LogError("Tool calls are not supported by the current backend.");
                return false;
            }
            if (tools.Count > 0)
                PromptBuilder.SetTools(tools);
            else
                OpenAI.Tool.ClearRegisteredTools();
            return true;
        }

        #endregion

        #region *** Visual Language Model Management ***

        /// <summary>
        /// Clears the list of images to be sent to the backend.
        /// </summary>
        public static void VLM_ClearImages()
        {
            PromptBuilder?.VLM_ClearImages();
        }

        /// <summary>
        /// Provide an image to be sent to the backend with the next prompt. The image will be resized to fit within the specified size (default 1024px).
        /// </summary>
        /// <param name="image">image</param>
        /// <param name="size">dimension</param>
        public static void VLM_AddImage(string imagepath, int size = 1024)
        {
            if (File.Exists(imagepath))
                PromptBuilder?.VLM_AddImage(imagepath, size);
        }

        #endregion

        #region *** Group Chat Management ***

        /// <summary>
        /// Checks if the current bot is a group persona.
        /// </summary>
        /// <returns>True if the current bot is a GroupPersona, false otherwise.</returns>
        public static bool IsGroupConversation => Bot is GroupPersonaBase;

        /// <summary>
        /// Gets the current GroupPersona if the bot is a group, null otherwise.
        /// </summary>
        /// <returns>The GroupPersona or null if not in group mode.</returns>
        public static GroupPersonaBase? GetGroupPersona() => Bot as GroupPersonaBase;

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

                // Emit tool call and tool result segments for each recorded tool invocation (BEFORE final response)
                if (e.ToolCallRecords != null)
                {
                    foreach (var record in e.ToolCallRecords)
                    {
                        RaiseInferenceSegment(new InferenceSegment
                        {
                            Channel = InferenceChannel.ToolCall,
                            ToolCall = new ToolCallInfo
                            {
                                CallId = record.CallId,
                                FunctionName = record.FunctionName,
                                ArgumentsJson = record.ArgumentsJson
                            },
                            IsComplete = true
                        });
                        RaiseInferenceSegment(new InferenceSegment
                        {
                            Channel = InferenceChannel.ToolResult,
                            ToolResult = new ToolResultInfo
                            {
                                CallId = record.CallId,
                                FunctionName = record.FunctionName,
                                Success = record.Success,
                                ResultJson = record.ResultJson,
                                Error = record.Error
                            },
                            IsComplete = true
                        });
                    }
                }

                // Log tool calls to history only in full-chat mode (not simple queries)
                if (!_isSimpleQuery && e.ToolCallRecords != null && e.ToolCallRecords.Count > 0)
                {
                    // ONE assistant message with ALL tool calls from this inference
                    var assistantToolMsg = new SingleMessage(AuthorRole.Assistant, string.Empty, toolCalls: e.ToolCallRecords.ToList());
                    Bot.History.LogMessage(assistantToolMsg);

                    // One ToolResult message per tool call
                    foreach (var record in e.ToolCallRecords)
                    {
                        var resultMsg = new SingleMessage(AuthorRole.Tool,
                            record.Success ? record.ResultJson : (record.Error ?? "Error"),
                            toolCalls: new List<ToolCallRecord> { record });
                        Bot.History.LogMessage(resultMsg);
                    }
                }

                Status = SystemStatus.Ready;
                RaiseOnInferenceEnded(response);

                // Build structured result for new event
                var thinkingContent = _thinkingBuffer.Length > 0 ? _thinkingBuffer.ToString().Trim() : null;
                var textResponse = Instruct.IsThinkFormat ? response.RemoveThinkingBlocks() : response;
                var inferenceResult = new InferenceResult
                {
                    Response = textResponse,
                    ThinkingContent = thinkingContent,
                    ToolCalls = e.ToolCallRecords ?? [],
                    FinishReason = e.FinishReason
                };
                RaiseInferenceCompleted(inferenceResult);
            }
            else
            {
                StreamingTextProgress += e.Token;

                // Detect channel transitions based on thinking delimiters
                if (Instruct.IsThinkFormat)
                {
                    _currentChannel = Instruct.IsThinkingPrompt(StreamingTextProgress)
                        ? InferenceChannel.Thinking
                        : InferenceChannel.Text;
                }

                // Route token to the appropriate content buffer
                if (_currentChannel == InferenceChannel.Thinking)
                    _thinkingBuffer.Append(e.Token);
                else
                    _textBuffer.Append(e.Token);

                RaiseInferenceSegment(new InferenceSegment { Channel = _currentChannel, Text = e.Token, IsComplete = false });
                RaiseOnInferenceStreamed(e.Token);
            }
        }

        /// <summary>
        /// Resets all per-generation streaming state. Must be called before each new generation.
        /// </summary>
        private static void ResetStreamingState()
        {
            _currentChannel = InferenceChannel.Text;
            _thinkingBuffer.Clear();
            _textBuffer.Clear();
            StreamingTextProgress = string.Empty;
        }

        /// <summary>
        /// Change the current bot persona.
        /// </summary>
        /// <param name="newbot"></param>
        private static void ChangeBot(BasePersona newbot)
        {
            InvalidatePromptCache();
            bot.EndChat(backup: true);
            bot.SaveToFile(Settings.DataPath);
            bot = newbot;

            OnBotChanged?.Invoke(null, bot);

            bot.BeginChat();
            Bot.Brain?.ReloadMemories();
            // if first time interaction, display welcome message from bot
            if (History.Sessions.Count == 0)
            {
                // Access CurrentSession to trigger automatic session creation via factory method
                _ = History.CurrentSession;
            }
            if (History.CurrentSession.Messages.Count == 0 && History.Sessions.Count == 1)
            {
                var message = new SingleMessage(AuthorRole.Assistant, DateTime.Now, bot.GetWelcomeLine(User.Name), bot.GetIdentifier(), User.GetIdentifier());
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
                    rawprompt.AppendLinuxLine(item.ToContent()).AppendLinuxLine();
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

            // Core facts section: top-ranked extracted facts about the user, inserted before session history
            // so the bot has durable user knowledge that doesn't depend on RAG similarity alone.
            if (Settings.FactRetrievalEnabled && Settings.CoreFactsTokenBudget > 0 && !string.IsNullOrEmpty(SystemPrompt.CoreFactsTitle))
            {
                var coreFacts = Bot.Brain.GetCoreFacts(Settings.CoreFactsTokenBudget);
                if (!string.IsNullOrEmpty(coreFacts))
                {
                    rawprompt.AppendLinuxLine(NewLine + Bot.ReplaceMacros(SystemPrompt.CoreFactsTitle) + NewLine);
                    rawprompt.AppendLinuxLine(coreFacts);
                }
            }
            
            if (Settings.AntiHallucinationMemoryFormat && !Bot.Brain.DisableEurekas)
            { 
                var abilities = Bot.AgentSystem?.AbilitiesToString();
                if (!string.IsNullOrEmpty(abilities))
                {
                    rawprompt.AppendLinuxLine(NewLine + "Note: Sometimes the system will insert events in the format <SystemEvent>[TYPE]: {content}.\nThese may include JOURNAL, WEBSEARCH, or GOAL.\nYou may acknowledge that you did one of the actions listed below when a system message says you did. However, you must not invent or describe the contents of those actions unless a <SystemEvent>[TYPE] has been explicitly provided:" + abilities);
                }
            }

            return Bot.ReplaceMacros(rawprompt.ToString()).CleanupAndTrim();
        }

        /// <summary>
        /// Generates a full prompt for the LLM to use
        /// </summary>
        /// <param name="newMessage">Added message from the user</param>
        /// <returns></returns>
        private static async Task<object> GenerateFullPrompt(SingleMessage message, string? pluginMessage = null)
        {
            var availtokens = MaxContextLength - Settings.MaxReplyLength;
            PromptBuilder!.Clear();

            // setup user message (+ optional plugin message) and count tokens used
            if (!string.IsNullOrEmpty(message.Message))
            {
                availtokens -= PromptBuilder.GetTokenCount(message.Role, message.Message);
            }
            if (!string.IsNullOrEmpty(pluginMessage))
            {
                availtokens -= PromptBuilder.GetTokenCount(AuthorRole.System, pluginMessage);
            }

            var searchstring = string.IsNullOrEmpty(message.Message) ? History.GetLastFromInSession(AuthorRole.User)?.Message : message.Message;

            // update the RAG, world info, and summary stuff
            var actbot = Bot is GroupPersonaBase gp ? gp.GetCurrentPersona() : Bot;
            actbot ??= Bot;
            await actbot.Brain.GetRAGandInserts(dataInserts, searchstring ?? string.Empty, -1, Settings.RAGDistanceCutOff).ConfigureAwait(false);

            // Prepare the full system prompt and count the tokens used
            var rawprompt = GenerateSystemPromptContent(message.Message);
            availtokens -= PromptBuilder.AddMessage(new SingleMessage(AuthorRole.SysPrompt, rawprompt));

            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(message.Message) && message.Role == AuthorRole.User)
                availtokens -= PromptBuilder.GetResponseTokenCount(User);
            else
                availtokens -= PromptBuilder.GetResponseTokenCount(Bot);

            // get the full, formated chat history complemented by the data inserts
            var addinserts = string.IsNullOrEmpty(Instruct.ThinkingStart) || !Settings.RAGMoveToThinkBlock;
            History.AddHistoryToPrompt(Settings.SessionHandling, availtokens, addinserts ? dataInserts : null);
            if (!string.IsNullOrEmpty(message.Message) || message.Role != AuthorRole.User)
            {
                if (!string.IsNullOrEmpty(pluginMessage))
                {
                    PromptBuilder.AddMessage(new SingleMessage(AuthorRole.System, pluginMessage));
                }
            }

            if (!string.IsNullOrEmpty(message.Message))
            {
                PromptBuilder.AddMessage(message);
            }

            var final = PromptBuilder.GetTokenUsage();
            if (final > (MaxContextLength - Settings.MaxReplyLength))
            {
                var diff = final - (MaxContextLength - Settings.MaxReplyLength);
                logger?.LogWarning("The prompt is {Diff} tokens over the limit.", diff);
            }
            if (string.IsNullOrEmpty(message.Message) && message.Role == AuthorRole.User)
                return PromptBuilder.PromptToQuery(AuthorRole.User, forceAltRoles: IsGroupConversation && Settings.GroupInstructFormatAdapter);
            else
                return PromptBuilder.PromptToQuery(AuthorRole.Assistant, forceAltRoles: IsGroupConversation && Settings.GroupInstructFormatAdapter);
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
        private static async Task StartGeneration(SingleMessage message)
        {
            _isSimpleQuery = false;
            if (Client == null || PromptBuilder == null)
                return;

            // Plugin pre-pass OUTSIDE the model slot to avoid deadlocks
            var lastuserinput = string.IsNullOrWhiteSpace(message.Message) ?
                History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty :
                message.Message;

            var pluginmessage = await BuildPluginSystemInsertAsync(lastuserinput).ConfigureAwait(false);



            // call the brain if there's no plugin interfering
            if (!string.IsNullOrEmpty(message.Message) && string.IsNullOrEmpty(pluginmessage))
            {
                var actbot = Bot is GroupPersonaBase gp ? gp.GetCurrentPersona() : Bot;
                actbot ??= Bot;
                await actbot.Brain.HandleMessages(message).ConfigureAwait(false);
            }

            using var _ = await AcquireModelSlotAsync(CancellationToken.None).ConfigureAwait(false);
            Status = SystemStatus.Busy;

            var genparams = await GenerateFullPrompt(message, pluginmessage).ConfigureAwait(false);

            ResetStreamingState();
            StreamingTextProgress = Instruct.GetThinkPrefill();
            if (Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart))
            {
                RaiseOnInferenceStreamed(StreamingTextProgress);
            }

            if (!string.IsNullOrEmpty(message.Message))
            {
                Bot.History.LogMessage(message);
            }

            RaiseOnFullPromptReady(PromptBuilder.PromptToText());
            await Client.GenerateTextStreaming(genparams).ConfigureAwait(false);
        }

        #endregion

    }
}
