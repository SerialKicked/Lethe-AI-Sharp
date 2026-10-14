using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using OpenAI;
using OpenAI.Chat;
using SharpToken;

namespace LetheAISharp
{
    public static class TokenTools
    {
        private static GptEncoding encoding = GptEncoding.GetEncoding("cl100k_base");
        public static GptEncoding Encoding { get => encoding; private set => encoding = value; }

        public static void SetEncoding(string encoding)
        {
            Encoding = GptEncoding.GetEncoding(encoding);
        }

        public static void SetEncodingForModel(string model)
        {

            string encodingName = Model.GetEncodingNameForModel(model);
            if (encodingName != null)
            {
                Encoding = GptEncoding.GetEncoding(encodingName);
            }
            else
            {
                Encoding = GptEncoding.GetEncoding("cl100k_base");
            }
        }

        /// <summary>
        /// Estimates the number of tokens in a string using a tokenizer.
        /// </summary>
        /// <param name="text">Text to count tokens for</param>
        /// <returns>Estimated token count</returns>
        public static int CountTokens(string text, InstructFormat? format = null)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            if (Encoding == null)
            {
                throw new InvalidOperationException("Encoding is not set.");
            }

            if (format == null)
            {
                return Encoding.CountTokens(text);
            }

            var specialchar = new HashSet<string>();
            if (!string.IsNullOrEmpty(format.ThinkingStart))
                specialchar.Add(format.ThinkingStart);
            if (!string.IsNullOrEmpty(format.ThinkingEnd))
                specialchar.Add(format.ThinkingEnd);
            if (!string.IsNullOrEmpty(format.BotStart))
                specialchar.Add(format.BotStart);
            if (!string.IsNullOrEmpty(format.BotEnd))
                specialchar.Add(format.BotEnd);
            if (!string.IsNullOrEmpty(format.UserStart))
                specialchar.Add(format.UserStart);
            if (!string.IsNullOrEmpty(format.UserEnd))
                specialchar.Add(format.UserEnd);
            if (!string.IsNullOrEmpty(format.BoSToken))
                specialchar.Add(format.BoSToken);

            return Encoding.CountTokens(text, specialchar);
        }

        /// <summary>
        /// Estimates the number of tokens in a string using a character-based approximation.
        /// </summary>
        /// <param name="text">Text to count tokens for</param>
        /// <returns>Estimated token count</returns>
        public static int CountTokensApprox(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            var consecutiveNewlinesCount = text.CountSubstring("\n\n");
            // Round up and add a small safety margin
            return (text.Length / 4) - consecutiveNewlinesCount;
        }

        public static List<Message> MaintainRoughTokenCount(IList<Message> messages, List<Message> toadd, InstructFormat? format = null)
        {
            var res = new List<Message>(messages);

            var totalTokens = CountTokens(messages, format);
            res.AddRange(toadd);
            while (CountTokens(res, format) > totalTokens && res.Count > 0)
            {
                if (res.Count <= 1)
                    break;
                res.RemoveAt(1);
            }
            return res;
        }

        /// <summary>
        /// Appends new tool-round messages (<paramref name="toadd"/>: the assistant tool-call message
        /// followed by its tool-result messages) to the running conversation and only evicts old messages
        /// when the conversation actually approaches the context budget.
        /// </summary>
        /// <remarks>
        /// Convenience wrapper over <see cref="TrimToolContext"/> that ignores the "fits" result and adds no
        /// fixed overhead. See that method for the eviction rules.
        /// </remarks>
        public static List<Message> TrimToolContextIfNeeded(IList<Message> messages, List<Message> toadd, InstructFormat? format, int maxTokens)
        {
            return TrimToolContext(messages, toadd, format, maxTokens, 0, out _);
        }

