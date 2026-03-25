using LetheAISharp.LLM;
using LetheAISharp.SearchAPI;
using HNSW.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LetheAISharp.Memory;
using LetheAISharp.API;

namespace LetheAISharp.Files
{
    public enum GroupChatPastSessionMode
    {
        None,
        ActiveOnly,
        All,
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

        /// <summary> API of the backend server, KoboldAPI (text completion) and OpenAI (chat completion) are both handled </summary>
        public BackendAPI BackendAPI { get; set; } = BackendAPI.KoboldAPI;

        /// <summary>
        /// On chat completion backends with thinking models (and on some jinja templates only), the initial "think" tag may not be streamed at all, 
        /// it's just silent and assumed (as it's prefilled internally). 
        /// This setting is used to recognize the behavior so LetheAI's output stays consistant.
        /// Set null to auto-detect based on the backend and template used. Toggle true/false if you notice issues with the think tags' behavior 
        /// (for example, if the thinking block is not properly recognized or if the "think" tag appears in the output when it shouldn't).
        /// </summary>
        public BackendChatCompletionThinkTagBehavior? BackendStartThinkTagBehavior { get; set; } = null;

        /// <summary>
        /// When set to true, if the backend supports it, tool calls will be made in parallel instead of sequentially. 
        /// This can speed up the generation when multiple tool calls are made, but can cause issues with some backends. 
        /// Depending on the backend and model you use, you might want to experiment with this setting to see if it improves performance or causes issues.
        /// null = auto (will auto detect on llama.cpp), true = force parallel, false = force sequential.
        /// </summary>
        public bool? BackendParallelToolCalls { get; set; } = null;

        /// <summary>
        /// Gets or sets a value indicating whether chat message prefill is allowed in the backend. 
        /// Prefill is mostly used to tell the LLM who is supposed to talk in group mode, or to make sure that the "think" tag is properly 
        /// used by smaller models. Set to false if your backend/model generates an error when writing a message.
        /// null = auto-detect based on the backend and template used, true = allow prefill, false = disallow prefill.
        /// </summary>
        public bool? BackendChatAllowPrefill { get; set; } = null;

        /// <summary>
        /// Set to true only if llama-server was launched with the "--props" option and you want to allow all the extended samplers available in llama.cpp
        /// If false, only the default OpenAI samplers can be used.
        /// </summary>
        public bool BackendLLamaCppAllowAllSamplers { get; set; } = false;


        /// <summary> API key for OpenAI (depends on the backend) </summary>
        public string OpenAIKey { get; set; } = "123";

        /// <summary> LlamaSharp: GPU layer count (255 = all) </summary>
        public int LlamaSharpGPULayers { get; set; } = 255;

        /// <summary> LlamaSharp: use flash attention or not </summary>
        public bool LlamaSharpFlashAttention { get; set; } = true;

        /// <summary> LlamaSharp: set to true to disable KV cache offloading to GPU (slower / less VRAM) </summary>
        public bool LlamaSharpNoKVoffload { get; set; } = false;

        /// <summary>
        /// Force the use of internal grammar rule generator, even if backend supports it.
        /// </summary>
        public bool ForceInternalGrammar { get; set; } = false;

        #endregion


        #region *** Tool Calling Settings ***

        /// <summary>
        /// Gets or sets a value indicating whether tool calls are allowed or not (tool calls only work in streaming mode). 
        /// When set to true, the agent can call registered tools during generation, allowing for dynamic interactions and real-time data retrieval. 
        /// When set to false, tool calls are disabled, and the agent will not be able to utilize any tools during generation.
        /// </summary>
        public bool ToolCallsAllowed { get; set; } = true;

        /// <summary>
        /// Maximum number of tool calls rounds the agent can perform during a single generation. 
        /// This is a safeguard to prevent infinite loops with tool calling. Depending on the complexity of the task and the tools available, you might want to adjust this number.
        /// </summary>
        public int ToolCallLimit { get; set; } = 10;

        /// <summary>
        /// TODO: Keep the last "ToolCallMemoryLimit" tool calls and results only, instead of all the tool calls performed during the generation. 
        /// This can help reduce the context length and improve performance when many tool calls are made. Set to 0 to keep all tool calls in memory.
        /// </summary>
        public int ToolCallMemoryLimit { get; set; } = 15;

        /// <summary>
        /// List of the toolsets that are allowed to be called by the agent. The toolsets must be registered in the ToolManager with their respective ID.
        /// </summary>
        public HashSet<string> AllowedToolsets { get; set; } = [];

        /// <summary>
        /// Gets or sets a value indicating whether all tool calls require manual confirmation before execution independantly of what the toolsets say.
        /// </summary>
        /// <remarks>Set this property to <see langword="true"/> to enforce manual confirmation for every tool call, regardless of other settings. 
        /// This can be used to increase safety or oversight in environments where automated execution is not permitted.</remarks>
        public bool ToolCallsAlwaysManualConfirm { get; set; } = false;


