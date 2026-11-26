using LetheAISharp.Agent;
using LetheAISharp.API;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using CommunityToolkit.HighPerformance;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.RegularExpressions;
using static System.Collections.Specialized.BitVector32;

namespace LetheAISharp.Files
{

    /// <summary>
    /// Specifies the behavior for handling session messages in a chat log.
    /// </summary>
    /// <remarks>This enumeration defines how messages from different sessions are included in the chat log. 
    /// Use <see cref="CurrentOnly"/> to restrict the log to the current session, or <see cref="FitAll"/>  to include
    /// messages from all sessions within the token limit.</remarks>
    public enum SessionHandling 
    {
        /// <summary>
        /// If set to CurrentOnly, the chatlog will only include messages from the current session.
        /// </summary>
        CurrentOnly,
        /// <summary>
        /// If set to FitAll, the chatlog will include messages from all sessions, fitting as many as possible within the token limit.
        /// </summary>
        FitAll
    }

    /// <summary>
    /// Represents the full chatlog for a specific agent.
    /// It provides functionality to manage, retrieve, and manipulate chat history.
    /// </summary>
    /// <remarks>The <see cref="Chatlog"/> class maintains a collection of chat sessions and provides methods
    /// to interact with the chat history, such as retrieving specific messages, logging new messages, and managing
    /// session boundaries. It supports event-driven notifications for key actions, such as adding messages or creating
    /// new sessions.</remarks>
    public class Chatlog : BaseFile
    {
        /// <summary>
        /// Current active session ID. If -1 (or out of rangeà, the last session in the list is considered current. This allows
        /// the user to continue chatting with the persona in previous sessions if needed.
        /// </summary>
        public int CurrentSessionID { get; set; } = -1;

        /// <summary>
        /// List of all chat sessions in this log.
        /// </summary>
        public readonly List<ChatSession> Sessions = [];

        /// <summary>
        /// Period summaries (meta-summaries of closed weeks, months, and quarters), stored aside from the session list.
        /// See <see cref="UpdatePeriodSummaries"/> and <see cref="GetPeriodSummaries"/>.
        /// </summary>
        public List<PeriodSummary> PeriodSummaries { get; set; } = [];

        /// <summary>
        /// Accessor for the current chat session. If no sessions exist, a new session is created automatically.
        /// This points to the session at CurrentSessionID, or the last session if CurrentSessionID is out of range.
        /// </summary>
        [JsonIgnore]
        public ChatSession CurrentSession
        {
            get
            {
                if (Sessions.Count == 0)
                {
                    Sessions.Add(CreateChatSession());
                    CurrentSessionID = 0;
                    return Sessions[0];
                }
                return CurrentSessionID >= 0 && CurrentSessionID < Sessions.Count ? Sessions[CurrentSessionID] : Sessions.Last();
            }
        }

        /// <summary>
        /// Events raised by the Chatlog class to notify listeners before a message is added to the current session.
        /// </summary>
        [JsonIgnore] public EventHandler<SingleMessage>? OnBeforeMessageAdded;
        /// <summary>
        /// Events raised by the Chatlog class to notify listeners after a message is added to the current session.
        /// </summary>
        [JsonIgnore] public EventHandler<SingleMessage>? OnMessageAdded;
        /// <summary>
        /// Events raised by the Chatlog class to notify listeners when a new session is created.
        /// </summary>
        [JsonIgnore] public EventHandler<ChatSession>? OnNewSession;

        private int lastSessionID = -1;

        /// <summary>
        /// Factory method for creating ChatSession instances. Override this in derived classes to provide custom ChatSession implementations.
        /// </summary>
        /// <returns>A new ChatSession instance</returns>
        protected virtual ChatSession CreateChatSession()
        {
            return new ChatSession();
        }

        /// <summary>
        /// Retrieves a concatenated summary of previous sessions, formatted with a specified section header,  while
        /// adhering to token limits and optional filtering criteria.
        /// </summary>
        /// <remarks>Sessions that already used for memory recall (use <seealso cref="LLMEngine.InvalidatePromptCache"/> before 
        /// call to include everything), are empty, or do not meet the filtering  criteria (e.g., roleplay sessions when <paramref name="allowRP"/> 
        /// is false are skipped. Sessions marked as sticky are always inserted (unless already in recall). The method stops adding content 
        /// once the maxTokens or maxCount limit are reached.</remarks>
        /// <param name="maxTokens">The maximum number of tokens allowed in the resulting summary. The method will truncate content to stay
        /// within this limit.</param>
        /// <param name="sectionHeader">The header string to prepend to each session's name in the summary. Defaults to "##".</param>
        /// <param name="allowRP">A boolean value indicating whether to include roleplay sessions in the summary. If <see langword="false"/>,
        /// roleplay sessions are excluded. Defaults to <see langword="true"/>.</param>
        /// <param name="maxCount">The maximum number of previous sessions to include in the summary. Defaults to <see cref="int.MaxValue"/>, meaning all valid sessions</param>
        /// <param name="ignoreList">An optional set of session GUIDs to ignore when compiling the summary. Sessions with GUIDs in this list will be skipped.</param>
        /// <returns>A string containing the formatted summaries of previous sessions, up to the specified token limit.  Returns
        /// an empty string if no valid sessions are found or if the token limit is too restrictive.</returns>
        public string GetPreviousSummaries(int maxTokens, string sectionHeader = "##", MemoryMode allowRP = MemoryMode.Normal, int maxCount = int.MaxValue, HashSet<Guid>? ignoreList = null)
        {
            return GetPreviousSummaries(maxTokens, out _, sectionHeader, allowRP, maxCount, ignoreList);
        }

