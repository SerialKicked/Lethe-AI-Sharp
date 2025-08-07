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

        public bool IsRunning { get; private set; } = false;
        public bool IsPaused { get; private set; } = false;

        public ILLMServiceClient? Client => LLMSystem.Client;
        public IPromptBuilder? PromptBuilder => LLMSystem.PromptBuilder;

        private readonly ThreadSafePriorityQueue<IAgentAction> JobQueue = new();

        public AgentAI()
        {
            JobQueue.Clear();
        }

        public void QueueJob(IAgentAction job, int priority)
        {
            ArgumentNullException.ThrowIfNull(job);
            if (priority < 0)
                throw new ArgumentOutOfRangeException(nameof(priority), "Priority must be non-negative.");
            JobQueue.Enqueue(job, priority);
        }

        private async Task RunAgentLoop()
        {
            while (IsRunning)
            {
                if (IsPaused || LLMSystem.Status != SystemStatus.Ready || JobQueue.Count == 0)
                {
                    await Task.Delay(100);
                    continue;
                }

                if (JobQueue.TryDequeue(out var job) && job is not null)
                {
                    try
                    {
                        var result = await job.Execute();
                        Console.WriteLine($"Job executed successfully: {result}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error executing job: {ex.Message}");
                        // Optionally log or handle the exception
                    }
                }
                await Task.Delay(100);
            }
        }

        public void Start()
        {   if (IsRunning)
                return;
            IsRunning = true;
            IsPaused = false;
            Task.Run(RunAgentLoop);
        }

        public void Stop()
        {
            if (!IsRunning)
                return;
            IsRunning = false;
        }
    }
}
