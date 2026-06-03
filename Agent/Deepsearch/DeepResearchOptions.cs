using LetheAISharp.GBNF;
using static LetheAISharp.SearchAPI.WebSearchAPI;

namespace LetheAISharp.Agent.Research
{
    /// <summary>
    /// Configuration options for the iterative deep research engine.
    /// Keep this small and practical at first; expand later if needed.
    /// </summary>
    public sealed class DeepResearchOptions
    {
        /// <summary>Maximum number of iterative search/synthesis rounds.</summary>
        public int MaxRounds { get; set; } = 5;

        /// <summary>Minimum number of rounds before the engine is allowed to stop early.</summary>
        public int MinRounds { get; set; } = 2;

        /// <summary>Maximum number of search queries generated per round.</summary>
        public int MaxQueriesPerRound { get; set; } = 3;

        /// <summary>
        /// Maximum number of findings to send into the synthesis prompt each round.
        /// Prevents prompt growth from exploding.
        /// </summary>
        public int SynthesisWindow { get; set; } = 8;

        /// <summary>Maximum total runtime for a research execution.</summary>
        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Optional category hint, if you want to force output style later
        /// (e.g. comparison / howto / product / factcheck / general).
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// If true, the engine may emit intermediate progress through a callback.
        /// </summary>
        public bool EnableProgressEvents { get; set; } = true;
    }

    /// <summary>
    /// High-level progress stages for UI/task/debug reporting.
    /// </summary>
    public enum DeepResearchPhase
    {
        Planning,
        Searching,
        Reading,
        Analyzing,
        Writing,
        Completed,
        Warning,
        Error
    }

    /// <summary>
    /// Lightweight progress payload for consumers that want real-time updates.
    /// </summary>
    public sealed class DeepResearchProgress
    {
        public DeepResearchPhase Phase { get; set; }
        public int Round { get; set; }
        public string? Message { get; set; }
        public string? QueryPreview { get; set; }
        public string? Url { get; set; }
        public int TotalSources { get; set; }
        public int TotalFindings { get; set; }
    }

    /// <summary>
    /// A single extracted/retained finding used during synthesis.
    /// This is intentionally simple for a first-pass implementation.
    /// </summary>
    public sealed class DeepResearchFinding
    {
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Optional larger body/snippet/full content if available from search enrichment.
        /// </summary>
        public string Evidence { get; set; } = string.Empty;

        /// <summary>
        /// Optional rationale for why this source matters.
        /// </summary>
        public string Rationale { get; set; } = string.Empty;
    }

    /// <summary>
    /// Final result object returned by the engine.
    /// Useful if you want more than just the final report text.
    /// </summary>
    public sealed class DeepResearchResult
    {
        public bool Success { get; set; } = false;
        public string Error { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public DeepSearchPlan ResearchPlan { get; set; } = new DeepSearchPlan();
        public string EvolvingReport { get; set; } = string.Empty;
        public string FinalReport { get; set; } = string.Empty;

        public int CompletedRounds { get; set; }
        public TimeSpan Duration { get; set; }

        public List<string> QueriesUsed { get; set; } = [];
        public List<string> UrlsVisited { get; set; } = [];
        public List<DeepResearchFinding> Findings { get; set; } = [];
    }
}