using LetheAISharp.Agent;
using LetheAISharp.Agent.Tools;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.Memory;
using LetheAISharp.Moods;
using LetheAISharp.Skills;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Reflection;
using System.Text;
using static LetheAISharp.SearchAPI.WebSearchAPI;


namespace LetheAISharp.LLM
{
    public enum SystemStatus { NotInit, Ready, Busy }
    public enum BackendAPI { KoboldAPI, LlamaCpp, OpenAI, LlamaSharp }

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
        public static double ForceTemperature { get; set; } = -1;

        /// <summary> 
        /// Override the Instruct Format setting deciding if character names should be inserted into the prompts (null to disable) 
        /// </summary>
        public static bool? NamesInPromptOverride { get; set; } = null;

        /// <summary>
        /// Override for the backend's <c>add_generation_prompt</c> flag (null to leave it at the backend
        /// default). Set by the prompt builders while a structured-output schema is active, so that
        /// chat-template token counting renders the same prompt the generation request will.
        /// </summary>
        public static bool? AddGenerationPromptOverride { get; set; } = null;

        /// <summary>
        /// Only prefill the character names when generating the prompt. This is useful for models interacting as a chatroom bot, 
        /// where the model needs to know its character name while the users' names are handled at the app's level.
        /// </summary>
        public static bool? NamesInPromptBotOnlyOverride { get; set; } = null;

        /// <summary>
        /// This is a list of words that are banned during search queries. It's on the application to load and maintain this list.
        /// </summary>
        public static BanList BannedSearchWords { get; set; } = new();

        internal static Dictionary<string, BasePersona> LoadedPersonas = [];

        /// <summary> Called when the non streaming inference has completed, returns the raw and complete response from the model </summary>
        public static event EventHandler<string>? OnQuickInferenceEnded;
        /// <summary> Called when this library has generated the full prompt, returns full prompt </summary>
        public static event EventHandler<string>? OnFullPromptReady;
        /// <summary> Called when the system changes states (no init, busy, ready) </summary>
        public static event EventHandler<SystemStatus>? OnStatusChanged;
        /// <summary> Called when the bot persona is changed, returns the new bot (sender is always null) </summary>
        public static event EventHandler<BasePersona>? OnBotChanged;
        /// <summary> Called during inference with typed, channel-tagged segments. Provides richer information than OnInferenceStreamed. </summary>
        public static event EventHandler<InferenceSegment>? OnInferenceSegment;
        /// <summary> Called when a complete inference cycle finishes. Provides structured results including thinking content and tool call records. </summary>
        public static event EventHandler<InferenceResult>? OnInferenceCompleted;
        /// <summary> Called when this library has generated the full prompt, returns full prompt </summary>
        public static event EventHandler<SingleMessage>? OnHistoryLogged;

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

        /// <summary> Gets the type of completion API currently in use by the client. </summary>
        public static CompletionType CompletionAPIType => Client?.CompletionType ?? CompletionType.Text;

        /// <summary>
        /// Gets a value indicating whether tool calls are currently available 
        /// (the backend supports tool calls, tool calls are allowed by settings, and at least one tool is registered).
        /// </summary>
        public static bool ToolCallsLoaded => SupportsToolCalls && Settings.ToolCallsAllowed && ToolManager.HasTools();

        private static void RaiseOnFullPromptReady(string fullprompt) => OnFullPromptReady?.Invoke(Bot, fullprompt);
        private static void RaiseOnStatusChange(SystemStatus newStatus)
        {
            // A throwing subscriber must never leave the engine half-transitioned (a turn that began but never ends, for instance).
            try
            {
                OnStatusChanged?.Invoke(Bot, newStatus);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[Core] An OnStatusChanged handler failed: {Message}", ex.Message);
            }
        }
        private static void RaiseOnQuickInferenceEnded(string fullprompt) => OnQuickInferenceEnded?.Invoke(Bot, fullprompt);
        private static void RaiseInferenceSegment(InferenceSegment segment) => OnInferenceSegment?.Invoke(Bot, segment);
        private static void RaiseInferenceCompleted(InferenceResult result) => OnInferenceCompleted?.Invoke(Bot, result);
        internal static void RaiseOnHistoryLogged(SingleMessage message) => OnHistoryLogged?.Invoke(Bot, message);

        /// <summary> List of loaded context plugins. IContextPlugins must be registered in this list before connecting to a persona</summary>
        public static List<IContextPlugin> ContextPlugins { get; set; } = [];

        /// <summary>
        /// Current status of the system. NoInit = not initialized, Ready = ready to use, Busy = working or generating a response.
        /// </summary>
        public static SystemStatus Status
        {
            get => status;
            private set
            {
                // Only notify on a real transition: nested operations (a plugin query inside a chat turn, for
                // example) re-assert the current status, which must not wake the UI up again.
                if (status == value)
                    return;
                status = value;
                RaiseOnStatusChange(value);
            }
        }

        /// <summary>
        /// True while a wire call to the backend is in flight
        /// (i.e. <see cref="ILLMServiceClient.GenerateText(object, CancellationToken)"/> or
        /// <see cref="ILLMServiceClient.GenerateTextStreaming(object, CancellationToken)"/> is on the stack,
        /// including the in-adapter tool-call loop for chat-completion backends).
        /// Distinct from <see cref="Status"/>: Status reflects library-user interactivity
        /// (it is Busy for the whole of a chat turn, including the preparation steps before the wire call),
        /// LLMBusy reflects model-slot occupancy.
        /// Agents should consult this in their main loop and skip their tick when true.
        /// In-engine sub-calls (plugins calling <see cref="SimpleQuery(object, CancellationToken)"/>
        /// from a plugin pre-pass) are allowed: they happen before the turn's own wire call, which waits for the slot.
        /// </summary>
        public static bool LLMBusy { get; private set; } = false;

        /// <summary> The currently loaded bot persona. You can change it here. </summary>
        /// <seealso cref="BasePersona"/>"
        public static BasePersona Bot { get => bot; set => ChangeBot(value); }

        /// <summary> The currently loaded user persona. You can change it here. </summary>
        /// <seealso cref="BasePersona"/>"
        public static BasePersona User { get => user; set => user = value; }

        /// <summary>
        /// Gets or sets the logger used for recording log messages throughout the application.
        /// </summary>
        /// <remarks>Assigning a custom logger allows integration with different logging frameworks or custom log handling. Given that LetheAI will only run exception on catastrophic errors, this is also the main way to monitor backend activity</remarks>
        public static ILogger? Logger
        {
            get => logger;
            set => logger = value;
        }

        /// <summary> 
        /// Instruction format: crucial for text completion backends, and still important for accurate token estimation on chat completetion backends.
        /// It's on the application to customize this format to match the loaded model.
        /// </summary>
        /// <seealso cref="InstructFormat"/>"
        public static InstructFormat Instruct { 
            get => instruct; 
            set
            {
                instruct = value;
                InvalidatePromptCache();
            } 
        }

