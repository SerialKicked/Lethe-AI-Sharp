using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;
using AIToolkit.LLM;
using AIToolkit.API;
using System.Collections.Generic;

namespace AIToolkit.Files
{
    public enum SessionHandling { CurrentOnly, FitAll }

    public class SingleMessage(AuthorRole role, DateTime date, string mess, string chara, string user)
    {
        [JsonIgnore] public Guid Guid = Guid.NewGuid();
        public AuthorRole Role = role;
        public string Message = mess;
        public DateTime Date = date;
        public string CharID = chara;
        public string UserID = user;
        [JsonIgnore] public BasePersona User => 
            !string.IsNullOrEmpty(UserID) && LLMSystem.LoadedPersonas.TryGetValue(UserID, out var u) ? u : LLMSystem.User;
        [JsonIgnore] public BasePersona Bot => 
            !string.IsNullOrEmpty(CharID) && LLMSystem.LoadedPersonas.TryGetValue(CharID, out var c) ? c : LLMSystem.Bot;
        [JsonIgnore] public BasePersona? Sender => 
            Role == AuthorRole.User? User : Role == AuthorRole.Assistant ? Bot : null;
    }

    public class ChatSession : KeywordEntry, IEmbed
    {
        [JsonIgnore] public Guid Guid { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string[] Sentiments { get; set; } = [];
        public string[] Associations { get; set; } = [];

        public float[] EmbedTitle { get; set; } = [];
        public float[] EmbedSummary { get; set; } = [];
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<SingleMessage> Messages { get; set; } = [];
        /// <summary>
        /// If set to true, this memory will always be included in the prompt
        /// </summary>
        public bool Sticky { get; set; } = false;
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Mostly placeholder for when proper sentiment analysis is implemented
        /// </summary>
        /// <returns></returns>
        public async Task<string[]> GenerateSentiment()
        {
            LLMSystem.NamesInPromptOverride = false;
            var msgtxt = "You are an automated system designed to associate chat sessions and stories with a list of sentiments." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine + LLMSystem.NewLine +
                "" + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of sentiments or moods {{char}} would associate with this chat. 1 to 4 words max." + LLMSystem.NewLine +
                "Example: Happiness, Playfulness, Surprise";
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);
            var tokencount = LLMSystem.GetTokenCount(msg);

            var availtokens = LLMSystem.MaxContextLength - tokencount - 1024;
            var docs = GetRawDialogs(availtokens, false);
            msgtxt = "You are an automated system designed to associate chat sessions and stories with a list of sentiments." +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine + LLMSystem.NewLine +
                docs + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of sentiments or moods {{char}} would associate with this chat. 1 to 4 words max." + LLMSystem.NewLine +
                "Example: Happiness, Playfulness, Surprise";
            var prompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = prompt + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            llmparams.Max_length = 1024;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;
            llmparams.Temperature = 0.5f;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            LLMSystem.NamesInPromptOverride = null;
            // convert the comma-separated list into an array
            var res = finalstr.Split(',');

            return res;
        }

        public async Task<string[]> GenerateKeywords()
        {
            LLMSystem.NamesInPromptOverride = false;
            var msgtxt = "You are an automated system designed to associate chat sessions and stories with a list of relevant keywords." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine + LLMSystem.NewLine +
                "" + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of keywords {{char}} would associate with this chat. The list must be between 1 and 5 keywords long.";
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);
            var tokencount = LLMSystem.GetTokenCount(msg);

            var availtokens = LLMSystem.MaxContextLength - tokencount - 1024;
            var docs = GetRawDialogs(availtokens, false);
            msgtxt = "You are an automated system designed to associate chat sessions and stories with a list of relevant keywords." + LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine + LLMSystem.NewLine +
                docs + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of keywords {{char}} would associate with this chat. The list must be between 1 and 5 keywords long.";
            var prompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = prompt + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            llmparams.Max_length = 1024;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;
            llmparams.Temperature = 0.5f;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            LLMSystem.NamesInPromptOverride = null;
            // convert the comma-separated list into an array
            var res = finalstr.Split(',');
            return res;
        }

