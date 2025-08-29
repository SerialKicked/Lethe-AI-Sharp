using AIToolkit.Files;
using AIToolkit.Memory;
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
            foreach (var (session, _, _) in memories)
            {
                if (session is WorldEntry entry)
                {
                    AddInsert(new PromptInsert(entry.Guid, entry.Content, entry.Position == WEPosition.SystemPrompt ? -1 : entry.PositionIndex, entry.Duration));
                }
                else if (session is ChatSession info)
                {
                    AddInsert(new PromptInsert(session.Guid, info.GetRawMemory(true, LLMSystem.Bot.DatesInSessionSummaries), LLMSystem.Settings.RAGIndex, 1));
                }
                else if (session is MemoryUnit unit)
                {
                    AddInsert(new PromptInsert(session.Guid, unit.Content, LLMSystem.Settings.RAGIndex, 1));
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
