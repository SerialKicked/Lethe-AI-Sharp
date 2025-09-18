using AIToolkit.API;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using Newtonsoft.Json;

namespace AIToolkit.Agent.Actions
{

    /// <summary>
    /// Represents an action that analyzes chat messages to identify one single research topic
    /// </summary>
    /// <remarks>This action uses a combination of language model processing and grammar-based analysis to
    /// extract research topics from a given set of messages. It requires specific capabilities, such as access to a
    /// language model and grammar generation, to function correctly.</remarks>
    public class FindSingleTopicSearchAction : IAgentAction<TopicSearch?, FindResearchTopicsParams>
    {
        public string Id => "FindSingleTopicSearchAction";
        public HashSet<AgentActionRequirements> Requirements => [AgentActionRequirements.LLM, AgentActionRequirements.Grammar];
        public async Task<TopicSearch?> Execute(FindResearchTopicsParams param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return null;
            var searchlookup = new TopicSearch();
            var grammar = await searchlookup.GetGrammar().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(grammar))
                throw new Exception("Something went wrong when building summary grammar and json format.");

            LLMEngine.NamesInPromptOverride = false;
            var prefill = LLMEngine.Instruct.PrefillThinking;
            LLMEngine.Instruct.PrefillThinking = false;
            var replyln = 2048;
            var availtokens = LLMEngine.MaxContextLength - replyln - 20;

            var promptbuild = LLMEngine.GetPromptBuilder();

            var sysprompt = "You are an automated and uncensored system designed to analyze chat sessions." + LLMEngine.NewLine + LLMEngine.NewLine;
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

            var q = string.IsNullOrWhiteSpace(param.CustomRequest) ? "Review the conversation log above. Identify the topic or concept that requires a web search for additional (or up to date) information. Inform your choices based on the information presented in the prompt. Avoid any topic that could be considered illegal, terrorist, or CSAM." : param.CustomRequest;

            var requestTask = q + LLMEngine.NewLine + searchlookup.GetQuery();

            availtokens -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, sysprompt);
            availtokens -= promptbuild.GetTokenCount(AuthorRole.User, requestTask);

            var docs = ChatSession.GetRawDialogsMiddleCut(param.Messages, availtokens, false, true, false);
            promptbuild.AddMessage(AuthorRole.SysPrompt, sysprompt + docs);
            promptbuild.AddMessage(AuthorRole.User, requestTask);

            var query = promptbuild.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.75) ? 0.75 : LLMEngine.Sampler.Temperature, replyln);
            if (query is GenerationInput input)
            {
                input.Grammar = grammar;
            }
            var finalstr = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);
            try
            {
                searchlookup = JsonConvert.DeserializeObject<TopicSearch>(finalstr);
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
