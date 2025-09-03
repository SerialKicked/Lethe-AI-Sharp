using AIToolkit.API;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using AIToolkit.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Runtime.Serialization;
using System.Text;

namespace AIToolkit.Files
{
    public class ChatSession : MemoryUnit, IChatSession
    {
        // Canonical fields proxied to MetaData for this subtype
        public override string Name
        {
            get => MetaData.Title ?? string.Empty;
            set => MetaData.Title = value ?? string.Empty;
        }

        public override string Content
        {
            get => MetaData?.Summary ?? string.Empty;
            set => MetaData.Summary = value ?? string.Empty;
        }

        public SessionMetaInfo MetaData { get; set; } = new();
        public TopicLookup NewTopics { get; set; } = new();

        public string Scenario { get; set; } = string.Empty;

        public DateTime StartTime 
        {
            get => Added;
            set => Added = value;
        }

        public List<SingleMessage> Messages { get; set; } = [];

        /// <summary>
        /// If set to true, this memory will always be included in the prompt
        /// </summary>
        public bool Sticky { get; set; } = false;

        [OnError]
        internal void OnError(StreamingContext ctx, Newtonsoft.Json.Serialization.ErrorContext error)
        {
            // fix loading with older versions of the chatlog.
            if (string.Equals(error.Member?.ToString(), "Duration", StringComparison.OrdinalIgnoreCase))
            {
                // Ignore legacy TimeSpan/Date token assigned to int Duration (from MemoryUnit)
                error.Handled = true;
            }
        }

        public ChatSession()
        {
            Category = MemoryType.ChatSession;
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext ctx)
        {
            // Ensure Category is correct on legacy files
            if (Category == default)
                Category = MemoryType.ChatSession;
        }

        public virtual async Task UpdateSession()
        {
            if (StartTime == default)
            {
                foreach (var item in Messages)
                {
                    if (item.Date != default)
                    {
                        StartTime = item.Date;
                        break;
                    }
                }
            }
            var previousmess = Messages.First();
            foreach (var item in Messages)
            {
                if (item.Date == default || item.Date.Year == 1)
                {
                    item.Date = previousmess.Date + new TimeSpan(0, 1, 0);
                    break;
                }
                previousmess = item;
            }
            EndTime = Messages.Last().Date;

            if (LLMSystem.Client?.SupportsSchema == true)
            {
                var meta = await GetSessionInfo();
                MetaData = meta;
                var sum = await GenerateNewSummary();
                if (sum.Length > MetaData.Summary.Length)
                    MetaData.Summary = sum;
                var topics = await GetResearchTopics();
                NewTopics = topics;
            }
            else
            {
                var sum = await GenerateNewSummary();
                MetaData.Summary = sum;
                var kw = await GenerateKeywords();
                MetaData.Keywords = [.. kw];
                var goals = await GenerateGoals();
                MetaData.FutureGoals = [.. goals.Split('\n').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x))];
                MetaData.IsRoleplaySession = await IsRoleplay();
                MetaData.Title = await GenerateNewTitle(sum);
            }
            await EmbedText();
        }

        public virtual async Task<SessionMetaInfo> GetSessionInfo()
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
                "## Duration: " + StringExtensions.TimeSpanToHumanString(EndTime-StartTime) + LLMSystem.NewLine + LLMSystem.NewLine;


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
            session?.ClampRelevance();
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }

        public virtual async Task<TopicLookup> GetResearchTopics()
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
                "## Duration: " + StringExtensions.TimeSpanToHumanString(EndTime - StartTime) + LLMSystem.NewLine + LLMSystem.NewLine;


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
            session?.ClampRelevance();
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }

        public virtual async Task<string[]> GenerateKeywords()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of keywords {{char}} would associate with this chat. The list must be between 1 and 5 keywords long.";
            var res = await GenerateTaskRes(query, 512, true, false);
            return res.Split(',');
        }

        public virtual async Task<string> GenerateGoals()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a list of the plans they both setup for the near future. This list should contain between 0 and 4 items. Each item should be summarized in a single sentence. If there's no items, don't answer with anything. Make sure those plans aren't already resolved within the span of the dialog." + LLMSystem.NewLine + "Example:" + LLMSystem.NewLine + "- They promised to eat together tomorrow." + LLMSystem.NewLine + "- {{user}} will watch the movie recommanded by {{char}}.";
            var res = await GenerateTaskRes(query, 1024, true, false);
            return res;
        }

        public virtual async Task<bool> IsRoleplay()
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

        public virtual async Task<string> GenerateNewSummary()
        {

            var query = "Write a detailed summary of the exchange between {{user}} and {{char}} shown above. Do not add a title, just write the summary directly.";

            if (Messages.Count > 40)
            {
                query += " The summary should be 2 to 4 paragraphs long.";
            }
            else
            {
                query += " The summary should be 1 to 2 paragraphs long.";
            }
            return await GenerateTaskRes(query, 1024, true, false);
        }

        public virtual async Task<string> GenerateNewTitle(string sum)
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

        public virtual async Task<string> GenerateTaskRes(string requestedTask, int responseLen, bool lightDialogs = false, bool showHidden = false)
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
                "## Duration: " + StringExtensions.TimeSpanToHumanString(EndTime - StartTime) + LLMSystem.NewLine + LLMSystem.NewLine;

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

        public string GetRawMemory(bool withtitle, bool includedates)
        {
            var sb = new StringBuilder();
            if (withtitle)
            {
                sb.Append($"{Name.RemoveNewLines()}: ");
            }
            if (includedates)
            {
                if (StartTime.Date == EndTime.Date)
                {
                    sb.AppendLinuxLine($"On {StartTime.DayOfWeek}, {StringExtensions.DateToHumanString(StartTime)}: {Content.RemoveNewLines()}");
                }
                else
                {
                    sb.AppendLinuxLine($"Between the {StartTime.DayOfWeek} {StringExtensions.DateToHumanString(StartTime)} and the {EndTime.DayOfWeek} {StringExtensions.DateToHumanString(EndTime)}: {Content.RemoveNewLines()}");
                }
            }
            else
            {
                sb.AppendLinuxLine($"{Content.RemoveNewLines()}");
            }

            return sb.ToString();
        }
    }
}