        public async Task<string> GenerateNewSummary()
        {
            LLMSystem.NamesInPromptOverride = false;
            var msgtxt = "You are an automated system designed to summarize chat sessions and stories." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine +
                "## Starting Date: " + StringExtensions.DateToHumanString(StartTime) + LLMSystem.NewLine +
                "## Duration: " + StringExtensions.TimeSpanToHumanString(Duration) + LLMSystem.NewLine + LLMSystem.NewLine +
                "" + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Write a summary of the exchange between {{user}} and {{char}} shown above. The summary must be written from {{char}}'s perspective. Do not introduce the characters. Do not add a title, just write the summary directly.";
            if (Messages.Count > 50)
            {
                msgtxt += " The summary should be 2 to 4 paragraphs long.";
            }
            else
            {
                msgtxt += " The summary should be 1 to 2 paragraphs long.";
            }
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);
            var tokencount = LLMSystem.GetTokenCount(msg);

            var availtokens = LLMSystem.MaxContextLength - tokencount - 1024;
            var docs = GetRawDialogs(availtokens, false);
            msgtxt = "You are an automated system designed to summarize chat sessions and stories." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine +
                "## Starting Date: " + StringExtensions.DateToHumanString(StartTime) + LLMSystem.NewLine +
                "## Duration: " + StringExtensions.TimeSpanToHumanString(Duration) + LLMSystem.NewLine + LLMSystem.NewLine +
                docs + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Write a summary of the exchange between {{user}} and {{char}} shown above. The summary must be written from {{char}}'s perspective. Do not introduce the characters. Do not add a title, just write the summary directly.";
            if (Messages.Count > 50)
            {
                msgtxt += " The summary should be 2 to 4 paragraphs long.";
            }
            else
            {
                msgtxt += " The summary should be 1 to 2 paragraphs long.";
            }

