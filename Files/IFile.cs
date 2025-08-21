using Newtonsoft.Json;

namespace AIToolkit.Files
{
    public interface IFile
    {
        string UniqueName { get; set; }
        static JsonSerializerSettings JsonSettings => new() { Formatting = Formatting.Indented };

        string ExportToString() => JsonConvert.SerializeObject(this, JsonSettings);

        void SaveToFile(string pPath) => File.WriteAllText(pPath, ExportToString());

    }
}