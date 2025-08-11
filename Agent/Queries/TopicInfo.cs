using AIToolkit.Files;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Agent
{
    public class TopicSearch : LLMExtractableBase<TopicSearch>
    {
        [Required][Description("The infamiliar topic to search for")]
        public string Topic { get; set; } = string.Empty;
        [Required][Description("The context or reason why this topic is of interest to {{char}} or {{user}}.")]
        public string Reason { get; set; } = string.Empty;
        [Required][Range(1, 3)][Description("Determines the level of urgency for this search (3 = important to the conversation or {{char}}'s interests, 2 = moderately relevant,  1 = tangential curiosity).")]
        public int Urgency { get; set; } = 1;
        [Required][MinLength(1)][MaxLength(3)][Description("1 to 3 concise, high-quality search queries that would help {{char}} learn about the topic.")]
        public List<string> SearchQueries { get; set; } = [];
    }
}