        #endregion


        #region *** Model Settings ***

        /// <summary> Max context length for the model. </summary>
        public int MaxTotalTokens { get; set; } = 16384;

        /// <summary> Max length for the bot's reply. </summary>
        public int MaxReplyLength { get; set; } = 512;

        /// <summary> Image embedding size (depends on the embedding model, but 768 is the most common one) </summary>
        public int ImageEmbeddingSize { get; set; } = 768;

        /// <summary> Maximum number of images to be sent in the prompt, this will save tokens when sending many images. Set to 0 for no limit. </summary>
        public int MaxImageCount { get; set; } = 4;

        /// <summary> Overrides the scenario field of the currently loaded character </summary>
        public string ScenarioOverride { get; set; } = string.Empty;

        /// <summary> Should we stop the generation after the first paragraph? </summary>
        public bool StopGenerationOnFirstParagraph { get; set; } = false;

        /// <summary> Thinking models only, attempt to disable the thinking block </summary>
        public bool DisableThinking { get; set; } = false;

        /// <summary> Allow keyword-activated snippets to be inserted in the prompt (see WorldInfo and BasePersona) </summary>
        public bool AllowWorldInfo { get; set; } = true;

        /// <summary> 
        /// Move all RAG, WorldInfo, and Brain entries to the system prompt independantly of their respective settings. 
        /// Some models perform better with such info in the system prompt, while others prefer it in the main dialog.
        /// </summary>
        public bool MoveAllInsertsToSysPrompt { get; set; } = false;

        /// <summary>
        /// If set to true (default) and the active chat session is not the lastest, date, mood and memories (with Natural insert policy)
        /// will not be inserted in the prompt. This is useful when continuing old chat sessions where this information could be irrelevant or
        /// even contradictory.
        /// </summary>
        public bool DisableDateAndMoodIfNotLastSession { get; set; } = true;

        #endregion


        #region *** Long term memory system and summaries ***

        /// <summary> 
        /// If set to true, summaries of previous chat sessions will be insereted in the system prompt to provide extended context.
        /// </summary>
        public bool SessionMemorySystem { get; set; } = false;

        /// <summary> Should the chatlog contains only the latest/current chat session or as much dialog as we can fit in? </summary>
        public SessionHandling SessionHandling { get; set; } = SessionHandling.FitAll;

        /// <summary> Reserved token space for summaries of previous sessions </summary>
        public int SessionReservedTokens { get; set; } = 2048;

        /// <summary>
        /// For long chat sessions, cut the summary in the middle instead of at the end for summary purpose. Both approaches have pros and cons.
        /// </summary>
        public bool CutInTheMiddleSummaryStrategy = false;

        /// <summary>
        /// Format the memory entries generated by the Brain and Task to be inserted in a format that reduces hallucination.
        /// </summary>
        public bool AntiHallucinationMemoryFormat { get; set; } = true;

        /// <summary> 
        /// If false, the standard summary from the structured output will be used instead (generally more concise and faster to generate).
        /// If true, a second more detailed summary will be generated for session memory purpose. It provides more details about the session, but use more tokens and time to generate.
        /// </summary>
        public bool SessionDetailedSummary { get; set; } = false;

        #endregion


        #region *** Sentiment Analysis Module ***

        public bool SentimentEnabled { get; set; } = false;
        public string SentimentModelPath { get; set; } = "data/classifiers/emotion-bert-classifier.gguf";
        public string SentimentGoEmotionHeadPath { get; set; } = "data/classifiers/goemotions_head.json";
        public string SentimentThresholdsPath { get; set; } = "data/classifiers/optimized_thresholds.json";

        #endregion


        #region *** RAG Settings (retrieval of past information based on text embedding similarity) ***

        /// <summary> Toggle RAG functionalities on/off </summary>
        public bool RAGEnabled { get; set; } = true;

        /// <summary>
        /// Enables the extracted-facts retrieval layer.
        /// When enabled, short extracted facts are used as a semantic index: user input is compared against
        /// fact embeddings, and matching facts pull in their source MemoryUnits directly by GUID,
        /// bypassing the embedding distance check that can miss multi-topic session summaries.
        /// </summary>
        public bool FactRetrievalEnabled { get; set; } = true;

        /// <summary>
        /// Token budget reserved for the core facts section of the system prompt.
        /// The top-ranked facts (by importance score) that fit within this budget are included.
        /// Set to 0 to disable system prompt inclusion of facts (retrieval still works).
        /// </summary>
        public int CoreFactsTokenBudget { get; set; } = 512;

        /// <summary>
        /// Cosine distance threshold for fact deduplication.
        /// If a new fact's embedding is within this distance of an existing fact, it is treated as the same fact:
        /// LastSeen and ReferenceCount are updated and the new session GUID is added to SourceMemories.
        /// Cosine distance scale: 0 = identical vectors, 1 = orthogonal, 2 = opposite.
        /// Lower = stricter deduplication. Recommended range: 0.05–0.08.
        /// </summary>
        public float FactDeduplicationThreshold { get; set; } = 0.05f;

