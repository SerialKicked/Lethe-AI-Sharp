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
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool DoEmbeds { get; set; } = true;
        public int ScanDepth { get; set; } = 1;
        public List<MemoryUnit> Entries { get; set; } = [];

        /// <summary>
        /// Check for entries from a string
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public List<MemoryUnit> FindEntries(string message)
        {
            var res = new List<MemoryUnit>();
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                if (!entry.Enabled)
                    continue;
                if (entry.Sticky || (entry.CheckKeywords(message) && entry.TriggerChance >= LLMEngine.RNG.NextDouble()))
                    res.Add(entry);
            }
            return res;
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
                    await item.BuildEmbedding().ConfigureAwait(false);
            }
        }
    }
}