        /// <summary> 
        /// Inference settings (Not all backends support all the settings). 
        /// Defaults are conservative to ensure a good out-of-the-box experience, but they can be tweaked to get better results with specific models.
        /// </summary>
        /// <seealso cref="SamplerSettings"/>
        public static SamplerSettings Sampler { get; set; } = new();

        /// <summary> 
        /// System prompt to be used when communicating with the LLM in full-chat mode. 
        /// It's on the application to customize this prompt to fit its needs.
        /// </summary>
        /// <seealso cref="Files.SystemPrompt"/>"
        public static SystemPrompt SystemPrompt { get; set; } = new();

        /// <summary> 
        /// Delegate to be called before executing a tool call, with the tool name and arguments. If it returns false, the tool call will be cancelled. 
        /// If left null, all tool calls will be executed without confirmation.
        /// </summary>
        /// <remarks>The application can use this to implement a confirmation step before executing potentially dangerous tool calls, 
        /// or to filter tool calls based on their arguments.</remarks>
        public static Func<string, string, Task<bool>>? ToolCallConfirmation { get; set; }

        /// <summary>
        /// Used to register / unregister tools (for function calling persona) in a unified way across the system. 
        /// </summary>
        public static ToolManager ToolManager { get; } = new();

        /// <summary>
        /// Library of skills (reusable markdown instructions for particular tasks). Call <see cref="Skills.SkillManager.Load"/> once the settings are loaded.
        /// </summary>
        public static SkillManager SkillManager { get; } = new();
        private static readonly HashSet<string> _loadedPluginPaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> Shortcut to the chat history of the currently loaded bot. </summary>
        public static Chatlog History => Bot.History;

