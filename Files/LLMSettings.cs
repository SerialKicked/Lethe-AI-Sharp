using HNSW.Net;
using LetheAISharp.Agent.Research;
using LetheAISharp.API;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using LetheAISharp.SearchAPI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Files
{
    public enum GroupChatPastSessionMode
    {
        None,
        ActiveOnly,
        All,
    }

    public enum MemoryMode
    {
        Normal, RPOnly, NonRPOnly
    }

    /// <summary>
    /// The main settings for LLMEngine, the backends, and the various modules.
    /// </summary>
    public class LLMSettings : BaseFile
    {

        /// <summary>
        /// Agentic and Brain data will be saved to this folder.
        /// It will use "{BasePersona.UniqueName}" filename with ".brain" and ".agent" extensions.
        /// </summary>
        public string DataPath { get; set; } = "data/chars/";


        #region *** Backend Connection ***

        /// <summary> URL of the backend server (KoboldAPI, OpenAI) or location of GGUF (LLamaSharp) </summary>
        public string BackendUrl { get; set; } = "http://localhost:5001";

        /// <summary> API of the backend server, KoboldAPI (text completion), OpenAI (chat completion), LLamaCpp (both) are handled </summary>
        [Description("The backend API to use for LLM interactions.\n" +
            "- Llama.cpp has the most complete feature set and best support (supports both chat and text completion).\n" +
            "- KoboldAPI is a good choice for text completion\n" +
            "- Generic OpenAI API support for chat completion.\n" +
            "- LLamaSharp is the integrated llama.cpp-based text completion backend.")]        
        public BackendAPI BackendAPI { get; set; } = BackendAPI.KoboldAPI;

        /// <summary>
        /// When set to true, if the backend supports it, tool calls will be made in parallel instead of sequentially. 
        /// This can speed up the generation when multiple tool calls are made, but can cause issues with some backends. 
        /// Depending on the backend and model you use, you might want to experiment with this setting to see if it improves performance or causes issues.
        /// null = auto (will auto detect on llama.cpp), true = force parallel, false = force sequential.
        /// </summary>
        [Description("When enabled, if the backend supports it, tool calls will be made in parallel instead of sequentially.\n" +
            "This can speed up generation when multiple tool calls are made, but may cause issues with some backends.")]
        public bool? BackendParallelToolCalls { get; set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether chat message prefill is allowed in the backend. 
        /// Prefill is mostly used to tell the LLM who is supposed to talk in group mode, or to make sure that the "think" tag is properly 
        /// used by smaller models. Set to false if your backend/model generates an error when writing a message.
        /// null = auto-detect based on the backend and template used, true = allow prefill, false = disallow prefill.
        /// </summary>
        [Description("Indicates whether chat message prefill is allowed in the backend. Prefill is used to specify the speaker in group mode, to ensure proper 'think' tag, or to biase thinking.\n" +
            "Set to false if your backend/model generates errors when prefill is used. In general text completion supports prefill, chat completion rarely does.")]
        public bool? BackendChatAllowPrefill { get; set; } = null;

        /// <summary>
        /// If set to true, the backend will be responsible for handling the BoS token and formatting the prompt accordingly.
        /// Otherwise, this library will handle the BoS token based on the instruction template being used.
        /// </summary>
        /// <remarks> This is only relevant for text completion mode. </remarks>
        [Description("Indicates whether the backend handles the BoS token itself.\n" +
            "If true, the backend is responsible for managing the BoS token and formatting the prompt accordingly. If false, this library will handle the BoS token based on the instruction template used.\n" +
            "Only relevant for text completion mode.")]
        public bool BackendHandlesBoSToken { get; set; } = true;

        /// <summary> 
        /// API key for OpenAI (depends on the backend, defaults 123 works when no key is required) 
        /// </summary>
        [Description("API key for OpenAI. This is only required if you're using a remote OpenAI server as your backend.")]
        public string OpenAIKey { get; set; } = "123";

        /// <summary>
        /// Completion type to use by default when sending a prompt to the backend. This is only for backends that support both text and chat 
        /// completion (only llama.cpp at the moment). Set to null to use the backend's default (chat completion for llama.cpp and OpenAI, 
        /// Text completion for Kobold and LlamaSharp).
        /// </summary>
        /// <remarks> If the backend doesn't have access to this completion type, it'll just use its default. </remarks>
        [Description("Completion type to use by default when communicating with the LLM. Text completion doesn't have access to tool calls or image support, but it allows prefill and a more lax formatting.")]
        public CompletionType? DefaultCompletionType { get; set; } = null;

        /// <summary> LlamaSharp: GPU layer count (255 = all) </summary>
        [Description("LlamaSharp-specific setting: number of layers to put in VRAM. Set to 255 to attempt to put everything.")]
        public int LlamaSharpGPULayers { get; set; } = 255;

        /// <summary> LlamaSharp: use flash attention or not </summary>
        [Description("LlamaSharp-specific setting: whether to use flash attention. Flash attention improve performance on most models.")]
        public bool LlamaSharpFlashAttention { get; set; } = true;

        /// <summary> LlamaSharp: set to true to disable KV cache offloading to GPU (slower / less VRAM) </summary>
        [Description("LlamaSharp-specific setting: whether to disable KV cache offloading to GPU. Disabling offloading can reduce VRAM usage but impact performance.")]
        public bool LlamaSharpNoKVoffload { get; set; } = false;

        #endregion


        #region *** General Settings ***

        [Description("Minimum time of user inactivity before the background agent is allowed to execute tasks. Default is 30 minutes.")]
        public TimeSpan BackgroundAgentMinInactivityTime { get; set; } = new TimeSpan(0, 30, 0);

        /// <summary>
        /// Force the use of internal grammar rule generator, even if backend supports it.
        /// </summary>
        [Description("Force the use of the internal structured output rule generator, even if the backend supports grammar rules.\n" +
            "This can be useful for testing or debugging purpose.")]
        public bool ForceInternalGrammar { get; set; } = false;

        /// <summary> Overrides the scenario field of the currently loaded character </summary>
        [Description("Overrides the scenario field of the currently loaded persona.\n" +
            "This can be useful to adapt the character to a specific scenario without having to create a new character for each scenario.")]
        public string ScenarioOverride { get; set; } = string.Empty;

        /// <summary> Should we stop the generation after the first paragraph? </summary>
        [Description("Should we stop the generation after the first paragraph?")]
        public bool StopGenerationOnFirstParagraph { get; set; } = false;

        /// <summary> Thinking models only, attempt to disable the thinking block </summary>
        [Description("Disable thinking block for models that support it.\n" +
            "Also useful for non-thinking models, so they can share the same instruction template with their thinking counter-part.")]
        public bool DisableThinking { get; set; } = false;

        /// <summary> 
        /// Move all RAG, WorldInfo, and Brain entries to the system prompt independantly of their respective settings. 
        /// Some models perform better with such info in the system prompt, while others prefer it in the main dialog.
        /// </summary>
        [Description("Move all RAG, WorldInfo, and Brain entries to the system prompt independently of their respective settings.\n" +
            "Some models (generally modern ones) perform better with such info in the system prompt, while others prefer it in the main dialog.")]
        public bool MoveAllInsertsToSysPrompt { get; set; } = false;

        /// <summary>
        /// If set to true (default) and the active chat session is not the lastest, date, mood and memories (with Natural insert policy)
        /// will not be inserted in the prompt. This is useful when continuing old chat sessions where this information could be irrelevant or
        /// even contradictory.
        /// </summary>
        [Description("If enabled (default) and the active chat session is not the latest, date, mood, and memories with Natural insert policy will not be included in the prompt.\n" +
            "This helps maintain relevance and consistency when revisiting older chat sessions where such information may be outdated or contradictory.")]
        public bool DisableDateAndMoodIfNotLastSession { get; set; } = true;

        /// <summary>
        /// If set to true, the names of the personas and user will be added in message's content (like "Bob: Hello!").
        /// </summary>
        [Description("If enabled, the names of the personas and user will be included in the message content (e.g., 'Bob: Hello!').\n" +
            "This can help clarify who is speaking, especially in group chats or when using models that don't handle role alternation well.")]
        public bool AddNamesToPrompt { get; set; } = false;

        #endregion


        #region *** Tool Calling Settings ***

        /// <summary>
        /// Gets or sets a value indicating whether tool calls are allowed or not (tool calls only work in streaming mode). 
        /// When set to true, the agent can call registered tools during generation, allowing for dynamic interactions and real-time data retrieval. 
        /// When set to false, tool calls are disabled, and the agent will not be able to utilize any tools during generation.
        /// </summary>
        [Description("Global On/Off switch indicates whether tool calls are allowed during generation.\n" +
            "When enabled, the agent can call registered tools for dynamic interactions and real-time data retrieval.\n" +
            "When disabled, tool calls are not permitted, and the agent will not utilize any tools during generation.\n\n" +
            "Tool calls are only available in chat completion.")]
        public bool ToolCallsAllowed { get; set; } = true;

        /// <summary>
        /// Maximum number of tool calls rounds the agent can perform during a single generation. 
        /// This is a safeguard to prevent infinite loops with tool calling. Depending on the complexity of the task and the tools available, you might want to adjust this number.
        /// </summary>
        [Description("Maximum number of tool call rounds the agent can perform during a single generation.\n" +
            "This serves as a safeguard against infinite loops in tool calling. Depending on the complexity of the task and the tools available, you may want to adjust this number.")]
        public int ToolCallLimit { get; set; } = 10;

        /// <summary>
        /// Limit the number of tool chains (call + results) to keep in the prompt to save tokens. Should be equal or higher than ToolCallLimit.
        /// This reduces the context length and improve performance when many tool calls are made. Set to 0 to keep all tool calls in prompt.
        /// </summary>
        [Description("Limit the number of tool chains (call + results) to keep in the prompt to save tokens. Should be equal or higher than ToolCallLimit.\n" +
            "This reduces the context length and improves performance when many tool calls are made. Set to 0 to keep all tool calls in the prompt.")]
        public int ToolCallChainLimit { get; set; } = 15;

        /// <summary>
        /// List of the toolsets that are allowed to be called by the agent. The toolsets must be registered in the ToolManager with their respective ID.
        /// </summary>
        [Description("List of the toolsets that are allowed to be called by the agent. The toolsets must be registered in the ToolManager with their respective ID.\n" +
            "This allows you to control which tools the agent can use during generation, which can be useful for safety or to guide the agent's behavior.")]
        public HashSet<string> AllowedToolsets { get; set; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether all tool calls require manual confirmation before execution independantly of what the toolsets say.
        /// </summary>
        /// <remarks>Set this property to <see langword="true"/> to enforce manual confirmation for every tool call, regardless of other settings. 
        /// This can be used to increase safety or oversight in environments where automated execution is not permitted.</remarks>
        [Description("When enabled, all tool calls will require manual confirmation before execution, regardless of the toolsets' individual settings.\n" +
            "This can be used to enhance safety and oversight in environments where automated execution is not allowed.")]
        public bool ToolCallsAlwaysManualConfirm { get; set; } = false;

        [Description("When enabled, add a note to the system prompt to give directives about tool calls. The directives help the model use those tools more autonomously.")]
        public bool ToolCallsAddSystemPromptNote { get; set; } = true;

        #endregion


        #region *** Model Settings ***

        /// <summary> Max context length for the model. </summary>
        [Description("Max context length for the model. This should be set according to the model you are using, and it will be used to calculate how many tokens can be used for the prompt and the reply.")]
        public int MaxTotalTokens { get; set; } = 16384;

        /// <summary> Max length for the bot's reply. </summary>
        [Description("Max length for the bot's reply. This setting determines the maximum number of tokens the model can generate in a single response.")]
        public int MaxReplyLength { get; set; } = 512;

        /// <summary> Image embedding size (depends on the embedding model, but 768 is the most common one) </summary>
        [Description("Image embedding size (depends on the embedding model, but 768 is the most common one)\n" +
            "This is the number of tokens used by each image being sent to the model. Important for accurate calculations.\n" +
            "Image support is only available on specific models and in chat completion mode.")]
        public int ImageEmbeddingSize { get; set; } = 768;

        /// <summary> Images larger than this resolution will be resized (depends on the model, but 1024 is, by far, the most common one) </summary>
        [Description("Images larger than this resolution will be resized (depends on the model, but 1024 is, by far, the most common one)")]
        public int ImageResolution { get; set; } = 1024;

        /// <summary> Maximum number of images to be sent in the prompt, this will save tokens when sending many images. Set to 0 for no limit. </summary>
        [Description("Maximum number of images to be kept in the active chat. This helps save tokens when sending many images. Set to 0 for no limit.")]
        public int MaxImageCount { get; set; } = 4;

        #endregion


        #region *** Memory Systems ***

        /// <summary> 
        /// If set to true, summaries of previous chat sessions will be insereted in the system prompt to provide extended context.
        /// </summary>
        [Description("When enabled, summaries of previous chat sessions will be inserted into the system prompt to provide extended context.\n" +
            "This allows the model to have access to information from past interactions, which improves continuity and relevance in conversations.")]
        public bool SessionMemorySystem { get; set; } = false;

        /// <summary> Should the chatlog contains only the latest/current chat session or as much dialog as we can fit in? </summary>
        [Description("Determines how much of the chat history is included in the prompt.\n" +
            "- 'FitAll' will include as much of the conversation history as possible within the token limits.\n" +
            "- 'LatestSessionOnly' will include only the most recent session.\n\n" +
            "Choosing 'FitAll' allows for richer context and continuity across sessions, but may lead to longer prompts and increased token usage. While \n" +
            "'LatestSessionOnly' helps to keep prompts concise and focused on the current session, which can be beneficial for performance and relevance.")]
        public SessionHandling SessionHandling { get; set; } = SessionHandling.FitAll;

        /// <summary>
        /// Allows to specify if the session memory system should include only RP sessions, only non-RP sessions, or all sessions. 
        /// This is useful to avoid mixing RP and non-RP sessions depending on context. Can also simplify when you want to resume a RP after
        /// several non-RP sessions, or the opposite.
        /// </summary>
        public MemoryMode RecallMemoryMode { get; set; } = MemoryMode.Normal;

        /// <summary> Reserved token space for summaries of previous sessions </summary>
        [Description("This setting allocates a specific number of tokens for including summaries of past interactions in the prompt,\n" +
            "ensuring that there is sufficient room for this contextual information while managing overall token limits.")]
        public int SessionReservedTokens { get; set; } = 2048;

        /// <summary>
        /// For long chat sessions, cut the summary in the middle instead of at the end for summary purpose. Both approaches have pros and cons.
        /// </summary>
        [Description("When the system summarize the session for the memory system, this setting determines how to handle chat sessions that are longer than then context size.\n" +
            "Cutting in the middle provide the more balanced overview, capturing both the beginning and the end, which is useful for understanding the flow of the conversation.\n" +
            "However, it may miss important details in the middle. Cutting at the end ensures that the most recent and potentially relevant information is included, but it may\n" +
            "omit important context from earlier in the conversation.")]
        public bool CutInTheMiddleSummaryStrategy = false;

        /// <summary>
        /// Format the memory entries generated by the Brain and Task to be inserted in a format that reduces hallucination.
        /// </summary>
        [Description("Format the memory entries generated by the Brain and Task to be inserted in a format that reduces hallucination.\n" +
            "When enabled, the system will attempt to format the memory entries in a way that is less likely to cause hallucinations when recalled by the model.\n" +
            "This require more tokens to store the same information.")]
        public bool AntiHallucinationMemoryFormat { get; set; } = true;

        /// <summary> 
        /// If false, the standard summary from the structured output will be used instead (generally more concise and faster to generate).
        /// If true, a second more detailed summary will be generated for session memory purpose. It provides more details about the session, but use more tokens and time to generate.
        /// </summary>
        [Description("When enabled, a more detailed summary will be generated for session memory purposes.\n" +
            "Memories of past session will be more detailed but use a lot more tokens, and the 'new session' process will be slower.")]
        public bool SessionDetailedSummary { get; set; } = false;

        /// <summary>
        /// List of memory types that are subject to decay and deletion if not recalled within a certain timeframe.
        /// </summary>
        [Description("List of memory types that are subject to decay and deletion if not recalled within a certain timeframe.\n" +
            "This helps manage the memory system by allowing certain types of memories to fade over time, ensuring that the most relevant\n" +
            "and frequently accessed information is retained while less relevant information is removed.")]
        public HashSet<MemoryType> DecayableMemories { get; set; } = [ MemoryType.WebSearch, MemoryType.Goal, MemoryType.Reminder ];

        /// <summary>
        /// Disable RAG usage for these memory types (might be useful if using a different system for some types).
        /// </summary>
        [Description("Disable RAG usage for these memory types (might be useful if using a different system for some types).\n" +
            "This allows you to exclude certain types of memories from being retrieved through RAG, which can be beneficial if your app has alternative\n" +
            "retrieval systems in place for those memory types or if you want to limit the scope of RAG retrieval.")]
        public HashSet<MemoryType> DisableRAG { get; set; } = [ MemoryType.File, MemoryType.Image ];

        /// <summary> 
        /// Allow keyword-activated snippets to be inserted in the prompt (see WorldInfo and BasePersona) 
        /// </summary>
        [Description("Allow keyword-activated contextual information to be inserted in the prompt (see WorldInfo / Lorebooks).\n" +
            "When enabled, the system will insert relevant information based on keywords found in the user input, providing dynamic context that can\n" +
            "enhance the model's understanding and response generation.")]
        public bool RAGKeywordEnabled { get; set; } = true;

        #endregion


        #region *** RAG Settings (retrieval of past information based on text embedding similarity) ***

        /// <summary> Toggle RAG functionalities on/off </summary>
        [Description("Toggle RAG functionalities on/off.\n" +
            "When enabled, the system will retrieve relevant information based on a variety of systems, including text embedding similarity,\n" +
            "allowing the model to access past interactions and contextual data that may not be explicitly included in the prompt.")]
        public bool RAGEnabled { get; set; } = true;

        /// <summary> 
        /// Path to embeddding model. RAG functionalities won't be available if this file is not present. 
        /// The model must be in the GGUF format. Default can be downloaded here:
        /// https://huggingface.co/ChristianAzinn/gte-large-gguf
        /// </summary>
        [Description("Path to the embedding model in GGUF format. RAG functionalities won't be available if this file is not present.\n" +
            "The default model can be downloaded from: https://huggingface.co/ChristianAzinn/gte-large-gguf\n" +
            "Make sure to choose a model with an embedding size that matches the RAGEmbeddingSize setting.")]
        public string RAGModelPath { get; set; } = "data/classifiers/gte-large.Q6_K.gguf";

        /// <summary> 
        /// Thinking models only, will move all RAG and WI to the thinking block. This is highly experimental. 
        /// </summary>
        [Description("Thinking models only, will move all RAG and WorldInfo to the thinking block. This is highly experimental.")]
        public bool RAGMoveToThinkBlock { get; set; } = false;

        /// <summary>
        /// Converts user sentences to 3rd person when performing RAG searches. English Only.
        /// This usually improves the relevance of the retrieved entries, especially chat sessions.
        /// </summary>
        [Description("Converts user sentences to 3rd person when performing RAG searches. English Only.\n" +
            "This usually improves the relevance of the retrieved entries, especially chat sessions, by aligning the perspective of the query with the perspective of the stored information.")]
        public bool RAGConvertTo3rdPerson { get; set; } = true;

        /// <summary> Maximum number of entries to be retrieved with RAG </summary>
        [Description("Maximum number of entries to be retrieved with embedding distance RAG.\n" +
            "This limits the number of relevant past interactions and contextual data that be inserted into the prompt at the same time,\n" +
            "helping to manage token usage and maintain focus on the most pertinent information.")]
        public int RAGMaxEntries { get; set; } = 3;

        /// <summary> Maximum number of entries to be retrieved from RAG keyword searches </summary>
        [Description("Maximum number of keyword activated entries to be retrieved from RAG keyword searches.\n" +
            "This limits the amount of keyword-activated contextual information that can be included in the prompt, ensuring that only the most relevant\n" +
            "information is provided to the model based on the user's input.")]
        public int RAGKeywordMaxEntries { get; set; } = 3;

        /// <summary> Index at which RAG entries will be inserted in the chatlog. -1 to insert in system prompt. </summary>
        [Description("Index at which RAG entries will be inserted in the chatlog. Set to -1 to insert in the system prompt.\n" +
            "This determines where the retrieved information from RAG will be placed in the conversation history, allowing for better integration of\n" +
            "relevant past interactions and contextual data into the ongoing dialogue.")]
        public int RAGIndex { get; set; } = 3;

        /// <summary> Embedding size (depends on the embedding model) </summary>
        [Description("Embedding size (depends on the embedding model).\n" +
            "This setting should match the embedding size of the model specified in RAGModelPath to ensure proper functioning of the RAG system.")]
        public int RAGEmbeddingSize { get; set; } = 1024;

        /// <summary> M Value for the Vector Search (SmallWorld / HNSW.NET implementation) </summary>
        [Description("M Value for the Vector Search (SmallWorld / HNSW.NET implementation).\n" +
            "This parameter controls the trade-off between search accuracy and speed in the vector search algorithm.")]
        public int RAGMValue { get; set; } = 15;

        /// <summary> Max distance for an entry to be retrieved (SmallWorld / HNSW.NET implementation) </summary>
        [Description("Max distance for an entry to be retrieved (SmallWorld / HNSW.NET implementation).\n" +
            "This setting determines the maximum allowable distance between the query and the entries in the vector space for them to be considered relevant.")]
        public float RAGDistanceCutOff { get; set; } = 0.1f;

        /// <summary> Search method. Simple is the most accurate method (but is very slightly slower). </summary>
        [Description("Search method for RAG retrieval:\n" +
            "- 'Simple:' Faster retrieval with a potential trade-off in accuracy.\n" +
            "- 'Heuristic:' It uses a more sophisticated approach to select neighbors, which can improve relevance with older character with a lot of data.\n" +
            "- 'Exact:' calculates the exact distance for all entries, Best accuracy but a lot slower, especially with larger datasets.")]
        public RAGSelectionHeuristic RAGHeuristic { get; set; } = RAGSelectionHeuristic.SelectSimple;

        #endregion


        #region *** User Fact Extraction Settings ***

        /// <summary>
        /// Enables the extracted-facts retrieval layer.
        /// When enabled, short extracted facts are used as a semantic index: user input is compared against
        /// fact embeddings, and matching facts pull in their source MemoryUnits directly by GUID,
        /// bypassing the embedding distance check that can miss multi-topic session summaries.
        /// </summary>
        [Description("Enables the extracted-facts retrieval layer.\n\n" +
            "When enabled, after each session, the persona will build a list of facts it learned about the user (like a bio it's building). \n" +
            "Short extracted facts are also used as a semantic index: user input is compared against fact embeddings, and matching facts pull \n" +
            "in their source memory, bypassing the embedding distance check that can miss multi-topic session summaries.")]
        public bool FactRetrievalEnabled { get; set; } = true;

        /// <summary>
        /// Determines if facts about the user can be retrieved for sessions that are identified as roleplay.
        /// With personas used for both roleplay and non-roleplay sessions, setting this to false ensures that only non-roleplay sessions contribute to the user facts, which can help maintain a more accurate and relevant fact base for real-life information about the user, while avoiding potential confusion from roleplay scenarios. 
        /// If your use case involves exclusively roleplay or exclusively non-roleplay sessions, you can set this to true to maximize the number of facts recorded.
        /// </summary>
        /// <remarks> This probably should become a persona-based setting in the future. </remarks>
        [Description("Determines if facts about the user can be retrieved for sessions that are identified as roleplay. \n" +
            "With personas used for both roleplay and non-roleplay sessions, setting this to false ensures that only non-roleplay sessions contribute \n" +
            "to the user facts, which can help maintain a more accurate and relevant fact base for real-life information about the user, while \n" +
            "avoiding potential confusion from roleplay scenarios. If your use case involves exclusively roleplay or exclusively non-roleplay sessions, \n" +
            "you can set this to true to maximize the number of facts recorded.")]
        public bool RecordFactsDuringRoleplay { get; set; } = false;

        /// <summary>
        /// Token budget reserved for the core facts section of the system prompt.
        /// The top-ranked facts (by importance score) that fit within this budget are included.
        /// Set to 0 to disable system prompt inclusion of facts (retrieval still works).
        /// </summary>
        [Description("Token budget reserved for the core facts section of the system prompt.\n" +
            "The top-ranked facts (by importance score) that fit within this budget are included.\n" +
            "Set to 0 to disable system prompt inclusion of facts (retrieval still works).")]
        public int CoreFactsTokenBudget { get; set; } = 512;

        /// <summary>
        /// Cosine distance threshold for fact deduplication.
        /// If a new fact's embedding is within this distance of an existing fact, it is treated as the same fact:
        /// LastSeen and ReferenceCount are updated and the new session GUID is added to SourceMemories.
        /// Cosine distance scale: 0 = identical vectors, 1 = orthogonal, 2 = opposite.
        /// Lower = stricter deduplication. Recommended range: 0.05–0.08.
        /// </summary>
        [Description("Cosine distance threshold for fact deduplication.\n" +
            "If a new fact's embedding is within this distance of an existing fact, it is treated as the same fact: \n" +
            "LastSeen and ReferenceCount are updated and the new session GUID is added to SourceMemories. \n" +
            "Lower = stricter deduplication. Recommended range: 0.05–0.08.")]
        public float FactDeduplicationThreshold { get; set; } = 0.05f;

        /// <summary>
        /// Cosine distance threshold for fact retrieval via user input similarity.
        /// Facts within this distance of the embedded user input trigger source memory retrieval.
        /// Cosine distance scale: 0 = identical vectors, 1 = orthogonal, 2 = opposite.
        /// Higher = more permissive retrieval. Recommended range: 0.10–0.15.
        /// </summary>
        [Description("Cosine distance threshold for fact retrieval via user input similarity.\n" +
            "Facts within this distance of the embedded user input trigger source memory retrieval.\n" +
            "Higher = more permissive retrieval. Recommended range: 0.10–0.15.")]
        public float FactRetrievalThreshold { get; set; } = 0.10f;

        /// <summary>
        /// Cosine distance threshold for fact supersession.
        /// If a new fact's embedding falls between FactDeduplicationThreshold and this value,
        /// the existing fact is marked as superseded and the new fact carries forward its SourceMemories.
        /// This handles cases where a related but meaningfully different fact replaces an older one
        /// (e.g., "User is a nurse" → "User is a teacher").
        /// Cosine distance scale: 0 = identical vectors, 1 = orthogonal, 2 = opposite.
        /// Recommended range: 0.075–0.1.
        /// </summary>
        [Description("Cosine distance threshold for fact supersession.\n" +
            "If a new fact's embedding falls between FactDeduplicationThreshold and this value, the existing fact is marked as superseded and the new fact carries forward its SourceMemories.\n" +
            "This handles cases where a related but meaningfully different fact replaces an older one (e.g., 'User is a nurse' → 'User is a teacher').\n" +
            "Recommended range: 0.075–0.1.")]
        public float FactSupersessionThreshold { get; set; } = 0.075f;

        #endregion

        #region *** WebSearch API Settings ***

        /// <summary>
        /// 2 API are available:
        /// - Brave API (requires manual registration, and an API key on their website). Provides detailed search results.
        /// - DuckDuckGo: no registration required, free to use. Behaves differently depending on Backend: On OpenAI API, it'll provides only basic AI generated summary for the query. On KoboldAPI, if KoboldCpp is configured correctly, it'll provides very detailed search results.
        /// </summary>
        [Description("Web search API to use for web search tool calls. Two APIs are available:\n" +
            "- Brave API: Requires manual registration and an API key on their website. Provides detailed search results.\n" +
            "- DuckDuckGo: No registration required, free to use. Behaves differently depending on the backend: \n" +
            "On OpenAI API, it provides only a basic AI-generated summary for the query. On KoboldAPI, if KoboldCpp is configured correctly, it provides very detailed search results.")]
        public BackendSearchAPI WebSearchAPI { get; set; } = BackendSearchAPI.DuckDuckGo;

        /// <summary> If using the Brave API, you API key should go there </summary>
        [Description("If using the Brave API for web search tool calls, enter your API key here. This key is required to authenticate your requests to the Brave API and access its detailed search results.")]
        public string WebSearchBraveAPIKey { get; set; } = string.Empty;

        /// <summary> Attempt to scrape the most relevant search results for their full content. </summary>
        [Description("When enabled, the system will attempt to scrape the most relevant search results for their full content, providing richer information for the model to utilize in its responses.\n" +
            "This enhance the quality of the information retrieved from web searches, but will increase response time by a lot.")]
        public bool WebSearchDetailedResults { get; set; } = true;

        /// <summary>
        /// Limits the length of the extracted content from web search results. To prevent making the context too long.
        /// 0 to disable.
        /// </summary>
        [Description("Limits the length of the extracted content from web search results to prevent making the context too long.\n" +
            "Set to 0 to disable this limit.")]
        public int WebSearchDetailedMaxLength { get; set; } = 5000;

        /// <summary>
        /// Maximum number of url to retrieve and potentially scrape for content. 
        /// Setting this to a higher number can provide more information but will increase response time and token usage, especially if WebSearchDetailedResults is enabled.
        /// </summary>
        [Description("Maximum number of URLs to retrieve and potentially scrape for content.\n" +
            "Setting this to a higher number can provide more information but will increase response time and token usage, especially if WebSearchDetailedResults is enabled.")]
        public int WebSearchResultsPerQuery { get; set; } = 3;

        public DeepResearchOptions DeepResearch { get; set; } = new();

        #endregion


        #region *** Group Chat Settings ***

        /// <summary>
        /// Should secondary personas in group chats be able to see summaries of past chat sessions?
        /// All = they see all past sessions.
        /// ActiveOnly = they see only past sessions where they were active.
        /// None = they don't see any past sessions.
        /// </summary>
        /// <remarks> only relevant when SessionMemorySystem is enabled </remarks>
        [Description("Determines whether secondary personas in group chats can see summaries of past chat sessions:\n" +
            "- 'All': They see all past sessions.\n" +
            "- 'ActiveOnly': They see only past sessions where they were active.\n" +
            "- 'None': They don't see any past sessions.\n\n" +
            "This setting is relevant when the Session Memory System is enabled, as it controls the visibility of past session summaries for secondary personas in group chat scenarios.")]
        public GroupChatPastSessionMode GroupSecondaryPersonaSeePastSessions { get; set; } = GroupChatPastSessionMode.All;

        /// <summary>
        /// If set to true, the Group Chat messages will alternate between user and bot role for each persona independantly of who sent the message.
        /// This is useful when using models that rely on role alternation for proper functioning.
        /// </summary>
        [Description("When enabled, group chat messages will alternate between user and bot roles for each persona independently of who sent the message.\n" +
            "This is useful when using models that rely on role alternation for proper functioning.")]
        public bool GroupInstructFormatAdapter { get; set; } = true;

        /// <summary>
        /// Indicates whether group session data should be committed to the secondary personas' history when ending or starting a new chat session.
        /// By default, only the main persona's history will be updated (false). If you want group chat activities to be added to the secondary personas's
        /// memory, set this to true. In that case they'll remember group activity, even when you're using them outside of the group.
        /// </summary>
        [Description("Indicates whether group session data should be committed to the secondary personas' history when ending or starting a new chat session.\n" +
            "By default, only the main persona's history will be updated (false). If you want group chat activities to be added to the secondary personas' memory, set this to true.\n")]
        public bool CommitGroupSessionToSecondaryPersonaHistory { get; set; } = false;
        [Description("For thinking models, indicates whether the thinking block should be used to insert who the model is supposed to impersonate.\n" +
            "This only work when prefill is available (text completion mode, generally) and if the selected instruction format is setup for it.")]
        public bool GroupChatInfoThinkingBlock { get; set; } = true;

        #endregion

    }
}
