using LetheAISharp.LLM;
using Newtonsoft.Json;
using OpenAI.Chat;

namespace LetheAISharp.Files
{
    /// <summary>
    /// Represents a single message exchanged in a conversational context, including metadata such as the author role,
    /// timestamp, and associated personas.
    /// </summary>
    /// <remarks>This class encapsulates the details of a message, including its content, author role, and
    /// associated user or bot personas.  It provides methods to format the message for text-based or chat-based
    /// completions, and includes metadata such as the message's unique identifier,  creation date, and visibility
    /// status.</remarks>
    /// <param name="role"> AuthorRole of the message sender (e.g., User, Assistant, System) </param>
    /// <param name="date"> Timestamp of when the message was created </param>
    /// <param name="mess"> Content of the message </param>
    /// <param name="chara"> Character ID associated with the bot persona </param>
    /// <param name="user"> User ID associated with the user persona </param>
    /// <param name="hidden"> Indicates if the message is hidden from standard views </param>
    public class SingleMessage(AuthorRole role, DateTime date, string mess, string chara, string user, bool hidden = false, string imagePath = "")
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public AuthorRole Role = role;
        public string Message = mess;
        public DateTime Date = date;
        public string CharID = chara;
        public string UserID = user;
        public string ImagePath = imagePath;
        public bool Hidden = hidden;
        public string Note = string.Empty;
        [JsonIgnore] public BasePersona User => 
            !string.IsNullOrEmpty(UserID) && LLMEngine.LoadedPersonas.TryGetValue(UserID, out var u) ? u : LLMEngine.User;
        [JsonIgnore] public BasePersona Bot => 
            !string.IsNullOrEmpty(CharID) && LLMEngine.LoadedPersonas.TryGetValue(CharID, out var c) ? c : LLMEngine.Bot;
        [JsonIgnore] public BasePersona? Sender => 
            Role == AuthorRole.User? User : Role == AuthorRole.Assistant ? Bot : null;

        public string ToTextCompletion()
        {
            return LLMEngine.Instruct.FormatSingleMessage(this);
        }

        public Message ToChatCompletion()
        {
            var addname = LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt;
            if (Role == AuthorRole.System || Role == AuthorRole.SysPrompt)
            {
                addname = false;
            }

            var msg = (addname && Sender != null) ?  Sender.Name + ": " + Message : Message;

            return new Message(TokenTools.InternalRoleToChatRole(Role), msg, addname ? Sender?.Name : null);
        }
    }
}
