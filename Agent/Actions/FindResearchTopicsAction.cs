using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using Newtonsoft.Json;

namespace LetheAISharp.Agent.Actions
{
    public class FindResearchTopicsParams
    {
        public List<SingleMessage> Messages { get; set; } = [];
        public bool IncludeBios = true;
        public string CustomRequest = string.Empty;
    }

    public class FindResearchTopicsAction : IAgentAction<TopicLookup?, FindResearchTopicsParams>
    {
        public string Id => "FindResearchTopicsAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.LLM, AgentActionRequirements.Grammar ];
        public async Task<TopicLookup?> Execute(FindResearchTopicsParams param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return null;
            var searchlookup = new TopicLookup();

            LLMEngine.NamesInPromptOverride = false;
            var prefill = LLMEngine.Instruct.PrefillThinking;
            LLMEngine.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMEngine.MaxContextLength - replyln - 20;

            var promptbuild = LLMEngine.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions and stories." + LLMEngine.NewLine + LLMEngine.NewLine;
            if (param.IncludeBios)
            {
                sysprompt +=
                "# Character Information:" + LLMEngine.NewLine + LLMEngine.NewLine +
                "## Name: {{char}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                "{{charbio}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                "## Name: {{user}}" + LLMEngine.NewLine + LLMEngine.NewLine +
                "{{userbio}}" + LLMEngine.NewLine + LLMEngine.NewLine;
            }
            sysprompt += "# Chat Session:" + LLMEngine.NewLine + LLMEngine.NewLine;

            var requestedTask = searchlookup.GetQuery();

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

            var docs = ChatSession.GetRawDialogsMiddleCut(param.Messages, availtokens, false, true, false);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);

            var q = string.IsNullOrWhiteSpace(param.CustomRequest) ? "Review the conversation log above. Identify the topic or concept that requires a web search for additional (or up to date) information. Inform your choices based on the information presented in the prompt. Avoid any topic that could be considered illegal, terrorist, or CSAM." : param.CustomRequest;


            promptbuild.AddMessage(AuthorRole.User, q + LLMEngine.NewLine + searchlookup.GetQuery());
            await promptbuild.SetStructuredOutput(searchlookup);
            var query = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, replyln);
            var finalstr = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);
            try
            {
                searchlookup = JsonConvert.DeserializeObject<TopicLookup>(finalstr);
                searchlookup?.ClampRelevance();
            }
            finally
            {
                LLMEngine.NamesInPromptOverride = null;
                LLMEngine.Instruct.PrefillThinking = prefill;
            }
            return searchlookup;
        }
    }
}
