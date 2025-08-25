using AIToolkit.Files;
using Newtonsoft.Json;

namespace AIToolkit.Agent
{
    public sealed class AgentConfig : BaseFile
    {
        public bool Enabled { get; set; } = true;
        public int LoopIntervalMs { get; set; } = 1500;
        public int DailyTokenBudget { get; set; } = 8000;
        public int DailySearchBudget { get; set; } = 25;
        public int MinIdleMinutesBeforeBackgroundWork { get; set; } = 3;
        public int SearchCooldownMinutesPerTopic { get; set; } = 120;
        public int StageMessageTTLMinutes { get; set; } = 360;
        public string[] Plugins { get; set; } = [ "CoreReflection", "WebIntelligence", "PersonaMaintenance"];

        public static AgentConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                var def = new AgentConfig();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(def, Formatting.Indented));
                return def;
            }
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<AgentConfig>(json) ?? new AgentConfig();
        }
    }
}