using Newtonsoft.Json;

namespace LetheAISharp.LLM
{
    public interface IEmbed
    {
        Guid Guid { get; set; }
        float[] EmbedSummary { get; set; }

        /// <summary>
        /// Generates the embedding for this object and stores it in the EmbedSummary property. 
        /// This is used for vector search and retrieval of relevant information based on semantic similarity.
        /// You should call this method after creating or updating the object to ensure the embedding is up to date with the current content of the object.
        /// </summary>
        /// <returns></returns>
        Task BuildEmbedding();
    }
}