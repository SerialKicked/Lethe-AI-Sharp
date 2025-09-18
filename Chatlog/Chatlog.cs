using LetheAISharp.Agent;
using LetheAISharp.API;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using CommunityToolkit.HighPerformance;
using LLama.Sampling;
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
        public string GetPreviousSummaries(int maxTokens, string sectionHeader = "##", bool allowRP = true, int maxCount = int.MaxValue, HashSet<Guid>? ignoreList = null)
        {
            if (lastSessionID == -1 && Sessions.Count >= 2)
            {
                if (CurrentSessionID != -1 && CurrentSessionID != Sessions.Count -1)
                    lastSessionID = CurrentSessionID - 1;
                else
                    lastSessionID = Sessions.Count - 1;
            }
            var entrydepth = lastSessionID - 1;
            if (entrydepth < 0)
                return string.Empty;

            var usedGuid = ignoreList ?? [];

            // Add the sticky sessions first
            var SelectedSessions = new List<ChatSession>();
            SelectedSessions.AddRange(Sessions.FindAll(e => e.Sticky));
            foreach (var item in SelectedSessions)
                usedGuid.Add(item.Guid);

            // Now check all previous entries
            var tokensleft = maxTokens;
            var count = 0;
            var sb = new StringBuilder();
            for (int i = entrydepth; i >= 0; i--)
            {
                var session = Sessions[i];
                if (usedGuid.Contains(session.Guid) || string.IsNullOrWhiteSpace(session.Content))
                    continue;
                if (!allowRP && session.MetaData.IsRoleplaySession)
                    continue;
                sb.Clear();
                sb.AppendLinuxLine($"{sectionHeader} {session.Name}");
                sb.AppendLinuxLine(session.GetRawMemory(false, LLMEngine.Bot.DatesInSessionSummaries));
                var tks = LLMEngine.GetTokenCount(sb.ToString()) + 1;
                if (tks <= tokensleft)
                {
                    SelectedSessions.Add(session);
                    count++;
                    tokensleft -= tks;
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
                sb.AppendLinuxLine(session.GetRawMemory(false, LLMEngine.Bot.DatesInSessionSummaries));
            }

            return sb.ToString();
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
                    foreach (var msg in Sessions[i].Messages)
                        messagelist.Add((i, msg));
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
                tokensleft -= LLMEngine.PromptBuilder.GetTokenCount(msg.Item2.Role, msg.Item2.Message);
                if (tokensleft <= 0)
                    break;
                LLMEngine.PromptBuilder.InsertMessage(startpos, msg.Item2.Role, msg.Item2.Message);
                // check if we need to add a memory
                if (memories?.Count > 0 && !LLMEngine.Settings.MoveAllInsertsToSysPrompt)
                {
                    var foundmemory = memories.GetContentByPosition(entrydepth);
                    if (!string.IsNullOrEmpty(foundmemory))
                    {
                        tokensleft -= LLMEngine.PromptBuilder.GetTokenCount(AuthorRole.System, foundmemory.CleanupAndTrim());
                        if (tokensleft > 0)
                        {
                            LLMEngine.PromptBuilder.InsertMessage(startpos, AuthorRole.System, foundmemory.CleanupAndTrim());
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
            if (Sessions.Count == 0)
                Sessions.Add(CreateChatSession());

            // Remove thinking block if any
            var stringfix = msg;
            if (!string.IsNullOrEmpty(LLMEngine.Instruct.ThinkingStart) && stringfix.Contains(LLMEngine.Instruct.ThinkingStart) && stringfix.Contains(LLMEngine.Instruct.ThinkingEnd))
            {
                // remove everything before the thinking end tag (included)
                var idx = stringfix.IndexOf(LLMEngine.Instruct.ThinkingEnd);
                stringfix = stringfix[(idx + LLMEngine.Instruct.ThinkingEnd.Length)..].CleanupAndTrim();
            }
            var single = new SingleMessage(role, DateTime.Now, stringfix, bot.UniqueName, user.UniqueName);
            OnBeforeMessageAdded?.Invoke(this, single);
            CurrentSession.Messages.Add(single);
            OnMessageAdded?.Invoke(this, single);
            return single;
        }

        /// <summary>
        /// Logs a pre-constructed message in the current chat session.
        /// </summary>
        /// <param name="single">A <seealso cref="SingleMessage"/> class instance representing the message to be logged. The message should already contain all necessary metadata.</param>
        public void LogMessage(SingleMessage single)
        {
            if (Sessions.Count == 0)
                Sessions.Add(CreateChatSession());
            OnBeforeMessageAdded?.Invoke(this, single);
            CurrentSession.Messages.Add(single);
            OnMessageAdded?.Invoke(this, single);
        }

        /// <summary>
        /// Removes the last message from the current chat session, if any messages exist.
        /// </summary>
        /// <returns></returns>
        public bool RemoveLast()
        {
            if (CurrentSession.Messages.Count > 0)
            {
                CurrentSession.Messages.RemoveAt(CurrentSession.Messages.Count - 1);
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
            }
            else
            {
                CurrentSession.Messages.Clear();
            }
            OnNewSession?.Invoke(this, CurrentSession);
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
            LogMessage(AuthorRole.System, LLMEngine.Bot.ReplaceMacros(msgtxt), LLMEngine.User, LLMEngine.Bot);
        }

        /// <summary>
        /// Divides a raw chatlog (likely imported from Silly Tavern) into sessions using timestamps and specific messages to determine the start of a new session
        /// </summary>
        public void DivideChatIntoSessions()
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
                sb.Append(LLMEngine.Instruct.FormatSingleMessage(message));
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
            return CurrentSession.Messages.LastOrDefault(m => m.Role == author);
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
                    res = Sessions[i].Messages.LastOrDefault(m => m.Role == author);
                    if (res != null)
                        break;
                }
            }
            return res;
        }

        /// <summary>
        /// Embedding of all the messages in the chatlog
        /// </summary>
        /// <param name="log"></param>
        /// <returns></returns>
        public async Task EmbedChatSessions()
        {
            if (!RAGEngine.Enabled)
                return;
            // Embed all the messages in the chatlog
            foreach (var session in Sessions)
            {
                await session.EmbedText().ConfigureAwait(false);
                if (SentimentAnalysis.Enabled)
                    await session.UpdateSentiment().ConfigureAwait(false);
            }
        }
        public virtual void SaveToFile(string pPath) 
        {
            var content = JsonConvert.SerializeObject(this, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
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
