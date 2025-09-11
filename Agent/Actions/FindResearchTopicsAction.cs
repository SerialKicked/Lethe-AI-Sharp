using AIToolkit.API;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using Newtonsoft.Json;

namespace AIToolkit.Agent.Actions
{
    public class FindResearchTopicsParams
    {
        public List<SingleMessage> Messages { get; set; } = [];
        public bool IncludeBios = true;
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
            var grammar = await searchlookup.GetGrammar().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(grammar))
                throw new Exception("Something went wrong when building summary grammar and json format.");

            LLMEngine.NamesInPromptOverride = false;
            var prefill = LLMEngine.Instruct.PrefillThinking;
            LLMEngine.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMEngine.MaxContextLength - replyln - 20;

            var promptbuild = LLMEngine.Client!.GetPromptBuilder();

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
            promptbuild.AddMessage(AuthorRole.User, requestedTask);

            var query = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, replyln);
            if (query is GenerationInput input)
            {
                input.Grammar = grammar;
            }
            var finalstr = await LLMEngine.SimpleQuery(query).ConfigureAwait(false);
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
