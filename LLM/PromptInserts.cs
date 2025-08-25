using AIToolkit.Files;
using System.Text;

namespace AIToolkit.LLM
{
    public class PromptInsert
    {
        public Guid guid = Guid.NewGuid();
        public string Content = string.Empty;
        public int Location = 0;
        public int Duration = 0;

        public PromptInsert(Guid? newguid, string content, int location, int duration)
        {
            if (newguid != null)
                guid = (Guid)newguid;
            Content = content;
            Location = location;
            Duration = duration;
        }
    }
    public class PromptInserts : List<PromptInsert> 
    { 
        public void DecreaseDuration()
        {
            foreach (var item in this)
                item.Duration--;
            RemoveAll(i => i.Duration <= 0);
        }

        public void AddInsert(PromptInsert data)
        {
            // Check if same guid exists, if so, replace the content
            var index = FindIndex(i => i.guid == data.guid);
            if (index >= 0)
            {
                this[index] = data;
            }
            else
            {
                Add(data);
            }
        }

        public List<PromptInsert> GetEntriesByPosition(int position)
        {
            return FindAll(i => i.Location == position);
        }

        public string GetContentByPosition(int position)
        {
            var res = new StringBuilder();
            foreach (var item in GetEntriesByPosition(position))
                res.AppendLinuxLine(LLMSystem.ReplaceMacros(item.Content));
            return res.ToString();
        }

        public void AddMemories(List<(IEmbed session, EmbedType category, float distance)> memories)
        {
            if (memories.Count == 0)
                return;
            foreach (var (session, embedtype, _) in memories)
            {
                if (embedtype == EmbedType.WorldInfo)
                {
                    var info = (session as WorldEntry)!;
                    AddInsert(new PromptInsert(info.Guid, info.Message, info.Position == WEPosition.SystemPrompt ? -1 : info.PositionIndex, info.Duration));
                }
                else
                {
                    var info = (session as ChatSession)!;
                    AddInsert(new PromptInsert(session.Guid, info.GetRawMemory(!LLMSystem.Settings.MarkdownMemoryFormating), LLMSystem.Settings.RAGIndex, 1));
                }
            }
        }

        public HashSet<Guid> GetGuids()
        {
            // Retrieve all guids from the content
            var res = new HashSet<Guid>();
            foreach (var item in this)
                res.Add(item.guid);
            return res;
        }
    }
}
