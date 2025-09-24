using LetheAISharp.Files;
using LetheAISharp.LLM;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace LetheAISharp.Memory
{
    public class PromptInsert
    {
        public Guid guid = Guid.NewGuid();
        public string Content = string.Empty;
        public int Location = 0;
        public int Duration = 0;

        public MemoryUnit? Memory => LLMEngine.Bot.Brain.GetMemoryByID(guid);

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
                LLMEngine.Bot.Brain.GetMemoryByID(data.guid)?.Touch();
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
                res.AppendLinuxLine(item.Content);
            return LLMEngine.Bot.ReplaceMacros(res.ToString());
        }

        public void AddMemories(List<(IEmbed session, EmbedType category, float distance)> memories)
        {
            if (memories.Count == 0)
                return;
            foreach (var (session, _, _) in memories)
            {
                if (session is ChatSession info)
                {
                    AddInsert(
                        new PromptInsert(
                            session.Guid, 
                            info.ToSnippet(TitleInsertType.Simple, LLMEngine.Bot.DatesInSessionSummaries, false, true), 
                            LLMEngine.Settings.RAGIndex, 
                            1)
                        );
                }
                else if (session is MemoryUnit entry)
                {
                    if (entry.Category == MemoryType.WorldInfo)
                        AddInsert(new PromptInsert(entry.Guid, entry.Content, entry.PositionIndex, entry.Duration));
                    else
                        AddInsert(new PromptInsert(session.Guid, entry.Content, LLMEngine.Settings.RAGIndex, 1));
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

        public bool Contains(Guid guid) => FindIndex(i => i.guid == guid) != -1;
    }
}