        /// <summary>
        /// Loads and registers a plugin assembly once, then delegates type discovery
        /// to each subsystem (agent, tools, moods) from the already-loaded assembly.
        /// Any <see cref="IPluginEntry"/> implementations are invoked once before subsystem scans.
        /// </summary>
        /// <param name="dllPath">Absolute or relative path to the plugin DLL.</param>
        /// <exception cref="FileNotFoundException">Thrown when the DLL file does not exist.</exception>
        public static void RegisterPlugin(string dllPath)
        {
            if (!File.Exists(dllPath))
                throw new FileNotFoundException($"Plugin DLL not found: {dllPath}", dllPath);

            var fullPath = Path.GetFullPath(dllPath);
            if (!_loadedPluginPaths.Add(fullPath))
                return;

            var assembly = Assembly.LoadFrom(fullPath);
            var dllName = Path.GetFileName(fullPath);

            foreach (var type in assembly.GetTypes()
                         .Where(t => typeof(IPluginEntry).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                try
                {
                    var entry = (IPluginEntry)Activator.CreateInstance(type)!;
                    entry.Register();
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Failed to invoke IPluginEntry on type {type} from {dll}", type.FullName, dllName);
                }
            }

            AgentRuntime.RegisterFromAssembly(assembly);
            ToolManager.RegisterFromAssembly(assembly);
            MoodManager.RegisterFromAssembly(assembly);
        }

        /// <summary>
        /// Loads all plugin DLLs from a directory, calling <see cref="RegisterPlugin"/> for each match.
        /// Returns silently if the directory does not exist. Errors for individual DLLs are logged
        /// and do not prevent other DLLs from loading.
        /// </summary>
        /// <param name="directoryPath">Path to the plugins folder.</param>
        /// <param name="searchPattern">File search pattern; defaults to <c>*.dll</c>.</param>
        public static void RegisterPluginsFromDirectory(string directoryPath, string searchPattern = "*.dll")
        {
            if (!Directory.Exists(directoryPath))
                return;

            foreach (var dll in Directory.GetFiles(directoryPath, searchPattern))
            {
                try
                {
                    RegisterPlugin(dll);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Failed to load plugin DLL: {dll}", Path.GetFileName(dll));
                }
            }
        }

        /// <summary> Language models use this character to mark a new line which is different than the one used on Windows.</summary>
        public static readonly string NewLine = "\n";

        private static SystemStatus status = SystemStatus.NotInit;
        internal static StringBuilder StreamingTextProgress = new();
        private static InferenceChannel _currentChannel = InferenceChannel.Text;
        /// <summary>
        /// True once the current response has produced at least one <c>reasoning_content</c>
        /// delta. In that mode the visible <c>content</c> deltas are routed directly to the
        /// talking buffer (bypassing the inline-tag splitter) so the parser state stays
        /// consistent with the channel the backend actually signaled.
        /// </summary>
        private static bool _useSeparateFieldRouting = false;
        private static InstructFormat instruct = new() 
        { 
            SystemStart = "### Instruction:" + NewLine,
            SystemEnd = "",
            UserStart = "### Input:" + NewLine,
            UserEnd = "",
            BotStart = "### Response:" + NewLine,
            BotEnd = "",
            NewLinesBetweenMessages = true,
            StopStrings = [ "### Instruct", "### Input", "### Response" ],
        };
        private static readonly TextStreamReceiver textStreamReceiver = new();

        private static bool _isSimpleQuery = false;
        private static ILogger? logger = null;
        private static BasePersona bot = new() { IsUser = false, Name = "Bot", Bio = "You are a helpful AI assistant whose goal is to answer questions and complete tasks.", UniqueName = string.Empty };
        private static BasePersona user = new() { IsUser = true, Name = "User", UniqueName = string.Empty };
        public static PromptInserts dataInserts = [];
        internal static readonly Random RNG = new();

        #region *** Turn and operation state (internal) ***

        // Everything that decides "is something running?" and "can it be cancelled?" lives here:
        //
        //  Status             UI facing. Busy for the whole of a chat turn (preparation steps included) or of a standalone query.
        //  LLMBusy            The model slot. Claimed atomically around a wire call, and for nothing else.
        //  _turnActive        A chat turn (send / reroll / impersonate / add bot message) is running. At most one at a time.
        //  _turnCts           Cancellation scope of that turn. Cancelled by CancelGeneration, checked between the turn's steps,
        //                     and linked into every wire call the turn (or a nested SimpleQuery) makes.
        //  _queryCts          Same thing for a standalone SimpleQuery / SimpleQueryStreaming (no turn around it).
        //  _completionRaised  Exactly-once guard for OnInferenceCompleted within the current streaming operation.
        //  _turnError         Set when the turn failed before/while talking to the backend, reported through the completion event.
        private static readonly Lock _stateLock = new();
        private static CancellationTokenSource? _turnCts;
        private static CancellationTokenSource? _queryCts;
        private static bool _turnActive;
        private static bool _completionRaised;
        private static string? _turnError;

        /// <summary> True when the operation currently running (turn or standalone query) was asked to cancel. </summary>
        private static bool IsOperationCancelled => (_turnCts?.IsCancellationRequested ?? false) || (_queryCts?.IsCancellationRequested ?? false);

        /// <summary>
        /// Atomically claims the model slot. Returns false when someone else holds it.
        /// </summary>
        private static bool TryClaimWire(bool simpleQuery)
        {
            lock (_stateLock)
            {
                if (LLMBusy)
                    return false;
                LLMBusy = true;
                _isSimpleQuery = simpleQuery;
                return true;
            }
        }

        /// <summary>
        /// Waits for the model slot (held by an agent query, for instance) and claims it. Cancellable.
        /// </summary>
        private static async Task ClaimWireAsync(CancellationToken ct, bool simpleQuery)
        {
            var warned = false;
            while (!TryClaimWire(simpleQuery))
            {
                if (!warned)
                {
                    warned = true;
                    logger?.LogInformation("[Core] Waiting for the model slot to be released.");
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        private static void ReleaseWire()
        {
            lock (_stateLock)
            {
                LLMBusy = false;
                _isSimpleQuery = false;
            }
        }

        /// <summary>
        /// Starts a chat turn: refuses when anything is already running, otherwise flips the engine to Busy
        /// right away (the preparation steps - plugins, memory, prompt building - are part of the turn).
        /// </summary>
        private static bool TryBeginTurn(out CancellationToken token)
        {
            lock (_stateLock)
            {
                if (_turnActive || LLMBusy || status == SystemStatus.Busy || Client == null || PromptBuilder == null)
                {
                    token = default;
                    return false;
                }
                _turnActive = true;
                _turnError = null;
                _completionRaised = false;
                _turnCts?.Dispose();
                _turnCts = new CancellationTokenSource();
                token = _turnCts.Token;
            }
            // Leftovers of the previous response must not leak into this turn (a cancel before the first token would report them).
            ResetStreamingState();
            Status = SystemStatus.Busy;
            return true;
        }

        private static void EndTurn()
        {
            CancellationTokenSource? cts;
            lock (_stateLock)
            {
                _turnActive = false;
                cts = _turnCts;
                _turnCts = null;
            }
            cts?.Dispose();
            // Only Busy goes back to Ready: Disconnect() may have moved us to NotInit in the meantime.
            if (status == SystemStatus.Busy)
                Status = SystemStatus.Ready;
        }

        /// <summary>
        /// Runs one chat turn. Contract: when this returns true, the turn was started and exactly one
        /// <see cref="OnInferenceCompleted"/> has been raised for it (normal, error or cancelled).
        /// Returns false, and raises nothing, when the turn could not start (engine busy or not connected).
        /// </summary>
        private static async Task<bool> RunTurn(Func<CancellationToken, Task> body)
        {
            if (!TryBeginTurn(out var ct))
            {
                logger?.LogWarning("[Core] Generation request ignored: the engine is busy or not connected.");
                return false;
            }
            try
            {
                await body(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                logger?.LogInformation("[Core] Generation cancelled.");
            }
            catch (Exception ex)
            {
                _turnError = $"Error during generation: {ex.Message}";
                logger?.LogError(ex, "[Core] Error during generation: {Message}", ex.Message);
            }
            finally
            {
                try
                {
                    EnsureCompletion(ct, _turnError);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "[Core] Error while finalizing the generation: {Message}", ex.Message);
                }
                EndTurn();
            }
            return true;
        }

        /// <summary>
        /// Guarantees <see cref="OnInferenceCompleted"/> was raised once for the operation that just ended.
        /// Backends are inconsistent when interrupted (some send nothing, some a fake error, some a stop), so the
        /// engine is the one reporting a cancellation, whatever the backend did.
        /// </summary>
        private static void EnsureCompletion(CancellationToken ct, string? error)
        {
            if (_completionRaised)
                return;

            var (think, talk) = textStreamReceiver.GetCurrentBuffers();
            if (ct.IsCancellationRequested)
            {
                _completionRaised = true;
                StreamingTextProgress.Clear();
                RaiseInferenceCompleted(new InferenceResult
                {
                    Response = talk.Trim(),
                    ThinkingContent = think.Length > 0 ? think.Trim() : null,
                    FinishReason = "cancelled",
                    WasCancelled = true
                });
                return;
            }

            if (error != null || (talk.Length == 0 && think.Length == 0))
            {
                _completionRaised = true;
                StreamingTextProgress.Clear();
                RaiseInferenceCompleted(new InferenceResult
                {
                    Response = error ?? "The backend ended the response without sending anything.",
                    FinishReason = "error"
                });
                return;
            }

            // The backend stopped talking without a final event (dropped connection, backend that never says "stop").
            logger?.LogWarning("[Core] The backend ended the stream without a completion event, finalizing with what was received.");
            Client_StreamingMessageReceived(Bot, new LLMTokenStreamingEventArgs(string.Empty, "stop"));
        }

        /// <summary>
        /// Streaming wire call of a chat turn. Waits for the model slot if an agent query is holding it.
        /// </summary>
        private static async Task StreamWireAsync(object query, CancellationToken ct)
        {
            await ClaimWireAsync(ct, simpleQuery: false).ConfigureAwait(false);
            try
            {
                ct.ThrowIfCancellationRequested();
                var client = Client ?? throw new InvalidOperationException("The backend was disconnected.");
                await client.GenerateTextStreaming(query, ct).ConfigureAwait(false);
            }
            finally
            {
                ReleaseWire();
            }
        }

        /// <summary>
        /// Creates the cancellation scope of a standalone query. Inside a chat turn (plugin pre-pass) the query joins the
        /// turn's scope, so cancelling the turn cancels it too.
        /// </summary>
        private static CancellationTokenSource BeginQueryScope(CancellationToken caller)
        {
            lock (_stateLock)
            {
                if (_turnActive && _turnCts != null)
                    return LinkSafe(caller, _turnCts.Token);
                var scope = LinkSafe(caller, default);
                _queryCts = scope;
                return scope;
            }
        }

        /// <summary>
        /// Links the caller's token (which may belong to an already disposed source: agents recycle theirs) with an optional
        /// engine token. A dead caller token is simply ignored.
        /// </summary>
        private static CancellationTokenSource LinkSafe(CancellationToken caller, CancellationToken engine)
        {
            try
            {
                return CancellationTokenSource.CreateLinkedTokenSource(caller, engine);
            }
            catch (ObjectDisposedException)
            {
                return CancellationTokenSource.CreateLinkedTokenSource(engine);
            }
        }

        private static void EndQueryScope(CancellationTokenSource scope)
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_queryCts, scope))
                    _queryCts = null;
            }
            scope.Dispose();
        }

