using LetheAISharp.LLM;
using Newtonsoft.Json;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace LetheAISharp.GBNF
{
    /// <summary>
    /// Structured output used when asking the model to write a skill from a conversation.
    /// </summary>
    public class SkillDraft : LLMExtractableBase<SkillDraft>
    {
        [JsonIgnore] protected static string Schema { get; set; } = string.Empty;

        [Required]
        [Description("A short identifier for the skill, in lowercase with words separated by hyphens (for example: 'write-commit-messages').")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Description("One or two sentences explaining what the skill does AND when it should be used. This is the only text the assistant sees when deciding to load the skill, so mention the situations or phrases that should trigger it.")]
        public string Description { get; set; } = string.Empty;

        [MaxLength(8)]
        [Description("An optional list of 0 to 8 short keywords or phrases that someone would use when asking for this kind of task.")]
        public List<string> Keywords { get; set; } = [];

        [Required]
        [Description("The full skill instructions, written in markdown and addressed directly to the assistant. Use short sections such as 'When to use', 'Steps' (numbered), and 'Notes and pitfalls'. Do not repeat the name as a title and do not include frontmatter.")]
        public string Content { get; set; } = string.Empty;

        public override async Task<string> GetGrammar()
        {
            if (Schema == string.Empty)
            {
                Schema = await base.GetGrammar().ConfigureAwait(false);
            }
            return Schema;
        }
    }
}
