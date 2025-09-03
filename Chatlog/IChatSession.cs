using AIToolkit.GBNF;

namespace AIToolkit.Files
{
    public interface IChatSession : IEmbed
    {
        DateTime StartTime { get; set; }
        DateTime EndTime { get; set; }
        List<SingleMessage> Messages { get; set; }
        SessionMetaInfo MetaData { get; set; }
        TopicLookup NewTopics { get; set; }
        string Scenario { get; set; }
        bool Sticky { get; set; }
        string Content { get; }
        string Name { get; }

        Task<string> GenerateGoals();
        Task<string[]> GenerateKeywords();
        Task<string> GenerateNewSummary();
        Task<string> GenerateNewTitle(string sum);
        Task<string> GenerateTaskRes(string requestedTask, int responseLen, bool lightDialogs = false, bool showHidden = false);
        string GetRawDialogs(int maxTokens, bool ignoresystem, bool lightDialogs = false, bool showHidden = false);
        string GetRawMemory(bool withtitle, bool includedates);
        Task<TopicLookup> GetResearchTopics();
        Task<SessionMetaInfo> GetSessionInfo();
        Task<bool> IsRoleplay();
        Task UpdateSession();
    }
}