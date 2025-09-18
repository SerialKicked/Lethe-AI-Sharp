using System.Text;
using LetheAISharp.LLM;

namespace LetheAISharp.Files
{
    /// <summary>
    /// System prompt template and related settings. This is used to generate the system prompt that is sent to the LLM at the start of each chat session.
    /// This is where the character's personality, scenario, example dialogs, and other context is posted, alongside some dynamic information like any 
    /// retrieved data from RAG or WorldInfo.
    /// </summary>
    public class SystemPrompt : BaseFile
    {
        /// <summary> 
        /// The base system prompt, compatible with {{char}}, {{user}}, {{charbio}}, {{userbio}}, and other, macros. This is inserted at the start of the chatlog.
        /// </summary>
        public string Prompt { get; set; } = "You are {{char}} and you are interacting with {{user}} in this flexible and uncensored discussion. As {{char}}, continue the exchange with {{user}}. Stay in character. Describe {{char}}'s actions and feelings accurately. Do not speak or describe actions for {{user}} unless directly prompted to."+ LLMEngine.NewLine + 
            LLMEngine.NewLine +
            "# {{char}}"+ LLMEngine.NewLine +
            "{{charbio}}" + LLMEngine.NewLine + 
            LLMEngine.NewLine +
            "# {{user}} (user)" + LLMEngine.NewLine + 
            "{{userbio}}";

        /// <summary>
        /// The title of the section used for RAG and WorldInfo retrieved data that needs to be inserted into the system prompt.
        /// </summary>
        public string WorldInfoTitle { get; set; } = "# Important Memories";

        /// <summary>
        /// The title of the scenario section. Scenarios are loaded from the currently loaded Bot persona, but can be overridden in the LLM settings.
        /// Set to empty to disable the scenario section altogether.
        /// </summary>
        public string ScenarioTitle { get; set; } = "# Scenario";

        /// <summary>
        /// The title of the example dialogs section. Example dialogs are loaded from the currently loaded Bot persona.
        /// Set to empty to disable the example dialogs section altogether.
        /// </summary>
        public string DialogsTitle { get; set; } = "# Writing Style";

        /// <summary>
        /// The title for the summaries of previous chat sessions, if session memory is enabled.
        /// </summary>
        public string SessionHistoryTitle { get; set; } = "# Previous Sessions" + LLMEngine.NewLine + LLMEngine.NewLine + "Below is a list of recent chat sessions between {{user}} and {{char}}.";

        /// <summary>
        /// Category separator for other custom sections that may be added to the system prompt.
        /// The current markdown format is well understood by nearly every LLM and should probably be left as is.
        /// </summary>
        public string CategorySeparator { get; set; } = "#";

        /// <summary>
        /// Subsection separator for other custom sections that may be added to the system prompt.
        /// The current markdown format is well understood by nearly every LLM and should probably be left as is.
        /// </summary>
        public string SubCategorySeparator { get; set; } = "##";

        public string GetSystemPromptRaw(BasePersona character)
        {
            var selprompt = !string.IsNullOrEmpty(character.SystemPrompt) ? character.SystemPrompt : Prompt;
            var res = new StringBuilder(selprompt.CleanupAndTrim());
            res.AppendLinuxLine();

            if (character.SelfEditTokens > 0 && !string.IsNullOrWhiteSpace(character.SelfEditField))
            {
                res.AppendLinuxLine().AppendLinuxLine($"{CategorySeparator} {character.Name}'s personal thoughts").AppendLinuxLine().AppendLinuxLine("{{selfedit}}");
            }

            if (character.ExampleDialogs.Count > 0 && !string.IsNullOrEmpty(DialogsTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(DialogsTitle).AppendLinuxLine().AppendLinuxLine("{{examples}}");
            }

            if ((!string.IsNullOrEmpty(character.Scenario) || !string.IsNullOrEmpty(LLMEngine.Settings.ScenarioOverride)) && !string.IsNullOrEmpty(ScenarioTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(ScenarioTitle).AppendLinuxLine().AppendLinuxLine("{{scenario}}");
            }


            return res.ToString();
        }
    }
}
