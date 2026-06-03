using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;
using static LetheAISharp.SearchAPI.WebSearchAPI;

namespace LetheAISharp.Agent.Research
{
    /// <summary>
    /// Iterative deep research engine inspired by Think -> Search -> Extract -> Synthesize loops.
    ///
    /// This is intentionally a skeleton implementation:
    /// - structure is in place
    /// - prompt flow is in place
    /// - extension points are commented
    /// - heuristics are simple and conservative
    ///
    /// Expected usage:
    /// 1. Create engine
    /// 2. Call ResearchAsync(question, ct)
    /// 3. Get a final report + intermediate findings
    /// </summary>
    public sealed class DeepResearchEngine
    {
        private readonly DeepResearchOptions _options;
        private readonly ILogger? _logger;
        private readonly Action<DeepResearchProgress>? _progress;

        private readonly HashSet<string> _queriesUsed = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _urlsVisited = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<DeepResearchFinding> _findings = [];

        private DeepSearchPlan _researchPlan = new DeepSearchPlan();
        private string _evolvingReport = string.Empty;
        private DateTime _startedUtc;

        public DeepResearchEngine(
            DeepResearchOptions? options = null,
            ILogger? logger = null,
            Action<DeepResearchProgress>? progress = null)
        {
            _options = options ?? new DeepResearchOptions();
            _logger = logger ?? LLMEngine.Logger;
            _progress = progress;
        }

        /// <summary>
        /// Runs the full iterative research loop and returns a structured result.
        /// </summary>
        public async Task<DeepResearchResult> ResearchAsync(string question, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(question))
                return new DeepResearchResult() { Success = false, Error = "Research question cannot be empty.", Question = question };

            if (!LLMEngine.SupportsWebSearch)
                return new DeepResearchResult() { Success = false, Error = "The current backend does not support web search.", Question = question };

            _startedUtc = DateTime.UtcNow;
            _researchPlan = new DeepSearchPlan();
            _evolvingReport = string.Empty;
            _queriesUsed.Clear();
            _urlsVisited.Clear();
            _findings.Clear();

            Emit(DeepResearchPhase.Planning, 0, "Creating research plan...");

            // Step 1: Create plan
            _researchPlan = await CreateResearchPlanAsync(question, ct).ConfigureAwait(false);

            // TODO: later add category classification here.
            // Example:
            // var category = await ClassifyCategoryAsync(question, ct);

            for (int round = 1; round <= _options.MaxRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                if (DateTime.UtcNow - _startedUtc > _options.MaxDuration)
                {
                    _logger?.LogWarning("Deep research stopped because max duration was exceeded.");
                    break;
                }

                Emit(DeepResearchPhase.Searching, round, "Generating search queries...");

                // Step 2: Generate round queries
                var queries = await GenerateQueriesAsync(question, _researchPlan, _evolvingReport, round, Getinstruction(round), ct).ConfigureAwait(false);
                if (queries.Count == 0)
                {
                    _logger?.LogWarning("No queries were generated on round {Round}.", round);
                    break;
                }

                // Step 3: Search + collect findings
                var roundFindings = await SearchAndExtractAsync(question, queries, round, ct).ConfigureAwait(false);

                if (roundFindings.Count > 0)
                {
                    _findings.AddRange(roundFindings);

                    Emit(
                        DeepResearchPhase.Analyzing,
                        round,
                        $"Synthesizing {roundFindings.Count} new findings...");

                    // Step 4: Synthesize evolving report
                    _evolvingReport = await SynthesizeAsync(question, _evolvingReport, _findings, ct).ConfigureAwait(false);
                }
                else
                {
                    Emit(DeepResearchPhase.Warning, round, "No useful findings found this round.");
                }

                // Step 5: Stop decision
                if (round >= _options.MinRounds)
                {
                    var shouldStop = await ShouldStopAsync(question, _evolvingReport, _researchPlan, round, ct).ConfigureAwait(false);
                    if (shouldStop)
                    {
                        _logger?.LogInformation("Deep research stopped early after round {Round}.", round);
                        break;
                    }
                }
            }

