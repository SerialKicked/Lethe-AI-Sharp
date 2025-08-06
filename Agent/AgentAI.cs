using AIToolkit.API;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Agent
{

    public class AgentAI
    {
        public const int Priority_UserQuery = 100;

        public bool IsRunning { get; private set; } = true;
        public bool IsPaused { get; private set; } = false;

        public ILLMServiceClient? Client => LLMSystem.Client;
        public IPromptBuilder? PromptBuilder => LLMSystem.PromptBuilder;

        private readonly AgentTaskSystem taskSystem = new();

        private readonly ThreadSafePriorityQueue<IAgentAction> JobQueue = new();

        public AgentAI()
        {
            taskSystem.RegisterAction(new WebSearchAction());
            JobQueue.Clear();
        }

        public async Task RunAgentLoop()
        {
            while (IsRunning)
            {
                if (IsPaused || LLMSystem.Status != SystemStatus.Ready)
                {
                    await Task.Delay(100);
                    continue;
                }

                //var job = await GetNextJob();
                //if (job != null)
                //    await ExecuteJob(job);
                //else
                //    await GenerateBackgroundTasks();

                await Task.Delay(100);
            }
        }
    }
}
