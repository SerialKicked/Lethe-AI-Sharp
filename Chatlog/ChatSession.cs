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

        /// <summary>
        /// Updates the session's metadata, including start and end times, summary, keywords, goals, and roleplay status.
        /// </summary>
        /// <returns></returns>
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
                var meta = await GetSessionInfo().ConfigureAwait(false);
                MetaData = meta;
                var sum = await GenerateSummary().ConfigureAwait(false);
                if (sum.Length > MetaData.Summary.Length)
                    MetaData.Summary = sum;
                var topics = await GetResearchTopics().ConfigureAwait(false);
                NewTopics = topics;
            }
            else
            {
                var sum = await GenerateSummary().ConfigureAwait(false);
                MetaData.Summary = sum;
                var kw = await GenerateKeywords().ConfigureAwait(false);
                MetaData.Keywords = [.. kw];
                var goals = await GenerateGoals().ConfigureAwait(false);
                MetaData.FutureGoals = [.. goals.Split('\n').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x))];
                MetaData.IsRoleplaySession = await IsRoleplay().ConfigureAwait(false);
                MetaData.Title = await GenerateTitle(sum).ConfigureAwait(false);
            }
            await EmbedText().ConfigureAwait(false);
        }

        /// <summary>
        /// Generates a detailed summary of the chat session, including character information and dialog context, and create reliable metadata class using a specified grammar.
        /// </summary>
        /// <returns> A <see cref="SessionMetaInfo"/> object containing the generated session metadata.</returns>
        protected virtual async Task<SessionMetaInfo> GetSessionInfo()
        {
            var session = new SessionMetaInfo();
            var grammar = await session.GetGrammar().ConfigureAwait(false);
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
            var finalstr = await LLMSystem.SimpleQuery(ct).ConfigureAwait(false);
            session = JsonConvert.DeserializeObject<SessionMetaInfo>(finalstr);
            session?.ClampRelevance();
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }

        /// <summary>
        /// Method that generates a list of research topics based on the chat session's content, character information, 
        /// and dialog context, using a specified grammar for structured output. 
        /// Used by the ResearchTask agentic plugin to identify topics for background web research.
        /// </summary>
        /// <returns>A <see cref="TopicLookup"/> object containing the generated research topics.</returns>
        protected virtual async Task<TopicLookup> GetResearchTopics()
        {
            var session = new TopicLookup();
            var grammar = await session.GetGrammar().ConfigureAwait(false);
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
            var finalstr = await LLMSystem.SimpleQuery(ct).ConfigureAwait(false);
            session = JsonConvert.DeserializeObject<TopicLookup>(finalstr);
            session?.ClampRelevance();
            LLMSystem.NamesInPromptOverride = null;
            LLMSystem.Instruct.PrefillThinking = prefill;
            return session!;
        }

        /// <summary>
        /// If the back-end doesn't provides GBNF grammar to formatted output, this method acts as a backup to 
        /// generate a list of keywords associated with the chat session.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<string[]> GenerateKeywords()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a comma-separated list of keywords {{char}} would associate with this chat. The list must be between 1 and 5 keywords long.";
            var res = await GenerateTaskRes(query, 512, true, false).ConfigureAwait(false);
            return res.Split(',');
        }

        /// <summary>
        /// If the back-end doesn't provides GBNF grammar to formatted output, this method acts as a backup to generate a list of goals.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<string> GenerateGoals()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, write a list of the plans they both setup for the near future. This list should contain between 0 and 4 items. Each item should be summarized in a single sentence. If there's no items, don't answer with anything. Make sure those plans aren't already resolved within the span of the dialog." + LLMSystem.NewLine + "Example:" + LLMSystem.NewLine + "- They promised to eat together tomorrow." + LLMSystem.NewLine + "- {{user}} will watch the movie recommanded by {{char}}.";
            var res = await GenerateTaskRes(query, 1024, true, false).ConfigureAwait(false);
            return res;
        }

        /// <summary>
        /// If the back-end doesn't provides GBNF grammar to formatted output, this method acts as a backup to determine if this is a roleplay session or not.
        /// </summary>
        /// <returns></returns>
        public virtual async Task<bool> IsRoleplay()
        {
            var query = "Based on the exchange between {{user}} and {{char}} shown above, determine if {{user}} and {{char}} are roleplaying a scenario. Respond Yes if they are acting a roleplay. Discussing a future roleplay doesn't count as a roleplay. Respond No if this is a just a chat." + LLMSystem.NewLine + LLMSystem.NewLine +
                "To qualify as a roleplay, the vast majority of the exchange must follow the following guidelines:" + LLMSystem.NewLine +
                "- Contains explicit actions (not just discussions)." + LLMSystem.NewLine +
                "- Both {{user}} and {{char}} are in a situation involving physical contact in a defined location." + LLMSystem.NewLine +
                "- Heavy use of narrative text (between asterisks)" + LLMSystem.NewLine +
                "- Clearly takes place outside of a chat interface." + LLMSystem.NewLine + LLMSystem.NewLine + "Your response must begin by either Yes or No.";
            var res = await GenerateTaskRes(query, 1024, true, false).ConfigureAwait(false);
            var s = res.ToLowerInvariant().Replace(" ", string.Empty);
            return s.StartsWith("yes");
        }

        /// <summary>
        /// Generates a detailed summary of the chat session.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<string> GenerateSummary()
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
            return await GenerateTaskRes(query, 1024, true, false).ConfigureAwait(false);
        }

        /// <summary>
        /// Generates a title for this chat session based on the provided summary.
        /// </summary>
        /// <returns></returns>
        protected virtual async Task<string> GenerateTitle(string sum)
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
            var finalstr = await LLMSystem.SimpleQuery(genparam).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            finalstr = finalstr.Replace("\"", "").Trim();
            LLMSystem.NamesInPromptOverride = null;
            return finalstr;
        }

        /// <summary>
        /// Generates a response based on the provided task description and specified parameters.
        /// </summary>
        /// <remarks>This method constructs a prompt based on the provided task description and available
        /// dialog data, then queries the system to generate a response. The response length and dialog inclusion
        /// options can be customized using the <paramref name="responseLen"/>, <paramref name="lightDialogs"/>, and
        /// <paramref name="showHidden"/> parameters.</remarks>
        /// <param name="requestedTask">The task or query for which a response is to be generated. This should be a descriptive string outlining the
        /// desired operation or analysis.</param>
        /// <param name="responseLen">The maximum length of the response, in tokens. This value determines the size of the generated output.</param>
        /// <param name="lightDialogs">A boolean value indicating whether to include only lightweight dialog data in the analysis. Set to <see
        /// langword="true"/> to reduce the amount of dialog context included; otherwise, <see langword="false"/>.</param>
        /// <param name="showHidden">A boolean value indicating whether to include hidden or filtered dialog data in the analysis. Set to <see
        /// langword="true"/> to include hidden data; otherwise, <see langword="false"/>.</param>
        /// <returns>A <see cref="Task{String}"/> representing the asynchronous operation. The result contains the generated
        /// response as a string. If the operation cannot proceed (e.g., the client is not initialized), an empty string
        /// is returned.</returns>
        public virtual async Task<string> GenerateTaskRes(string requestedTask, int responseLen, bool lightDialogs = true, bool showHidden = false)
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
            var finalstr = await LLMSystem.SimpleQuery(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(LLMSystem.Instruct.ThinkingStart))
            {
                finalstr = finalstr.RemoveThinkingBlocks(LLMSystem.Instruct.ThinkingStart, LLMSystem.Instruct.ThinkingEnd);
            }
            LLMSystem.NamesInPromptOverride = null;
            return finalstr.CleanupAndTrim();
        }

        /// <summary>
        /// Constructs a formatted string representation of the dialog messages, based on the specified parameters.
        /// </summary>
        /// <remarks> 
        /// Messages are formatted based on their role and the provided parameters. System messages are optionally
        /// excluded, and hidden messages are included only if <paramref name="showHidden"/> is set to <see
        /// langword="true"/>. The token count is calculated for each message, and the output is truncated once the
        /// token limit is reached.
        /// </remarks>
        /// <param name="maxTokens">The maximum number of tokens to include in the output. If the total token count exceeds this value, the
        /// output will be truncated.</param>
        /// <param name="ignoresystem">A value indicating whether system messages should be excluded from the output. Set to <see langword="true"/>
        /// to ignore system messages; otherwise, <see langword="false"/>.</param>
        /// <param name="lightDialogs">A value indicating whether to use a simplified format for user and assistant messages. Set to <see
        /// langword="true"/> for a lighter format; otherwise, <see langword="false"/>.</param>
        /// <param name="showHidden">A value indicating whether hidden messages should be included in the output. Set to <see langword="true"/>
        /// to include hidden messages; otherwise, <see langword="false"/>.</param>
        /// <returns>A string containing the formatted dialog messages, adhering to the specified parameters. The string may be
        /// truncated if the token limit is reached.</returns>
        public string GetRawDialogs(int maxTokens, bool ignoresystem, bool lightDialogs = true, bool showHidden = false)
        {
            return GetRawDialogs(Messages, maxTokens, ignoresystem, lightDialogs, showHidden);
        }

        /// <summary>
        /// Retrieves and formats a subset of dialog messages based on their position, applying specified formatting options and token limits.
        /// </summary>
        /// <param name="FirstID">start location in list (included)</param>
        /// <param name="LastID">end location in list (included)</param>
        /// <param name="maxTokens">Max tokens (will stop if we reach that)</param>
        /// <param name="ignoresystem">skip all system messages</param>
        /// <param name="lightDialogs">token light version</param>
        /// <param name="showHidden">include hidden messages or not (only relevant if ignoresystem is false)</param>
        /// <returns></returns>
        public string GetRawDialogs(int FirstID, int LastID, int maxTokens, bool ignoresystem, bool lightDialogs = true, bool showHidden = false)
        {
            // get the messages in the range with ID being their position in the list (0 based)
            if (FirstID < 0)
                FirstID = 0;
            if (LastID >= Messages.Count)
                LastID = Messages.Count - 1;
            if (FirstID > LastID)
                return string.Empty;
            var selected = this.Messages.GetRange(FirstID, LastID - FirstID + 1);
            if (selected.Count == 0)
                return string.Empty;
            return GetRawDialogs(selected, maxTokens, ignoresystem, lightDialogs, showHidden);
        }

        static internal string GetRawDialogs(List<SingleMessage> messages, int maxTokens, bool ignoresystem, bool lightDialogs = true, bool showHidden = false)
        {
            var sb = new StringBuilder();
            var totaltks = maxTokens;

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var msg = messages[i];
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

        /// <summary>
        /// Generates a formatted string representation of the memory content, optionally including a title and date
        /// information.
        /// </summary>
        /// <remarks>The method formats the memory content based on the specified parameters. If both
        /// <paramref name="withtitle"/>  and <paramref name="includedates"/> are <see langword="true"/>, the output
        /// includes the title, the date range  (if applicable), and the content. If <paramref name="includedates"/> is
        /// <see langword="true"/> and the start  and end dates are the same, only the single date is
        /// included.</remarks>
        /// <param name="withtitle">A value indicating whether to include the title in the output.  If <see langword="true"/>, the title is
        /// included; otherwise, it is omitted.</param>
        /// <param name="includedates">A value indicating whether to include date information in the output.  If <see langword="true"/>, the start
        /// and end dates are included; otherwise, they are omitted.</param>
        /// <returns>A string containing the formatted memory content. The output may include the title and/or date information 
        /// depending on the values of <paramref name="withtitle"/> and <paramref name="includedates"/>.</returns>
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