            Emit(DeepResearchPhase.Writing, 0, "Writing final report...");

            // Step 6: Final polished report
            var finalReport = await BuildFinalReportAsync(question, _evolvingReport, ct).ConfigureAwait(false);

            var result = new DeepResearchResult
            {
                Question = question,
                ResearchPlan = _researchPlan,
                EvolvingReport = _evolvingReport,
                FinalReport = finalReport,
                CompletedRounds = EstimateCompletedRounds(),
                Duration = DateTime.UtcNow - _startedUtc,
                QueriesUsed = [.. _queriesUsed],
                UrlsVisited = [.. _urlsVisited],
                Findings = [.. _findings]
            };

            Emit(DeepResearchPhase.Completed, result.CompletedRounds, "Deep research completed.");
            return result;
        }

        #region Core steps

        /// <summary>
        /// Uses the LLM to create a structured research plan for the original question.
        /// </summary>
        private async Task<DeepSearchPlan> CreateResearchPlanAsync(string question, CancellationToken ct)
        {
            var searchplan = new DeepSearchPlan();
            var meta = new SessionMetaInfo();

            var prompt = new StringBuilder("You are a research strategist designed to overview deep web searches.");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"Before searching, you need to analyze the question and create a research plan.");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"**Question:** {question}");
            prompt.AppendLinuxLine();
            prompt.AppendLinuxLine($"Your task is to break down the question:");
            prompt.AppendLinuxLine("1. What are the key sub-topics that need to be covered for a comprehensive answer?");
            prompt.AppendLinuxLine("2. What specific data points, facts, or perspectives should we look for?");
            prompt.AppendLinuxLine("3. What would a complete, high-quality answer include?");
            prompt.AppendLinuxLine();

            var pb = LLMEngine.GetPromptBuilder();
            pb.Clear();

            // Keep the system prompt simple here.
            // You can make this smarter later if you want category-specific behavior.
            pb.AddMessage(AuthorRole.System, prompt.ToString());
            pb.AddMessage(AuthorRole.User, searchplan.GetQuery());
            await pb.SetStructuredOutput(searchplan);
            var query = pb.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, 2048);
            var raw = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);
            raw = raw.RemoveThinkingBlocks();

            try
            {
                searchplan = JsonConvert.DeserializeObject<DeepSearchPlan>(raw);
            }
            finally
            {
            }
            return searchplan ?? new DeepSearchPlan();
        }

        private string Getinstruction(int round)
        {
            if (round <= 1)
                return "This is the first round — generate broad, diverse queries that explore the key facets of the question.";
            else
                return "We already have partial findings.  Generate targeted follow-up queries to fill gaps, verify claims, or explore specific aspects that the report doesn't yet cover well.";
        }

        /// <summary>
        /// Generates search queries for a given round.
        /// </summary>
        private async Task<List<string>> GenerateQueriesAsync(string question, DeepSearchPlan plan, string currentReport, int round, string round_instruction, CancellationToken ct)
        {
            var prompt = $"""
                You are planning web searches.

                **Original question:** {question}

                **Research plan:**

                {plan.ToPlan(false)}

                **What we know so far:**

                {currentReport}

                **Round:** {round}

                Generate up to {_options.MaxQueriesPerRound} focused, high-quality, web search queries that will help answer the question. Keep them specific and non-redundant.
                {round_instruction}
                """
                +
                """

                Return a JSON object with:
                - "web_search": Array of specific web search queries.

                Example:
                {
                  "web_search": [
                    "cost of living in X 2024",
                    "quality of healthcare in X",
                    "safety and crime rates in X"
                  ]
                }               
                """;

            var res = new DeepSearchQueries();
            var pb = LLMEngine.GetPromptBuilder();
            pb.Clear();

            // Keep the system prompt simple here.
            // You can make this smarter later if you want category-specific behavior.
            pb.AddMessage(AuthorRole.System, "You are a high-quality research assistant.");
            pb.AddMessage(AuthorRole.User, prompt);
            await pb.SetStructuredOutput(res);

            var query = pb.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, 2048);
            var raw = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);

            try
            {
                res = JsonConvert.DeserializeObject<DeepSearchQueries>(raw);
            }
            finally
            {
            }

            var parsed = res?.WebQueries ?? [];

            // De-duplicate across all previous rounds
            var unique = parsed
                .Select(q => q.Trim())
                .Where(q => !string.IsNullOrWhiteSpace(q))
                .Where(q => _queriesUsed.Add(q))
                .Take(_options.MaxQueriesPerRound)
                .ToList();

            return unique;
        }

        /// <summary>
        /// Performs search for each generated query and converts results into findings.
        /// </summary>
        private async Task<List<DeepResearchFinding>> SearchAndExtractAsync(string question, List<string> queries, int round, CancellationToken ct)
        {
            var findings = new List<DeepResearchFinding>();

            foreach (var query in queries)
            {
                ct.ThrowIfCancellationRequested();

                Emit(DeepResearchPhase.Searching, round, $"Searching: {query}", queryPreview: query);

                List<EnrichedSearchResult> results;
                try
                {
                    results = await LLMEngine.WebSearch(query).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Search failed for query: {Query}", query);
                    continue;
                }

                if (results.Count == 0)
                    continue;

                foreach (var result in results.Take(_options.MaxResultsPerQuery))
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(result.Url))
                        continue;

                    if (!_urlsVisited.Add(result.Url))
                        continue;

                    Emit(DeepResearchPhase.Reading, round, result.Title ?? result.Url, url: result.Url);

                    var rawText = result.ToMarkDown(false, false);
                    if (string.IsNullOrWhiteSpace(rawText))
                        continue;

                    // Data extraction pass: "Given the research question, what matters from this page?"
                    var extracted = await ExtractFindingAsync(question, result, rawText, ct).ConfigureAwait(false);
                    if (extracted != null)
                        findings.Add(extracted);
                }
            }

            return findings;
        }

        /// <summary>
        /// Merges the current set of findings into an evolving research report.
        /// The synthesis window prevents prompt size from growing without bound.
        /// </summary>
        private async Task<string> SynthesizeAsync(string question, string currentReport, List<DeepResearchFinding> allFindings, CancellationToken ct)
        {
            var window = allFindings.TakeLast(_options.SynthesisWindow).ToList();

            var findingsText = FormatFindings(window);

            var prompt = $"""
                You are updating an evolving research report.

                **Original question:** {question}

                **Current report:**
                {(string.IsNullOrWhiteSpace(currentReport) ? "(none yet)" : currentReport)}

                **New findings:**
                {findingsText}

                
                Produce an updated report that:
                - integrates the new findings
                - removes redundancy
                - keeps important source URLs inline
                - stays organized and factual

                Return only the updated report.
                """;

            return await RunPromptAsync(prompt, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Lets the LLM decide whether the current report is already sufficiently complete.
        /// 
        /// Keep this conservative at first.
        /// </summary>
        private async Task<bool> ShouldStopAsync(string question, string currentReport, DeepSearchPlan plan, int round, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(currentReport))
                return false;

            var prompt = $"""
                You are deciding whether research should stop.

                **Question:** {question}

                **Success criteria for a complete answer:**
                {plan.SuccessCriteria}

                **Current report:**
                {currentReport}

                **Round:** {round}

                Reply with:
                YES - reason
                or
                NO - reason

                Stop only if the answer appears comprehensive enough.
                """;

            var raw = await RunPromptAsync(prompt, ct).ConfigureAwait(false);
            return raw.TrimStart().StartsWith("YES", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the final polished research report.
        /// 
        /// Later you can add category-specific formatting here
        /// (comparison table, how-to steps, fact-check structure, etc.)
        /// </summary>
        private async Task<string> BuildFinalReportAsync(string question, string report, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(report))
                return "No information could be gathered for this question.";

            var prompt = $"""
                Write a polished, detailed research report answering this question.

                Question:
                {question}

                Research material:
                {report}

                Requirements:
                - Add a short executive summary
                - Use clear markdown headings
                - Include concrete details where available
                - End with a direct conclusion
                - Keep source URLs inline where relevant

                Return only the final report.
                """;

            return await RunPromptAsync(prompt, ct).ConfigureAwait(false);
        }

        #endregion

        #region Extraction / formatting helpers

        /// <summary>
        /// Optional extraction stage for a single search result.
        /// 
        /// If your search results already contain good enriched content,
        /// you may skip the LLM extraction step and map directly to finding objects.
        /// </summary>
        private async Task<DeepResearchFinding?> ExtractFindingAsync(
            string question,
            EnrichedSearchResult result,
            string sourceText,
            CancellationToken ct)
        {
            // Simple first-pass extraction prompt.
            // Later you can enforce JSON and parse title/summary/evidence/rationale separately.
            var prompt = $"""
                You are extracting useful evidence from a web page for a research task.

                Research question:
                {question}

                Source title:
                {result.Title}

                Source URL:
                {result.Url}

                Source content:
                {sourceText}

                Return a short structured response with:
                1. Summary
                2. Key evidence
                3. Why this source matters

                Be concise and factual.
                """;

            var raw = await RunPromptAsync(prompt, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            return new DeepResearchFinding
            {
                Url = result.Url ?? string.Empty,
                Title = result.Title ?? string.Empty,
                Summary = raw,
                Evidence = sourceText.Length > 2000 ? sourceText[..2000] : sourceText,
                Rationale = string.Empty
            };
        }

        /// <summary>
        /// Formats findings for synthesis prompts.
        /// </summary>
        private static string FormatFindings(IEnumerable<DeepResearchFinding> findings)
        {
            var sb = new StringBuilder();
            var index = 1;

            foreach (var finding in findings)
            {
                sb.AppendLine($"Finding {index}: {finding.Title}");
                sb.AppendLine($"URL: {finding.Url}");
                sb.AppendLine($"Summary: {finding.Summary}");

                if (!string.IsNullOrWhiteSpace(finding.Evidence))
                {
                    sb.AppendLine("Evidence:");
                    sb.AppendLine(finding.Evidence);
                }

                sb.AppendLine();
                index++;
            }

            return sb.ToString().Trim();
        }

        #endregion

        #region LLM helpers / parsing

        /// <summary>
        /// Minimal helper for one-shot prompt execution through Lethe's existing LLM entry point.
        /// 
        /// You may later replace this with:
        /// - schema/GBNF constrained output
        /// - a dedicated JSON helper
        /// - lower-level client access
        /// </summary>
        private static async Task<string> RunPromptAsync(string prompt, CancellationToken ct)
        {
            var pb = LLMEngine.GetPromptBuilder();
            pb.Clear();

            // Keep the system prompt simple here.
            // You can make this smarter later if you want category-specific behavior.
            pb.AddMessage(AuthorRole.System, "You are a precise research assistant.");
            pb.AddMessage(AuthorRole.User, prompt);
            var query = pb.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, 2048);
            var raw = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);

            return raw ?? string.Empty;
        }

        #endregion

        #region Misc helpers

        private void Emit(
            DeepResearchPhase phase,
            int round,
            string? message = null,
            string? queryPreview = null,
            string? url = null)
        {
            if (!_options.EnableProgressEvents || _progress == null)
                return;

            _progress(new DeepResearchProgress
            {
                Phase = phase,
                Round = round,
                Message = message,
                QueryPreview = queryPreview,
                Url = url,
                TotalSources = _urlsVisited.Count,
                TotalFindings = _findings.Count
            });
        }

        private int EstimateCompletedRounds()
        {
            // Skeleton approximation.
            // If you want exact count, track it explicitly in the loop.
            return Math.Max(0, _queriesUsed.Count > 0 ? 1 : 0);
        }

        #endregion
    }
}