        /// <summary>
        /// Cosine distance threshold for fact retrieval via user input similarity.
        /// Facts within this distance of the embedded user input trigger source memory retrieval.
        /// Cosine distance scale: 0 = identical vectors, 1 = orthogonal, 2 = opposite.
        /// Higher = more permissive retrieval. Recommended range: 0.10–0.15.
        /// </summary>
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
        public float FactSupersessionThreshold { get; set; } = 0.075f;

        /// <summary> 
        /// Path to embeddding model. RAG functionalities won't be available if this file is not present. 
        /// The model must be in the GGUF format. Default can be downloaded here:
        /// https://huggingface.co/ChristianAzinn/gte-large-gguf
        /// </summary>
        public string RAGModelPath { get; set; } = "data/classifiers/gte-large.Q6_K.gguf";

        /// <summary> 
        /// Thinking models only, will move all RAG and WI to the thinking block. This is highly experimental. 
        /// </summary>
        public bool RAGMoveToThinkBlock { get; set; } = false;

        /// <summary>
        /// Converts user sentences to 3rd person when performing RAG searches. English Only.
        /// This usually improves the relevance of the retrieved entries, especially chat sessions.
        /// </summary>
        public bool RAGConvertTo3rdPerson { get; set; } = true;

        /// <summary> Maximum number of entries to be retrieved with RAG </summary>
        public int RAGMaxEntries { get; set; } = 3;

        /// <summary> Maximum number of entries to be retrieved from WorldInfo </summary>
        public int WorldInfoMaxEntries { get; set; } = 3;

        /// <summary> Index at which RAG entries will be inserted in the chatlog. -1 to insert in system prompt. </summary>
        public int RAGIndex { get; set; } = 3;

        /// <summary> Embedding size (depends on the embedding model) </summary>
        public int RAGEmbeddingSize { get; set; } = 1024;

        /// <summary> M Value for the Vector Search (SmallWorld / HNSW.NET implementation) </summary>
        public int RAGMValue { get; set; } = 15;

        /// <summary> Max distance for an entry to be retrieved (SmallWorld / HNSW.NET implementation) </summary>
        public float RAGDistanceCutOff { get; set; } = 0.1f;

        /// <summary> Search method. Simple is the most accurate method (but is very slightly slower). </summary>
        public RAGSelectionHeuristic RAGHeuristic { get; set; } = RAGSelectionHeuristic.SelectSimple;

        #endregion


        #region *** WebSearch API Settings ***

        /// <summary>
        /// 2 API are available:
        /// - Brave API (requires manual registration, and an API key on their website). Provides detailed search results.
        /// - DuckDuckGo: no registration required, free to use. Behaves differently depending on Backend: On OpenAI API, it'll provides only basic AI generated summary for the query. On KoboldAPI, if KoboldCpp is configured correctly, it'll provides very detailed search results.
        /// </summary>
        public BackendSearchAPI WebSearchAPI { get; set; } = BackendSearchAPI.DuckDuckGo;

        /// <summary> If using the Brave API, you API key should go there </summary>
        public string WebSearchBraveAPIKey { get; set; } = string.Empty;

        /// <summary> Attempt to scrape the most relevant search results for their full content. </summary>
        public bool WebSearchDetailedResults { get; set; } = true;

        /// <summary>
        /// Limits the length of the extracted content from web search results. To prevent making the context too long.
        /// 0 to disable.
        /// </summary>
        public int WebSearchDetailedMaxLength { get; set; } = 5000;

        #endregion


        #region *** Group Chat Settings ***

        /// <summary>
        /// Should secondary personas in group chats be able to see summaries of past chat sessions?
        /// All = they see all past sessions.
        /// ActiveOnly = they see only past sessions where they were active.
        /// None = they don't see any past sessions.
        /// </summary>
        /// <remarks> only relevant when SessionMemorySystem is enabled </remarks>
        public GroupChatPastSessionMode GroupSecondaryPersonaSeePastSessions { get; set; } = GroupChatPastSessionMode.All;

        /// <summary>
        /// If set to true, the Group Chat messages will alternate between user and bot role for each persona independantly of who sent the message.
        /// This is useful when using models that rely on role alternation for proper functioning.
        /// </summary>
        public bool GroupInstructFormatAdapter { get; set; } = false;

        /// <summary>
        /// Indicates whether group session data should be committed to the secondary personas' history when ending or starting a new chat session.
        /// By default, only the main persona's history will be updated (false). If you want group chat activities to be added to the secondary personas's
        /// memory, set this to true. In that case they'll remember group activity, even when you're using them outside of the group.
        /// </summary>
        public bool CommitGroupSessionToSecondaryPersonaHistory { get; set; } = false;

        #endregion

    }
}
