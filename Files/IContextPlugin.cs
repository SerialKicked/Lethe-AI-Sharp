using AIToolkit.LLM;

namespace AIToolkit.Files
{
    public class PluginResponse
    {
        public bool IsHandled { get; set; }
        public string? Response { get; set; }
        public bool Replace { get; set; } = true;
        public AuthorRole AuthorRole { get; set; } = AuthorRole.User;
    }

    public interface IContextPlugin
    {
        string PluginID { get; }
        bool Enabled { get; set; }
        bool AddToSystemPrompt(string userinput, Chatlog log, out string response);
        bool ReplaceOutput(string botoutput, Chatlog log, out string response);
        Task<PluginResponse> ReplaceUserInput(string userinput);
    }
}