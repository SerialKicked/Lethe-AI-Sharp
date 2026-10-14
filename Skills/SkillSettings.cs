using System.ComponentModel;

namespace LetheAISharp.Skills
{
    /// <summary>
    /// Settings for the skill system. Stored as a sub-object of <see cref="Files.LLMSettings"/>.
    /// </summary>
    public class SkillSettings
    {
        /// <summary>
        /// Folder containing the skills. Each skill lives in its own sub-folder with a SKILL.md file.
        /// </summary>
        [Description("Folder containing the skills. Each skill lives in its own sub-folder with a SKILL.md file.")]
        public string Folder { get; set; } = "data/skills/";

        /// <summary>
        /// Maximum number of skills listed (name and description) in the system prompt. 
        /// Additional skills remain reachable via the search tool.
        /// </summary>
        [Description("Maximum number of skills listed (name and description) in the system prompt.\n" +
            "Additional skills remain reachable through the skill search tool.")]
        public int MaxIndexEntries { get; set; } = 40;

        /// <summary>
        /// Maximum number of characters of a skill description shown in the system prompt index.
        /// </summary>
        [Description("Maximum number of characters of a skill's description shown in the system prompt index.")]
        public int MaxIndexDescriptionLength { get; set; } = 220;

        /// <summary>
        /// When enabled, skills matching the user's message are suggested (or injected when tool calls are unavailable) in the system prompt.
        /// </summary>
        [Description("When enabled, skills matching the user's latest message are suggested in the system prompt.\n" +
            "If the model can't use tools, the matching skill content is injected directly instead.\n" +
            "Note that this changes the system prompt when a skill matches, which can reduce prompt cache reuse.")]
        public bool AutoSuggest { get; set; } = true;

        /// <summary>
        /// Maximum number of skills suggested or injected automatically for a single message.
        /// </summary>
        [Description("Maximum number of skills suggested or injected automatically for a single message.")]
        public int AutoSuggestMax { get; set; } = 2;

        /// <summary>
        /// Maximum number of characters injected in the system prompt by the auto-suggest feature when tool calls are unavailable.
        /// </summary>
        [Description("Maximum number of characters injected in the system prompt by auto-suggest when the model can't load skills by itself.")]
        public int AutoSuggestMaxChars { get; set; } = 6000;

        /// <summary>
        /// Use embedding similarity (in addition to keywords) when searching for skills. Requires RAG to be enabled.
        /// </summary>
        [Description("Use embedding similarity (in addition to keyword matching) when searching for skills. Requires the RAG embedding model to be enabled.")]
        public bool UseEmbeddings { get; set; } = true;

        /// <summary>
        /// Maximum embedding distance (0 to 2) for a skill to be considered a similarity match.
        /// </summary>
        [Description("Maximum embedding distance (0 = identical, 2 = opposite) for a skill to be considered a match. Lower is stricter.")]
        public float SimilarityCutoff { get; set; } = 0.45f;

        /// <summary>
        /// Allow the model to create and edit skills with tool calls (always requires user confirmation when the host application supports it).
        /// </summary>
        [Description("Allow the model to create and edit skills through tool calls.\n" +
            "The user is asked to confirm each change when the application supports confirmations. Otherwise new skills are created disabled.")]
        public bool AllowAgentWrites { get; set; } = false;

        /// <summary>
        /// When generating a skill draft from the chat, ask the model for plain labelled sections (NAME / DESCRIPTION / KEYWORDS / CONTENT) instead of a JSON object.
        /// </summary>
        [Description("When writing a skill from the chat, ask the model for plain labelled sections (NAME, DESCRIPTION, KEYWORDS, CONTENT) instead of a JSON object.\n" +
            "The markdown body is then written as raw text, which is usually more reliable with smaller models. Disable to use structured JSON output.")]
        public bool DraftWithPlainSections { get; set; } = false;

        /// <summary>
        /// Maximum size, in characters, of a skill's content.
        /// </summary>
        [Description("Maximum size, in characters, of a skill's content.")]
        public int MaxContentLength { get; set; } = 24000;

        /// <summary>
        /// Maximum number of skills that can exist in the library. Prevents runaway skill creation by the model.
        /// </summary>
        [Description("Maximum number of skills allowed in the library. Prevents runaway skill creation.")]
        public int MaxSkillCount { get; set; } = 200;
    }
}
