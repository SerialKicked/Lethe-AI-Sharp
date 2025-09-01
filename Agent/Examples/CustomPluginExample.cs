using AIToolkit.Agent;
using System.Text.Json;

namespace AIToolkit.Agent.Examples
{
    /// <summary>
    /// Example demonstrating how to create and register a custom Agent plugin
    /// </summary>
    public class CustomPluginExample
    {
        /// <summary>
        /// Example custom plugin that monitors for specific keywords and creates tasks
        /// </summary>
        public class KeywordMonitorPlugin : IAgentPlugin
        {
            private readonly string[] _keywords;
            private readonly string _pluginId;

            public KeywordMonitorPlugin(string[] keywords)
            {
                _keywords = keywords ?? throw new ArgumentNullException(nameof(keywords));
                _pluginId = $"KeywordMonitor_{string.Join("_", keywords)}";
            }

            public string Id => _pluginId;
            
            public IEnumerable<AgentTaskType> Supported => [AgentTaskType.PluginSpecific, AgentTaskType.StageMessage];

            public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
            {
                var tasks = new List<AgentTask>();
                
                // Check if last user message contains any of our keywords
                var lastMessage = ctx.LastUserMessage.ToLowerInvariant();
                var foundKeywords = _keywords.Where(k => lastMessage.Contains(k.ToLowerInvariant())).ToArray();
                
                if (foundKeywords.Length > 0 && ctx.IdleTime.TotalMinutes > 2)
                {
                    tasks.Add(new AgentTask
                    {
                        Type = AgentTaskType.PluginSpecific,
                        Priority = 2,
                        PayloadJson = JsonSerializer.Serialize(new { Keywords = foundKeywords }),
                        CorrelationKey = $"keyword-response-{string.Join("-", foundKeywords)}",
                        RequiresLLM = false
                    });
                }
                
                return Task.FromResult<IEnumerable<AgentTask>>(tasks);
            }

            public async Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
            {
                if (task.Type != AgentTaskType.PluginSpecific)
                    return AgentTaskResult.Fail();

                try
                {
                    var payload = JsonSerializer.Deserialize<JsonElement>(task.PayloadJson);
                    var keywords = payload.GetProperty("Keywords").EnumerateArray()
                        .Select(e => e.GetString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();

                    // Simulate processing time
                    await Task.Delay(500, ct);
                    
                    var staged = new StagedMessage
                    {
                        TopicKey = $"keyword-followup-{string.Join("-", keywords)}",
                        Draft = $"I noticed you mentioned: {string.Join(", ", keywords)}. I've made a note of this for our future conversations.",
                        Rationale = $"User mentioned keywords: {string.Join(", ", keywords)}",
                        ExpireUtc = DateTime.UtcNow.AddHours(4)
                    };
                    
                    return AgentTaskResult.Ok(staged: [staged]);
                }
                catch
                {
                    return AgentTaskResult.Fail();
                }
            }
        }

        /// <summary>
        /// Demonstrates how to integrate custom plugins with the Agent system
        /// </summary>
        public static void RunExample()
        {
            Console.WriteLine("=== Custom Agent Plugin Example ===");
            
            var runtime = AgentRuntime.Instance;
            
            // Example 1: Register a keyword monitoring plugin
            var keywordPlugin = new KeywordMonitorPlugin(new[] { "important", "urgent", "remember" });
            runtime.RegisterPlugin(keywordPlugin.Id, keywordPlugin);
            Console.WriteLine($"✓ Registered plugin: {keywordPlugin.Id}");
            
            // Example 2: Register using a factory function
            runtime.RegisterPlugin("SimpleNotificationPlugin", () => new SimpleNotificationPlugin());
            Console.WriteLine("✓ Registered SimpleNotificationPlugin via factory");
            
            // Example 3: Enable the plugins in configuration
            runtime.EnablePlugin(keywordPlugin.Id);
            runtime.EnablePlugin("SimpleNotificationPlugin");
            Console.WriteLine("✓ Enabled plugins in configuration");
            
            // Show registered plugins
            var registeredIds = runtime.GetRegisteredPluginIds();
            Console.WriteLine($"✓ Total registered plugins: {registeredIds.Count}");
            foreach (var id in registeredIds)
            {
                Console.WriteLine($"  - {id}");
            }
            
            Console.WriteLine("\nExample complete! The plugins are now ready to be used by the Agent system.");
            Console.WriteLine("Note: Call AgentRuntime.Instance.Start() to begin the agent loop.");
        }
    }

    /// <summary>
    /// Simple notification plugin for demonstration
    /// </summary>
    public class SimpleNotificationPlugin : IAgentPlugin
    {
        public string Id => "SimpleNotification";
        
        public IEnumerable<AgentTaskType> Supported => [AgentTaskType.StageMessage];

        public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
        {
            // Create a notification task every hour of idle time
            if (ctx.IdleTime.TotalHours >= 1 && ctx.IdleTime.TotalHours < 1.1)
            {
                return Task.FromResult<IEnumerable<AgentTask>>([
                    new AgentTask
                    {
                        Type = AgentTaskType.StageMessage,
                        Priority = 4,
                        CorrelationKey = "hourly-notification"
                    }
                ]);
            }
            
            return Task.FromResult<IEnumerable<AgentTask>>([]);
        }

        public Task<AgentTaskResult> ExecuteAsync(AgentTask task, IAgentContext ctx, CancellationToken ct)
        {
            var staged = new StagedMessage
            {
                TopicKey = "hourly-notification",
                Draft = "I'm still here and ready to help whenever you need me!",
                Rationale = "Hourly check-in notification",
                ExpireUtc = DateTime.UtcNow.AddMinutes(30)
            };

            return Task.FromResult(AgentTaskResult.Ok(staged: [staged]));
        }
    }
}