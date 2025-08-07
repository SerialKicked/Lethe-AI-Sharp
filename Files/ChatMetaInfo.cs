using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Files
{
    public class ChatMetaInfo
    {
        [Required][Description("The title for this chat session")]
        public string Title { get; set; } = string.Empty;
        [Required][Description("The summary of this chat session")]
        public string Summary { get; set; } = string.Empty;
        [Required][Description("Determine if this session is mostly roleplay")]
        public bool IsRoleplaySession { get; set; } = false;
        [MinLength(0)][MaxLength(5)][Description("List of goals and projects for future sessions")]
        public List<string> FutureGoals { get; set; } = [];
        // list of single word tags
        [Required][MinLength(4)][MaxLength(6)][Description("List of tags for this chat session, used for filtering and searching")]
        public List<string> Keywords { get; set; } = [];


    }
}
