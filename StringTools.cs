using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIToolkit.LLM;
using AIToolkit.Files;
using Newtonsoft.Json;
using AIToolkit.API;
using System.Text.RegularExpressions;
using System.Globalization;

namespace AIToolkit
{
    using System;
    using System.Text;

    public class StringFix(bool removeAllBoldedText, bool fixQuotes, bool removeSingleWorldEmphasis, bool removeAllQuotes)
    {
        public bool RemoveAllBoldedText = removeAllBoldedText;
        public bool RemoveAllQuotes = removeAllQuotes;
        public bool FixQuotes = fixQuotes;
        public bool RemoveSingleWorldEmphasis = removeSingleWorldEmphasis;
    }

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
        /// Removes the occurences of what's in slop from text. Ignores case and only removes if there's a space before and after the word.
        /// </summary>
        /// <param name="text">text to scan</param>
        /// <param name="slop">slop to remove</param>
        /// <param name="removechance">chance to remove</param>
        /// <returns></returns>
        public static string RemoveSlop(this string text, string[] slop, float removechance)
        {
            // Claude Sonnet 3.5 code
            var result = text;
            foreach (var word in slop)
            {
                // Pattern now explicitly requires space/start/end boundaries around the word
                string pattern = $@"(^|\s){Regex.Escape(word)}(\s|$)";
                result = Regex.Replace(result, pattern, match =>
                {
                    // Preserve the space before/after when removing the word
                    bool removeWord = LLMSystem.RNG.NextDouble() < removechance;
                    if (!removeWord) return match.Value;
                    
                    // Keep one space unless we're at start/end of string
                    bool isStart = match.Groups[1].Value == "";
                    bool isEnd = match.Groups[2].Value == "";
                    return (isStart ? "" : " ") + (isEnd ? "" : " ");
                }, RegexOptions.IgnoreCase);
            }
            // Clean up any multiple spaces that might have been created
            return result.Replace("  ", " ");
        }

        public static string FixRoleplayString(this string input, StringFix fix)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            var workstring = input;
            // Turn weird quotes into normal quotes
            workstring = workstring.Replace("“", "\"");
            workstring = workstring.Replace("”", "\"");
            workstring = workstring.Replace("—", " - ");
            if (fix.RemoveAllBoldedText)
            {
                // Remove all bolded text
                workstring = workstring.Replace("**", "");
            }
            if (fix.RemoveAllQuotes)
            {
                workstring = workstring.Replace("\"", "");
            }

            if (fix.RemoveSingleWorldEmphasis)
            {
                // Process tokens between ** ** (double asterisks)
                string pattern = @"\*\*([^\s*]+?)(\p{P}?)\*\*";
                workstring = Regex.Replace(workstring, pattern, "$1$2");

                // Process tokens between * * (single asterisks)
                pattern = @"\*([^\s*]+?)(\p{P}?)\*";
                workstring = Regex.Replace(workstring, pattern, "$1$2");
            }

            if (fix.FixQuotes && !fix.RemoveAllQuotes)
            {
                // Remove asterisks if they are between quotes
                workstring = Regex.Replace(workstring,
                    "\"([^\"]*)\"",
                    match => "\"" + match.Groups[1].Value.Replace("*", "") + "\"");

                // Remove asterisks just before and after quotes
                workstring = workstring.Replace(" **\"", " \"");
                workstring = workstring.Replace("\"** ", "\" ");
                workstring = workstring.Replace("\n**\"", "\n\"");
                workstring = workstring.Replace("\"**\n", "\"\n");
                workstring = workstring.Replace(" *\"", " \"");
                workstring = workstring.Replace("\"* ", "\" ");
                workstring = workstring.Replace("\n*\"", "\n\"");
                workstring = workstring.Replace("\"*\n", "\"\n");
            }
            return workstring;
        }

        public static string RemoveThinkingBlocks(this string text, string thinkstart, string thinkend)
        {
            var workstring = text;
            if (workstring.Contains(LLMSystem.Instruct.ThinkingEnd))
            {
                // remove everything before the thinking end tag (included)
                var idx = workstring.IndexOf(LLMSystem.Instruct.ThinkingEnd);
                workstring = workstring[(idx + LLMSystem.Instruct.ThinkingEnd.Length)..].CleanupAndTrim();
            }
            return workstring;
        }

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
            return text.TrimEnd('\n').TrimStart('\n').Trim();
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

        public static string DateToHumanString(DateTime date)
        {
            static string GetDaySuffix(int day)
            {
                if (day >= 11 && day <= 13)
                {
                    return "th";
                }

                return (day % 10) switch
                {
                    1 => "st",
                    2 => "nd",
                    3 => "rd",
                    _ => "th",
                };
            }

            string daySuffix = GetDaySuffix(date.Day);
            string formattedDate = date.ToString("MMMM d", CultureInfo.InvariantCulture) + daySuffix + ", " + date.Year.ToString(CultureInfo.InvariantCulture);
            return formattedDate;
        }

        /// <summary>
        /// Turn a time span into something clearly legible for a human
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static string TimeSpanToHumanString(TimeSpan span)
        {
            // Turn a time span into something clearly legible for a human
            if (span.Days > 1)
                return span.Days.ToString() + " days";
            else if (span.Days > 0)
                return "1 day";
            else if (span.Hours > 0)
                return span.Hours.ToString() + " hours";
            else if (span.Minutes > 0)
                return span.Minutes.ToString() + " minutes";
            else
                return span.Seconds.ToString() + " seconds";
        }

    }
}
