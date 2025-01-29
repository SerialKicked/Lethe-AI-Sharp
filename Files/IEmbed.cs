using AIToolkit.LLM;
using Newtonsoft.Json;

namespace AIToolkit.Files
{
    public interface IEmbed
    {
        [JsonIgnore] Guid Guid { get; set; }
        float[] EmbedSummary { get; set; }
    }
}