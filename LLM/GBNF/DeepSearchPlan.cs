using LetheAISharp.LLM;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.GBNF
{
    public class DeepSearchPlan : LLMExtractableBase<DeepSearchPlan>
    {
        [JsonIgnore] 
        protected static string Schema { get; set; } = string.Empty;

        [Required]
        [Description("A title for this deep-research report.")]
        public string Title { get; set; } = string.Empty;

        [Required]
        [Description("A summary regarding how you intend to approach the research.")]
        public string Summary { get; set; } = string.Empty;

        [Required]
        [MinLength(3)]
        [MaxLength(6)]
        [Description("A list of 3 to 6 specific sub-topics to investigate.")]
        public List<string> SubQuestions { get; set; } = [];

        [Required]
        [MinLength(1)]
        [MaxLength(5)]
        [Description("Array of key topics/angles to cover.")]
        public List<string> KeyTopics { get; set; } = [];

        [Required]
        [Description("One sentence describing what a complete answer looks like.")]
        public string SuccessCriteria { get; set; } = string.Empty;

        public override async Task<string> GetGrammar()
        {
            if (Schema == string.Empty)
            {
                Schema = await base.GetGrammar().ConfigureAwait(false);
            }
            return Schema;
        }

        public string ToPlan(bool includeSuccessCriteria = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Sub-Questions:");
            for (int i = 0; i < SubQuestions.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {SubQuestions[i]}");
            }
            sb.AppendLine();
            sb.AppendLine("Key Topics/Angles:");
            for (int i = 0; i < KeyTopics.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {KeyTopics[i]}");
            }
            if (includeSuccessCriteria)
            {
                sb.AppendLine();
                sb.AppendLine($"Success Criteria: {SuccessCriteria}");
            }
            return sb.ToString();
        }
    }

    public class DeepSearchQueries : LLMExtractableBase<DeepSearchQueries>
    {
        [Required]
        [Description("Array of specific web search queries.")]
        public List<string> WebQueries { get; set; } = [];
    }
}
