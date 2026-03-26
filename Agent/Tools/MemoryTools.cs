using LetheAISharp.Agent.Actions;
using LetheAISharp.GBNF;
using LetheAISharp.LLM;
using LetheAISharp.Memory;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.Agent.Tools
{
    /// <summary>
    /// Basic toolset for demonstration purposes. 
    /// This toolset includes simple tools like performing a web search, and getting the current date and time.
    /// </summary>
    public class MemoryTools : IToolList
    {
        public string Id => "Memory Tools";
        private List<Tool> toolList = [];

        public IReadOnlyList<Tool> GetToolList() => toolList;

        public void LoadTools(bool clearExisting = false)
        {
            toolList.Clear();
            if (clearExisting)
            {
                Tool.ClearRegisteredTools();
            }
            toolList.Add(Tool.GetOrCreateTool(this, nameof(SaveMemory), "Commit information to your long term memory. Provide a short title, and the content of the memory you want to save (which can be of any length). You can use this tool automatically."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(MemorySearch), "Search your long term memory for relevant information. Provide the text to look for. Keep it short and direct (ex: a keyword, title, or short sentence like). You can use this tool automatically without user input."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(GetMemoryByDate), "Search your long term memory for relevant information. Provide the year, month, and day (as numbers). You can search for any day in a month by setting day to 0. You can use this tool automatically without user input."));
            toolList.Add(Tool.GetOrCreateTool(this, nameof(SetReminder), "Set a reminder for a specific date. Provide a title for the reminder, the message you want to be reminded of, and the date of the reminder."));
        }

        public void UnloadTools()
        {
            foreach (var tool in toolList)
            {
                Tool.TryUnregisterTool(tool);
            }
            toolList.Clear();
        }

        public async Task<string> MemorySearch(string query)
        {
            var datafound = new PromptInserts();
            await LLMEngine.Bot.Brain.GetRAGandInserts(datafound, query, 8, 0.525f).ConfigureAwait(false);

            var foundMemories = new HashSet<MemoryUnit>();
            foreach (var insert in datafound)
            {
                // if query is a single word, only add memories that contains it in the content, otherwise add all
                if (query.Trim().Contains(' ') || insert.Memory.Content.Contains(query, StringComparison.OrdinalIgnoreCase) || insert.Memory.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    foundMemories.Add(insert.Memory);
            }

            var lst = LLMEngine.Bot.Brain.SearchMemories(query);
            // add 5 first items from lst that are not already in foundMemories
            int addedCount = 0;
            foreach (var memory in lst)
            {
                if (!foundMemories.Contains(memory))
                {
                    foundMemories.Add(memory);
                    addedCount++;
                    if (addedCount >= 5)
                        break;
                }
            }

            // if list has more than 5 items, keep the 8 most recent ones
            if (foundMemories.Count > 5)
            {
                foundMemories = [.. foundMemories.OrderByDescending(m => m.Added).Take(8)];
            }

            if (foundMemories.Count == 0)
                return $"No relevant memories found for '{query}'.";

            var sb = new StringBuilder();
            sb.AppendLinuxLine($"Here are some relevant memories for '{query}':").AppendLinuxLine();

            foreach (var res in foundMemories)
            {
                sb.AppendLinuxLine(res.ToSnippet(TitleInsertType.Bold, true, false, true)).AppendLinuxLine();
            }
            return sb.ToString();
        }

        public async Task<string> GetMemoryByDate(int year, int month, int day)
        {
            await Task.Delay(5).ConfigureAwait(false);

            var res = LLMEngine.Bot.Brain.Memories.Where(m =>
                m.Insertion == MemoryInsertion.Trigger &&
                m.Added.Year == year &&
                m.Added.Month == month &&
                (day == 0 || m.Added.Day == day));

            // also get conversation sessions where the date provide is between StartTime and the EndTime of the session
            var sessions = LLMEngine.Bot.History.Sessions.Where(m =>
            {
                if (day == 0)
                {
                    var monthStart = new DateTime(year, month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
                    return m.StartTime <= monthEnd && m.EndTime >= monthStart;
                }
                var targetDate = new DateTime(year, month, day);
                return m.StartTime.Date <= targetDate && m.EndTime.Date >= targetDate;
            });

            if (!res.Any() && !sessions.Any())
                return $"No memories found for {year}/{month}/{day}.";

            var sb = new StringBuilder();
            if (sessions.Any())
            {
                sb.AppendLinuxLine("Here are some relevant conversation sessions:").AppendLinuxLine();
                foreach (var session in sessions)
                {
                    sb.AppendLinuxLine(session.ToSnippet(TitleInsertType.Bold, true, false, true)).AppendLinuxLine();
                }
                sb.AppendLinuxLine();
            }
            if (res.Any())
            {
                sb.AppendLinuxLine("Here are some relevant memories:").AppendLinuxLine();
                foreach (var memory in res)
                {
                    sb.AppendLinuxLine(memory.ToSnippet(TitleInsertType.Bold, true, false, true)).AppendLinuxLine();
                }
            }
            return sb.ToString();
        }

        public async Task<string> SaveMemory(string MemoryTitle, string MemoryContent)
        {
            var mem = new MemoryUnit()
            {
                Name = MemoryTitle,
                Content = MemoryContent,
                Category = MemoryType.General,
                Insertion = MemoryInsertion.Trigger
            };
            await mem.EmbedText().ConfigureAwait(false);
            LLMEngine.Bot.Brain.Memorize(mem);
            return $"Memory '{MemoryTitle}' saved successfully.";
        }

        public async Task<string> SetReminder(string ReminderTitle, string Message, DateTime date)
        {
            var mem = new MemoryUnit()
            {
                Name = ReminderTitle,
                Content = Message,
                Category = MemoryType.General,
                Insertion = MemoryInsertion.UserReturn,
                Added = date,
                EndTime = date + new TimeSpan(1,0,0,0)
            };
            await mem.EmbedText().ConfigureAwait(false);
            LLMEngine.Bot.Brain.Memorize(mem);
            return $"Reminder '{ReminderTitle}' set for {date.ToHumanString()} successfully.";
        }

        public bool RequiresConfirmation(string functionName)
        {
            return false;
        }
    }
}
