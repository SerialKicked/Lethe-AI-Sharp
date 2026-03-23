using LetheAISharp.Agent.Tools;

namespace LetheAISharp.Agent
{
    /// <summary>
    /// Optional entry point interface for plugin DLLs.
    /// If a class in a loaded assembly implements this, it is invoked during
    /// <see cref="AgentRuntime.RegisterDll"/> and <see cref="ToolManager.RegisterDll"/> 
    /// to allow the plugin to register itself with full control before automatic discovery runs.
    /// </summary>
    public interface IPluginEntry
    {
        /// <summary>
        /// Called after the assembly is loaded and before automatic type discovery runs. 
        /// Implement this to register tasks, actions, or other resources with full control over the registration logic.
        /// </summary>
        void Register();
    }

    /// <summary>
    /// Optional entry point interface for plugin DLLs.
    /// If a class in a loaded assembly implements this, it is invoked during <see cref="ToolManager.RegisterDll"/> 
    /// to allow the plugin to register itself with full control before automatic discovery runs.
    /// </summary>
    public interface IToolPluginEntry
    {
        /// <summary>
        /// Called after the assembly is loaded and before automatic type discovery runs. 
        /// Implement this to register tasks, actions, or other resources with full control over the registration logic.
        /// </summary>
        void Register();
    }

}
