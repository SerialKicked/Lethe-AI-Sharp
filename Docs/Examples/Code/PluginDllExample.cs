// =============================================================================
// PluginDllExample.cs
//
// This file shows the complete pattern for building a plugin DLL for LetheAI Sharp.
// It is intended as a reference / starting point — copy it into a dedicated
// Class Library project that references LetheAISharp, build the DLL, and drop
// it into your host app's Plugins/ folder.
//
// Host-app registration (one line at startup):
//   LLMEngine.RegisterPluginsFromDirectory(
//       Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"));
// =============================================================================

using LetheAISharp.Agent;
using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LetheAISharp.Examples
{
    // -------------------------------------------------------------------------
    // 1. (Optional) Plugin entry point
    //    Implement IPluginEntry if you need custom registration logic — for
    //    example, registering actions with constructor parameters, or setting
    //    up shared state before the auto-discovery pass runs.
    //    If you don't need it, skip this class entirely; auto-discovery will
    //    still find your IAgentTask / IAgentAction<,> implementations.
    // -------------------------------------------------------------------------
    public sealed class ExamplePluginEntry : IPluginEntry
    {
        public void Register()
        {
            // Any manual registration can go here.
            // Auto-discovery runs after this method returns, so you only need
            // to handle cases that require special construction.
            LLMEngine.Logger?.LogInformation("ExamplePlugin: entry point invoked.");
        }
    }

    // -------------------------------------------------------------------------
    // 2. A simple IAgentTask implementation
    //    The agent loop calls Observe() on every tick to decide whether to run,
    //    then Execute() if Observe() returned true.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Example background task that greets the persona every 30 minutes.
    /// Replace the Execute() body with your own logic.
    /// </summary>
    public sealed class GreetingTask : IAgentTask
    {
        // Must be unique across all registered tasks.
        public string Id => "GreetingTask";

        // Short human-readable description shown in ability lists.
        public string Ability => "Send periodic greetings";

        /// <summary>
        /// Called on every agent tick. Return true to trigger Execute().
        /// </summary>
        public async Task<bool> Observe(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            await Task.Delay(10, ct).ConfigureAwait(false);

            // Only run when the engine is idle
            if (LLMEngine.Status != SystemStatus.Ready)
                return false;

            // Run at most once every 30 minutes
            var lastRun = cfg.GetSetting<DateTime>("LastRun");
            return DateTime.Now - lastRun >= TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Contains the actual task logic. Called only when Observe() returns true.
        /// </summary>
        public async Task Execute(BasePersona owner, AgentTaskSetting cfg, CancellationToken ct)
        {
            await Task.Delay(10, ct).ConfigureAwait(false);

            // Insert a message into the persona's next prompt
            owner.Brain.AddUserReturnInsert(
                $"[Plugin] It's {DateTime.Now:HH:mm} — consider greeting the user warmly.",
                Id);

            // Persist the last-run timestamp so Observe() knows when to fire again
            cfg.SetSetting("LastRun", DateTime.Now);

            LLMEngine.Logger?.LogInformation("GreetingTask executed for {persona}.", owner.Name);
        }

        /// <summary>
        /// Returns the default settings object used the first time this task
        /// is loaded for a persona that has no saved config yet.
        /// </summary>
        public AgentTaskSetting GetDefaultSettings()
        {
            var settings = new AgentTaskSetting();
            settings.SetSetting("LastRun", DateTime.MinValue);
            return settings;
        }
    }

    // -------------------------------------------------------------------------
    // 3. A simple IAgentAction<TResult, TParam> implementation
    //    Actions are shared utilities called by tasks (or other code) via
    //    AgentRuntime.GetAction<TResult, TParam>(id).
    // -------------------------------------------------------------------------

    /// <summary>Parameter bag for <see cref="TimeOfDayAction"/>.</summary>
    public sealed class TimeOfDayParam
    {
        public string TimeZoneId { get; set; } = "UTC";
    }

    /// <summary>
    /// Example action that returns a human-readable time-of-day string.
    /// Retrieve it from tasks via:
    ///   var action = AgentRuntime.GetAction&lt;string, TimeOfDayParam&gt;("TimeOfDayAction");
    /// </summary>
    public sealed class TimeOfDayAction : IAgentAction<string, TimeOfDayParam>
    {
        // Must be unique across all registered actions.
        public string Id => "TimeOfDayAction";

        // Declare which host capabilities this action requires.
        public HashSet<AgentActionRequirements> Requirements => [];

        public async Task<string> Execute(TimeOfDayParam param, CancellationToken ct)
        {
            await Task.Delay(1, ct).ConfigureAwait(false);

            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(param.TimeZoneId);
                var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                return local.ToString("HH:mm");
            }
            catch
            {
                return DateTime.UtcNow.ToString("HH:mm") + " UTC";
            }
        }
    }

    // -------------------------------------------------------------------------
    // 4. Host-app usage (put this in your Program.cs / startup code)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Demonstrates how the host app loads the plugin folder.
    /// The two lines below are all you need — no per-plugin wiring required.
    /// </summary>
    public static class PluginDllExampleUsage
    {
        public static void RegisterPlugins()
        {
            // Load every *.dll in the Plugins/ subfolder next to the executable.
            // Safe to call even if the folder doesn't exist yet.
            var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
            LLMEngine.RegisterPluginsFromDirectory(pluginsDir);

            // Or load a single, known DLL directly:
            // LLMEngine.RegisterPlugin("/absolute/path/to/MyPlugin.dll");

            // Legacy paths still work but are deprecated:
            // AgentRuntime.RegisterPluginsFromDirectory(pluginsDir);
            // LLMEngine.ToolManager.RegisterPluginsFromDirectory(pluginsDir);
        }
    }
}
