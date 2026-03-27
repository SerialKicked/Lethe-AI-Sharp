using CommunityToolkit.HighPerformance;
using LetheAISharp.LLM;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using System.Text;
using System.Text.Json.Nodes;

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
        public string ThinkBlock = string.Empty;
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

        internal string ToTextCompletion()
        {
            return LLMEngine.Instruct.FormatSingleMessage(this);
        }

        internal string ToolCallToString()
        {
            if (ToolCalls.Count == 0) 
                return string.Empty;

            var tmsg = new StringBuilder("[Function Call History]");
            tmsg.AppendLinuxLine();
            foreach (var call in ToolCalls)
            {
                tmsg.AppendLinuxLine(call.ToString());
            }
            return tmsg.ToString();
        }

        internal Message ToChatCompletion()
        {
            // Tool result messages: skip all name/image logic
            if (Role == AuthorRole.Tool && ToolCalls.Count > 0)
            {
                return new Message(OpenAI.Role.Tool, Message);
            }

            // Assistant tool-call-only messages: skip all name/image logic
            if (Role == AuthorRole.Assistant && ToolCalls?.Count > 0 && string.IsNullOrEmpty(Message))
            {
                var calls = new List<ToolCall>();
                foreach (var toolCallRecord in ToolCalls)
                    calls.Add(toolCallRecord.ToToolcall());
                // new system
                return new Message(OpenAI.Role.Assistant, calls, Message);
            }

            var realprompt = Message;
            var addname = LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt;

            // In group conversations, ALWAYS add names so the LLM knows which persona is speaking
            if (Bot is GroupPersonaBase)
                addname = true;

            if (Role != AuthorRole.Assistant && Role != AuthorRole.User)
                addname = false;
            string? selname = null;
            if (addname)
            {
                if (Role == AuthorRole.Assistant)
                {
                    selname = Bot.Name;
                    realprompt = string.Format("{0}: {1}", Bot.Name, Message);
                }
                else if (Role == AuthorRole.User)
                {
                    selname = User.Name;
                    realprompt = string.Format("{0}: {1}", User.Name, Message);
                }
            }

            if (!LLMEngine.SupportsVision || string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath))
                return new Message(TokenTools.InternalRoleToChatRole(Role), Bot.ReplaceMacros(realprompt, User), selname);

            var content = new List<Content>();

            var extension = Path.GetExtension(ImagePath).ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    content.Add(new(ContentType.ImageUrl, $"data:image/jpeg;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                case ".png":
                    content.Add(new(ContentType.ImageUrl, $"data:image/png;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                case ".gif":
                    content.Add(new(ContentType.ImageUrl, $"data:image/gif;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                case ".bmp":
                    content.Add(new(ContentType.ImageUrl, $"data:image/bmp;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                case ".webp":
                    content.Add(new(ContentType.ImageUrl, $"data:image/webp;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                case ".tiff ":
                    content.Add(new(ContentType.ImageUrl, $"data:image/tiff;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
                default:
                    content.Add(new(ContentType.ImageUrl, $"data:image/gif;base64,{ImageUtils.ImageToBase64(ImagePath, LLMEngine.Settings.ImageResolution)!}"));
                    break;
            }
            content.Add(Bot.ReplaceMacros(realprompt, User));

            return new Message(TokenTools.InternalRoleToChatRole(Role), content);
        }
    }
}