        /// <summary>
        /// Appends new tool-round messages to the running conversation (a tool-calling chain) and evicts old
        /// messages only when the conversation exceeds <paramref name="maxTokens"/>.
        /// </summary>
        /// <remarks>
        /// The conversation is allowed to grow freely until the fast local estimate exceeds the budget. Then,
        /// eviction happens in this order, always preserving the system prompt (index 0), the user message that
        /// started the current turn, and the newest tool round:
        /// <list type="number">
        /// <item>Older chat history (everything between the system prompt and the current user message), oldest first.</item>
        /// <item>Older rounds of the current chain, oldest first, as atomic units (the assistant tool-call message
        /// together with all of its tool results) so no orphaned tool message is ever left behind.</item>
        /// </list>
        /// Counting uses the cheap local estimate only (no HTTP round-trips), so <paramref name="maxTokens"/> should
        /// already include a safety margin. If the pinned messages alone are over budget, nothing more can be
        /// evicted and <paramref name="fits"/> is <see langword="false"/>, which the caller should treat as a cue
        /// to wrap up the chain.
        /// </remarks>
        /// <param name="messages">The conversation so far.</param>
        /// <param name="toadd">Messages of the round just completed (assistant tool-call message and its tool results). May be empty.</param>
        /// <param name="format">Instruct format used for the per-message overhead estimate.</param>
        /// <param name="maxTokens">Token budget for the messages (the caller subtracts the generation reservation and margin).</param>
        /// <param name="overheadTokens">Fixed cost counted against the budget but not part of the messages (e.g. tool definitions).</param>
        /// <param name="fits">Whether the returned conversation fits within the budget.</param>
        public static List<Message> TrimToolContext(IList<Message> messages, List<Message> toadd, InstructFormat? format, int maxTokens, int overheadTokens, out bool fits)
        {
            var res = new List<Message>(messages);
            res.AddRange(toadd);
            fits = true;

            if (maxTokens <= 0 || res.Count <= 1)
                return res;

            var costs = res.ConvertAll(m => CountMessageTokens(m, format));
            var total = costs.Sum() + overheadTokens;
            if (total <= maxTokens)
                return res;

            // Locate the anchor of the current turn: the last user message that existed before this round.
            var pin = -1;
            for (int i = messages.Count - 1; i >= 1; i--)
            {
                if (res[i].Role == OpenAI.Role.User)
                {
                    pin = i;
                    break;
                }
            }
            if (pin < 0)
            {
                // No user message: protect everything from the start of the trailing tool chain.
                var start = res.Count;
                while (start > 1 && IsToolChainMessage(res[start - 1]))
                    start--;
                pin = Math.Max(start - 1, 0);
            }

            void RemoveAtIndex(int index)
            {
                total -= costs[index];
                res.RemoveAt(index);
                costs.RemoveAt(index);
            }

            // Phase 1: scroll out older chat history (between the system prompt and the current turn's user message).
            while (total > maxTokens && pin > 1)
            {
                RemoveAtIndex(1);
                pin--;
            }
            // Never leave an orphaned tool-result message as the first post-system message.
            while (pin > 1 && res[1].Role == OpenAI.Role.Tool)
            {
                RemoveAtIndex(1);
                pin--;
            }

            // Phase 2: drop whole older rounds of the current chain (keep the newest round).
            while (total > maxTokens)
            {
                var first = -1;
                var last = -1;
                for (int i = pin + 1; i < res.Count; i++)
                {
                    if (IsToolRoundStart(res[i]))
                    {
                        if (first < 0)
                            first = i;
                        last = i;
                    }
                }
                if (first < 0 || first == last)
                    break;
                RemoveAtIndex(first);
                while (first < res.Count && res[first].Role == OpenAI.Role.Tool)
                    RemoveAtIndex(first);
            }

            fits = total <= maxTokens;
            return res;
        }

        private static bool IsToolRoundStart(Message m) => m.Role == OpenAI.Role.Assistant && m.ToolCalls is { Count: > 0 };

        private static bool IsToolChainMessage(Message m) => m.Role == OpenAI.Role.Tool || IsToolRoundStart(m);