        /// <summary>
        /// Used by the "think prefill" feature: feeds the prefill text as if the model had written it.
        /// </summary>
        private static void EmitThinkPrefill(bool roleAllowsIt)
        {
            if (roleAllowsIt && Instruct.PrefillThinking && !string.IsNullOrEmpty(Instruct.ThinkingStart) && !ToolCallsLoaded && Client?.AllowPrefill == true)
            {
                var prefill = Instruct.GetThinkPrefill();
                if (!string.IsNullOrWhiteSpace(prefill))
                    Client_StreamingMessageReceived(Bot, new LLMTokenStreamingEventArgs(prefill, null, null));
            }
        }

        #endregion

        #region *** Initialization and Loading ***

        private static void LoadBackendClient()
        {
            if (Status != SystemStatus.NotInit)
                return;
            if (!Settings.ValidateSettings(out var validationError) && !string.IsNullOrEmpty(validationError))
            {
                logger?.LogError("[Core] Invalid settings: {Error}", validationError);
            }            // Create the appropriate client based on the selected backend
            var httpClient = new HttpClient
            {
                Timeout = new TimeSpan(0, 5, 0)
            };

            Client = Settings.BackendAPI switch
            {
                BackendAPI.KoboldAPI => new KoboldCppAdapter(httpClient),
                BackendAPI.OpenAI => new OpenAIAdapter(httpClient),
                BackendAPI.LlamaSharp => new LlamaSharpAdapter(Settings.BackendUrl),
                BackendAPI.LlamaCpp => new LlamaCppAdapter(httpClient),
                _ => throw new NotSupportedException($"Backend {Settings.BackendAPI} is not supported")
            };
            // Subscribe to the TokenReceived event
            Client.BaseUrl = Settings.BackendUrl;
            Client.TokenReceived += Client_StreamingMessageReceived;
            if (Settings.DefaultCompletionType.HasValue)
                Client.SelectCompletionType(Settings.DefaultCompletionType.Value);

            PromptBuilder = GetPromptBuilder();
            AgentRuntime.LoadDefaultActions();
            MoodManager.LoadDefaultMoods();
            if (LoadedPersonas.Count == 0)
                LoadPersonas();
            Status = SystemStatus.Ready;
        }

