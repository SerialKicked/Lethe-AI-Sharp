using Newtonsoft.Json;
using System.IO;

namespace LetheAISharp.Files
{
    public interface IFile
    {
        string UniqueName { get; set; }
        static JsonSerializerSettings JsonSettings => new() { Formatting = Formatting.Indented };

        string ExportToString() => JsonConvert.SerializeObject(this, JsonSettings);

        void SaveToFile(string pPath)
        {
            var dir = Path.GetDirectoryName(pPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(pPath, ExportToString());
        }

    }
}