using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using SharpToken;

namespace LetheAISharp
{
    public static class TokenTools
    {
        public static GptEncoding Encoding { get; private set; } = GptEncoding.GetEncoding("cl100k_base");

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
            if (!string.IsNullOrEmpty(format.SysPromptStart))
                specialchar.Add(format.SysPromptStart);
            if (!string.IsNullOrEmpty(format.SysPromptEnd))
                specialchar.Add(format.SysPromptEnd);
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

        internal static OpenAI.Role InternalRoleToChatRole(AuthorRole role)
        {
            return role switch
            {
                AuthorRole.User => OpenAI.Role.User,
                AuthorRole.Assistant => OpenAI.Role.Assistant,
                AuthorRole.System => OpenAI.Role.System,
                AuthorRole.SysPrompt => OpenAI.Role.System,
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
                OpenAI.Role.System => AuthorRole.SysPrompt,
                _ => AuthorRole.User
            };
        }

    }
}
