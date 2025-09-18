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
    public class TopicSearch : LLMExtractableBase<TopicSearch>
    {
        [Required]
        [Description("The topic to look at.")]
        public string Topic { get; set; } = string.Empty;
        [Required]
        [Description("The context or reason why this topic is of interest to {{char}}, {{user}}, or both.")]
        public string Reason { get; set; } = string.Empty;
        [Required]
        [Description("Determine the level of urgency for this search, ranging from 1 = tangential curiosity to 10 = very important to the conversation and to both {{user}} and {{char}}'s interests.")]
        public int Urgency { get; set; } = 1;
        [Required]
        [MinLength(1)]
        [MaxLength(3)]
        [Description("1 to 3 concise, high-quality search queries that would help {{char}} learn about the topic.")]
        public List<string> SearchQueries { get; set; } = [];
    }

    public class TopicLookup : LLMExtractableBase<TopicLookup>
    {
        public List<TopicSearch> Unfamiliar_Topics { get; set; } = [];

        public override Task<string> GetGrammar()
        {
            var gramm = """
root ::= "{" space "\"Unfamiliar_Topics\"" space ":" space topics "}" space

topics ::= "[" space "]" space | "[" space topicitem ("," space topicitem){0,2} "]" space

topicitem ::= "{" space topickv "," space contextkv "," space urgencykv "," space querieskv "}" space

topickv ::= "\"Topic\"" space ":" space string
contextkv ::= "\"Reason\"" space ":" space string  
urgencykv ::= "\"Urgency\"" space ":" space integer
querieskv ::= "\"SearchQueries\"" space ":" space queries

queries ::= "[" space "]" space | "[" space queryitem ("," space queryitem){0,2} "]" space

queryitem ::= string

string ::= "\"" char* "\"" space
char ::= [^"\\\x7F\x00-\x1F] | [\\] (["\\bfnrt] | "u" [0-9a-fA-F]{4})

integer ::= ("-"? integralpart) space
integralpart ::= [0] | [1-9] [0-9]{0,15}

space ::= | " " | "\n"{1,2} [ \t]{0,20}
""";
            return Task.FromResult(gramm);
        }

        public override string GetQuery()
        {
            var requestedTask = "Respond in valid JSON using this schema:" + LLMEngine.NewLine;
            var schema = DescriptionHelper.GetAllDescriptionsRecursive<TopicLookup>();
            foreach (var prop in schema)
            {
                requestedTask += $"- {prop.Key}: {prop.Value}\n";
            }
            requestedTask = LLMEngine.Bot.ReplaceMacros(requestedTask);
            return requestedTask;
        }

        /// <summary>
        /// Due to LLM bias, relevance is converted back to 1-3 value manually
        /// </summary>
        public void ClampRelevance()
        {
            foreach (var item in Unfamiliar_Topics)
            {
                int shifted = item.Urgency - 1;
                // divide into 3 buckets
                int bucket = (shifted - 1) / 3 + 1;
                item.Urgency = Math.Clamp(bucket, 1, 3);
            }
        }
    }
}
