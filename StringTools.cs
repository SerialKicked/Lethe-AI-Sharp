using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIToolkit.LLM;
using AIToolkit.Files;
using Newtonsoft.Json;
using AIToolkit.API;
using System.Text.RegularExpressions;

namespace AIToolkit
{

    public static class StringExtensions
    {
        public static StringBuilder AppendLinuxLine(this StringBuilder sb, string? text = null)
        {
            return text == null ? sb.Append(LLMSystem.NewLine) : sb.Append(text).Append(LLMSystem.NewLine);
        }

        public static string ToWinFormat(this string text) => text.Replace("\n", "\r\n");

        public static string ToLinuxFormat(this string text) => text.Replace("\r\n", "\n");

        public static string SanitizeForJS(this string text)
        {
            return text.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
        }

        public static string RemoveNewLines(this string text) => text.ToLinuxFormat().Replace("\n\n", " ").Replace('\n', ' ').Replace("  ", " ").Trim();

        /// <summary>
        /// Tries to fix missing asterisks in the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string FixAsterisks(this string text)
        {
            // Automatically close asterisks if they are not closed before the end of each paragraph delimited by a newline
            var lines = text.Split(LLMSystem.NewLine);
            for (int i = 0; i < lines.Length; i++)
            {
                // skip small lines
                if (lines[i].Length <= 2)
                    continue;
                if (lines[i].StartsWith('*'))
                {
                    // remove the first character from lines[i]
                    lines[i] = lines[i][1..];
                    lines[i] = "*" + lines[i].Trim();
                }
                if (lines[i].Count(c => c == '*') % 2 == 1)
                {
                    // If a line ends with but doesn't start with an asterisk, add one at the beginning
                    if (lines[i].EndsWith('*') && !lines[i].StartsWith('*'))
                    {
                        lines[i] = "*" + lines[i].Trim();
                    }
                    else
                        lines[i] = lines[i].Trim() + "*";
                }
            }
            return string.Join(LLMSystem.NewLine, lines);
        }

        /// <summary>
        /// Trim the string and remove any trailing newlines
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string CleanupAndTrim(this string text)
        {
            return text.Trim().TrimEnd('\n');
        }

        /// <summary>
        /// Replaces Discord emojis with their text representation
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string ReplaceDiscordEmojis(this string input)
        {
            string pattern = @"<:(.*?):\d+>";
            return Regex.Replace(input, pattern, ":$1:");
        }

    }
}
