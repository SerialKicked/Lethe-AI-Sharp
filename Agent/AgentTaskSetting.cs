using LetheAISharp.Files;
using Newtonsoft.Json.Linq;

namespace LetheAISharp.Agent
{
    public class AgentTaskSetting
    {
        public string PluginId { get; set; } = string.Empty;
        public Dictionary<string, JToken> Settings { get; set; } = [];

        public T? GetSetting<T>(string key, T? defaultValue = default)
        {
            if (!Settings.TryGetValue(key, out var token) || token is null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                var result = token.ToObject<T>();
                return result is null ? defaultValue : result;
            }
            catch
            {
                return defaultValue;
            }
        }

        public void SetSetting<T>(string key, T value)
        {
            Settings[key] = value is null ? JValue.CreateNull() : JToken.FromObject(value!);
        }
    }
}