        /// <summary>
        /// Counts the tokens of a set of chat messages with a fast local estimate (about 4 characters per token,
        /// plus per-message overhead). Includes text parts, reasoning content, tool-call names/arguments and images.
        /// </summary>
        public static int CountTokens(IList<Message> messages, InstructFormat? format = null)
        {
            var total = 0;
            foreach (var message in messages)
                total += CountMessageTokens(message, format);
            return total;
        }

        private static int CountMessageTokens(Message message, InstructFormat? format)
        {
            var charcnt = 0;
            var imgcnt = 0;

            if (format != null)
            {
                switch (message.Role)
                {
                    case OpenAI.Role.System:
                    case OpenAI.Role.Developer:
                        charcnt += CountTokens(format.SystemStart) + CountTokens(format.SystemEnd);
                        break;
                    case OpenAI.Role.Tool:
                    case OpenAI.Role.Assistant:
                        charcnt += CountTokens(format.BotStart) + CountTokens(format.BotEnd);
                        break;
                    case OpenAI.Role.User:
                        charcnt += CountTokens(format.UserStart) + CountTokens(format.UserEnd);
                        break;
                    default:
                        charcnt += CountTokens(format.SystemStart) + CountTokens(format.SystemEnd);
                        break;
                }
                if (format.NewLinesBetweenMessages)
                {
                    charcnt += 2; // For the newlines
                }
            }

            object? content = message.Content;
            if (content is string strContent)
            {
                charcnt += strContent.Length;
            }
            else if (content is IEnumerable<Content> parts)
            {
                foreach (var part in parts)
                {
                    switch (part.Type)
                    {
                        case ContentType.Text:
                            charcnt += part.Text switch
                            {
                                string s => s.Length,
                                TextContent tc => tc.Value?.Length ?? 0,
                                _ => 0
                            };
                            break;
                        case ContentType.ImageUrl:
                        case ContentType.ImageFile:
                            imgcnt++;
                            break;
                    }
                }
            }
            else if (content is IEnumerable<object> items)
            {
                foreach (var item in items)
                {
                    if (item is string strItem)
                        charcnt += strItem.Length;
                    else if (item is ImageUrl || item is ImageFile)
                        imgcnt++;
                }
            }

            if (!string.IsNullOrEmpty(message.ReasoningContent))
                charcnt += message.ReasoningContent.Length;

            if (message.ToolCalls is { Count: > 0 })
            {
                foreach (var call in message.ToolCalls)
                {
                    charcnt += (call.Function?.Name?.Length ?? 0) + 16; // JSON scaffolding around the call
                    var args = call.Function?.Arguments;
                    if (args != null)
                    {
                        charcnt += args is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)
                            ? text.Length
                            : args.ToJsonString().Length;
                    }
                }
            }

            return (charcnt / 4) + (imgcnt * LLMEngine.Settings.ImageEmbeddingSize) + 10; // Approximate token count with a safety margin
        }

        internal static OpenAI.Role InternalRoleToChatRole(AuthorRole role)
        {
            return role switch
            {
                AuthorRole.User => OpenAI.Role.User,
                AuthorRole.Assistant => OpenAI.Role.Assistant,
                AuthorRole.System => OpenAI.Role.System,
                AuthorRole.SysPrompt => OpenAI.Role.System,
                AuthorRole.Tool => OpenAI.Role.Tool,
                _ => OpenAI.Role.User
            };
        }

        internal static AuthorRole ChatRoleToInternalRole(OpenAI.Role role)
        {
            return role switch
            {
                OpenAI.Role.User => AuthorRole.User,
                OpenAI.Role.Assistant => AuthorRole.Assistant,
                OpenAI.Role.Developer => AuthorRole.System,
                OpenAI.Role.System => AuthorRole.System,
                OpenAI.Role.Tool => AuthorRole.Tool,
                _ => AuthorRole.User
            };
        }

    }
}
