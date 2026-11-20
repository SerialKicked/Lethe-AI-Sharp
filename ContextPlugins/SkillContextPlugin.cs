using LetheAISharp.Files;
using LetheAISharp.Skills;
using System.Text;

namespace LetheAISharp.LLM
{
    /// <summary>
    /// Context plugin that surfaces skills matching the user's latest message.
    /// When the model can load skills itself (tool calls active and the "Skills" toolset allowed) it only adds a hint naming the matching skills. 
    /// Otherwise the skill content is injected directly in the system prompt.
    /// </summary>
    /// <remarks>
    /// This plugin is controlled globally by <see cref="SkillSettings.AutoSuggest"/>, so the per-persona plugin list doesn't affect it.
    /// </remarks>
    public class SkillContextPlugin : IContextPlugin
    {
        public const string ToolsetId = "Skills";

        public string PluginID => "Skills";

        public bool Enabled
        {
            get => LLMEngine.Settings.SkillOptions.AutoSuggest;
            set { /* controlled by SkillSettings.AutoSuggest */ }
        }

        private string lastInput = string.Empty;
        private List<SkillMatch> lastMatches = [];

        public bool AddToSystemPrompt(string userinput, Chatlog log, out string response)
        {
            response = string.Empty;
            var options = LLMEngine.Settings.SkillOptions;
            if (!options.AutoSuggest || string.IsNullOrWhiteSpace(userinput) || userinput.Trim().Length < 4)
                return false;
            var manager = LLMEngine.SkillManager;
            if (manager.Enabled.Count == 0)
                return false;

            List<SkillMatch> matches;
            lock (this)
            {
                if (lastInput != userinput)
                {
                    lastMatches = manager.Search(userinput, Math.Max(1, options.AutoSuggestMax), SkillManager.AutoMinScore);
                    lastInput = userinput;
                }
                matches = lastMatches;
            }
            if (matches.Count == 0)
                return false;

            var toolsActive = LLMEngine.ToolCallsLoaded && LLMEngine.ToolManager.AllowedToolSets.Contains(ToolsetId);
            var sb = new StringBuilder();
            sb.Append(LLMEngine.NewLine).Append(LLMEngine.SystemPrompt.CategorySeparator).Append(" Relevant Skills").Append(LLMEngine.NewLine);
            if (toolsActive)
            {
                sb.Append("The following skills look relevant to the user's request. Call Skill_Load to read the one you need before starting:").Append(LLMEngine.NewLine);
                foreach (var m in matches)
                    sb.Append("- ").Append(m.Skill.Name).Append(": ").Append(m.Skill.Description).Append(LLMEngine.NewLine);
            }
            else
            {
                sb.Append("The following skills look relevant to the user's request. Follow their instructions when they apply:").Append(LLMEngine.NewLine).Append(LLMEngine.NewLine);
                var budget = Math.Max(500, options.AutoSuggestMaxChars);
                foreach (var m in matches)
                {
                    var body = m.Skill.Body;
                    if (body.Length > budget)
                        body = body[..budget] + "\n[...]";
                    sb.Append(LLMEngine.SystemPrompt.CategorySeparator).Append("# ").Append(m.Skill.Name).Append(LLMEngine.NewLine).Append(body).Append(LLMEngine.NewLine).Append(LLMEngine.NewLine);
                    budget -= body.Length;
                    if (budget < 300)
                        break;
                }
            }
            response = sb.ToString();
            return true;
        }

        public bool ReplaceOutput(string botoutput, Chatlog log, out string response)
        {
            response = string.Empty;
            return false;
        }

        public Task<PluginResponse> ReplaceUserInput(string userinput)
        {
            return Task.FromResult(new PluginResponse { IsHandled = false });
        }
    }
}
