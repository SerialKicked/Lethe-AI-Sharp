using AIToolkit.LLM;
using HNSW.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Files
{
    public class LLMSettings : BaseFile
    {
        /// <summary> URL of the backend server (KoboldAPI, OpenAI, etc.) </summary>
        public string BackendUrl { get; set; } = "http://localhost:5001";

        /// <summary> API of the backend server, KoboldAPI (text completion) and OpenAI (chat completion) are both handled </summary>
        public BackendAPI BackendAPI { get; set; } = BackendAPI.KoboldAPI;

        /// <summary> API key for OpenAI (depends on the backend) </summary>
        public string OpenAIKey { get; set; } = "123";

        /// <summary> Reserved token space for summaries of previous sessions (0 to disable) </summary>
        public int ReservedSessionTokens { get; set; } = 2048;

        /// <summary> Max length for the bot's reply. </summary>
        public int MaxReplyLength { get; set; } = 512;

        /// <summary> Overrides the scenario field of the currently loaded character </summary>
        public string ScenarioOverride { get; set; } = string.Empty;

        /// <summary> Should the prompt format the memories and RAG entries into markdown. Some models like it better than others. </summary>
        public bool MarkdownMemoryFormating { get; set; } = false;

        /// <summary> Should we stop the generation after the first paragraph? </summary>
        public bool StopGenerationOnFirstParagraph { get; set; } = false;

        /// <summary> Thinking models only, attempt to disable the thinking block </summary>
        public bool DisableThinking { get; set; } = false;

        /// <summary> Allow keyword-activated snippets to be inserted in the prompt (see WorldInfo and BasePersona) </summary>
        public bool AllowWorldInfo { get; set; } = true;

        /// <summary> Should the prompt contains only the latest chat session or as much dialog as we can fit? </summary>
        public SessionHandling SessionHandling { get; set; } = SessionHandling.FitAll;

        /// <summary> Thinking models only, will move all RAG and WI to the thinking block </summary>
        public bool RAGMoveToThinkBlock { get; set; } = false;

        /// <summary> Move all RAG entries (and WI entries) to the system prompt </summary>
        public bool RAGMoveToSysPrompt { get; set; } = false;

        /// <summary> Maximum number of entries to be retrieved with RAG </summary>
        public int RAGMaxEntries { get; set; } = 3;

        /// <summary> Index at which RAG entries will be inserted </summary>
        public int RAGIndex { get; set; } = 3;

        /// <summary> Embedding size (depends on the embedding model) </summary>
        public int RAGEmbeddingSize { get; set; } = 1024;

        /// <summary> Use summaries' embeddings for RAG </summary>
        public bool RAGUseSummaries { get; set; } = true;

        /// <summary> Use titles embeddings for RAG </summary>
        public bool RAGUseTitles { get; set; } = true;

        /// <summary> M Value for the Vector Search (SmallWorld / HNSW.NET implementation) </summary>
        public int RAGMValue { get; set; } = 15;

        /// <summary> Max distance for an entry to be retrieved (SmallWorld / HNSW.NET implementation) </summary>
        public float RAGDistanceCutOff { get; set; } = 0.2f;

        /// <summary> Search method. Simple tends to be the most consistent method </summary>
        public NeighbourSelectionHeuristic RAGHeuristic { get; set; } = NeighbourSelectionHeuristic.SelectSimple;

        /// <summary> Toggle RAG functionalities on/off </summary>
        public bool RAGEnabled { get; set; } = true;

    }
}
