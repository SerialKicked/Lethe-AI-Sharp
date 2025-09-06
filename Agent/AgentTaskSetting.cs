using Newtonsoft.Json.Linq;

namespace AIToolkit.Agent
{
    public class AgentTaskSetting
    {
        public string PluginId { get; set; } = string.Empty;
        public Dictionary<string, JToken> Settings { get; set; } = [];

        public T? GetSetting<T>(string key, T? defaultValue = default)
        {
            return Settings.TryGetValue(key, out var token) ? token.Value<T>() : defaultValue;
        }

        public void SetSetting<T>(string key, T value)
        {
            Settings[key] = value is null ? JToken.FromObject(string.Empty) : JToken.FromObject(value);
        }
    }
}
