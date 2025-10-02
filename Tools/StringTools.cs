using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LetheAISharp.LLM;
using LetheAISharp.Files;
using Newtonsoft.Json;
using LetheAISharp.API;
using System.Text.RegularExpressions;
using System.Globalization;

namespace LetheAISharp
{
    public class StringFix(bool removeAllBoldedText, bool fixQuotes, bool removeSingleWorldEmphasis, bool removeAllQuotes, bool removeItalic, float removeItalicRatio, int removeItalicMaxWords, bool lastParagraphDeleter)
    {
        public bool RemoveAllBoldedText = removeAllBoldedText;
        public bool RemoveAllQuotes = removeAllQuotes;
        public bool FixQuotes = fixQuotes;
        public bool RemoveSingleWorldEmphasis = removeSingleWorldEmphasis;
        public bool RemoveItalic = removeItalic;
        public float RemoveItalicRatio = removeItalicRatio;
        public int RemoveItalicMaxWords = removeItalicMaxWords;
        public bool LastParagraphDeleter = lastParagraphDeleter;
    }

    public static class StringExtensions
    {

        private static readonly (string pattern, string replacement)[] replacements = new (string, string)[]
        {
            // First-person -> {user}
            (@"\bI am\b", "{{user}} is"),
            (@"\bI'm\b", "{{user}} is"),
            (@"\bI have\b", "{{user}} has"),
            (@"\bI've\b", "{{user}} has"),
            (@"\bI'd\b", "{{user}} would"),
            (@"\bI feel\b", "{{user}} feels"),
            (@"\bI think\b", "{{user}} thinks"),
            (@"\bI want\b", "{{user}} wants"),
            (@"\bmy\b", "{{user}}'s"),
            (@"\bme\b", "{{user}}"),
            (@"\bI\b", "{{user}}"),

            // Second-person -> {bot}
            (@"\byou are\b", "{{char}} is"),
            (@"\byou're\b", "{{char}} is"),
            (@"\byour\b", "{{char}}'s"),
            (@"\byou have\b", "{{char}} has"),
            (@"\byou've\b", "{{char}} has"),
            (@"\byou'd\b", "{{char}} would"),
            (@"\byou feel\b", "{{char}} feels"),
            (@"\byou think\b", "{{char}} thinks"),
            (@"\byou want\b", "{{char}} wants"),
            (@"\byou\b", "{{char}}"),

            // Optional first-person plural
            (@"\bus\b", "{{user}} and {{char}}"),
            (@"\bour\b", "{{user}} and {{char}}'s")
        };

        public static StringBuilder AppendLinuxLine(this StringBuilder sb, string? text = null)
        {
            return text == null ? sb.Append(LLMEngine.NewLine) : sb.Append(text).Append(LLMEngine.NewLine);
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

        public static string SanitizeSearchQuery(this string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return "";

            // Remove or replace problematic characters
            var sanitized = query
                .Replace("\"", "")           // Remove quotes
                .Replace("'", "")            // Remove single quotes
                .Replace("`", "")            // Remove backticks
                .Replace("[", "")            // Remove brackets
                .Replace("]", "")
                .Replace("{", "")            // Remove braces
                .Replace("}", "")
                .Replace("(", "")            // Remove parentheses  
                .Replace(")", "")
                .Replace("|", " ")           // Replace pipes with spaces
                .Replace("&", " and ")       // Replace & with "and"
                .Replace("*", "")            // Remove asterisks
                .Replace("?", "")            // Remove question marks at end
                .Replace("!", "")            // Remove exclamation marks
                .Replace("#", "")            // Remove hashtags
                .Replace("@", "")            // Remove at symbols
                .Replace("%", "")            // Remove percent signs
                .Replace("^", "")            // Remove carets
                .Replace("~", "")            // Remove tildes
                .Replace("<", "")            // Remove angle brackets
                .Replace(">", "")
                .Trim();

            // Replace multiple spaces with single spaces
            while (sanitized.Contains("  "))
            {
                sanitized = sanitized.Replace("  ", " ");
            }

            // Limit length to avoid issues
            if (sanitized.Length > 200)
            {
                sanitized = sanitized[..200].Trim();
            }

            return sanitized;
        }

        public static int CountSubstring(this string text, string substring)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substring))
                return 0;

            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
            {
                count++;
                index += substring.Length;
            }

