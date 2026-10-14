using LetheAISharp.Files;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.Skills;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace LetheAISharp.Agent.Actions
{
    /// <summary>
    /// Parameters for <see cref="SkillDraftAction"/>.
    /// </summary>
    public class SkillDraftParams
    {
        /// <summary> What the user wants captured (optional, e.g. "the way we debugged the build errors"). </summary>
        public string Request { get; set; } = string.Empty;

        /// <summary> If set, the existing skill is shown to the model and the draft is an improved version of it. </summary>
        public Skill? ExistingSkill { get; set; }

        /// <summary> Upper limit of conversation tokens given to the model. </summary>
        public int MaxTranscriptTokens { get; set; } = 8000;
    }

    /// <summary>
    /// Result of <see cref="SkillDraftAction"/>. Either <see cref="Skill"/> is set, or <see cref="Error"/> explains the failure.
    /// </summary>
    public class SkillDraftResult
    {
        public Skill? Skill { get; set; }
        public string Error { get; set; } = string.Empty;
        public bool Success => Skill != null;
    }

    /// <summary>
    /// Writes a skill based on the recent conversation. The result is an unsaved <see cref="Skill"/> (marked as agent-written)
    /// that should be reviewed by the user before being saved. This action runs a full LLM query, so it can't be called from inside a tool call.
    /// </summary>
    public partial class SkillDraftAction : IAgentAction<SkillDraftResult, SkillDraftParams>
    {
        public string Id => "SkillDraftAction";
        public HashSet<AgentActionRequirements> Requirements => [AgentActionRequirements.LLM];

        public async Task<SkillDraftResult> Execute(SkillDraftParams param, CancellationToken ct)
        {
            if (LLMEngine.Client == null)
                return new SkillDraftResult { Error = "No LLM backend is connected." };
            if (LLMEngine.LLMBusy)
                return new SkillDraftResult { Error = "The LLM is busy, try again when it has finished generating." };

            var thinkingExtra = string.IsNullOrWhiteSpace(LLMEngine.Instruct.ThinkingStart) ? 0 : 1024;
            var replyln = 3072 + thinkingExtra;
            LLMEngine.NamesInPromptOverride = false;
            try
            {
                var draft = new SkillDraft();
                var promptbuild = LLMEngine.GetPromptBuilder();
                var plain = LLMEngine.Settings.SkillOptions.DraftWithPlainSections;
                var task = BuildTask(param, draft, plain);
                var intro = BuildSystemIntro(param);

                var tokenleft = LLMEngine.MaxContextLength - replyln - 50;
                tokenleft -= promptbuild.GetTokenCount(AuthorRole.System, intro);
                tokenleft -= promptbuild.GetTokenCount(AuthorRole.User, task);
                tokenleft = Math.Min(tokenleft, param.MaxTranscriptTokens);
                if (tokenleft < 300)
                    return new SkillDraftResult { Error = "Not enough context left to analyze the conversation." };

                var transcript = GetTranscript(tokenleft);
                if (string.IsNullOrWhiteSpace(transcript))
                    return new SkillDraftResult { Error = "There is no conversation to build a skill from." };

                promptbuild.AddMessage(new SingleMessage(AuthorRole.System, intro + transcript));
                promptbuild.AddMessage(new SingleMessage(AuthorRole.User, task));
                if (!plain)
                    await promptbuild.SetStructuredOutput(draft).ConfigureAwait(false);
                var temp = LLMEngine.Sampler.Temperature > 0.6 ? 0.6 : LLMEngine.Sampler.Temperature;
                var query = promptbuild.PromptToQuery(AuthorRole.Assistant, temp, replyln);
                var response = await LLMEngine.SimpleQuery(query, ct).ConfigureAwait(false);
                response = response.RemoveThinkingBlocks();
                if (string.IsNullOrWhiteSpace(response))
                    return new SkillDraftResult { Error = "The model returned an empty response." };

                var parsed = plain
                    ? ParseSections(response) ?? ParseDraft(response)
                    : ParseDraft(response) ?? ParseSections(response);
                if (parsed == null)
                {
                    LLMEngine.Logger?.LogWarning("[Skills] Unreadable skill draft ({mode} mode): {response}", plain ? "sections" : "json", response);
                    return new SkillDraftResult { Error = "The model's answer could not be read as a skill. Try again." };
                }

                var skill = new Skill
                {
                    Name = Skill.Slugify(parsed.Name),
                    Description = parsed.Description.Replace("\r\n", " ").Replace('\n', ' ').Trim(),
                    Keywords = [.. parsed.Keywords.Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(8)],
                    Body = parsed.Content.Replace("\r\n", "\n").Trim(),
                    Source = SkillSource.Agent,
                    Enabled = true
                };
                if (param.ExistingSkill != null)
                {
                    // refining keeps the identity of the original skill
                    skill.Name = param.ExistingSkill.Name;
                    skill.Source = param.ExistingSkill.Source;
                    skill.ExtraFrontmatter = [.. param.ExistingSkill.ExtraFrontmatter];
                    skill.FolderPath = param.ExistingSkill.FolderPath;
                    skill.Enabled = param.ExistingSkill.Enabled;
                }
                if (string.IsNullOrEmpty(skill.Name))
                    skill.Name = "new-skill";
                return new SkillDraftResult { Skill = skill };
            }
            catch (OperationCanceledException)
            {
                return new SkillDraftResult { Error = "Cancelled." };
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[Skills] Failed to draft a skill");
                return new SkillDraftResult { Error = ex.Message };
            }
            finally
            {
                LLMEngine.NamesInPromptOverride = null;
            }
        }

        private static string GetTranscript(int maxTokens)
        {
            var text = LLMEngine.History.CurrentSession.GetRawDialogs(maxTokens, true, false, false, true);
            if (string.IsNullOrWhiteSpace(text))
                text = LLMEngine.History.GetRawDialogs(maxTokens, true, false, true);
            return text;
        }

        private static string BuildSystemIntro(SkillDraftParams param)
        {
            var sb = new StringBuilder();
            sb.AppendLinuxLine("You are an automated system that writes reusable skills for an AI assistant.");
            sb.AppendLinuxLine("A skill is a markdown document that teaches an assistant how to carry out a particular kind of task. It is stored in a library and loaded on demand in later, unrelated conversations.");
            sb.AppendLinuxLine();
            sb.AppendLinuxLine("# Guidelines");
            sb.AppendLinuxLine("- Capture the procedure, method or knowledge that was worked out in the conversation, and generalize it so it can be reused for similar tasks.");
            sb.AppendLinuxLine("- Leave out personal details, names, and one-off specifics unless they are parameters of the procedure.");
            sb.AppendLinuxLine("- Write directly to the assistant using instructions (for example: 'Ask the user for...', 'Check that...').");
            sb.AppendLinuxLine("- Be concise but complete. Prefer numbered steps for procedures. Include pitfalls that were discovered.");
            sb.AppendLinuxLine("- Only write what is supported by the conversation. Do not invent facts or steps.");
            sb.AppendLinuxLine();
            if (param.ExistingSkill != null)
            {
                sb.AppendLinuxLine("# Existing skill to improve");
                sb.AppendLinuxLine("Name: " + param.ExistingSkill.Name);
                sb.AppendLinuxLine("Description: " + param.ExistingSkill.Description);
                sb.AppendLinuxLine();
                sb.AppendLinuxLine(param.ExistingSkill.Body);
                sb.AppendLinuxLine();
            }
            sb.AppendLinuxLine("# Conversation between {{user}} and {{mchar}}");
            sb.AppendLinuxLine();
            return sb.ToString();
        }

        private static string BuildTask(SkillDraftParams param, SkillDraft draft, bool plainSections)
        {
            var sb = new StringBuilder();
            if (param.ExistingSkill != null)
                sb.Append("Rewrite the existing skill above, improving it with what the conversation shows. Keep what is still correct.");
            else
                sb.Append("Write a skill based on the conversation above.");
            if (!string.IsNullOrWhiteSpace(param.Request))
                sb.Append(" The user asks for the following focus: ").Append(param.Request.Trim());
            sb.AppendLinuxLine().AppendLinuxLine();
            if (plainSections)
            {
                sb.AppendLinuxLine("Answer using exactly this format, with nothing before or after it:");
                sb.AppendLinuxLine();
                sb.AppendLinuxLine("NAME: a-short-lowercase-name-with-hyphens");
                sb.AppendLinuxLine("DESCRIPTION: One or two sentences explaining what the skill does AND when it should be used. This is the only text the assistant sees when deciding to load it.");
                sb.AppendLinuxLine("KEYWORDS: up to 8 short keywords or phrases, separated by commas (may be left empty)");
                sb.AppendLinuxLine("CONTENT:");
                sb.AppendLinuxLine("The full instructions in markdown, addressed to the assistant, using short sections such as 'When to use', 'Steps' (numbered) and 'Notes and pitfalls'. Do not repeat the name as a title.");
            }
            else
            {
                sb.Append(draft.GetQuery());
            }
            return sb.ToString();
        }

        /// <summary>
        /// Parses the plain labelled format (NAME / DESCRIPTION / KEYWORDS / CONTENT). Tolerates markdown decoration around the labels,
        /// a code fence around the whole answer, and multi-line descriptions. Everything after CONTENT: is the body.
        /// </summary>
        internal static SkillDraft? ParseSections(string response)
        {
            var text = response.Replace("\r\n", "\n").Trim();
            // unwrap a code fence around the whole answer
            if (text.StartsWith("```"))
            {
                var firstNl = text.IndexOf('\n');
                if (firstNl > 0)
                    text = text[(firstNl + 1)..];
                var lastFence = text.LastIndexOf("```");
                if (lastFence >= 0)
                    text = text[..lastFence];
            }

            var draft = new SkillDraft();
            string? field = null;
            var buffers = new Dictionary<string, StringBuilder>
            {
                ["NAME"] = new(), ["DESCRIPTION"] = new(), ["KEYWORDS"] = new(), ["CONTENT"] = new()
            };
            foreach (var line in text.Split('\n'))
            {
                if (field != "CONTENT")
                {
                    var m = LabelRegex().Match(line);
                    if (m.Success)
                    {
                        field = m.Groups[1].Value.ToUpperInvariant();
                        var rest = m.Groups[2].Value;
                        if (rest.Length > 0)
                            buffers[field].AppendLine(rest);
                        continue;
                    }
                }
                if (field != null)
                    buffers[field].AppendLine(line);
            }

            var content = buffers["CONTENT"].ToString().Trim('\n', '\r', ' ');
            if (string.IsNullOrWhiteSpace(content))
                return null;
            draft.Name = buffers["NAME"].ToString().Trim().Trim('`', '*', '"');
            draft.Description = string.Join(" ", buffers["DESCRIPTION"].ToString().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            draft.Keywords = [.. buffers["KEYWORDS"].ToString().Replace('\n', ',').Split(',').Select(k => k.Trim().Trim('*', '`', '-', ' ')).Where(k => k.Length > 0)];
            draft.Content = content;
            return draft;
        }

        [GeneratedRegex(@"^\s*[#>*_\-\s]*(NAME|DESCRIPTION|KEYWORDS|CONTENT)\s*[*_]*\s*:\s*[*_]*\s*(.*)$", RegexOptions.IgnoreCase)]
        private static partial Regex LabelRegex();

        private static SkillDraft? ParseDraft(string response)
        {
            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;
            try
            {
                var draft = JsonConvert.DeserializeObject<SkillDraft>(response[start..(end + 1)]);
                if (draft == null || string.IsNullOrWhiteSpace(draft.Content))
                    return null;
                return draft;
            }
            catch (JsonException ex)
            {
                LLMEngine.Logger?.LogWarning(ex, "[Skills] Could not parse skill draft");
                return null;
            }
        }
    }
}
