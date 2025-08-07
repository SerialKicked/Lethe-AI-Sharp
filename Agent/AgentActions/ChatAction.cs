using AIToolkit.LLM;

namespace AIToolkit.Agent
{
    public class ChatAction : BaseAction, IAgentAction
    {
        public override async Task<ActionResult> Execute()
        {
            var author = (AuthorRole)Parameters.GetValueOrDefault("role", AuthorRole.User);
            var query = Parameters.GetValueOrDefault("query", string.Empty).ToString()!;
            await LLMSystem.SendMessageToBot(author, query);
            return new ActionResult(ActionResultType.Success, $"Query sent from {author} to agent: {query}", null);
        }

        public ChatAction(Dictionary<string, object>? parameters) : base(parameters)
        {
            Name = "chat_action";
            Description = "Action to handle chat interactions.";
            if (parameters is null || parameters["role"] is not AuthorRole)
            {
                throw new ArgumentNullException(nameof(parameters), "Parameters cannot be null and must contain author role role");
            }
        }
    }
}