            return count;
        }

        public static string RemoveEverythingAfterLast(this string text, string substring)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substring))
                return text;

            // Find the first occurrence of the substring
            int index = text.LastIndexOf(substring, StringComparison.Ordinal);

            // If substring is found, return text before it
            if (index != -1)
            {
                return text[..index];
            }

            // If substring not found, return the original text
            return text;
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
                    bool removeWord = LLMEngine.RNG.NextDouble() < removechance;
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

        /// <summary>
        /// Advanced string filtering, generally used for roleplay text
        /// </summary>
        /// <param name="input"></param>
        /// <param name="fix"></param>
        /// <returns></returns>
        public static string FixRoleplayString(this string input, StringFix fix, bool streamed = false)
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

            // If the text starts with * and has less than 4 words before the next *, remove the asterisks and what's in between
            if (workstring.StartsWith('*'))
            {
                var endIdx = workstring.IndexOf('*', 1);
                if (endIdx != -1)
                {
                    var between = workstring[1..endIdx];
                    if (between.Split(' ').Length < 3)
                    {
                        workstring = workstring[(endIdx + 1)..].TrimStart();
                    }
                }
            };



            if (fix.RemoveSingleWorldEmphasis)
            {
                List<string> initlist = ["Grins", "Grin", "Mock-gasp", "Gasp", "Wink", "Winks", "Yawn", "Laughs", "Grins", "Winks", "Laughs", "Grinning", "Winking", "Laughing", "Smirks", "Smirking", "purrs", "Purring", "Giggle", "Giggles", "shivers" ];
                List<string> excludedWords = [.. initlist];
                foreach (var item in initlist)
                {
                    excludedWords.Add(item + ".");
                    excludedWords.Add(item + ",");
                }

                string excludedPattern = string.Join("|", excludedWords.Select(Regex.Escape));

                // Process tokens between ** ** (double asterisks) - excluding specific words
                string pattern = $@"\*\*(?!({excludedPattern})\b)([^\s*]+?)(\p{{P}}?)\*\*";
                workstring = Regex.Replace(workstring, pattern, "$2$3", RegexOptions.IgnoreCase);

                // Process tokens between * * (single asterisks) - excluding specific words
                pattern = $@"\*(?!({excludedPattern})\b)([^\s*]+?)(\p{{P}}?)\*";
                workstring = Regex.Replace(workstring, pattern, "$2$3", RegexOptions.IgnoreCase);
            }

            if (fix.FixQuotes && !fix.RemoveAllQuotes)
            {
                // Remove asterisks if they are between quotes
                workstring = Regex.Replace(workstring, "\"([^\"]*)\"", match => "\"" + match.Groups[1].Value.Replace("*", "") + "\"");

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

            if (fix.RemoveItalic && !streamed)
            {
                // Doesn't work during streaming so only done at the end.
                // find all occurences of *some text* and remove them all (asterisks included) according to the ratio
                var matches = Regex.Matches(workstring, @"\*[^*]+\*");
                foreach (Match match in matches)
                {
                    if (LLMEngine.RNG.NextDouble() < fix.RemoveItalicRatio)
                    {
                        // count words in the match
                        var words = match.Value.Split(' ');
                        if (words.Length <= fix.RemoveItalicMaxWords)
                            workstring = workstring.Replace(match.Value, "");
                    }
                }
            }

            if (fix.LastParagraphDeleter && !streamed)
            {
                var originalHadWindowsNewlines = workstring.Contains("\r\n");
                var linux = originalHadWindowsNewlines ? workstring.ToLinuxFormat() : workstring;

                // Split on one or more blank lines
                var rawParagraphs = Regex.Split(linux, @"\n\s*\n");
                // Filter out paragraphs that are entirely whitespace only if they are not meaningful
                var paragraphs = rawParagraphs
                    .Select(p => p.TrimEnd()) // keep leading spaces inside RP blocks but trim end
                    .Where(p => p.Length > 0)
                    .ToList();

                if (paragraphs.Count >= 3)
                {
                    var list = paragraphs.Take(paragraphs.Count - 1)
                                                         .Any(p => p.TrimStart().StartsWith('-') || p.TrimStart().StartsWith("1)") || p.TrimStart().StartsWith("a)"));

                    bool earlierHasQuestion = paragraphs.Take(paragraphs.Count - 1)
                                                         .Any(p => p.Contains('?'));
                   
                    bool deleteLast = !list && (earlierHasQuestion || LLMEngine.RNG.NextDouble() < (0.05f * (float)paragraphs.Count) );

                    if (deleteLast)
                    {
                        paragraphs.RemoveAt(paragraphs.Count - 1);
                        // Reconstruct with double newline as separator
                        linux = string.Join("\n\n", paragraphs);
                        workstring = originalHadWindowsNewlines ? linux.ToWinFormat() : linux;
                    }
                }
            }

            workstring = workstring.Replace("  ", " ");
            workstring = workstring.Replace("... ...", "... ");
            workstring = workstring.Replace("… …", "… ");
            return workstring.CleanupAndTrim();
        }

        public static string RemoveThinkingBlocks(this string text, string thinkstart, string thinkend)
        {
            var workstring = text;
            if (!string.IsNullOrWhiteSpace(thinkstart) && workstring.Contains(thinkend))
            {
                // remove everything before the thinking end tag (included)
                var idx = workstring.IndexOf(thinkend);
                workstring = workstring[(idx + thinkend.Length)..].CleanupAndTrim();
            }
            return workstring;
        }


        public static string RemoveTitle(this string text)
        {
            var workstring = text.Trim().TrimStart('\n');
            // If the string starts with a # remove the first line
            if (workstring.StartsWith('#'))
            {
                var idx = workstring.IndexOf('\n');
                if (idx > 0)
                    return workstring[(idx + 1)..];
            }
            return text;
        }

        public static string RemoveThinkingBlocks(this string text) => RemoveThinkingBlocks(text, LLMEngine.Instruct.ThinkingStart, LLMEngine.Instruct.ThinkingEnd);

        /// <summary>
        /// Tries to fix missing asterisks in the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string FixAsterisks(this string text)
        {
            // Automatically close asterisks if they are not closed before the end of each paragraph delimited by a newline
            var lines = text.Split(LLMEngine.NewLine);
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
            return string.Join(LLMEngine.NewLine, lines);
        }

        /// <summary>
        /// Trim the string and remove any trailing newlines
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string CleanupAndTrim(this string text)
        {
            return text.Trim().TrimEnd('\n').TrimStart('\n').Trim();
        }

        /// <summary>
        /// Remove the last unfinished sentence (no period) from the text
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static string RemoveUnfinishedSentence(this string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            var workstring = text.Trim();
            workstring = workstring[..workstring.LastIndexOf('.')] + ".";
            return workstring;
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

        /// <summary>
        /// Turn a date into a long string that is easily readable by a human
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
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

        public static string ConvertToThirdPerson(this string userInput)
        {

            if (string.IsNullOrWhiteSpace(userInput))
                return userInput;

            string output = userInput;

            foreach (var (pattern, replacement) in replacements)
            {
                // RegexOptions.IgnoreCase for case-insensitive matching
                output = Regex.Replace(output, pattern, replacement, RegexOptions.IgnoreCase);
            }
            return LLMEngine.Bot.ReplaceMacros(output);
        }


    }
}
