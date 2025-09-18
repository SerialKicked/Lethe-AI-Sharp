using LetheAISharp.LLM;
using LetheAISharp.Files;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LetheAISharp.Memory
{
    public enum WEPosition { SystemPrompt, Chat }
    public enum KeyWordLink
    {
        /// <summary> Triggers when there's at least one keyword from both Main and Secondary </summary>
        And,
        /// <summary> Triggers when there's a keyword from Main or Secondary </summary>
        Or,
        /// <summary> Triggers when there's a keyword from Main but not from Secondary </summary>
        Not
    }

    public class WorldInfo : BaseFile
    {
        private class ActiveLink
        {
            public int RecordID = 0;
            public int DurationLeft = 0;
        }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool DoEmbeds { get; set; } = true;
        public int ScanDepth { get; set; } = 1;
        public List<MemoryUnit> Entries { get; set; } = [];
        private readonly List<ActiveLink> activeEntries = [];

        /// <summary>
        /// Check for entries from a string
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public List<MemoryUnit> FindEntries(string message)
        {
            foreach (var entry in activeEntries)
                entry.DurationLeft--;
            activeEntries.RemoveAll(a => a.DurationLeft <= 0);
            var active = activeEntries.Where(a => a.DurationLeft > 0).ToList();
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (!entry.Enabled || active.Any(a => a.RecordID == i))
                    continue;
                if (entry.CheckKeywords(message) && entry.TriggerChance >= LLMEngine.RNG.NextDouble())
                    activeEntries.Add(new ActiveLink { RecordID = i, DurationLeft = entry.Duration });
            }
            return [.. activeEntries.Select(a => Entries[a.RecordID])];
        }

        /// <summary>
        /// Check the ScanDepth last messages for any active entries
        /// </summary>
        /// <param name="log"></param>
        /// <returns></returns>
        public List<MemoryUnit> FindEntries(Chatlog log, string? userinput = null)
        {
            if (log.CurrentSession.Messages.Count == 0)
                return [];
            // retrieve the last User and Bot messages from the chatlog
            var messages = new List<SingleMessage>();
            var min = log.CurrentSession.Messages.Count - ScanDepth;
            if (min < 0)
                min = 0;
            for (int i = log.CurrentSession.Messages.Count - 1; i >= min; i--)
            {
                var mess =log.CurrentSession.Messages[i];
                if (mess.Role == AuthorRole.User || mess.Role == AuthorRole.Assistant)
                {
                    messages.Add(mess);
                }
            }
            if (messages.Count == 0)
                return [];
            var stbuilder = new StringBuilder();
            foreach (var item in messages)
                stbuilder.AppendLinuxLine(item.Message);
            if (userinput != null)
                stbuilder.AppendLinuxLine(userinput);
            return FindEntries(stbuilder.ToString());
        }

        public void Reset()
        {
            activeEntries.Clear();
        }

        public async Task EmbedText()
        {
            if (!DoEmbeds)
            {
                foreach (var item in Entries)
                    item.EmbedSummary = [];
            }
            else
            {
                foreach (var item in Entries)
                    await item.EmbedText().ConfigureAwait(false);
            }
        }
    }
}
