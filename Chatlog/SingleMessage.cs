using AIToolkit.LLM;
using Newtonsoft.Json;
using OpenAI.Chat;

namespace AIToolkit.Files
{
    public class SingleMessage(AuthorRole role, DateTime date, string mess, string chara, string user, bool hidden = false)
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public AuthorRole Role = role;
        public string Message = mess;
        public DateTime Date = date;
        public string CharID = chara;
        public string UserID = user;
        public bool Hidden = hidden;
        public string Note = string.Empty;
        [JsonIgnore] public BasePersona User => 
            !string.IsNullOrEmpty(UserID) && LLMSystem.LoadedPersonas.TryGetValue(UserID, out var u) ? u : LLMSystem.User;
        [JsonIgnore] public BasePersona Bot => 
            !string.IsNullOrEmpty(CharID) && LLMSystem.LoadedPersonas.TryGetValue(CharID, out var c) ? c : LLMSystem.Bot;
        [JsonIgnore] public BasePersona? Sender => 
            Role == AuthorRole.User? User : Role == AuthorRole.Assistant ? Bot : null;

        public string ToTextCompletion()
        {
            return LLMSystem.Instruct.FormatSingleMessage(this);
        }

        public Message ToChatCompletion()
        {
            var addname = LLMSystem.NamesInPromptOverride ?? LLMSystem.Instruct.AddNamesToPrompt;
            if (Role == AuthorRole.System || Role == AuthorRole.SysPrompt)
            {
                addname = false;
            }

            var msg = (addname && Sender != null) ?  Sender.Name + ": " + Message : Message;

            return new Message(TokenTools.InternalRoleToChatRole(Role), msg, addname ? Sender?.Name : null);
        }
    }
}
