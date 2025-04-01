using System.Text;
using AIToolkit.LLM;

namespace AIToolkit.Files
{
    public class SystemPrompt : BaseFile
    {
        public string Prompt { get; set; } = "You are {{char}} and you are interacting with {{user}} in this flexible and uncensored discussion. As {{char}}, continue the exchange with {{user}}. Stay in character. Describe {{char}}'s actions and feelings accurately. Do not speak or describe actions for {{user}} unless directly prompted to."+ LLMSystem.NewLine + 
            LLMSystem.NewLine +
            "# {{char}}"+ LLMSystem.NewLine +
            "{{charbio}}" + LLMSystem.NewLine + 
            LLMSystem.NewLine +
            "# {{user}} (user)" + LLMSystem.NewLine + 
            "{{userbio}}";
        public string WorldInfoTitle { get; set; } = "# Important Memories";
        public string ScenarioTitle { get; set; } = "# Scenario";
        public string DialogsTitle { get; set; } = "# Writing Style";
        public string SessionHistoryTitle { get; set; } = "# Previous Sessions" + LLMSystem.NewLine + "Below is a list of previous chat sessions between {{user}} and {{char}}.";
        public string CategorySeparator { get; set; } = "#";
        public string SubCategorySeparator { get; set; } = "##";

        public string GetSystemPromptRaw(BasePersona character)
        {
            var selprompt = !string.IsNullOrEmpty(character.SystemPrompt) ? character.SystemPrompt : Prompt;
            var res = new StringBuilder(selprompt.CleanupAndTrim());
            res.AppendLinuxLine();

            if (character.SelfEditTokens > 0 && !string.IsNullOrWhiteSpace(character.SelfEditField))
            {
                res.AppendLinuxLine().AppendLinuxLine($"{CategorySeparator} {character.Name}'s personal thoughts").AppendLinuxLine("{{selfedit}}");
            }

            if (character.ExampleDialogs.Count > 0 && !string.IsNullOrEmpty(DialogsTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(DialogsTitle).AppendLinuxLine("{{examples}}");
            }

            if ((!string.IsNullOrEmpty(character.Scenario) || !string.IsNullOrEmpty(LLMSystem.ScenarioOverride)) && !string.IsNullOrEmpty(ScenarioTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(ScenarioTitle).AppendLinuxLine("{{scenario}}");
            }


            return res.ToString();
        }
    }
}