            var prompt = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = prompt + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            llmparams.Max_length = 1024;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            llmparams.Grammar = string.Empty;
            llmparams.Temperature = 0.5f;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            LLMSystem.NamesInPromptOverride = null;
            return finalstr.Trim();
        }

        public static async Task<string> GenerateNewTitle(string sum)
        {
            LLMSystem.NamesInPromptOverride = false;
            var msgtxt = "You are an automated system designed to give titles to summaries." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Summary:" + LLMSystem.NewLine +
                sum + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Instruction:" + LLMSystem.NewLine +
                "Give a title to the summary above. This title should be a single short and descriptive sentence. Write only the title, nothing else.";
            var msg = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, msgtxt);
            var res = msg + LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);

            var llmparams = LLMSystem.Sampler.GetCopy();
            llmparams.Prompt = res;
            llmparams.Max_length = 350;
            llmparams.Temperature = 0.4f;
            llmparams.Max_context_length = LLMSystem.MaxContextLength;
            var finalstr = await LLMSystem.SimpleQuery(llmparams);
            // remove any " character from the finalstr
            finalstr = finalstr.Replace("\"", "").Trim();
            LLMSystem.NamesInPromptOverride = null;
            return finalstr;
        }

        public async Task GenerateEmbeds()
        {
            if (!RAGSystem.Enabled)
                return;
            EmbedTitle = await RAGSystem.EmbeddingText(Title);
            EmbedSummary = await RAGSystem.EmbeddingText(Summary);
        }

        public string GetRawDialogs(int maxTokens, bool ignoresystem)
        {
            var sb = new StringBuilder();
            var totaltks = maxTokens;

            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var msg = Messages[i];
                var text = string.Empty;
                switch (msg.Role)
                {
                    case AuthorRole.System:
                    case AuthorRole.SysPrompt:
                        if (ignoresystem)
                            continue;
                        text = msg.Message.StartsWith('*') ? LLMSystem.NewLine + msg.Message.Trim() + LLMSystem.NewLine : LLMSystem.NewLine + "*" + msg.Message.Trim() + "*" + LLMSystem.NewLine;
                        break;
                    case AuthorRole.User:
                    case AuthorRole.Assistant:
                        text = "**" + msg.Sender?.Name + ":** " + msg.Message.Trim().Replace(LLMSystem.NewLine, " ") + LLMSystem.NewLine;
                        break;
                }
                if (text == string.Empty)
                    continue;
                var tks = maxTokens == int.MaxValue ? 0 : LLMSystem.GetTokenCount(text);
                totaltks -= tks;
                if (totaltks <= 0)
                    return sb.ToString();
                sb.Insert(0, text);
            }
            return sb.ToString();
        }

        public string GetFormatedDialogs(int maxTokens, ref int currentDepth, Dictionary<int, string>? memories)
        {
            var sb = new StringBuilder();
            var totaltks = maxTokens;
            var mems = memories ?? [];
            var entrydepth = currentDepth;

            void InsertMemories()
            {
                if (!mems.TryGetValue(entrydepth, out string? value) || string.IsNullOrEmpty(value))
                    return;
                var formattedmemory = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, value);
                var tksmem = LLMSystem.GetTokenCount(formattedmemory);
                if (tksmem <= totaltks)
                {
                    totaltks -= tksmem;
                    sb.Insert(0, formattedmemory);
                }
            }

            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var msg = Messages[i];
                var res = LLMSystem.Instruct.FormatSingleMessage(msg);
                var tks = LLMSystem.GetTokenCount(res);
                totaltks -= tks;
                if (totaltks <= 0)
                    return sb.ToString();
                sb.Insert(0, res);
                InsertMemories();
                entrydepth++;
                currentDepth = entrydepth;
            }
            return sb.ToString();
        }

        public string GetRawSummary(string title = "Chat Session", bool NaturalLanguage = false)
        {
            var sb = new StringBuilder();
            var tit = LLMSystem.ReplaceMacros(title);
            if (!NaturalLanguage)
            {
                sb.AppendLinuxLine("# " + tit);
                if (StartTime.Date == EndTime.Date)
                    sb.AppendLinuxLine("## Date: " + StartTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(StartTime));
                else
                    sb.AppendLinuxLine("## From " + StartTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(StartTime) + " to " + EndTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(EndTime));
                sb.AppendLinuxLine("## Title: " + Title.Trim());
                sb.AppendLinuxLine("## Summary: " + LLMSystem.NewLine + Summary.Replace("\n\n", " ").Trim() + LLMSystem.NewLine);
            }
            else
            {
                if (StartTime.Date == EndTime.Date)
                    sb.AppendLinuxLine($"{tit} on the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)}: *{Title.Trim()}* {Summary.RemoveNewLines()}");
                else
                    sb.AppendLinuxLine($"{tit} from the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)}, to the {EndTime.DayOfWeek} {StringExtensions.DateToHumanString(EndTime)}: *{Title.Trim()}* {Summary.RemoveNewLines()}");
            }
            return sb.ToString();
        }

        public string GetRawMemory(bool Natural = false)
        {
            var sb = new StringBuilder();
            if (!Natural)
            {
                sb.AppendLinuxLine("# " + Title.Trim());
                if (StartTime.Date == EndTime.Date)
                    sb.AppendLinuxLine("## Date: " + StartTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(StartTime));
                else
                    sb.AppendLinuxLine("## From " + StartTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(StartTime) + " to " + EndTime.DayOfWeek.ToString() + " " + StringExtensions.DateToHumanString(EndTime));
                sb.AppendLinuxLine("## Memory: " + Summary.RemoveNewLines());
            }
            else
            {
                if (StartTime.Date == EndTime.Date)
                    sb.AppendLinuxLine($"On {StartTime.DayOfWeek}, {StringExtensions.DateToHumanString(StartTime)}, the following events took place from {LLMSystem.Bot.Name}'s perspective. {Summary.RemoveNewLines()}");
                else
                    sb.AppendLinuxLine($"Between the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)} and the {EndTime.DayOfWeek} {StringExtensions.DateToHumanString(EndTime)}, the following event took places from {LLMSystem.Bot.Name}'s perspective. {Summary.RemoveNewLines()}");
            }

            return sb.ToString();
        }

        public string GetFormatedSummary(string title = "Chat Session", bool NaturalLanguage = false)
        {
            return LLMSystem.Instruct.FormatSingleMessage(new SingleMessage(AuthorRole.System, DateTime.Now, GetRawSummary(title, NaturalLanguage), LLMSystem.Bot.UniqueName, LLMSystem.User.UniqueName));
        }

        public int GetFormatedSummaryTokenCount(bool NaturalLanguage = false)
        {
            return LLMSystem.GetTokenCount(GetFormatedSummary(NaturalLanguage: NaturalLanguage));
        }
    }

    public class Chatlog : BaseFile, IFile
    {
        public string Name { get; set; } = string.Empty;
        public int CurrentSessionID { get; set; } = -1;
        public readonly List<ChatSession> Sessions = [];
        private int lastSessionID = -1;

        [JsonIgnore] public ChatSession CurrentSession => CurrentSessionID >= 0 && CurrentSessionID < Sessions.Count ? Sessions[CurrentSessionID] : Sessions.Last();

        [JsonIgnore] public EventHandler<SingleMessage>? OnMessageAdded;

        private void RaiseOnMessageAdded(SingleMessage message) => OnMessageAdded?.Invoke(this, message);

        public string GetPreviousSummaries(int maxTokens, string sectionHeader = "##")
        {
            var res = new StringBuilder();
            var tokensleft = maxTokens;
            var entrydepth = lastSessionID - 1;
            if (entrydepth < 0)
                return string.Empty;

            for (int i = entrydepth; i >= 0; i--)
            {
                var session = Sessions[i];
                if (LLMSystem.usedGuidInSession.Contains(session.Guid) || string.IsNullOrWhiteSpace(session.Summary))
                    continue;
                var sb = new StringBuilder();
                sb.AppendLinuxLine($"{sectionHeader} {session.Title}");
                sb.AppendLinuxLine($"Between {session.StartTime.DayOfWeek} {StringExtensions.DateToHumanString(session.StartTime)} and {session.EndTime.DayOfWeek} {StringExtensions.DateToHumanString(session.EndTime)}, the following events took places from {LLMSystem.Bot.Name}'s perspective: {session.Summary.RemoveNewLines()}").AppendLinuxLine();
                var tks = LLMSystem.GetTokenCount(sb.ToString());
                if (tks <= tokensleft)
                {
                    tokensleft -= tks;
                    res.Insert(0, sb.ToString());
                }
                else
                    break;
                tokensleft = maxTokens - LLMSystem.GetTokenCount(res.ToString());
            }
            return res.ToString();
        }

        public string GetMessageHistory(SessionHandling sessionHandling, int maxTokens, Dictionary<int, string>? memories)
        {
            var sb = new StringBuilder();
            var tokensleft = maxTokens;
            var mems = memories ?? [];
            var entrydepth = 0;

            /// <summary> Insert WorldInfo memories into the chatlog </summary>
            void InsertMemories()
            {
                if (!mems.TryGetValue(entrydepth, out string? value) || string.IsNullOrEmpty(value))
                    return;
                var formattedmemory = LLMSystem.Instruct.FormatSinglePrompt(AuthorRole.System, LLMSystem.User, LLMSystem.Bot, value);
                var tksmem = LLMSystem.GetTokenCount(formattedmemory);
                if (tksmem <= tokensleft)
                {
                    tokensleft -= tksmem;
                    sb.Insert(0, formattedmemory);
                }
            }

            // add all messages together in the same list
            var messagelist = new List<(int, SingleMessage)>();
            var curSessionID = CurrentSessionID == -1 ? Sessions.Count - 1 : CurrentSessionID;
            // make a list of all messages
            if (sessionHandling == SessionHandling.FitAll)
            {
                for (int i = 0; i <= curSessionID; i++)
                {
                    foreach (var msg in Sessions[i].Messages)
                    {
                        messagelist.Add((i,msg));
                    }
                }
            }
            else
            {
                foreach (var msg in CurrentSession.Messages)
                {
                    messagelist.Add((curSessionID, msg));
                }
            }

            var oldest = int.MaxValue;
            // iterate through the messages in reverse order until we reach the token limit or end of messages
            for (int i = messagelist.Count - 1; i >= 0; i--)
            {
                var msg = messagelist[i];
                oldest = Math.Min(oldest, msg.Item1);
                var res = LLMSystem.Instruct.FormatSingleMessage(msg.Item2);
                var tks = LLMSystem.GetTokenCount(res);
                tokensleft -= tks;
                if (tokensleft <= 0)
                    break;
                sb.Insert(0, res);
                // check if we need to add a memory
                InsertMemories();
                entrydepth++;
            }
            lastSessionID = oldest;
            // return the result
            return sb.ToString();
        }

        public ChatSession? GetSessionByID(Guid id) => Sessions.FirstOrDefault(s => s.Guid == id);

        public SingleMessage? GetMessageByID(Guid id) => CurrentSession.Messages.FirstOrDefault(m => m.Guid == id);

        public SingleMessage LogMessage(AuthorRole role, string msg, BasePersona user, BasePersona bot)
        {
            if (Sessions.Count == 0)
                Sessions.Add(new ChatSession());
            var single = new SingleMessage(role, DateTime.Now, msg, bot.UniqueName, user.UniqueName);
            CurrentSession.Messages.Add(single);
            RaiseOnMessageAdded(single);
            return single;
        }

        public SingleMessage LogMessage(SingleMessage single)
        {
            if (Sessions.Count == 0)
                Sessions.Add(new ChatSession());
            CurrentSession.Messages.Add(single);
            RaiseOnMessageAdded(single);
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
                item.EmbedTitle = [];
            }
        }

        /// <summary>
        /// Generate title, summary and embeddings for the selected session. Also fixes date issues if any.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static async Task<ChatSession> UpdateSession(ChatSession session)
        {
            if (session.StartTime == default)
            {
                foreach (var item in session.Messages)
                {
                    if (item.Date != default)
                    {
                        session.StartTime = item.Date;
                        break;
                    }
                }
            }
            var previousmess = session.Messages.First();
            foreach (var item in session.Messages)
            {
                if (item.Date == default || item.Date.Year == 1)
                {
                    item.Date = previousmess.Date + new TimeSpan(0,1,0);
                    break;
                }
                previousmess = item;
            }

            session.EndTime = session.Messages.Last().Date;
            var sum = await session.GenerateNewSummary();
            session.Summary = sum;
            session.Title = await ChatSession.GenerateNewTitle(sum);
            session.Sentiments = await session.GenerateSentiment();
            session.Associations = await session.GenerateKeywords();
            await session.GenerateEmbeds();
            return session;
        }

        /// <summary>
        /// Generate title, summary and embeddings for all the sessions in the chatlog
        /// </summary>
        /// <returns></returns>
        public async Task UpdateAllSessions()
        {
            foreach (var item in Sessions)
            {
                await UpdateSession(item);
            }
        }

        public async Task StartNewChatSession(bool archivePreviousSession = true)
        {
            // Save current session if it has enough messages otherwise just reset it
            if (archivePreviousSession && CurrentSession.Messages.Count > 2)
            {
                await UpdateSession(CurrentSession);
                // reset session ID
                CurrentSessionID = -1;
                // Create new session
                var newsession = new ChatSession();
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
        }

        /// <summary>
        /// Divides a raw chatlog (likely imported from ST) into sessions using timestamps and specific messages to determine the start of a new session
        /// </summary>
        public void DivideChatIntoSessions()
        {
            if (CurrentSession.Messages.Count == 0)
                return;
            List<SingleMessage> Messages = new(CurrentSession.Messages);
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
            var currentsession = new ChatSession();
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
                    currentsession = new ChatSession();
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

        public void SaveToFile(string pPath) 
        {
            var content = JsonConvert.SerializeObject(this);
            File.WriteAllText(pPath, content);
        }

        public void DeleteAll(bool AreYouSure)
        {
            if (AreYouSure)
                Sessions.Clear();
        }
    }
}
