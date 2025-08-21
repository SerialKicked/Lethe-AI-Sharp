using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIToolkit.Agent
{
    internal static class ResearchStore
    {
        private static readonly string Root = Path.Combine("data", "agent", "research");

        public static bool HasSession(Guid sessionId)
            => File.Exists(FileFor(sessionId));

        public static ResearchDoc Load(Guid sessionId)
        {
            var path = FileFor(sessionId);
            if (!File.Exists(path)) return new ResearchDoc { SessionId = sessionId };
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ResearchDoc>(json, Options()) ?? new ResearchDoc { SessionId = sessionId };
        }

        public static void Save(ResearchDoc doc)
        {
            Directory.CreateDirectory(Root);
            var json = JsonSerializer.Serialize(doc, Options());
            File.WriteAllText(FileFor(doc.SessionId), json);
        }

        public static void AppendResults(Guid sessionId, string topic, string query, IEnumerable<SearchItem> items)
        {
            var doc = Load(sessionId);
            var t = doc.Topics.FirstOrDefault(x => x.Topic == topic);
            if (t == null)
            {
                t = new TopicResearch { Topic = topic };
                doc.Topics.Add(t);
            }
            t.Queries.Add(new QueryResearch
            {
                Query = query,
                Results = items.Take(8).ToList()
            });
            Save(doc);
        }

        private static string FileFor(Guid sessionId) => Path.Combine(Root, sessionId + ".json");

        private static JsonSerializerOptions Options() => new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    internal sealed class ResearchDoc
    {
        public Guid SessionId { get; set; }
        public List<TopicResearch> Topics { get; set; } = [];
    }

    internal sealed class TopicResearch
    {
        public string Topic { get; set; } = string.Empty;
        public List<QueryResearch> Queries { get; set; } = [];
    }

    internal sealed class QueryResearch
    {
        public string Query { get; set; } = string.Empty;
        public List<SearchItem> Results { get; set; } = [];
    }

    public sealed class SearchItem
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
    }
}