        /// <summary>
        /// Initializes the backend service configuration with the specified URL, API type, optional authentication
        /// and completion preferences.
        /// </summary>
        /// <remarks>This method must be called before making requests to the backend service. Calling
        /// this method resets the system status and updates the backend client configuration.</remarks>
        /// <param name="url">The base URL of the backend service to connect to (or the GGUF file path for LlamaSharp). Cannot be null, empty, or whitespace.</param>
        /// <param name="backend">The type of backend API to use for service requests.</param>
        /// <param name="key">An optional API key used for authentication with the backend service. If not provided, a default key is used.</param>
        /// <param name="preferCompletion">An optional value indicating the preferred completion type for backend operations.</param>
        /// <exception cref="ArgumentException">Thrown if the url parameter is null, empty, or consists only of whitespace.</exception>
        public static void Setup(string url, BackendAPI backend, string? key = null, CompletionType? preferCompletion = null)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("Backend URL cannot be null or empty", nameof(url));
            Status = SystemStatus.NotInit;
            Settings.BackendUrl = url;
            Settings.BackendAPI = backend;
            Settings.OpenAIKey = key ?? "123";
            Settings.DefaultCompletionType = preferCompletion;
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
            if (!Settings.ValidateSettings(out var validationError) && !string.IsNullOrEmpty(validationError))
            {
                logger?.LogError("[Core] Invalid settings: {Error}", validationError);
            }
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
                // Never clobber a running turn/query: only an idle or uninitialized engine becomes Ready.
                if (status != SystemStatus.Busy)
                    Status = SystemStatus.Ready;
            }
            catch (Exception ex)
            {
                MaxContextLength = 4096;
                CurrentModel = "Error";
                Backend = "Error";
                Status = SystemStatus.NotInit;
                LLMEngine.Logger?.LogError(ex, "Failed to connect to LLM server: {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Disconnects from the LLM server and releases associated resources.
        /// </summary>
        /// <remarks>This method unsubscribes from server events, clears cached prompts, and resets the
        /// client state. It is safe to call this method multiple times; if no connection exists, the method has no
        /// effect. You don't need to use this function before reconnecting or leaving the app. It's more like a "reset" of the library's state.</remarks>
        public static void Disconnect()
        {
            if (Client == null)
                return;
            try
            {
                Client.TokenReceived -= Client_StreamingMessageReceived;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Failed to disconnect from LLM server: {Message}", ex.Message);
            }
            finally
            {
                InvalidatePromptCache();
                Client = null;
                PromptBuilder = null;
                Status = SystemStatus.NotInit;
            }
        }

        #endregion

        #region *** Persona Management ***

        /// <summary>
        /// Preload personas available in the application, so the system can interpret chatlogs from personas that aren't the currently loaded ones.
        /// </summary>
        /// <param name="toload"> List of personas to load. If null, it'll try to load from the DataPath folder. </param>
        public static void LoadPersonas(List<BasePersona>? toload = null)
        {
            LoadedPersonas = [];
            if (toload is not null)
            {
                foreach (var item in toload)
                    LoadedPersonas.Add(item.UniqueName, item);
            }
            else
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
        }

        /// <summary>
        /// Determines whether a persona with the specified unique name is currently loaded.
        /// </summary>
        /// <param name="uniqueName">The unique name of the persona to check for existence. Cannot be null.</param>
        /// <returns>true if a persona with the specified unique name is loaded; otherwise, false.</returns>
        public static bool HasPersona(string uniqueName) => LoadedPersonas.ContainsKey(uniqueName);

        /// <summary>
        /// Retrieves the loaded persona associated with the specified unique name, if it exists.
        /// </summary>
        /// <param name="uniqueName">The unique identifier of the persona to retrieve. Cannot be null.</param>
        /// <returns>The persona associated with the specified unique name, or null if no such persona is loaded.</returns>
        public static BasePersona? GetPersona(string uniqueName) => LoadedPersonas.TryGetValue(uniqueName, out BasePersona? value) ? value : null;

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

        #region *** Full Communications (Send/reroll messages using History, RAG, and all features) ***

        /// <summary>
        /// Asks the model to generate a message based on the the chat history. 
        /// Response done through the OnInferenceSegment and OnInferenceCompleted events.
        /// </summary>
        /// <returns>
        /// true if the turn ran (exactly one <see cref="OnInferenceCompleted"/> was raised: normal, error or cancelled),
        /// false if it could not start because the engine is busy or not connected (nothing is raised in that case).
        /// It's the app's responsibility to log (or not) the response to the chat history through the OnInferenceCompleted event.
        /// </returns>
        public static async Task<bool> AddBotMessage()
        {
            return await StartGeneration(new SingleMessage(AuthorRole.Assistant, string.Empty)).ConfigureAwait(false);
        }

        /// <summary>
        /// Ask the model to impersonate the user based on the chat history. 
        /// Response done through the OnInferenceSegment and OnInferenceCompleted events.
        /// </summary>
        /// <returns>See <see cref="AddBotMessage"/>.</returns>
        public static async Task<bool> ImpersonateUser()
        {
            return await StartGeneration(new SingleMessage(AuthorRole.User, string.Empty)).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a message to the LLM. Message is logged to chat history (even when the turn is cancelled or fails before 
        /// the model is reached: the message was sent). 
        /// Response is done through the OnInferenceSegment and OnInferenceCompleted events.
        /// </summary>
        /// <param name="message">Fully fledged message</param>
        /// <returns>See <see cref="AddBotMessage"/>.</returns>
        public static async Task<bool> SendMessageToBot(SingleMessage message)
        {
            return await StartGeneration(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Rerolls the last response from the bot. It will automatically remove the last message from the chat history (if it's from the bot).
        /// Response done through the OnInferenceSegment and OnInferenceCompleted events.
        /// </summary>
        /// <returns>
        /// See <see cref="AddBotMessage"/>. Returns false, and leaves the chat history untouched, when there is nothing to reroll
        /// or the engine is busy.
        /// </returns>
        public static async Task<bool> RerollLastMessage()
        {
            if (History.CurrentSession.Messages.Count == 0 || History.LastMessage()?.Role != AuthorRole.Assistant || Client == null || PromptBuilder == null)
                return false;

            return await RunTurn(async ct =>
            {
                // Only touch the history once we know the turn is really going to happen.
                History.RemoveLast(true);
                var pb = PromptBuilder ?? throw new InvalidOperationException("The backend was disconnected.");
                var oldquery = (pb.LastQuery != null) ? pb.RegenLastQuery() : null;

                if (oldquery != null)
                {
                    ResetStreamingState();
                    EmitThinkPrefill(true);
                    RaiseOnFullPromptReady(pb.PromptToText());
                    await StreamWireAsync(oldquery, ct).ConfigureAwait(false);
                }
                else if (pb.Count == 0)
                {
                    await GenerationBody(new SingleMessage(AuthorRole.Assistant, string.Empty), ct).ConfigureAwait(false);
                }
                else
                {
                    var query = pb.PromptToQuery(AuthorRole.Assistant, forceAltRoles: IsGroupConversation && Settings.GroupInstructFormatAdapter);
                    ResetStreamingState();
                    EmitThinkPrefill(true);
                    RaiseOnFullPromptReady(pb.PromptToText());
                    await StreamWireAsync(query, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Cancel whatever the engine is currently doing: a chat turn (including its preparation steps: plugins,
        /// memory, prompt building), a streaming/simple query, or the backend call underneath them. 
        /// Safe to call at any time, from any thread. 
        /// </summary>
        /// <remarks>
        /// A cancelled chat turn always ends with one <see cref="OnInferenceCompleted"/> whose 
        /// <see cref="InferenceResult.WasCancelled"/> is true, and then the status goes back to Ready. 
        /// Note that some backends need an HTTP round trip to abort (KoboldCpp), so prefer calling this off the UI thread.
        /// </remarks>
        /// <returns>true if something was cancelled (or there is no client at all)</returns>
        public static bool CancelGeneration()
        {
            var cancelled = false;

            // 1. Engine level: stops the turn / query wherever it is, even when no wire call exists yet.
            CancellationTokenSource? turn, query;
            lock (_stateLock)
            {
                turn = _turnCts;
                query = _queryCts;
            }
            foreach (var cts in new[] { turn, query })
            {
                if (cts == null)
                    continue;
                try
                {
                    if (!cts.IsCancellationRequested)
                    {
                        cts.Cancel();
                        cancelled = true;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // the operation finished in the meantime, nothing left to cancel
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "[Core] Failed to cancel generation: {err}", ex.Message);
                }
            }

            // 2. Backend level: some backends (KoboldCpp) keep generating after the connection is dropped and need an explicit abort.
            var client = Client;
            if (client == null)
                return true;
            try
            {
                cancelled |= client.AbortGenerationSync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "[Core] Failed to abort the backend generation: {err}", ex.Message);
            }
            return cancelled;
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
            var client = Client ?? throw new InvalidOperationException("LLMEngine not initialized. Call Setup() and Connect() first.");
            if (!TryClaimWire(simpleQuery: true))
            {
                logger?.LogError("[Core] Cannot perform SimpleQuery: LLM is currently busy.");
                return string.Empty;
            }

            // From here on the model slot is ours: everything below must end in ReleaseWire(), whatever happens.
            var oldst = status;
            CancellationTokenSource? scope = null;
            var result = string.Empty;
            try
            {
                Status = SystemStatus.Busy;
                scope = BeginQueryScope(ctx);
                if (!scope.IsCancellationRequested)
                    result = await client.GenerateText(chatlog, scope.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (scope?.IsCancellationRequested == true)
            {
                result = string.Empty;
            }
            finally
            {
                // A cancelled query has no meaningful answer, whatever the backend returned (some return an error text).
                if (scope?.IsCancellationRequested == true)
                    result = string.Empty;
                ReleaseWire();
                if (scope != null)
                    EndQueryScope(scope);
                // Nested inside a chat turn, oldst is Busy and nothing changes: the turn owns the status.
                if (status == SystemStatus.Busy)
                    Status = oldst;
            }
            RaiseOnQuickInferenceEnded(result);
            return string.IsNullOrEmpty(result) ? string.Empty : result;
        }

        /// <summary>
        /// Submit a chatlog, get a response from the LLM. Streamed response through event system
        /// </summary>
        /// <param name="chatlog">Full chatlog to sent to the LLM. GenerationInput for Text Completion API or ChatRequest for Chat Completion API. Use PromptBuilder to generate proper format for the currently loaded API automatically.</param>
        public static async Task SimpleQueryStreaming(object chatlog, CancellationToken ctx = default)
        {
            var client = Client ?? throw new InvalidOperationException("LLMEngine not initialized. Call Setup() and Connect() first.");
            if (!TryClaimWire(simpleQuery: true))
            {
                logger?.LogError("[Core] Cannot perform SimpleQueryStreaming: LLM is currently busy.");
                return;
            }

            var oldst = status;
            CancellationTokenSource? scope = null;
            string? error = null;
            try
            {
                Status = SystemStatus.Busy;
                scope = BeginQueryScope(ctx);
                lock (_stateLock)
                {
                    _completionRaised = false;
                }
                ResetStreamingState();
                if (!scope.IsCancellationRequested)
                    await client.GenerateTextStreaming(chatlog, scope.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (scope?.IsCancellationRequested == true)
            {
                // reported below through the completion event
            }
            catch (Exception ex)
            {
                error = $"Error during generation: {ex.Message}";
                logger?.LogError(ex, "[Core] Error during streaming query: {Message}", ex.Message);
            }
            finally
            {
                try
                {
                    EnsureCompletion(scope?.Token ?? default, error);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "[Core] Error while finalizing the streaming query: {Message}", ex.Message);
                }
                ReleaseWire();
                if (scope != null)
                    EndQueryScope(scope);
                if (status == SystemStatus.Busy)
                    Status = oldst;
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

        /// <summary>
        /// Called to clear the prompt cache and force a rebuild of the prompt on next generation. Must be called when changing any setting that affects the prompt.
        /// </summary>
        public static void InvalidatePromptCache()
        {
            PromptBuilder?.Clear();
            dataInserts.Clear();
            ResetStreamingState();
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
            if (!BannedSearchWords.IsAllowed(query))
            {
                Logger?.LogWarning("Search Query Cancelled: {query}", query);
                return [];
            }
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

        #endregion


        #region *** Private and Internal Methods ***

        /// <summary>
        /// Handles streaming message events from the client, processing incoming tokens and managing the completion of
        /// streamed responses.
        /// </summary>
        /// <remarks>This method processes both intermediate and final tokens received during a streaming
        /// inference operation. On completion, it finalizes the response, handles tool call records, updates
        /// conversation history, and raises relevant events. The method supports both simple and full-chat modes, and
        /// integrates with context plugins to allow output modification.</remarks>
        /// <param name="sender">The source of the event, typically the client instance that is streaming the message.</param>
        /// <param name="e">An event argument containing details about the streamed token, completion status, and any associated tool
        /// call records.</param>
        private static void Client_StreamingMessageReceived(object? sender, LLMTokenStreamingEventArgs e)
        {
            // "null", "stop", "length"

            // Once the user cancelled, whatever the backend still delivers (a chunk already in flight, a "stop" sent
            // while shutting down...) belongs to a response that was abandoned. The engine reports the end of the
            // operation itself (see EnsureCompletion), so nothing more must reach the application or the buffers.
            if (IsOperationCancelled)
                return;

            if (e.IsComplete)
            {
                // If the final delta carries reasoning content (e.g. a reasoning chunk
                // emitted together with the stop finish_reason), route it to the
                // thinking buffer before finalizing the response.
                if (!string.IsNullOrEmpty(e.ReasoningToken))
                {
                    _useSeparateFieldRouting = true;
                    StreamingTextProgress.Append(e.ReasoningToken);
                    textStreamReceiver.FeedToChannel(InferenceChannel.Thinking, e.ReasoningToken);
                    RaiseInferenceSegment(new InferenceSegment
                    {
                        Channel = InferenceChannel.Thinking,
                        Text = e.ReasoningToken,
                        IsComplete = true
                    });
                }

                if (!string.IsNullOrEmpty(e.Token))
                {
                    StreamingTextProgress.Append(e.Token);
                    if (_useSeparateFieldRouting)
                    {
                        // Backend signaled channels via separate fields; bypass the
                        // inline-tag parser entirely.
                        textStreamReceiver.FeedToChannel(InferenceChannel.Text, e.Token);
                        _currentChannel = InferenceChannel.Text;
                    }
                    else
                    {
                        _currentChannel = textStreamReceiver.FeedToken(e.Token);
                    }
                }
                var response = textStreamReceiver.GetFormattedText();
                // We went over the max response length (or reached stop string on some backends) and the backend had to cut the response
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

                // if the finish reason is NOT a "tool_calls", we can execute the context plugin replacement.
                if (e.FinishReason != "tool_calls")
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
                        textStreamReceiver.ForceFeed(InferenceChannel.Thinking, $"\n[Tool {record.FunctionName} called]\n");
                        RaiseInferenceSegment(new InferenceSegment
                        {
                            Channel = InferenceChannel.ToolResult,
                            ToolResult = new ToolResultInfo
                            {
                                CallId = record.CallId,
                                FunctionName = record.FunctionName,
                                Success = record.Success,
                                ResultJson = record.ResultJson,
                                Error = record.Error,
                                Images = record.Images
                            },
                            IsComplete = true
                        });
                    }
                }

                // Log tool calls to history only in full-chat mode (not simple queries)
                if (!_isSimpleQuery && e.ToolCallRecords?.Count > 0)
                {
                    // ONE assistant message with ALL tool calls from this inference
                    var assistantToolMsg = new SingleMessage(AuthorRole.Assistant, string.Empty, toolCalls: [.. e.ToolCallRecords]);
                    Bot.History.LogMessage(assistantToolMsg);

                    // One ToolResult message per tool call
                    foreach (var record in e.ToolCallRecords)
                    {
                        // Images attached by the tool (unpacked from the ToolMediaResult envelope by the
                        // client) are stored in ImagePaths so replayed history renders the same content
                        // parts as the live call, images included.
                        var resultMsg = new SingleMessage(AuthorRole.Tool,
                            record.Success ? record.ResultJson : (record.Error ?? $"Error calling {record.FunctionName}"),
                            imgs: record.Images,
                            toolCalls: [.. new List<ToolCallRecord> { record }]);
                        Bot.History.LogMessage(resultMsg);
                    }
                }
                if (e.FinishReason != "tool_calls")
                {
                    var (ThinkContent, TalkContent) = textStreamReceiver.GetCurrentBuffers();

                    // Build structured result for new event
                    var thinkingContent = ThinkContent.Length > 0 ? ThinkContent.Trim() : null;
                    var textResponse = TalkContent.Trim();
                    var inferenceResult = new InferenceResult
                    {
                        Response = textResponse,
                        ThinkingContent = thinkingContent,
                        ToolCalls = e.ToolCallRecords ?? [],
                        FinishReason = e.FinishReason
                    };
                    _completionRaised = true;
                    RaiseInferenceCompleted(inferenceResult);
                }
                // reset StreamingTextProgress to get it ready for next message (if any)
                if (!string.IsNullOrEmpty(StreamingTextProgress.ToString()))
                {
                    Logger?.LogInformation("[Message] {message}", StreamingTextProgress.ToString().RemoveNewLines());
                }
                StreamingTextProgress.Clear();
            }
            else
            {
                // Reasoning content delivered through a separate streaming field
                // (the reasoning_content JSON property) is routed directly into the
                // thinking channel and bypasses the inline-tag splitter entirely.
                if (!string.IsNullOrEmpty(e.ReasoningToken))
                {
                    _useSeparateFieldRouting = true;
                    StreamingTextProgress.Append(e.ReasoningToken);
                    textStreamReceiver.FeedToChannel(InferenceChannel.Thinking, e.ReasoningToken);
                    _currentChannel = InferenceChannel.Thinking;
                    RaiseInferenceSegment(new InferenceSegment
                    {
                        Channel = InferenceChannel.Thinking,
                        Text = e.ReasoningToken,
                        IsComplete = false
                    });
                    return;
                }

                // Once the backend has signaled reasoning via a separate field for this
                // response, route any subsequent visible deltas directly to the talking
                // channel rather than running them through the inline-tag parser — the
                // parser state would otherwise stay in Thinking and silently absorb
                // visible content into the thinking buffer.
                if (_useSeparateFieldRouting && !string.IsNullOrEmpty(e.Token))
                {
                    StreamingTextProgress.Append(e.Token);
                    textStreamReceiver.FeedToChannel(InferenceChannel.Text, e.Token);
                    _currentChannel = InferenceChannel.Text;
                    RaiseInferenceSegment(new InferenceSegment
                    {
                        Channel = InferenceChannel.Text,
                        Text = e.Token,
                        IsComplete = false
                    });
                    return;
                }

                var token = e.Token;
                if (CompletionAPIType == CompletionType.Chat 
                    && StreamingTextProgress.Length == 0 
                    && Instruct.IsThinkFormat 
                    && !Settings.DisableThinking 
                    && (e.Token.Replace("\n", "") != Instruct.ThinkingStart.Replace("\n", ""))
                    && (e.Token.Length > 3 && !Instruct.ThinkingStart.StartsWith(e.Token))
                    )
                {
                    StreamingTextProgress = new StringBuilder(Instruct.ThinkingStart);
                    token = Instruct.ThinkingStart + token;
                }
                else if (CompletionAPIType == CompletionType.Text 
                    && StreamingTextProgress.Length == 0 
                    && Settings.DisableThinking
                    && Instruct.IsThinkFormat
                    && Instruct.RequireEmptyThinkBlockWhenThinkingDisabled)
                {
                    token = Instruct.GetThinkPrefill() + token;
                    StreamingTextProgress.Append(token);
                    return;
                }
                StreamingTextProgress.Append(e.Token);
                _currentChannel = textStreamReceiver.FeedToken(token);
                if (_currentChannel != InferenceChannel.Unknown)
                    RaiseInferenceSegment(new InferenceSegment { Channel = _currentChannel, Text = token, IsComplete = false });
            }
        }

        /// <summary>
        /// Computes the per-round token ceiling used for mid-chain tool-calling rounds (every round of a turn
        /// after the first one, until the final answer). Such rounds produce reasoning plus a tool call, which
        /// must not be cut short by the limit meant for the final reply.
        /// </summary>
        /// <remarks>
        /// <see cref="LLMSettings.ToolRoundMaxLength"/> &gt; 0 is used as-is (clamped to half the context). 
        /// Otherwise (automatic) it is twice the reply limit when thinking is enabled, clamped to a quarter of
        /// the context, or the reply limit when it is not. The result is never below <paramref name="replyLimit"/>.
        /// </remarks>
        /// <param name="replyLimit">The reply limit of the request (<see cref="LLMSettings.MaxReplyLength"/> or an override).</param>
        internal static int GetToolRoundBudget(int replyLimit)
        {
            if (replyLimit <= 0)
                return replyLimit;
            int ceiling;
            if (Settings.ToolRoundMaxLength > 0)
            {
                ceiling = Math.Min(Settings.ToolRoundMaxLength, MaxContextLength / 2);
            }
            else if (Instruct.IsThinkFormat && !Settings.DisableThinking)
            {
                ceiling = Math.Min(replyLimit * 2, MaxContextLength / 4);
            }
            else
            {
                ceiling = replyLimit;
            }
            return Math.Max(ceiling, replyLimit);
        }

        /// <summary>
        /// Resets all per-generation streaming state. Must be called before each new generation.
        /// </summary>
        private static void ResetStreamingState()
        {
            StreamingTextProgress.Clear();
            textStreamReceiver.Reset();
            _useSeparateFieldRouting = false;
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
                rawprompt.AppendLinuxLine($"{NewLine}{SystemPrompt.WorldInfoTitle}").AppendLinuxLine();
                foreach (var item in syspromptentries)
                    rawprompt.AppendLinuxLine(item.ToContent());
            }

            if (Settings.SessionMemorySystem && History.Sessions.Count > 1)
            {
                var shistory = History.GetPreviousSummaries(Settings.SessionReservedTokens - GetTokenCount(Bot.ReplaceMacros(SystemPrompt.SessionHistoryTitle)) - 3, SystemPrompt.SubCategorySeparator, allowRP: Settings.RecallMemoryMode, ignoreList: dataInserts.GetGuids());
                if (!string.IsNullOrEmpty(shistory))
                {
                    rawprompt.AppendLinuxLine(NewLine + Bot.ReplaceMacros(SystemPrompt.SessionHistoryTitle) + NewLine);
                    rawprompt.AppendLinuxLine(shistory.CleanupAndTrim());
                }
            }

            // Core facts section: top-ranked extracted facts about the user, inserted before session history
            // so the bot has durable user knowledge that doesn't depend on RAG similarity alone.
            if (Settings.FactRetrievalEnabled && Settings.CoreFactsTokenBudget > 0 && !string.IsNullOrEmpty(SystemPrompt.CoreFactsTitle))
            {
                var coreFacts = Bot.Brain.GetCoreFacts(Settings.CoreFactsTokenBudget);
                if (!string.IsNullOrEmpty(coreFacts))
                {
                    rawprompt.AppendLinuxLine($"{NewLine}{Bot.ReplaceMacros(SystemPrompt.CoreFactsTitle)}").AppendLinuxLine();
                    rawprompt.AppendLinuxLine(coreFacts.CleanupAndTrim());
                }
            }

            if (ToolCallsLoaded)
            {
                if (!string.IsNullOrEmpty(Settings.ToolInstructions))
                {
                    rawprompt.AppendLinuxLine($"{NewLine}{SystemPrompt.CategorySeparator} Tool-use Enforcement").AppendLinuxLine();
                    rawprompt.AppendLinuxLine(Settings.ToolInstructions.CleanupAndTrim());
                }
                if (Settings.ToolCallsAddSystemPromptNote)
                {
                    var tools = ToolManager.GetToolsets().Where(e => !string.IsNullOrEmpty(e.SystemPromptInstruction));
                    if (tools.Any())
                    {
                        rawprompt.AppendLinuxLine($"{NewLine}{SystemPrompt.CategorySeparator} Tool-use Notes").AppendLinuxLine();
                        foreach (var tool in tools)
                        {
                            rawprompt.AppendLinuxLine(tool.SystemPromptInstruction);
                        }
                    }
                }
            }
            
            if (Settings.AntiHallucinationMemoryFormat && !Bot.Brain.DisableEurekas)
            { 
                var abilities = Bot.AgentSystem?.AbilitiesToString();
                if (!string.IsNullOrEmpty(abilities))
                {
                    rawprompt.AppendLinuxLine(NewLine + "**Note:** Sometimes the system will insert events in the format <SystemEvent>[TYPE]: {content}.\nThese may include JOURNAL, WEBSEARCH, or GOAL.\nYou may acknowledge that you did one of the actions listed below when a system message says you did. However, you must not invent or describe the contents of those actions unless a <SystemEvent>[TYPE] has been explicitly provided:" + abilities);
                }
            }
            return Bot.ReplaceMacros(rawprompt.ToString()).CleanupAndTrim();
        }

        /// <summary>
        /// Generates a full prompt for the LLM to use
        /// </summary>
        /// <param name="newMessage">Added message from the user</param>
        /// <returns></returns>
        public static async Task<object> GenerateFullPrompt(SingleMessage message, string? pluginMessage = null, bool? forceAltRolesAnyway = null)
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

            if (ToolCallsLoaded)
                availtokens -= ToolManager.EstimatedTokenCost();

            var searchstring = string.IsNullOrEmpty(message.Message) ? History.GetLastFromInSession(AuthorRole.User)?.Message : message.Message;

            // update the RAG, world info, and summary stuff
            var actbot = Bot is GroupPersonaBase gp ? gp.GetCurrentPersona() : Bot;
            actbot ??= Bot;
            await actbot.Brain.GetRAGandInserts(dataInserts, searchstring ?? string.Empty, -1, Settings.RAGDistanceCutOff).ConfigureAwait(false);

            // Prepare the full system prompt and count the tokens used
            var rawprompt = GenerateSystemPromptContent(message.Message);
            availtokens -= PromptBuilder.AddMessage(new SingleMessage(AuthorRole.System, rawprompt));

            // Prepare the bot's response tokens and count them
            if (string.IsNullOrEmpty(message.Message) && message.Role == AuthorRole.User)
                availtokens -= PromptBuilder.GetResponseTokenCount(User);
            else
                availtokens -= PromptBuilder.GetResponseTokenCount(Bot);

            // get the full, formated chat history complemented by the data inserts
            var addinserts = string.IsNullOrEmpty(Instruct.ThinkingStart) || !Settings.RAGMoveToThinkBlock;
            var cushion = Client?.CompletionType == CompletionType.Chat ? MaxContextLength / 2 : 0;
            if (cushion > 15240)
                cushion = 15240;
            var historytokens = availtokens + cushion;
            History.AddHistoryToPrompt(Settings.SessionHandling, historytokens, addinserts ? dataInserts : null);
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

            // In chat mode the count below is pure overhead: it costs a full backend token-count round
            // trip, and its only consumer is the over-limit warning for text completion (chat prompts
            // are authoritatively trimmed in PromptToQuery anyway).
            if (Client?.CompletionType == CompletionType.Text)
            {
                var final = PromptBuilder.GetTokenUsage();
                if (final > (MaxContextLength - Settings.MaxReplyLength))
                {
                    var diff = final - (MaxContextLength - Settings.MaxReplyLength);
                    logger?.LogWarning("The prompt is {Diff} tokens over the limit.", diff);
                }
            }
            if (string.IsNullOrEmpty(message.Message) && message.Role == AuthorRole.User)
                return PromptBuilder.PromptToQuery(AuthorRole.User, forceAltRoles: forceAltRolesAnyway ?? (IsGroupConversation && Settings.GroupInstructFormatAdapter));
            else
                return PromptBuilder.PromptToQuery(AuthorRole.Assistant, forceAltRoles: forceAltRolesAnyway ?? (IsGroupConversation && Settings.GroupInstructFormatAdapter));
        }

        /// <summary>
        /// Plugin Handler
        /// </summary>
        /// <param name="lastuserinput"></param>
        /// <param name="ct">cancellation of the turn: stops the plugin loop as soon as possible</param>
        /// <returns></returns>
        private static async Task<string> BuildPluginSystemInsertAsync(string? lastuserinput, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(lastuserinput))
                return string.Empty;

            var insertmessages = new List<string>();
            foreach (var ctxplug in ContextPlugins)
            {
                ct.ThrowIfCancellationRequested();
                if (!ctxplug.Enabled)
                    continue;
                // Plugins may call LLMEngine.SimpleQuery here. That is fine: the turn is already Busy, and the model slot
                // (LLMBusy) is only claimed by the turn's own wire call, which comes after this pre-pass.
                // Nested queries join the turn's cancellation scope.
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
        /// Starts the generation process for the bot (one chat turn).
        /// </summary>
        /// <param name="message">Message to send (empty message = ask for a response / an impersonation, depending on the role)</param>
        /// <returns>See <see cref="AddBotMessage"/></returns>
        private static async Task<bool> StartGeneration(SingleMessage message)
        {
            return await RunTurn(ct => GenerationBody(message, ct)).ConfigureAwait(false);
        }

        /// <summary>
        /// The steps of a chat turn. Runs inside <see cref="RunTurn"/>: the engine is already Busy, a turn token exists, and 
        /// whatever happens here (cancel, exception) is reported by RunTurn through a single completion event.
        /// Every step boundary checks the token, so a cancel is honored even before the backend is involved.
        /// </summary>
        private static async Task GenerationBody(SingleMessage message, CancellationToken ct)
        {
            var pb = PromptBuilder ?? throw new InvalidOperationException("The backend was disconnected.");
            var messageLogged = false;
            try
            {
                var lastuserinput = string.IsNullOrWhiteSpace(message.Message) ?
                    History.GetLastFromInSession(AuthorRole.User)?.Message ?? string.Empty :
                    message.Message;

                var pluginmessage = await BuildPluginSystemInsertAsync(lastuserinput, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                // call the brain if there's no plugin interfering
                if (!string.IsNullOrEmpty(message.Message) && string.IsNullOrEmpty(pluginmessage))
                {
                    var actbot = Bot is GroupPersonaBase gp ? gp.GetCurrentPersona() : Bot;
                    actbot ??= Bot;
                    await actbot.Brain.HandleMessages(message).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                }

                var genparams = await GenerateFullPrompt(message, pluginmessage).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                pb.LastQuery = (message.Role != AuthorRole.User) ? genparams : null;

                ResetStreamingState();
                EmitThinkPrefill(message.Role == AuthorRole.Assistant);

                if (!string.IsNullOrEmpty(message.Message))
                {
                    Bot.History.LogMessage(message);
                    messageLogged = true;
                }

                RaiseOnFullPromptReady(pb.PromptToText());
                await StreamWireAsync(genparams, ct).ConfigureAwait(false);
            }
            finally
            {
                // The message was sent: it has to be in the history (and therefore on screen) even if the turn was
                // cancelled or failed before the normal logging point. Only the bot's response gets discarded.
                if (!messageLogged && !string.IsNullOrEmpty(message.Message))
                    Bot.History.LogMessage(message);
            }
        }

        #endregion

    }
}
