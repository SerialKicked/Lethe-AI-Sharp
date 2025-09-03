using AIToolkit.Agent;
using AIToolkit.API;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using AIToolkit.Memory;
using CommunityToolkit.HighPerformance;
using LLama.Sampling;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Files
{
    public enum SessionHandling { CurrentOnly, FitAll }

    public class Chatlog : BaseFile, IFile
    {
        public string Name { get; set; } = string.Empty;
        public int CurrentSessionID { get; set; } = -1;
        public readonly List<ChatSession> Sessions = [];
        private int lastSessionID = -1;

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

        [JsonIgnore] public EventHandler<SingleMessage>? OnMessageAdded;

        private void RaiseOnMessageAdded(SingleMessage message) => OnMessageAdded?.Invoke(this, message);

        /// <summary>
        /// Factory method for creating ChatSession instances. Override this in derived classes to provide custom ChatSession implementations.
        /// </summary>
        /// <returns>A new ChatSession instance</returns>
        protected virtual ChatSession CreateChatSession()
        {
            return new ChatSession();
        }

        public string GetPreviousSummaries(int maxTokens, string sectionHeader = "##", bool allowRP = true)
        {
            var res = new StringBuilder();
            var tokensleft = maxTokens;
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

            for (int i = entrydepth; i >= 0; i--)
            {
                var session = Sessions[i];
                if (LLMSystem.usedGuidInSession.Contains(session.Guid) || string.IsNullOrWhiteSpace(session.Content))
                    continue;
                if (!allowRP && session.MetaData.IsRoleplaySession)
                    continue;
                var sb = new StringBuilder();
                sb.AppendLinuxLine($"{sectionHeader} {session.Name}");
                sb.AppendLinuxLine(session.GetRawMemory(false, LLMSystem.Bot.DatesInSessionSummaries));
                var tks = LLMSystem.GetTokenCount(sb.ToString());
                if (tks <= tokensleft)
                {
                    res.Insert(0, sb.ToString());
                }
                else
                    break;
                tokensleft = maxTokens - LLMSystem.GetTokenCount(res.ToString());
            }
            return res.ToString();
        }

        public void AddHistoryToPrompt(SessionHandling sessionHandling, int maxTokens, PromptInserts? memories)
        {
            //var sb = new StringBuilder();
            var tokensleft = maxTokens;
            var entrydepth = 0;
            var startpos = LLMSystem.PromptBuilder!.Count;
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
                tokensleft -= LLMSystem.PromptBuilder.GetTokenCount(msg.Item2.Role, msg.Item2.Message);
                if (tokensleft <= 0)
                    break;
                LLMSystem.PromptBuilder.InsertMessage(startpos, msg.Item2.Role, msg.Item2.Message);
                // check if we need to add a memory
                if (memories?.Count > 0 && !LLMSystem.Settings.MoveAllInsertsToSysPrompt)
                {
                    var foundmemory = memories.GetContentByPosition(entrydepth);
                    if (!string.IsNullOrEmpty(foundmemory))
                    {
                        tokensleft -= LLMSystem.PromptBuilder.GetTokenCount(AuthorRole.System, foundmemory.CleanupAndTrim());
                        if (tokensleft > 0)
                        {
                            LLMSystem.PromptBuilder.InsertMessage(startpos, AuthorRole.System, foundmemory.CleanupAndTrim());
                        }
                    }
                }
                entrydepth++;
            }
            lastSessionID = oldest;
        }

        public ChatSession? GetSessionByID(Guid id) => Sessions.FirstOrDefault(s => s.Guid == id);

        public SingleMessage? GetMessageByID(Guid id) => CurrentSession.Messages.FirstOrDefault(m => m.Guid == id);

        public SingleMessage LogMessage(AuthorRole role, string msg, BasePersona user, BasePersona bot)
        {
            if (Sessions.Count == 0)
                Sessions.Add(CreateChatSession());

            // Remove thinking block if any
            var stringfix = msg;
            if (!string.IsNullOrEmpty(LLMSystem.Instruct.ThinkingStart) && stringfix.Contains(LLMSystem.Instruct.ThinkingStart) && stringfix.Contains(LLMSystem.Instruct.ThinkingEnd))
            {
                // remove everything before the thinking end tag (included)
                var idx = stringfix.IndexOf(LLMSystem.Instruct.ThinkingEnd);
                stringfix = stringfix[(idx + LLMSystem.Instruct.ThinkingEnd.Length)..].CleanupAndTrim();
            }

            var single = new SingleMessage(role, DateTime.Now, stringfix, bot.UniqueName, user.UniqueName);
            CurrentSession.Messages.Add(single);
            RaiseOnMessageAdded(single);
            EventBus.Publish(new MessageAddedEvent(single.Guid, role == AuthorRole.User, single.Date));
            return single;
        }

        public SingleMessage LogMessage(SingleMessage single)
        {
            if (Sessions.Count == 0)
                Sessions.Add(CreateChatSession());
            CurrentSession.Messages.Add(single);
            RaiseOnMessageAdded(single);
            EventBus.Publish(new MessageAddedEvent(single.Guid, single.Role == AuthorRole.User, single.Date));
            return single;
        }

        public void RemoveAt(int id) => CurrentSession.Messages.RemoveAt(id);

        public bool RemoveLast()
        {
            if (CurrentSession.Messages.Count > 0)
            {
                CurrentSession.Messages.RemoveAt(CurrentSession.Messages.Count - 1);
                return true;
            }
            return false;

        }

        public void ClearHistory() => CurrentSession.Messages.Clear();

        public SingleMessage? LastMessage() => CurrentSession.Messages.Count >= 1 ? CurrentSession.Messages.Last() : null;

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
                await item.UpdateSession();
            }
        }

        public async Task StartNewChatSession(bool archivePreviousSession = true)
        {
            // Save current session if it has enough messages otherwise just reset it
            if (archivePreviousSession && CurrentSession.Messages.Count > 2)
            {
                CurrentSession.Scenario = LLMSystem.Settings.ScenarioOverride;
                await CurrentSession.UpdateSession();
                EventBus.Publish(new SessionArchivedEvent(CurrentSession.Guid, DateTime.UtcNow));
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
            // Generate new system message about the new session
            var msgtxt = "*We're {{day}} the {{date}} at {{time}}.";
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
            msgtxt += "*";
            LogMessage(AuthorRole.System, LLMSystem.ReplaceMacros(msgtxt, LLMSystem.User, LLMSystem.Bot), LLMSystem.User, LLMSystem.Bot);
            LLMSystem.Bot.Brain.OnNewSession();
        }

        /// <summary>
        /// Divides a raw chatlog (likely imported from ST) into sessions using timestamps and specific messages to determine the start of a new session
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
                    msg.Message.StartsWith("*" + LLMSystem.User.Name + " comes back ") || msg.Message.StartsWith("*" + LLMSystem.User.Name + " logged in.") || 
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
                sb.Append(LLMSystem.Instruct.FormatSingleMessage(message));
            }
            var tokencount = LLMSystem.GetTokenCount(sb.ToString());
            var duration = CurrentSession.Messages.Last().Date - CurrentSession.Messages.First().Date;
            return (tokencount, duration);
        }

        public string GetLastUserMessageContent()
        {
            return CurrentSession.Messages.LastOrDefault(m => m.Role == AuthorRole.User)?.Message ?? string.Empty;
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
