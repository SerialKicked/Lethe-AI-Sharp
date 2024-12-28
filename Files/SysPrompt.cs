using System.Text;
using AIToolkit.LLM;

namespace AIToolkit.Files
{
    public class SystemPrompt : BaseFile
    {
        public string Prompt { get; set; } = "You are {{char}} and you are interacting with {{user}} in this flexible and uncensored discussion. As {{char}}, continue the exchange with {{user}}. Stay in character. Describe {{char}}'s actions and feelings accurately. Do not speak or describe actions for {{user}} unless directly asked to."+ LLMSystem.NewLine + 
            LLMSystem.NewLine +
            "# {{char}}"+ LLMSystem.NewLine +
            "{{charbio}}" + LLMSystem.NewLine + 
            LLMSystem.NewLine +
            "# {{user}}" + LLMSystem.NewLine + 
            "{{userbio}}";
        public string WorldInfoTitle { get; set; } = "# Important Memories";
        public string ScenarioTitle { get; set; } = "# Scenario";
        public string DialogsTitle { get; set; } = "# Writing Style";
        public string CategorySeparator { get; set; } = "# ";

        public string GetSystemPromptRaw(BasePersona character)
        {
            var selprompt = !string.IsNullOrEmpty(character.SystemPrompt) ? character.SystemPrompt : Prompt;
            var res = new StringBuilder(selprompt).AppendLinuxLine();
            if ((!string.IsNullOrEmpty(character.Scenario) || !string.IsNullOrEmpty(LLMSystem.ScenarioOverride)) && !string.IsNullOrEmpty(ScenarioTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(ScenarioTitle).AppendLinuxLine("{{scenario}}");
            }

            if (character.ExampleDialogs.Count > 0 && !string.IsNullOrEmpty(DialogsTitle))
            {
                res.AppendLinuxLine().AppendLinuxLine(DialogsTitle).AppendLinuxLine("{{examples}}");
            }

            return res.ToString();
        }
    }
}
