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
        Dictionary<string, object> Parameters { get; }
        Task<ActionResult> Execute();
    }

    public class BaseAction : IAgentAction
    {
        public string Name { get; protected set; } = "base_action";
        public string Description { get; protected set; } = "Base action with no specific functionality.";
        public Dictionary<string, object> Parameters { get; protected set; } = [];

        public BaseAction(Dictionary<string, object>? parameters)
        {
            SetParameters(parameters);
        }

        public void SetParameters(Dictionary<string, object>? parameters)
        {
            Parameters = parameters ?? [];
        }

        public virtual Task<ActionResult> Execute()
        {
            return Task.FromResult(new ActionResult(ActionResultType.Success, "Base action executed successfully."));
        }
    }
}
