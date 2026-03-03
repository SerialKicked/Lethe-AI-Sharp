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
    /// <param name="charID"> Character ID associated with the bot persona </param>
    /// <param name="userID"> User ID associated with the user persona </param>
    /// <param name="hidden"> Indicates if the message is hidden from standard views </param>
    public class SingleMessage(AuthorRole role, DateTime date, string mess, string charID, string userID, bool hidden = false, string imagePath = "", List<ToolCallRecord>? toolCalls = null)
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public AuthorRole Role = role;
        public string Message = mess;
        public DateTime Date = date;
        public string CharID = charID;
        public string UserID = userID;
        public string ImagePath = imagePath;
        public bool Hidden = hidden;
        public string Note = string.Empty;
        public List<ToolCallRecord> ToolCalls { get; set; } = toolCalls ?? [];
        [JsonIgnore] public BasePersona User => 
            !string.IsNullOrEmpty(UserID) && LLMEngine.LoadedPersonas.TryGetValue(UserID, out var u) ? u : LLMEngine.User;
        [JsonIgnore] public BasePersona Bot => 
            !string.IsNullOrEmpty(CharID) && LLMEngine.LoadedPersonas.TryGetValue(CharID, out var c) ? c : LLMEngine.Bot;
        [JsonIgnore] public BasePersona? Sender => 
            Role == AuthorRole.User? User : Role == AuthorRole.Assistant ? Bot : null;

        public SingleMessage(AuthorRole role, string mess, string img = "", List<ToolCallRecord>? toolCalls = null) : 
            this(role, DateTime.Now, mess, LLMEngine.Bot.GetIdentifier(), LLMEngine.User.GetIdentifier(), false, img, toolCalls)
        { }

        public SingleMessage() : this(AuthorRole.User, DateTime.Now, "", "", "", false, "")
        {
        }

        public string ToTextCompletion()
        {
            return LLMEngine.Instruct.FormatSingleMessage(this);
        }

        public Message ToChatCompletion()
        {
            // Tool result messages need the tool_call_id for the API to correlate results.
            // Each ToolResult message corresponds to exactly one tool call (one record per message).
            if (Role == AuthorRole.ToolResult && ToolCalls.Count > 0)
            {
                var record = ToolCalls[0];
                return new Message(record.CallId, record.FunctionName, new List<OpenAI.Content> { Message });
            }

            // Assistant messages that contain tool call records (i.e. were a tool-call request).
            // Each such message is logged with exactly one tool call record for history reconstruction.
            if (Role == AuthorRole.Assistant && ToolCalls.Count > 0)
            {
                return new Message(OpenAI.Role.Assistant, string.Empty);
            }

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
