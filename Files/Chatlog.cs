using AIToolkit.Agent;
using AIToolkit.API;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using AIToolkit.Memory;
using CommunityToolkit.HighPerformance;
using Newtonsoft.Json;
using OpenAI.Chat;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AIToolkit.Files
{
    public enum SessionHandling { CurrentOnly, FitAll }

    public class SingleMessage(AuthorRole role, DateTime date, string mess, string chara, string user, bool hidden = false)
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public AuthorRole Role = role;
        public string Message = mess;
        public DateTime Date = date;
        public string CharID = chara;
        public string UserID = user;
        public bool Hidden = hidden;
        public string Note = string.Empty;
        [JsonIgnore] public BasePersona User => 
            !string.IsNullOrEmpty(UserID) && LLMSystem.LoadedPersonas.TryGetValue(UserID, out var u) ? u : LLMSystem.User;
        [JsonIgnore] public BasePersona Bot => 
            !string.IsNullOrEmpty(CharID) && LLMSystem.LoadedPersonas.TryGetValue(CharID, out var c) ? c : LLMSystem.Bot;
        [JsonIgnore] public BasePersona? Sender => 
            Role == AuthorRole.User? User : Role == AuthorRole.Assistant ? Bot : null;

        public string ToTextCompletion()
        {
            return LLMSystem.Instruct.FormatSingleMessage(this);
        }

        public Message ToChatCompletion()
        {
            var addname = LLMSystem.NamesInPromptOverride ?? LLMSystem.Instruct.AddNamesToPrompt;
            if (Role == AuthorRole.System || Role == AuthorRole.SysPrompt)
            {
                addname = false;
            }

            var msg = (addname && Sender != null) ?  Sender.Name + ": " + Message : Message;

            return new Message(TokenTools.InternalRoleToChatRole(Role), msg, addname ? Sender?.Name : null);
        }
    }

    public class ChatSession : KeywordEntry, IEmbed
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        [JsonIgnore] public string Title => MetaData.Title;
        [JsonIgnore] public string Summary => MetaData.Summary;
        public bool FirstPersonSummary { get; set; } = true;

        public SessionMetaInfo MetaData { get; set; } = new();
        public TopicLookup NewTopics { get; set; } = new();

        public string Scenario { get; set; } = string.Empty;

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

        public async Task<SessionMetaInfo> GetSessionInfo()
        {
            var session = new SessionMetaInfo();
            var grammar = await session.GetGrammar();
            if (string.IsNullOrWhiteSpace(grammar))
                throw new Exception("Something went wrong when building summary grammar and json format.");

            LLMSystem.NamesInPromptOverride = false;
            var prefill = LLMSystem.Instruct.PrefillThinking;
            LLMSystem.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMSystem.MaxContextLength - replyln - 20;

            var promptbuild = LLMSystem.Client!.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions and stories." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine +
                "## Starting Date: " + StringExtensions.DateToHumanString(StartTime) + LLMSystem.NewLine +
                "## Duration: " + StringExtensions.TimeSpanToHumanString(Duration) + LLMSystem.NewLine + LLMSystem.NewLine;


            var requestedTask = session.GetQuery();

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

            var docs = GetRawDialogs(availtokens, false, true, false);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);
            promptbuild.AddMessage(AuthorRole.User, requestedTask);

            var ct = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMSystem.Sampler.Temperature > 0.75) ? 0.75 : LLMSystem.Sampler.Temperature, replyln);
            if (ct is GenerationInput input)
            {
                input.Grammar = grammar;
            }
            var finalstr = await LLMSystem.SimpleQuery(ct);
            session = JsonConvert.DeserializeObject<SessionMetaInfo>(finalstr);
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }

        public async Task<TopicLookup> GetResearchTopics()
        {
            var session = new TopicLookup();
            var grammar = await session.GetGrammar();
            if (string.IsNullOrWhiteSpace(grammar))
                throw new Exception("Something went wrong when building summary grammar and json format.");

            LLMSystem.NamesInPromptOverride = false;
            var prefill = LLMSystem.Instruct.PrefillThinking;
            LLMSystem.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMSystem.MaxContextLength - replyln - 20;

            var promptbuild = LLMSystem.Client!.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions and stories." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine +
                "## Starting Date: " + StringExtensions.DateToHumanString(StartTime) + LLMSystem.NewLine +
                "## Duration: " + StringExtensions.TimeSpanToHumanString(Duration) + LLMSystem.NewLine + LLMSystem.NewLine;


            var requestedTask = session.GetQuery();

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

            var docs = GetRawDialogs(availtokens, false, true, false);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);
            promptbuild.AddMessage(AuthorRole.User, requestedTask);

            var ct = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMSystem.Sampler.Temperature > 0.75) ? 0.75 : LLMSystem.Sampler.Temperature, replyln);
            if (ct is GenerationInput input)
            {
                input.Grammar = grammar;
            }
            var finalstr = await LLMSystem.SimpleQuery(ct);
            session = JsonConvert.DeserializeObject<TopicLookup>(finalstr);
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }


        public async Task<string[]> GenerateKeywords()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of keywords {{char}} would associate with this chat. The list must be between 1 and 5 keywords long.";
            var res = await GenerateTaskRes(query, 512, true, false);
            return res.Split(',');
        }

        public async Task<string> GenerateGoals()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a list of the plans they both setup for the near future. This list should contain between 0 and 4 items. Each item should be summarized in a single sentence. If there's no items, don't answer with anything. Make sure those plans aren't already resolved within the span of the dialog." + LLMSystem.NewLine + "Example:" + LLMSystem.NewLine + "- They promised to eat together tomorrow." + LLMSystem.NewLine + "- {{user}} will watch the movie recommanded by {{char}}.";
            var res = await GenerateTaskRes(query, 1024, true, false);
            return res;
        }

        public async Task<bool> IsRoleplay()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, determine if {{user}} and {{char}} are roleplaying a scenario. Respond Yes if they are acting a roleplay. Discussing a future roleplay doesn't count as a roleplay. Respond No if this is a just a chat." + LLMSystem.NewLine + LLMSystem.NewLine + 
                "To qualify as a roleplay, the vast majority of the exchange must follow the following guidelines:" + LLMSystem.NewLine + 
                "- Contains explicit actions (not just discussions)." + LLMSystem.NewLine + 
                "- Both {{user}} and {{char}} are in a situation involving physical contact in a defined location." + LLMSystem.NewLine +
                "- Heavy use of narrative text (between asterisks)" + LLMSystem.NewLine +
                "- Clearly takes place outside of a chat interface." + LLMSystem.NewLine + LLMSystem.NewLine + "Your response must begin by either Yes or No.";
            var res = await GenerateTaskRes(query, 1024, true, false);
            var s = res.ToLowerInvariant().Replace(" ", string.Empty);
            return s.StartsWith("yes");
        }

        public async Task<string> GenerateNewSummary()
        {
         
            var query = (LLMSystem.Bot.FirstPersonSummary) ?
                "Identify the most important elements in the the exchange between {{user}} and {{char}} shown above and write a summary of this exchange. The summary must be written from {{char}}'s perspective. Do not introduce the characters. Do not add a title, just write the summary directly." :
                "Write a detailed summary of the exchange between {{user}} and {{char}} shown above. Do not add a title, just write the summary directly.";
            
            if (Messages.Count > 30)
            {
                query += " The summary should be 2 to 4 paragraphs long.";
            }
            else
            {
                query += " The summary should be 1 to 2 paragraphs long.";
            }
            return await GenerateTaskRes(query, 1024, true, false);
        }

        public static async Task<string> GenerateNewTitle(string sum)
        {
            if (LLMSystem.Client == null)
                return string.Empty;
            LLMSystem.NamesInPromptOverride = false;
            var replyln = 350;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                replyln += 1024;
            var promptBuilder = LLMSystem.Client.GetPromptBuilder();
            var msgtxt = "You are an automated system designed to give titles to summaries." + LLMSystem.NewLine +
                LLMSystem.NewLine + "# Summary:" + LLMSystem.NewLine + LLMSystem.NewLine + sum;
            promptBuilder.AddMessage(AuthorRole.SysPrompt, msgtxt);
            promptBuilder.AddMessage(AuthorRole.User, "Give a title to the summary above. This title should be a single short and descriptive sentence. Write only the title, nothing else.");
            var temp = LLMSystem.Sampler.Temperature;
            if (temp > 0.5f)
                temp = 0.5f;
            var genparam = promptBuilder.PromptToQuery(AuthorRole.Assistant, temp, replyln);
            var finalstr = await LLMSystem.SimpleQuery(genparam);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            finalstr = finalstr.Replace("\"", "").Trim();
            LLMSystem.NamesInPromptOverride = null;
            return finalstr;
        }

        public async Task<string> GenerateTaskRes(string requestedTask, int responseLen, bool lightDialogs = false, bool showHidden = false)
        {
            if (LLMSystem.Client == null)
                return string.Empty;
            LLMSystem.NamesInPromptOverride = false;
            var replyln = responseLen;
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                replyln += 1024;
            var availtokens = LLMSystem.MaxContextLength - replyln - 20;

            var promptbuild = LLMSystem.Client.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions and stories." + LLMSystem.NewLine +
                LLMSystem.NewLine +
                "# Character Information:" + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "# Chat Session:" + LLMSystem.NewLine +
                "## Starting Date: " + StringExtensions.DateToHumanString(StartTime) + LLMSystem.NewLine +
                "## Duration: " + StringExtensions.TimeSpanToHumanString(Duration) + LLMSystem.NewLine + LLMSystem.NewLine;

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

            var docs = GetRawDialogs(availtokens, false, lightDialogs, showHidden);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);
            promptbuild.AddMessage(AuthorRole.User, requestedTask);
            
            var ct = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMSystem.Sampler.Temperature > 0.5) ? 0.5 : LLMSystem.Sampler.Temperature, replyln);
            var finalstr = await LLMSystem.SimpleQuery(ct);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
            {
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            }
            LLMSystem.NamesInPromptOverride = null;
            return finalstr.CleanupAndTrim();
        }

        public async Task GenerateEmbeds()
        {
            if (!RAGSystem.Enabled)
                return;
            EmbedTitle = await RAGSystem.EmbeddingText(Title);
            EmbedSummary = await RAGSystem.EmbeddingText(Summary);
        }

        public string GetRawDialogs(int maxTokens, bool ignoresystem, bool lightDialogs = false, bool showHidden = false)
        {
            var sb = new StringBuilder();
            var totaltks = maxTokens;

            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                var msg = Messages[i];
                if (msg.Hidden && !showHidden)
                    continue;
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
                        if (lightDialogs)
                            text = LLMSystem.NewLine + msg.Sender?.Name + ": " + msg.Message.Trim().Replace(LLMSystem.NewLine, " ") + LLMSystem.NewLine;
                        else
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
                {
                    if (FirstPersonSummary)
                    {
                        sb.AppendLinuxLine($"On {StartTime.DayOfWeek}, {StringExtensions.DateToHumanString(StartTime)}, the following events took place from {LLMSystem.Bot.Name}'s perspective: {Summary.RemoveNewLines()}");
                    }
                    else
                    {
                        sb.AppendLinuxLine($"On {StartTime.DayOfWeek}, {StringExtensions.DateToHumanString(StartTime)}: {Summary.RemoveNewLines()}");
                    }
                }
                else
                {
                    if (FirstPersonSummary)
                    {
                        sb.AppendLinuxLine($"Between the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)} and the {EndTime.DayOfWeek} {StringExtensions.DateToHumanString(EndTime)}, the following events took place from {LLMSystem.Bot.Name}'s perspective: {Summary.RemoveNewLines()}");
                    }
                    else
                    {
                        sb.AppendLinuxLine($"Between the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)} and the {EndTime.DayOfWeek} {StringExtensions.DateToHumanString(EndTime)}: {Summary.RemoveNewLines()}");
                    }

                }
            }

            return sb.ToString();
        }
    }

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
                    Sessions.Add(new ChatSession());
                    CurrentSessionID = 0;
                    return Sessions[0];
                }
                return CurrentSessionID >= 0 && CurrentSessionID < Sessions.Count ? Sessions[CurrentSessionID] : Sessions.Last();
            }
        }

        [JsonIgnore] public EventHandler<SingleMessage>? OnMessageAdded;

        private void RaiseOnMessageAdded(SingleMessage message) => OnMessageAdded?.Invoke(this, message);

        public string GetPreviousSummaries(int maxTokens, string sectionHeader = "##")
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
                if (LLMSystem.usedGuidInSession.Contains(session.Guid) || string.IsNullOrWhiteSpace(session.Summary))
                    continue;
                var sb = new StringBuilder();
                sb.AppendLinuxLine($"{sectionHeader} {session.Title}");
                sb.AppendLinuxLine(session.GetRawMemory(true));
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
                if (memories?.Count > 0 && !LLMSystem.Settings.RAGMoveToSysPrompt)
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
                Sessions.Add(new ChatSession());

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
                Sessions.Add(new ChatSession());
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

            if (LLMSystem.Client?.SupportsSchema == true)  
            {
                var meta = await session.GetSessionInfo();
                session.MetaData = meta;
                var topics = await session.GetResearchTopics();
                session.NewTopics = topics;
                session.FirstPersonSummary = false;
            }
            else
            {
                var sum = await session.GenerateNewSummary();
                session.MetaData.Summary = sum;
                session.FirstPersonSummary = LLMSystem.Bot.FirstPersonSummary;
                var kw = await session.GenerateKeywords();
                session.MetaData.Keywords = [.. kw];
                var goals = await session.GenerateGoals();
                session.MetaData.FutureGoals = [.. goals.Split('\n').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x))];
                session.MetaData.IsRoleplaySession = await session.IsRoleplay();
                session.MetaData.Title = await ChatSession.GenerateNewTitle(sum);
            }
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
                CurrentSession.Scenario = LLMSystem.Settings.ScenarioOverride;
                await UpdateSession(CurrentSession);
                EventBus.Publish(new SessionArchivedEvent(CurrentSession.Guid, DateTime.UtcNow));
                // reset session ID
                CurrentSessionID = -1;
                // Create new session
                var newsession = new ChatSession()
                {
                    Enabled = false,

                };
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
            var content = JsonConvert.SerializeObject(this, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
            // create directory if it doesn't exist
            var dir = Path.GetDirectoryName(pPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(pPath, content);
        }

        public void DeleteAll(bool AreYouSure)
        {
            if (AreYouSure)
                Sessions.Clear();
        }
    }
}
