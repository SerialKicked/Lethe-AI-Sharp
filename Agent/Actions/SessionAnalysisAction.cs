using AIToolkit.API;
using AIToolkit.Files;
using AIToolkit.GBNF;
using AIToolkit.LLM;
using Newtonsoft.Json;
using System.Text;

namespace AIToolkit.Agent.Actions
{
    public class SessionAnalysisParams(ChatSession session, string request)
    {
        public ChatSession Session = session;
        public string Request = request;
    }

    /// <summary>
    /// Represents an action that analyzes a session and generates a response based on the session's context and the
    /// request provided as a parameter.
    /// </summary>
    /// <remarks>This action uses a language model to process the session data and user request, generating a
    /// response that reflects on the session. It requires specific capabilities, such as access to a language model, 
    /// to execute successfully.</remarks>
    public class SessionAnalysisAction : IAgentAction<string, SessionAnalysisParams>
    {
        public string Id => "SessionAnalysisAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.LLM ];
        public async Task<string> Execute(SessionAnalysisParams param, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return string.Empty;

            var prompt = GetSystemPromt(param.Session, param.Request);
            var fullprompt = prompt.PromptToQuery(AuthorRole.Assistant, LLMEngine.Sampler.Temperature, 1024);
            var response = await LLMEngine.SimpleQuery(fullprompt, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(LLMEngine.Instruct.ThinkingStart))
            {
                response = response.RemoveThinkingBlocks(LLMEngine.Instruct.ThinkingStart, LLMEngine.Instruct.ThinkingEnd);
            }
            response = response.RemoveUnfinishedSentence().CleanupAndTrim();
            return response;
        }

        private static IPromptBuilder GetSystemPromt(ChatSession param, string request)
        {
            var promptbuild = LLMEngine.GetPromptBuilder();

            var str = new StringBuilder();
            var tokenleft = LLMEngine.MaxContextLength - 1024; // leave some space for response + mix
            str.AppendLinuxLine("You are {{char}} and you are meant to reflect on this session with {{user}}.").AppendLinuxLine();

            str.AppendLinuxLine("## {{char}} (this is you)").AppendLinuxLine();
            str.AppendLine("{{charbio}}").AppendLinuxLine();
            str.AppendLinuxLine("## {{user}} (this is the user)").AppendLinuxLine();
            str.AppendLine("{{userbio}}").AppendLinuxLine();
            if (!string.IsNullOrEmpty(param.Content))
            {
                str.AppendLinuxLine($"## Session Summary: {param.Name}").AppendLinuxLine();
                str.AppendLine($"{param.Content}").AppendLinuxLine();
            }
            str.AppendLinuxLine("## Transcript").AppendLinuxLine();

            tokenleft -= promptbuild.GetTokenCount(AuthorRole.SysPrompt, str.ToString());
            tokenleft -= promptbuild.GetTokenCount(AuthorRole.User, request);

            var transcript = param.GetRawDialogs(tokenleft, true, false, false, true);
            str.Append(transcript);

            promptbuild.AddMessage(AuthorRole.SysPrompt, str.ToString());
            promptbuild.AddMessage(AuthorRole.User, request);

            return promptbuild;
        }
    }
}
