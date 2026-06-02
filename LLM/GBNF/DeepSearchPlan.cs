using LetheAISharp.Files;
using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.GBNF
{
    public class DeepSearchPlan : LLMExtractableBase<DeepSearchPlan>
    {
        [Required]
        [Description("Array of 3-6 specific sub-questions to investigate.")]
        public List<string> sub_questions { get; set; } = [];
        [Required]
        [Description("Array of key topics/angles to cover.")]
        public List<string> key_topics { get; set; } = [];
        [Required]
        [Description("One sentence describing what a complete answer looks like.")]
        public string success_criteria { get; set; } = string.Empty;

        public string ToPlan(bool includeSuccessCriteria = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Sub-Questions:");
            for (int i = 0; i < sub_questions.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {sub_questions[i]}");
            }
            sb.AppendLine();
            sb.AppendLine("Key Topics/Angles:");
            for (int i = 0; i < key_topics.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {key_topics[i]}");
            }
            if (includeSuccessCriteria)
            {
                sb.AppendLine();
                sb.AppendLine($"Success Criteria: {success_criteria}");
            }
            return sb.ToString();
        }
    }

    public class DeepSearchQueries : LLMExtractableBase<DeepSearchQueries>
    {
        [Required]
        [Description("Array of specific web search queries.")]
        public List<string> web_search { get; set; } = [];
    }
}
