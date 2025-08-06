using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Agent
{
    public enum ActionResultType
    {
        Success,
        Failure,
        InProgress,
        Cancelled
    }

    public class ActionResult
    {
        public ActionResultType ResultType { get; set; } // success, failure, cancelled...
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object> Data { get; set; } = [];
        public ActionResult(ActionResultType resultType, string message = "", Dictionary<string, object>? data = null)
        {
            ResultType = resultType;
            Message = message;
            if (data != null)
                Data = data;
        }
    }

    public interface IAgentAction
    {
        string Name { get; }
        string Description { get; }
        List<string> ValidContexts { get; } // ["chat", "background", "proactive"]
        Task<ActionResult> Execute(Dictionary<string, object> parameters);
    }

    public class AgentTaskSystem
    {
        private Dictionary<string, IAgentAction> availableActions = [];

        public string GetAvailableActionsPrompt()
        {
            var descriptions = availableActions.Values
                .Select(a => $"{a.Name}: {a.Description} (contexts: {string.Join(",", a.ValidContexts)})")
                .ToList();

            return "Available actions:\n" + string.Join("\n", descriptions);
        }

        public void RegisterAction(IAgentAction action)
        {
            if (!availableActions.ContainsKey(action.Name))
            {
                availableActions[action.Name] = action;
            }
        }
    }
}
