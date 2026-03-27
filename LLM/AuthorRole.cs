namespace LetheAISharp.LLM
{
    public enum AuthorRole
    {
        System,
        User,
        Assistant,
        Unknown,
        [Obsolete("Use System instead of SysPrompt")]
        SysPrompt,
        Tool
    }

}