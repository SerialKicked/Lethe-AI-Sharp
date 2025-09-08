using AIToolkit.API;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using Newtonsoft.Json;
using static AIToolkit.SearchAPI.WebSearchAPI;

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

            LLMSystem.NamesInPromptOverride = false;
            var prefill = LLMSystem.Instruct.PrefillThinking;
            LLMSystem.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMSystem.MaxContextLength - replyln - 20;

            var promptbuild = LLMSystem.Client!.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions and stories." + LLMSystem.NewLine + LLMSystem.NewLine;
            if (param.IncludeBios)
            {
                sysprompt +=
                "# Character Information:" + LLMSystem.NewLine + LLMSystem.NewLine +
                "## Name: {{char}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "{{charbio}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "## Name: {{user}}" + LLMSystem.NewLine + LLMSystem.NewLine +
                "{{userbio}}" + LLMSystem.NewLine + LLMSystem.NewLine;
            }
            sysprompt += "# Chat Session:" + LLMSystem.NewLine + LLMSystem.NewLine;

            var requestedTask = searchlookup.GetQuery();

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestedTask);

            var docs = ChatSession.GetRawDialogs(param.Messages, availtokens, false, true, false);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);
            promptbuild.AddMessage(AuthorRole.User, requestedTask);

            var query = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMSystem.Sampler.Temperature > 0.75) ? 0.75 : LLMSystem.Sampler.Temperature, replyln);
            if (query is GenerationInput input)
            {
                input.Grammar = grammar;
            }
            var finalstr = await LLMSystem.SimpleQuery(query).ConfigureAwait(false);
            try
            {
                searchlookup = JsonConvert.DeserializeObject<TopicLookup>(finalstr);
                searchlookup?.ClampRelevance();
            }
            finally
            {
                LLMSystem.NamesInPromptOverride = null;
                LLMSystem.Instruct.PrefillThinking = prefill;
            }
            return searchlookup;
        }
    }
}
