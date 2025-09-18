using LetheAISharp.LLM;
using Newtonsoft.Json;

namespace LetheAISharp.Files
{
    public interface IEmbed
    {
        Guid Guid { get; set; }
        float[] EmbedSummary { get; set; }

        Task EmbedText();
    }
}