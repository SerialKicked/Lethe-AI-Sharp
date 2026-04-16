using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.LLM.GBNF;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;

namespace LetheAISharp.Agent.Actions
{

    /// <summary>
    /// Represents an action that analyzes a session and generates a response based on the session's context and the
    /// request provided as a parameter.
    /// </summary>
    /// <remarks>This action uses a language model to process the session data and user request, generating a
    /// response that reflects on the session. It requires specific capabilities, such as access to a language model, 
    /// to execute successfully.</remarks>
    public class CalendarUpdateAction : IAgentAction<CalendarUpdateResult?, string[]>
    {
        public string Id => "CalendarUpdateAction";
        public HashSet<AgentActionRequirements> Requirements => [ AgentActionRequirements.LLM, AgentActionRequirements.Grammar ];

        public async Task<CalendarUpdateResult?> Execute(string[] calendar, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
                return null;
            var result = new CalendarUpdateResult(calendar);
            var promptbuilder = GetSystemPromt(calendar, result.GetQuery());
            await promptbuilder.SetStructuredOutput(result);
            var fullprompt = promptbuilder.PromptToQuery(AuthorRole.Assistant, (LLMEngine.Sampler.Temperature > 0.5) ? 0.5 : LLMEngine.Sampler.Temperature, 1200);
            var response = await LLMEngine.SimpleQuery(fullprompt, ct).ConfigureAwait(false);
            response = response.RemoveThinkingBlocks();

            try
            {
                result = JsonConvert.DeserializeObject<CalendarUpdateResult>(response);
            }
            catch (Exception e)
            {
                LLMEngine.Logger?.LogError("Error Updating calendar: {mess}", e.Message);
            }
            return result;
        }

        private static IPromptBuilder GetSystemPromt(string[] curcalendar, string request)
        {
            var promptbuild = LLMEngine.GetPromptBuilder();

            var str = new StringBuilder();
            var tokenleft = LLMEngine.MaxContextLength - 1200; // leave some space for response + mix
            str.AppendLinuxLine("You are {{mchar}} and you are meant to check a dialog between you and {{user}} in order to update your schedule information.").AppendLinuxLine();

            str.AppendLinuxLine("# {{mchar}} (this is you)").AppendLinuxLine();
            str.AppendLinuxLine("{{mcharbio}}").AppendLinuxLine();
            str.AppendLinuxLine("# {{user}} (this is the user)").AppendLinuxLine();
            str.AppendLinuxLine("{{userbio}}").AppendLinuxLine();
            str.AppendLinuxLine("# Transcript").AppendLinuxLine();

            tokenleft -= promptbuild.GetTokenCount(AuthorRole.System, str.ToString());

            var req = new StringBuilder();
            req.AppendLinuxLine($"Current Date: {DateTime.Now.ToHumanString()}").AppendLinuxLine();
            req.AppendLinuxLine("# Current Schedule").AppendLinuxLine();
            for (int i = 0; i < curcalendar.Length; i++)
            {
                req.AppendLinuxLine($"- **{(DayOfWeek)i}:** {(string.IsNullOrEmpty(curcalendar[i]) ? "no schedule" : curcalendar[i])}");
            }
            req.AppendLinuxLine().Append(request);

            tokenleft -= promptbuild.GetTokenCount(AuthorRole.User, req.ToString());

            var transcript = LLMEngine.History.GetRawDialogs(tokenleft - 2000, false, true, false, TimeSpan.FromDays(7));
            str.Append(transcript);

            promptbuild.AddMessage(new SingleMessage(AuthorRole.System, str.ToString()));
            promptbuild.AddMessage(new SingleMessage(AuthorRole.User, req.ToString()));

            return promptbuild;
        }
    }
}
