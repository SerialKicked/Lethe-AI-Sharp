using AIToolkit.Agent;
using System.Text.Json;

namespace AIToolkit.Agent.Tests
{
    /// <summary>
    /// Example custom plugin for testing the extensible Agent system
    /// </summary>
    public class TestCustomPlugin : IAgentPlugin
    {
        public string Id => "TestCustomPlugin";
        
        public IEnumerable<AgentTaskType> Supported => [AgentTaskType.PluginSpecific];

        public Task<IEnumerable<AgentTask>> ObserveAsync(IAgentContext ctx, CancellationToken ct)
        {
            var tasks = new List<AgentTask>();
            
            // Create a test task when idle for more than 1 minute
            if (ctx.IdleTime.TotalMinutes > 1)
            {
                tasks.Add(new AgentTask
                {
                    Type = AgentTaskType.PluginSpecific,
                    Priority = 3,
                    PayloadJson = JsonSerializer.Serialize(new { TestData = "Custom plugin working!" }),
                    CorrelationKey = "test-custom-plugin",
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
                // Simulate some work
                await Task.Delay(100, ct);
                
                // Stage a message to show the plugin worked
                var staged = new StagedMessage
                {
                    TopicKey = "test-custom-plugin",
                    Draft = "(Test) Custom plugin executed successfully!",
                    Rationale = "Demonstrating external plugin functionality",
                    ExpireUtc = DateTime.UtcNow.AddMinutes(30)
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
    /// Test class to demonstrate the new plugin registration functionality
    /// </summary>
    public static class AgentExtensibilityTest
    {
        /// <summary>
        /// Test registering a custom plugin and verifying it's loaded
        /// </summary>
        public static void TestPluginRegistration()
        {
            var runtime = AgentRuntime.Instance;
            
            // Test 1: Register plugin instance
            var customPlugin = new TestCustomPlugin();
            runtime.RegisterPlugin("TestCustomPlugin", customPlugin);
            
            // Verify plugin is registered
            var registeredIds = runtime.GetRegisteredPluginIds();
            if (!registeredIds.Contains("TestCustomPlugin"))
                throw new Exception("Plugin registration failed");
            
            // Test 2: Register plugin factory
            runtime.RegisterPlugin("TestCustomPluginFactory", () => new TestCustomPlugin());
            
            // Verify factory plugin is registered
            registeredIds = runtime.GetRegisteredPluginIds();
            if (!registeredIds.Contains("TestCustomPluginFactory"))
                throw new Exception("Plugin factory registration failed");
            
            // Test 3: Unregister plugin
            runtime.UnregisterPlugin("TestCustomPluginFactory");
            registeredIds = runtime.GetRegisteredPluginIds();
            if (registeredIds.Contains("TestCustomPluginFactory"))
                throw new Exception("Plugin unregistration failed");
            
            Console.WriteLine("✓ Plugin registration system working correctly!");
        }
        
        /// <summary>
        /// Example of how an application would integrate a custom plugin
        /// </summary>
        public static void ExampleIntegration()
        {
            var runtime = AgentRuntime.Instance;
            
            // Step 1: Register custom plugin
            runtime.RegisterPlugin("TestCustomPlugin", () => new TestCustomPlugin());
            
            // Step 2: Note - you would also need to add "TestCustomPlugin" to your AgentConfig.Plugins array
            // This can be done programmatically or via configuration file
            
            // Step 3: Start the agent (plugin will be loaded automatically)
            // runtime.Start(); // Commented out for testing
            
            Console.WriteLine("✓ Example integration complete!");
            Console.WriteLine("Note: Add 'TestCustomPlugin' to AgentConfig.Plugins array to enable it");
        }
    }
}