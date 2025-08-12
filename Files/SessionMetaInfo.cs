using AIToolkit.Agent;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace AIToolkit.Files
{

    public class SessionMetaInfo : LLMExtractableBase<SessionMetaInfo>
    {
        [Required][Description("A title for this chat session.")]
        public string Title { get; set; } = string.Empty;
        
        [Required][Description("A detailed summary of this chat session. Focus on important parts. Should be at least 2 paragraphs.")]
        public string Summary { get; set; } = string.Empty;
        
        [Required][Description("A boolean to say if this chat correspond to a roleplay session or a normal chat (roleplay sessions have physical actions and a lot of *narrative text in asterisks like this*). Only mark a session as roleplay if it's practically entirely roleplay.")]
        public bool IsRoleplaySession { get; set; } = false;
        
        [MinLength(0)][MaxLength(5)][Description("An optional list of future plans set during the discussion. It can contain up to 5 elements, and can be empty if no particular goals were set during the session.")]
        public List<string> FutureGoals { get; set; } = [];

        [Required][MinLength(4)][MaxLength(6)][Description("A list of 4 to 6 keywords for this chat session, used for filtering and searching.")]
        public List<string> Keywords { get; set; } = [];

        [Required][Description("A value between 1 and 5 determining how important and meaningful this session is to {{char}}. Ranging from 1 (not important) to 5 (critical importance). Make your judgement based on {{char}}'s personality and the session's overal tone.")]
        public int Relevance { get; set; } = 1;
    }
}
