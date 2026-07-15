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
        /// Unlike <see cref="MaintainRoughTokenCount"/> (which pins the budget to the pre-round size and
        /// therefore evicts on <em>every</em> tool round regardless of context usage), this method lets the
        /// conversation grow freely until the fast char/4 estimate exceeds <paramref name="maxTokens"/>.
        /// Only then does it delete the oldest messages (from index 1, preserving the system prompt at
        /// index 0). This matches the intended behaviour: keep everything in a fresh/low-context chat, and
        /// fall back to dropping old messages near the real context limit.
        ///
        /// Counting uses the cheap local estimate only (no HTTP round-trips), so <paramref name="maxTokens"/>
        /// should already include a generous safety margin to absorb the estimate's vagueness.
        ///
        /// After trimming, any orphaned leading tool-result message (a <see cref="OpenAI.Role.Tool"/> message
        /// at index 1 whose originating assistant tool-call message was removed) is also dropped, so backends
        /// whose chat templates reject a leading tool message do not error out.
        /// </remarks>
        public static List<Message> TrimToolContextIfNeeded(IList<Message> messages, List<Message> toadd, InstructFormat? format, int maxTokens)
        {
            var res = new List<Message>(messages);
            res.AddRange(toadd);

            // Fresh / below-budget case: let the conversation grow, evict nothing.
            if (maxTokens <= 0 || CountTokens(res, format) <= maxTokens)
                return res;

            // Near the context limit: drop the oldest messages (index 1 onward, preserving the system
            // prompt at index 0) until we fit. The newest messages -- the tool round we are about to
            // re-send -- are always kept, matching the priority used by ChatPromptBuilder.TrimToFit.
            // As with that method, if the retained tail (system prompt + newest round) is itself larger
            // than maxTokens we cannot trim further and return it as-is; the margin baked into maxTokens
            // by the caller is what protects against the estimate under-counting the real prompt.
            while (res.Count > 1 && CountTokens(res, format) > maxTokens)
            {
                res.RemoveAt(1);
            }

            // Guard against leaving an orphaned tool-result message as the first post-system message.
            while (res.Count > 1 && res[1].Role == OpenAI.Role.Tool)
            {
                res.RemoveAt(1);
            }

            return res;
        }

        public static int CountTokens(IList<Message> messages, InstructFormat? format = null)
        {
            if (format == null)
            {
                return messages.Sum(m => CountTokens(m.Content.ToString()));
            }
            var total = 0;
            foreach (var message in messages)
            {
                var charcnt = 0;
                var imgcnt = 0;
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

                if (message.Content is string strContent)
                {
                    charcnt += strContent.Length;
                }
                else if (message.Content is IEnumerable<object> enumez)
                {
                    foreach (var item in enumez)
                    {
                        if (item is string strItem)
                        {
                            charcnt += strItem.Length;
                        }
                        else if (item is ImageUrl || item is ImageFile)
                        {
                            imgcnt++;
                        }
                    }
                }
                total += (charcnt / 4) + (imgcnt * LLMEngine.Settings.ImageEmbeddingSize) + 10; // Approximate token count with a safety margin
            }
            return total;
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