        /// <summary>
        /// Retrieves a concatenated summary of previous sessions, and reports the start of the time range covered by the recent history 
        /// (chatlog in context and session summaries). See <see cref="GetPreviousSummaries(int, string, MemoryMode, int, HashSet{Guid}?)"/>.
        /// </summary>
        /// <param name="maxTokens">The maximum number of tokens allowed in the resulting summary.</param>
        /// <param name="coverageStart">Start time of the oldest non-sticky session included in the summaries, or of the oldest session 
        /// present in the chatlog if no summary was included. Used by <see cref="GetPeriodSummaries"/> to avoid overlapping content.</param>
        /// <param name="sectionHeader">The header string to prepend to each session's name in the summary.</param>
        /// <param name="allowRP">Memory mode used to filter roleplay and non-roleplay sessions.</param>
        /// <param name="maxCount">The maximum number of previous sessions to include in the summary.</param>
        /// <param name="ignoreList">An optional set of session GUIDs to ignore when compiling the summary.</param>
        /// <returns>A string containing the formatted summaries of previous sessions, or an empty string.</returns>
        public string GetPreviousSummaries(int maxTokens, out DateTime coverageStart, string sectionHeader = "##", MemoryMode allowRP = MemoryMode.Normal, int maxCount = int.MaxValue, HashSet<Guid>? ignoreList = null)
        {
            if (lastSessionID == -1 && Sessions.Count >= 2)
            {
                if (CurrentSessionID != -1 && CurrentSessionID != Sessions.Count -1)
                    lastSessionID = CurrentSessionID - 1;
                else
                    lastSessionID = Sessions.Count - 1;
            }
            coverageStart = Sessions.Count > 0 ? Sessions[Math.Clamp(lastSessionID, 0, Sessions.Count - 1)].StartTime : DateTime.Now;
            var entrydepth = lastSessionID - 1;
            if (entrydepth < 0)
                return string.Empty;
            if (LLMEngine.IsGroupConversation && LLMEngine.Bot is GroupPersonaBase groupPersona && groupPersona.CurrentBotId != groupPersona.PrimaryPersonaName && LLMEngine.Settings.GroupSecondaryPersonaSeePastSessions == GroupChatPastSessionMode.None)
            {
                return string.Empty;
            }

            var usedGuid = ignoreList ?? [];

            // Add the sticky sessions first
            var SelectedSessions = new List<ChatSession>();
            SelectedSessions.AddRange(Sessions.FindAll(e => e.Sticky && e.CompatibleWithMemoryMode()));
            foreach (var item in SelectedSessions)
                usedGuid.Add(item.Guid);

            // Now check all previous entries
            var tokensleft = maxTokens;
            var count = 0;
            var sb = new StringBuilder();
            for (int i = entrydepth; i >= 0; i--)
            {
                if (i >= Sessions.Count)
                    continue;
                var session = Sessions[i];
                if (usedGuid.Contains(session.Guid) || string.IsNullOrWhiteSpace(session.Content))
                    continue;
                if (!session.CompatibleWithMemoryMode(allowRP))
                    continue;
                // if we're dealing with group conversation, and we're having a secondary persona playing we have more checks
                if (LLMEngine.IsGroupConversation && LLMEngine.Bot is GroupPersonaBase group && group.CurrentBotId != group.PrimaryPersonaName && LLMEngine.Settings.GroupSecondaryPersonaSeePastSessions == GroupChatPastSessionMode.ActiveOnly)
                {
                    if (!session.ContainsPersona(group.GetCurrentPersona()))
                        continue;
                }

                sb.Clear();
                sb.AppendLinuxLine($"{sectionHeader} {session.Name}");
                sb.AppendLinuxLine(session.ToSnippet(TitleInsertType.None, LLMEngine.Bot.DatesInSessionSummaries, false, true)).AppendLinuxLine();
                var tks = LLMEngine.GetTokenCount(sb.ToString()) + 1;
                if (tks <= tokensleft)
                {
                    SelectedSessions.Add(session);
                    count++;
                    tokensleft -= tks;
                    if (session.StartTime < coverageStart)
                        coverageStart = session.StartTime;
                }
                if (tokensleft <= 0)
                    break;
                if (count >= maxCount)
                    break;
            }
            if (SelectedSessions.Count == 0)
                return string.Empty;

            // Now we should have a proper list (with roughly correct size) sort from oldest to youngest
            SelectedSessions.Sort((x, y) => x.StartTime.CompareTo(y.StartTime));
            sb.Clear();
            foreach (var session in SelectedSessions)
            {
                sb.AppendLinuxLine($"{sectionHeader} {session.Name}");
                sb.AppendLinuxLine(session.ToSnippet(TitleInsertType.None, LLMEngine.Bot.DatesInSessionSummaries, false, true)).AppendLinuxLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Retrieves the period summaries (past weeks, months, and quarters) that cover the history older than <paramref name="coverageStart"/>, 
        /// formatted for the system prompt and ordered from oldest to most recent.
        /// </summary>
        /// <remarks>Selection goes from the most recent to the oldest periods, coarsening over time: up to two weeks, then up to two months, then as many
        /// quarters as the token budget allows. Past those two entries, a tier keeps going only when the remaining gap to the next boundary (month or quarter) 
        /// is less than half the larger period; otherwise the larger period is used and overlaps slightly. A period is skipped when the majority of its 
        /// sessions are already covered by the recent history (tier 1 takes priority). The budget is filled from the most recent entry and stops at the
        /// first entry that doesn't fit. 
        /// Only available to the primary persona in group conversations.</remarks>
        /// <param name="maxTokens">The maximum number of tokens allowed in the resulting text.</param>
        /// <param name="coverageStart">Start of the time range already covered by the chatlog and recent session summaries 
        /// (see <see cref="GetPreviousSummaries(int, out DateTime, string, MemoryMode, int, HashSet{Guid}?)"/>).</param>
        /// <param name="sectionHeader">The header string to prepend to each period's name.</param>
        /// <returns>The formatted period summaries, or an empty string if none apply.</returns>
        public string GetPeriodSummaries(int maxTokens, DateTime coverageStart, string sectionHeader = "##")
        {
            if (maxTokens <= 0 || PeriodSummaries.Count == 0)
                return string.Empty;
            if (LLMEngine.IsGroupConversation && LLMEngine.Bot is GroupPersonaBase group && group.CurrentBotId != group.PrimaryPersonaName)
                return string.Empty;

            var now = DateTime.Now;
            var sessions = GetPeriodSourceSessions(LLMEngine.Settings.PeriodSummaryIncludeRoleplay);
            var cursor = coverageStart;
            var candidates = new List<PeriodSummary>();

            bool IsMostlyUncovered(PeriodSummary period)
            {
                if (period.PeriodStart >= cursor)
                    return false;
                var total = 0;
                var covered = 0;
                foreach (var s in sessions)
                {
                    if (s.StartTime < period.PeriodStart || s.StartTime >= period.PeriodEnd)
                        continue;
                    total++;
                    if (s.StartTime >= cursor)
                        covered++;
                }
                return total > 0 && covered * 2 < total;
            }

            foreach (var type in new[] { PeriodType.Week, PeriodType.Month, PeriodType.Quarter })
            {
                var parent = PeriodSummary.GetParentPeriod(type);
                var picked = 0;
                var list = PeriodSummaries
                    .Where(e => e.Period == type && e.PeriodEnd <= now && !string.IsNullOrWhiteSpace(e.Content))
                    .OrderByDescending(e => e.PeriodStart);
                foreach (var period in list)
                {
                    if (!IsMostlyUncovered(period))
                        continue;
                    if (parent.HasValue && picked >= MinPeriodsPerTier)
                    {
                        // Past the minimum, only fill the remaining gap up to the parent's boundary if it's small (less than half the parent period).
                        // Otherwise the parent period is mostly before the cursor and will be picked by the next tier, with a limited overlap.
                        var parentStart = PeriodSummary.GetPeriodStart(parent.Value, cursor);
                        var halfParent = (PeriodSummary.GetPeriodEnd(parent.Value, parentStart) - parentStart).TotalDays / 2;
                        var gap = (cursor - parentStart).TotalDays;
                        if (gap <= 0 || gap > halfParent)
                            break;
                        // Never add a period that mostly belongs to the previous parent period
                        var midpoint = period.PeriodStart + ((period.PeriodEnd - period.PeriodStart) / 2);
                        if (midpoint < parentStart)
                            break;
                    }
                    candidates.Add(period);
                    picked++;
                    if (period.PeriodStart < cursor)
                        cursor = period.PeriodStart;
                }
            }

            // Fill the budget from the most recent to the oldest, stopping at the first entry that doesn't fit to avoid gaps in the timeline
            var tokensleft = maxTokens;
            var selected = new List<PeriodSummary>();
            foreach (var period in candidates)
            {
                var tks = LLMEngine.GetTokenCount(FormatPeriodEntry(period, sectionHeader, now)) + 1;
                if (tks > tokensleft)
                    break;
                selected.Add(period);
                tokensleft -= tks;
            }
            if (selected.Count == 0)
                return string.Empty;

            selected.Sort((x, y) => x.PeriodStart != y.PeriodStart ? x.PeriodStart.CompareTo(y.PeriodStart) : x.PeriodEnd.CompareTo(y.PeriodEnd));
            var sb = new StringBuilder();
            foreach (var period in selected)
                sb.Append(FormatPeriodEntry(period, sectionHeader, now));
            return sb.ToString();
        }

        private const int MinPeriodsPerTier = 2;

        private static string FormatPeriodEntry(PeriodSummary period, string sectionHeader, DateTime now)
        {
            var sb = new StringBuilder();
            sb.AppendLinuxLine($"{sectionHeader} {period.Name} ({period.GetRelativeLabel(now)})");
            sb.AppendLinuxLine(period.ToSnippet(TitleInsertType.None, false, false, true)).AppendLinuxLine();
            return sb.ToString();
        }

        /// <summary>
        /// Returns the archived sessions that can be used as sources by the period summary system.
        /// </summary>
        /// <param name="includeRoleplay">If false, roleplay sessions are excluded.</param>
        /// <returns>The list of eligible sessions (excluding the current session and sessions without a summary).</returns>
        protected virtual List<ChatSession> GetPeriodSourceSessions(bool includeRoleplay)
        {
            if (Sessions.Count == 0)
                return [];
            var current = CurrentSession;
            return [.. Sessions.Where(s => s != current && s.StartTime != default && !string.IsNullOrWhiteSpace(s.Content) && (includeRoleplay || !s.MetaData.IsRoleplaySession))];
        }

        /// <summary>
        /// Compiles the period summaries (weeks, months, and quarters) for all closed periods. Only periods that are new, or whose sources 
        /// changed since the last update, are (re)generated. Periods that no longer have any source are removed.
        /// </summary>
        /// <remarks>Weeks are built from session summaries, months from week summaries (and session summaries for the partial weeks at the 
        /// edges of the month), and quarters from month summaries. A period with a single source reuses that source's content without querying the LLM.
        /// The first run on a long chatlog may require many queries.</remarks>
        /// <param name="regenerateAll">If true, all period summaries are regenerated from scratch, regardless of their state.</param>
        /// <param name="ct">Cancellation token.</param>
        public virtual async Task UpdatePeriodSummaries(bool regenerateAll = false, CancellationToken ct = default)
        {
            var includeRP = LLMEngine.Settings.PeriodSummaryIncludeRoleplay;
            var sessions = GetPeriodSourceSessions(includeRP).OrderBy(s => s.StartTime).ToList();
            var now = DateTime.Now;
            var kept = new HashSet<Guid>();
            var weeks = new Dictionary<DateTime, PeriodSummary>();
            var months = new Dictionary<DateTime, PeriodSummary>();

            // Weeks, from session summaries
            foreach (var bucket in sessions.GroupBy(s => PeriodSummary.GetPeriodStart(PeriodType.Week, s.StartTime)))
            {
                if (PeriodSummary.GetPeriodEnd(PeriodType.Week, bucket.Key) > now)
                    continue;
                var res = await UpdatePeriodSummary(PeriodType.Week, bucket.Key, [.. bucket], includeRP, regenerateAll, ct).ConfigureAwait(false);
                if (res != null)
                {
                    weeks[bucket.Key] = res;
                    kept.Add(res.Guid);
                }
            }

            // Months, from the weeks fully inside the month, and sessions of the partial weeks at the edges
            foreach (var bucket in sessions.GroupBy(s => PeriodSummary.GetPeriodStart(PeriodType.Month, s.StartTime)))
            {
                var monthEnd = PeriodSummary.GetPeriodEnd(PeriodType.Month, bucket.Key);
                if (monthEnd > now)
                    continue;
                var sources = new List<MemoryUnit>();
                for (var ws = PeriodSummary.GetPeriodStart(PeriodType.Week, bucket.Key); ws < monthEnd; ws = ws.AddDays(7))
                {
                    var we = ws.AddDays(7);
                    if (ws >= bucket.Key && we <= monthEnd && weeks.TryGetValue(ws, out var week))
                        sources.Add(week);
                    else
                        sources.AddRange(bucket.Where(s => s.StartTime >= ws && s.StartTime < we));
                }
                var res = await UpdatePeriodSummary(PeriodType.Month, bucket.Key, sources, includeRP, regenerateAll, ct).ConfigureAwait(false);
                if (res != null)
                {
                    months[bucket.Key] = res;
                    kept.Add(res.Guid);
                }
            }

            // Quarters, from month summaries (falling back to session summaries for months that failed to generate)
            foreach (var bucket in sessions.GroupBy(s => PeriodSummary.GetPeriodStart(PeriodType.Quarter, s.StartTime)))
            {
                if (PeriodSummary.GetPeriodEnd(PeriodType.Quarter, bucket.Key) > now)
                    continue;
                var sources = new List<MemoryUnit>();
                for (var ms = bucket.Key; ms < bucket.Key.AddMonths(3); ms = ms.AddMonths(1))
                {
                    var me = ms.AddMonths(1);
                    if (months.TryGetValue(ms, out var month))
                        sources.Add(month);
                    else
                        sources.AddRange(bucket.Where(s => s.StartTime >= ms && s.StartTime < me));
                }
                var res = await UpdatePeriodSummary(PeriodType.Quarter, bucket.Key, sources, includeRP, regenerateAll, ct).ConfigureAwait(false);
                if (res != null)
                    kept.Add(res.Guid);
            }

            PeriodSummaries.RemoveAll(e => !kept.Contains(e.Guid));
            PeriodSummaries.Sort((x, y) => x.PeriodStart != y.PeriodStart ? x.PeriodStart.CompareTo(y.PeriodStart) : x.Period.CompareTo(y.Period));
        }

        private async Task<PeriodSummary?> UpdatePeriodSummary(PeriodType period, DateTime start, List<MemoryUnit> sources, bool includeRP, bool force, CancellationToken ct)
        {
            var existing = PeriodSummaries.FirstOrDefault(e => e.Period == period && e.PeriodStart == start);
            if (sources.Count == 0)
                return null;
            var signature = PeriodSummary.ComputeSignature(sources, includeRP);
            if (!force && existing != null && existing.SourceSignature == signature && !string.IsNullOrWhiteSpace(existing.Content))
                return existing;

            ct.ThrowIfCancellationRequested();
            var content = sources.Count == 1 ? sources[0].Content : await GeneratePeriodSummary(period, start, sources, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content))
                return existing;

            var summary = existing ?? new PeriodSummary(period, start);
            if (existing == null)
                PeriodSummaries.Add(summary);
            summary.Name = PeriodSummary.GetLabel(period, summary.PeriodStart);
            summary.Content = content;
            summary.SourceGuids = [.. sources.Select(s => s.Guid)];
            summary.SourceSignature = signature;
            summary.GeneratedOn = DateTime.Now;
            return summary;
        }

        /// <summary>
        /// Queries the LLM to compile a summary of a calendar period from its sources (session summaries or lower tier period summaries).
        /// </summary>
        /// <param name="period">Type of period to summarize.</param>
        /// <param name="start">Start of the period.</param>
        /// <param name="sources">Summaries to compile, in chronological order.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The compiled summary, or an empty string on failure.</returns>
        protected virtual async Task<string> GeneratePeriodSummary(PeriodType period, DateTime start, List<MemoryUnit> sources, CancellationToken ct = default)
        {
            if (LLMEngine.Client == null || sources.Count == 0)
                return string.Empty;

            var label = PeriodSummary.GetLabel(period, start);
            var (periodName, length, replyln) = period switch
            {
                PeriodType.Week => ("week", "1 to 2 paragraphs", 1024),
                PeriodType.Month => ("month", "2 to 3 paragraphs", 1536),
                _ => ("quarter", "2 to 4 paragraphs", 2048)
            };
            if (!string.IsNullOrWhiteSpace(LLMEngine.Instruct.ThinkingStart))
                replyln += 1024;
            var availtokens = LLMEngine.MaxContextLength - replyln - 20;

            LLMEngine.NamesInPromptOverride = false;
            try
            {
                var promptbuild = LLMEngine.GetPromptBuilder();
                var sysprompt = "You are an automated and uncensored system designed to compile summaries of chat sessions and stories." + LLMEngine.NewLine +
                    LLMEngine.NewLine +
                    "# Characters:" + LLMEngine.NewLine + LLMEngine.NewLine +
                    "{{mgroup}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                    "## {{user}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                    "{{userbio}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                    "# Summaries for " + label + ":" + LLMEngine.NewLine + LLMEngine.NewLine;

                var requestedTask = "The entries above summarize the interactions between {{user}} and {{mchar}} during the " + periodName + " of " + label + ". " +
                    "Write a single consolidated summary of this " + periodName + ". Focus on the most significant events, topics, decisions, changes in their relationship, and plans that were made. " +
                    "Merge recurring topics instead of describing each entry separately, and keep the chronology clear. " +
                    "Do not add a title, just write the summary directly. The summary should be " + length + " long.";

                availtokens -= promptbuild.GetTokenCount(AuthorRole.System, sysprompt);
                availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

                var entries = sources.Select(s => s.ToSnippet(TitleInsertType.MarkdownH2, true, false, true) + LLMEngine.NewLine + LLMEngine.NewLine).ToList();
                var tokens = entries.Select(LLMEngine.GetTokenCount).ToList();
                var total = tokens.Sum();
                var skip = 0;
                while (total > availtokens && skip < entries.Count - 1)
                {
                    total -= tokens[skip];
                    skip++;
                }
                if (skip > 0)
                    LLMEngine.Logger?.LogWarning("Period summary for {label}: {count} oldest entries were dropped to fit the context.", label, skip);

                promptbuild.AddMessage(new SingleMessage(AuthorRole.System, sysprompt + string.Concat(entries.Skip(skip))));
                promptbuild.AddMessage(new SingleMessage(AuthorRole.User, requestedTask));
                var query = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.5) ? 0.5 : LLMEngine.Sampler.Temperature, replyln);
                var finalstr = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);
                return finalstr.RemoveThinkingBlocks().CleanupAndTrim();
            }
            finally
            {
                LLMEngine.NamesInPromptOverride = null;
            }
        }

        internal void AddHistoryToPrompt(SessionHandling sessionHandling, int maxTokens, PromptInserts? memories)
        {
            var tokensleft = maxTokens;
            var entrydepth = 0;
            var startpos = LLMEngine.PromptBuilder!.Count;
            // add all messages together in the same list
            var messagelist = new List<(int, SingleMessage)>();
            var curSessionID = CurrentSessionID == -1 ? Sessions.Count - 1 : CurrentSessionID;
            // make a list of all messages
            if (sessionHandling == SessionHandling.FitAll)
            {
                for (int i = 0; i <= curSessionID; i++)
                {
                    if (LLMEngine.IsGroupConversation && LLMEngine.Bot is GroupPersonaBase group && group.CurrentBotId != group.PrimaryPersonaName && LLMEngine.Settings.GroupSecondaryPersonaSeePastSessions == GroupChatPastSessionMode.ActiveOnly)
                    {
                        if (i != curSessionID && !Sessions[i].ContainsPersona(group.GetCurrentPersona()))
                            continue;
                    }
                    if (!Sessions[i].CompatibleWithMemoryMode() && i != curSessionID)
                        continue;

                    foreach (var msg in Sessions[i].Messages)
                        messagelist.Add((i, msg));
                }
            }
            else
            {
                foreach (var msg in CurrentSession.Messages)
                    messagelist.Add((curSessionID, msg));
            }

            var oldest = int.MaxValue;
            // iterate through the messages in reverse order until we reach the token limit or end of messages
            for (int i = messagelist.Count - 1; i >= 0; i--)
            {
                var msg = messagelist[i];
                oldest = Math.Min(oldest, msg.Item1);
                // Skip counting tools as they will be pruned / handled by the prompt builder
                if (msg.Item2.Role == AuthorRole.Tool || (msg.Item2.Role == AuthorRole.Assistant && msg.Item2.ToolCalls.Count > 0))
                    tokensleft -= 0;
                else
                    tokensleft -= LLMEngine.PromptBuilder.GetTokenCount(msg.Item2);
                if (tokensleft <= 0)
                    break;
                LLMEngine.PromptBuilder.InsertMessage(startpos, msg.Item2);
                // check if we need to add a memory
                if (memories?.Count > 0 && !LLMEngine.Settings.MoveAllInsertsToSysPrompt)
                {
                    var foundmemory = memories.GetContentByPosition(entrydepth);
                    if (!string.IsNullOrEmpty(foundmemory))
                    {
                        tokensleft -= LLMEngine.PromptBuilder.GetTokenCount(AuthorRole.System, foundmemory.CleanupAndTrim());
                        if (tokensleft > 0)
                        {
                            LLMEngine.PromptBuilder.InsertMessage(startpos, new SingleMessage(AuthorRole.System, foundmemory.CleanupAndTrim()));
                        }
                    }
                }
                entrydepth++;
            }
            lastSessionID = oldest;
        }

        /// <summary>
        /// Retrieves a chat session with the specified unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the chat session to retrieve.</param>
        /// <returns>The <see cref="ChatSession"/> with the specified identifier, or <see langword="null"/> if no matching
        /// session is found.</returns>
        public ChatSession? GetSessionByID(Guid id) => Sessions.FirstOrDefault(s => s.Guid == id);

        /// <summary>
        /// Retrieves all chat sessions with the specified title
        /// </summary>
        /// <param name="id">The unique identifier of the chat session to retrieve.</param>
        /// <returns>The <see cref="ChatSession"/> with the specified identifier, or <see langword="null"/> if no matching
        /// session is found.</returns>
        public List<ChatSession> GetSessionsByTitle(string title, bool partialok = true) 
        {
            if (partialok)
                return [.. Sessions.Where(s => s.Name.Contains(title, StringComparison.InvariantCultureIgnoreCase))];
            else
                return [.. Sessions.Where(s => s.Name.Equals(title, StringComparison.InvariantCultureIgnoreCase))];
        }

        public List<ChatSession> SearchSessions(string searchstring)
        {
            return [.. Sessions.Where(s => 
                s.Name.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase) ||
                s.Content.Contains(searchstring, StringComparison.InvariantCultureIgnoreCase) ||
                s.MetaData.Keywords.Contains(searchstring))
                ];
        }

        /// <summary>
        /// Retrieves a message with the specified unique identifier.
        /// </summary>
        /// <remarks>This method searches the messages and returns the first match based on the provided GUID. 
        /// If no match is found, the method returns null.</remarks>
        /// <param name="id">The unique GUID of the message to retrieve.</param>
        /// <param name="currentSessionOnly">If set to <see langword="true"/>, the search is limited to the current session only.</param>
        /// <returns>The <see cref="SingleMessage"/> instance with the specified identifier, or <see langword="null"/>  if no
        /// message with the given identifier is found.</returns>
        public SingleMessage? GetMessageByID(Guid id, bool currentSessionOnly = false)
        {
            if (currentSessionOnly)
                return CurrentSession.Messages.FirstOrDefault(m => m.Guid == id);
            foreach (var session in Sessions)
            {
                var msg = session.Messages.FirstOrDefault(m => m.Guid == id);
                if (msg != null)
                    return msg;
            }
            return null;
        }

        /// <summary>
        /// Logs a message in the current chat session and returns the created message object.
        /// </summary>
        /// <remarks>If no chat sessions exist, a new session is created before logging the message. The
        /// method ensures that any predefined "thinking" markers in the message content are removed before logging.
        /// Events are raised before and after the message is added to the current session.</remarks>
        /// <param name="role">The role of the author of the message, such as user or bot.</param>
        /// <param name="msg">The content of the message to be logged. If the message contains specific markers, they will be removed.</param>
        /// <param name="user">The persona representing the user who authored the message.</param>
        /// <param name="bot">The persona representing the bot associated with the message.</param>
        /// <returns>A <see cref="SingleMessage"/> object representing the logged message, including metadata such as the author
        /// role, timestamp, and cleaned message content.</returns>
        public SingleMessage LogMessage(AuthorRole role, string msg, BasePersona user, BasePersona bot)
        {
            var message = new SingleMessage(
                role, 
                DateTime.Now,
                msg.RemoveThinkingBlocks(LLMEngine.Instruct.ThinkingStart, LLMEngine.Instruct.ThinkingEnd), 
                bot.GetIdentifier(), 
                user.GetIdentifier());
            LogMessage(message);
            return message;
        }

        /// <summary>
        /// Logs a pre-constructed message in the current chat session.
        /// </summary>
        /// <param name="single">A <seealso cref="SingleMessage"/> class instance representing the message to be logged. The message should already contain all necessary metadata.</param>
        public void LogMessage(SingleMessage single)
        {
            if (Sessions.Count == 0)
                Sessions.Add(CreateChatSession());
            if (single.ToolCalls?.Count > 0)
                single.Hidden = true;
            OnBeforeMessageAdded?.Invoke(this, single);
            CurrentSession.Messages.Add(single);
            OnMessageAdded?.Invoke(this, single);
            LLMEngine.RaiseOnHistoryLogged(single);
        }

        /// <summary>
        /// Removes the last message from the current chat session, if any messages exist.
        /// </summary>
        /// <returns></returns>
        public bool RemoveLast(bool hidden = false)
        {
            if (CurrentSession.Messages.Count > 0)
            {
                CurrentSession.Messages.RemoveAt(CurrentSession.Messages.Count - 1);
                if (hidden)
                {
                    while (CurrentSession.Messages.Count > 0 && CurrentSession.Messages[^1].Hidden)
                        CurrentSession.Messages.RemoveAt(CurrentSession.Messages.Count - 1);
                }
                return true;
            }
            return false;

        }

        /// <summary>
        /// Removes all messages from the current chat session, effectively clearing the chat history.
        /// </summary>
        public void ClearHistory() => CurrentSession.Messages.Clear();

        /// <summary>
        /// Gets the last message from the current chat session, or null if there are no messages.
        /// </summary>
        /// <returns></returns>
        public SingleMessage? LastMessage() => CurrentSession.Messages.Count >= 1 ? CurrentSession.Messages.Last() : null;

        /// <summary>
        /// Removes all embedded summaries from the sessions.
        /// </summary>
        /// <remarks>This method iterates through all sessions and clears their embedded summaries. After
        /// calling this method, the EmbedSummary property of each session will be empty.</remarks>
        public void RemoveEmbeds()
        {
            foreach (var item in Sessions)
            {
                item.EmbedSummary = [];
            }
        }

        /// <summary>
        /// Generate title, summary and embeddings for all the sessions in the chatlog
        /// </summary>
        /// <returns></returns>
        public async Task UpdateAllSessions()
        {
            foreach (var item in Sessions)
            {
                await item.UpdateSession().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Starts a new chat session, optionally archiving the current session if it meets certain criteria.
        /// </summary>
        /// <remarks>This method creates a new chat session and raises an event to notify listeners about
        /// the new session. If the current session is archived, its scenario is updated based on the system settings,
        /// and the session is saved before being replaced. If the current session is not archived, its messages are
        /// cleared instead. Additionally, an optional system message can be generated to provide context about the new session,
        /// including the time elapsed since the last session, if applicable.</remarks>
        /// <param name="archivePreviousSession">A value indicating whether the current session should be archived before starting a new session. If <see
        /// langword="true"/>, the current session is archived if it contains more than two messages; otherwise, it is
        /// reset. The default value is <see langword="true"/>.</param>
        /// <returns></returns>
        public async Task StartNewChatSession(bool archivePreviousSession = true, bool addDateInfo = false)
        {
            // Save current session if it has enough messages otherwise just reset it
            if (archivePreviousSession && CurrentSession.Messages.Count > 2)
            {
                CurrentSession.Scenario = LLMEngine.Settings.ScenarioOverride;
                await CurrentSession.UpdateSession().ConfigureAwait(false);
                // reset session ID
                CurrentSessionID = -1;
                // Create new session
                var newsession = CreateChatSession();
                newsession.Enabled = false;
                Sessions.Add(newsession);
                if (LLMEngine.Settings.SessionMemorySystem && LLMEngine.Settings.PeriodSummaryReservedTokens > 0)
                {
                    try
                    {
                        await UpdatePeriodSummaries().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LLMEngine.Logger?.LogError(ex, "Failed to update period summaries: {message}", ex.Message);
                    }
                }
            }
            else
            {
                CurrentSession.Messages.Clear();
            }
            OnNewSession?.Invoke(this, CurrentSession);
            await LLMEngine.Bot.Brain.ProcessPreviousSession().ConfigureAwait(false);
            if (!addDateInfo)
                return;
            // Generate new system message about the new session
            var msgtxt = "We're {{day}} the {{date}} at {{time}}.";
            if (Sessions.Count > 1)
            {
                var lastsession = Sessions[^2];
                var timespan = DateTime.Now - lastsession.EndTime;
                if (timespan.Days > 1)
                    msgtxt += " Your last chat was " + timespan.Days.ToString() + " days ago.";
                else if (timespan.Days == 1)
                    msgtxt += " The last chat was yesterday.";
                else if (timespan.Hours > 1)
                    msgtxt += " The last chat was " + timespan.Hours + " hours ago.";
                else
                    msgtxt += " The last chat was " + ((int)timespan.TotalMinutes).ToString() + " minutes ago.";
            }
            LogMessage(new SingleMessage(AuthorRole.System, LLMEngine.Bot.ReplaceMacros(msgtxt)));
        }

        /// <summary>
        /// Divides a raw chatlog (likely imported from Silly Tavern) into sessions using timestamps and specific messages to determine the start of a new session
        /// </summary>
        public virtual void DivideChatIntoSessions()
        {
            if (CurrentSession.Messages.Count == 0)
                return;
            List<SingleMessage> Messages = [.. CurrentSession.Messages];
            Sessions.Clear();

            // Fix potential date problems
            var firstdate = default(DateTime);
            foreach (var item in Messages)
            {
                if (item.Date != default)
                {
                    firstdate = item.Date;
                    break;
                }
            }
            var previousmess = Messages.First();
            previousmess.Date = firstdate;
            foreach (var item in Messages)
            {
                if (item.Date == default || item.Date.Year == 1)
                {
                    item.Date = previousmess.Date + new TimeSpan(0, 0, 15);
                }
                previousmess = item;
            }
            // I want to check if msg.Message starts with "*We're " followed by a number to consider it as a new session
            string pattern = @"^\*We're \d+";
            // iterate through Messages, and divide them into sessions by checking the time between messages or the presence of a sentence starting by "*We're [number]" or a "Hello" message from user
            var currentsession = CreateChatSession();
            var lastmsg = Messages.First();
            currentsession.StartTime = lastmsg.Date;
            currentsession.Messages.Add(lastmsg);
            var sessionmsgcount = 1;
            for (int i = 1; i < Messages.Count; i++)
            {
                var msg = Messages[i];
                var timespan = msg.Date - lastmsg.Date;
                var totaltimespan = msg.Date - currentsession.StartTime;
                var validinitmessage = (msg.Role == AuthorRole.User || msg.Role == AuthorRole.System) && (
                    Regex.IsMatch(msg.Message, pattern) ||
                    msg.Message.StartsWith("Hello ") || msg.Message.StartsWith("Hi!") || msg.Message.StartsWith("Hi ") ||
                    msg.Message.StartsWith("*" + LLMEngine.User.Name + " comes back ") || msg.Message.StartsWith("*" + LLMEngine.User.Name + " logged in.") || 
                    msg.Message.StartsWith("*A few days later") ||
                    msg.Message.StartsWith("*We're a day later") || msg.Message.StartsWith("*We're a week"));
                // Minimum session length should be about 30 messages
                if (sessionmsgcount > 35 && (timespan.TotalDays >= 1 || (totaltimespan.TotalDays > 3 && sessionmsgcount > 120) || validinitmessage))
                {
                    currentsession.EndTime = lastmsg.Date;
                    if (currentsession.Messages.Count > 0)
                        Sessions.Add(currentsession);
                    currentsession = CreateChatSession();
                    sessionmsgcount = 0;
                    currentsession.StartTime = msg.Date;
                }
                currentsession.Messages.Add(msg);
                sessionmsgcount++;
                lastmsg = msg;
            }
            currentsession.EndTime = lastmsg.Date;
            if (currentsession.Messages.Count > 0)
                Sessions.Add(currentsession);
            Messages.Clear();
        }

        /// <summary>
        /// Retrieves information about the current chat session, including the total token count and the session
        /// duration.
        /// </summary>
        /// <returns>A tuple containing the total number of tokens in the current chat session and the duration of the session.
        /// The token count is calculated based on the content of all messages in the session, and the duration is the 
        /// time span between the first and last message timestamps. If the session contains one or no messages,  the
        /// token count will be 0 and the duration will be <see cref="TimeSpan.Zero"/>.</returns>
        public (int tokens, TimeSpan duration) GetCurrentChatSessionInfo()
        {
            if (CurrentSession.Messages.Count <= 1)
            {
                return (0, TimeSpan.Zero);
            }
            var messagesCopy = CurrentSession.Messages.ToList(); // Create a copy of the collection
            var sb = new StringBuilder();
            foreach (var message in messagesCopy)
            {
                if (!message.Hidden)
                    sb.Append(message.ToTextCompletion());
            }
            var tokencount = LLMEngine.GetTokenCount(sb.ToString());
            var duration = CurrentSession.Messages.Last().Date - CurrentSession.Messages.First().Date;
            return (tokencount, duration);
        }

        /// <summary>
        /// Fetches the last message from a specified author role within the current chat session.
        /// </summary>
        /// <param name="author"></param>
        /// <returns></returns>
        public SingleMessage? GetLastFromInSession(AuthorRole author)
        {
            return CurrentSession.Messages.LastOrDefault(m => m.Role == author && (m.ToolCalls == null || m.ToolCalls.Count == 0));
        }

        /// <summary>
        /// Fetches the last message from a specified author role, searching through previous sessions if necessary.
        /// </summary>
        /// <param name="author"></param>
        /// <returns></returns>
        public SingleMessage? GetLastMessageFrom(AuthorRole author)
        {
            var res = CurrentSession.Messages.LastOrDefault(m => m.Role == author);
            // if not found, search in previous sessions until found
            if (res == null)
            {
                for (int i = Sessions.Count - 2; i >= 0; i--)
                {
                    res = Sessions[i].Messages.LastOrDefault(m => m.Role == author && (m.ToolCalls == null || m.ToolCalls.Count == 0));
                    if (res != null)
                        break;
                }
            }
            return res;
        }
        public SingleMessage? GetLastMessageFrom(BasePersona author)
        {
            var res = CurrentSession.Messages.LastOrDefault(m => m.Sender == author);
            // if not found, search in previous sessions until found
            if (res == null)
            {
                for (int i = Sessions.Count - 2; i >= 0; i--)
                {
                    res = Sessions[i].Messages.LastOrDefault(m => m.Sender == author && (m.ToolCalls == null || m.ToolCalls.Count == 0));
                    if (res != null)
                        break;
                }
            }
            return res;
        }

        /// <summary>
        /// Retrieves the raw dialog content from the chat log, formatted according to specified parameters and adhering to token limits.
        /// This will go over multiple chat sessions if needed, starting from the current one and going back until the token limit is reached. 
        /// </summary>
        /// <param name="maxTokens">Maximum number of tokens to include in the dialog content.</param>
        /// <param name="ignoresystem">A value indicating whether system messages should be excluded from the output. Set to <see langword="true"/> to ignore system messages; otherwise, <see langword="false"/>.</param>
        /// <param name="showHidden">A value indicating whether hidden messages should be included in the output. Set to <see langword="true"/> to include hidden messages; otherwise, <see langword="false"/>.</param>
        /// <param name="middleCut">A value indicating whether to perform a middle cut when truncating messages to fit within the token limit, or cut the end</param>
        /// <param name="maxAge">The maximum age of messages to include in the dialog content. Messages older than this will be excluded.</param>
        /// <returns>The raw dialog content as a string.</returns>
        public string GetRawDialogs(int maxTokens, bool ignoresystem, bool showHidden = false, bool middleCut = false, TimeSpan? maxAge = null)
        {
            var currentSessionID = CurrentSessionID == -1 ? Sessions.Count - 1 : CurrentSessionID;
            var sb = new StringBuilder();
            var tokensleft = maxTokens;
            while (tokensleft > 0 && currentSessionID >= 0) 
            {
                var session = Sessions[currentSessionID];
                if (maxAge.HasValue && DateTime.Now - session.EndTime > maxAge.Value)
                    break;
                var sessionContent = session.GetRawDialogs(tokensleft, ignoresystem, false, showHidden, middleCut);
                tokensleft -= LLMEngine.GetTokenCount(sessionContent);
                if (tokensleft > 0)
                    sb.Insert(0, sessionContent);
                if (tokensleft <= 50)
                    break;
                currentSessionID--;
            }
            return sb.ToString();
        }


        /// <summary>
        /// Embedding of all the messages in the chatlog
        /// </summary>
        /// <param name="log"></param>
        /// <returns></returns>
        public async Task EmbedChatSessions()
        {
            if (!LLMEngine.Settings.RAGEnabled)
                return;
            // Embed all the messages in the chatlog
            foreach (var session in Sessions)
            {
                await session.BuildEmbedding().ConfigureAwait(false);
            }
        }

        public virtual void SaveToFile(string pPath) 
        {
            var content = JsonConvert.SerializeObject(this, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            // create directory if it doesn't exist
            var dir = Path.GetDirectoryName(pPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(pPath, content);
        }

        public virtual void DeleteAll(bool AreYouSure)
        {
            if (AreYouSure)
                Sessions.Clear();
        }

    }
}
