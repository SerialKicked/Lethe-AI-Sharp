using Newtonsoft.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace LetheAISharp.Skills
{
    /// <summary>
    /// Where a skill comes from.
    /// </summary>
    public enum SkillSource
    {
        /// <summary> Written by the user. </summary>
        User,
        /// <summary> Written by the model (through a tool call or the draft generator). </summary>
        Agent,
        /// <summary> Imported from an external file. </summary>
        Imported
    }

    /// <summary>
    /// A skill is a markdown document describing how to perform a particular task.
    /// It is stored as a folder containing a SKILL.md file (YAML-like frontmatter followed by the markdown body),
    /// which is compatible with the common "Agent Skills" layout.
    /// </summary>
    public partial class Skill
    {
        public const string FileName = "SKILL.md";
        public const int MaxNameLength = 64;
        public const int MaxDescriptionLength = 1024;

        /// <summary> Unique skill name. Lowercase letters, digits and hyphens only (also the folder name). </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary> Short explanation of what the skill does and when it should be used. This is what the model sees in the skill index. </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary> Optional keywords used to improve search and automatic suggestion. </summary>
        public List<string> Keywords { get; set; } = [];

        /// <summary> Disabled skills are ignored by the index, the tools and the search. </summary>
        public bool Enabled { get; set; } = true;

        /// <summary> Origin of the skill. </summary>
        public SkillSource Source { get; set; } = SkillSource.User;

        /// <summary> The markdown instructions. </summary>
        public string Body { get; set; } = string.Empty;

        /// <summary> Absolute path of the skill's folder (empty if never saved). </summary>
        [JsonIgnore] public string FolderPath { get; set; } = string.Empty;

        /// <summary> Unrecognized frontmatter entries, preserved when the skill is saved again. </summary>
        [JsonIgnore] public List<string> ExtraFrontmatter { get; set; } = [];

        // Embedding cache (not persisted)
        [JsonIgnore] internal float[] Embedding { get; set; } = [];
        [JsonIgnore] internal string EmbeddedText { get; set; } = string.Empty;

        /// <summary> Text used to compute the skill's embedding. </summary>
        internal string GetEmbedText()
        {
            var sb = new StringBuilder();
            sb.Append(Name.Replace('-', ' ')).Append(". ").Append(Description);
            if (Keywords.Count > 0)
                sb.Append(" Keywords: ").Append(string.Join(", ", Keywords));
            return sb.ToString();
        }

        #region *** Name handling ***

        [GeneratedRegex("[^a-z0-9]+")]
        private static partial Regex InvalidNameChars();

        /// <summary>
        /// Converts any string into a valid skill name (lowercase, digits and single hyphens, max 64 characters).
        /// Returns an empty string if nothing usable remains.
        /// </summary>
        public static string Slugify(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            var lower = name.Trim().ToLowerInvariant();
            // strip accents
            var normalized = lower.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            var slug = InvalidNameChars().Replace(sb.ToString(), "-").Trim('-');
            if (slug.Length > MaxNameLength)
                slug = slug[..MaxNameLength].Trim('-');
            return slug;
        }

        #endregion

        #region *** Parsing and serialization ***

        /// <summary>
        /// Parse the content of a SKILL.md file. Never throws: missing or malformed frontmatter simply yields default values.
        /// </summary>
        /// <param name="text">Full file content.</param>
        /// <param name="fallbackName">Name to use if the frontmatter doesn't provide a valid one (usually the folder name).</param>
        public static Skill Parse(string text, string fallbackName)
        {
            var skill = new Skill();
            text = (text ?? string.Empty).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
            string body = text;
            var frontmatter = new List<string>();

            if (text.StartsWith("---\n") || text == "---")
            {
                var lines = text.Split('\n');
                int end = -1;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].TrimEnd() == "---")
                    {
                        end = i;
                        break;
                    }
                }
                if (end > 0)
                {
                    frontmatter = [.. lines[1..end]];
                    body = string.Join("\n", lines[(end + 1)..]);
                }
            }

            ParseFrontmatter(skill, frontmatter);
            skill.Body = body.Trim('\n', ' ', '\t');

            var name = Slugify(skill.Name);
            if (name.Length == 0)
                name = Slugify(fallbackName);
            skill.Name = name;
            if (skill.Description.Length > MaxDescriptionLength)
                skill.Description = skill.Description[..MaxDescriptionLength];
            return skill;
        }

        private static void ParseFrontmatter(Skill skill, List<string> lines)
        {
            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                // a top-level key starts at column 0
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(' ') || line.StartsWith('\t') || line.StartsWith('#'))
                {
                    i++;
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    i++;
                    continue;
                }
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();

                // gather continuation lines (indented lines or blank lines followed by indented lines)
                var block = new List<string>();
                int j = i + 1;
                while (j < lines.Count)
                {
                    var next = lines[j];
                    if (next.StartsWith(' ') || next.StartsWith('\t') || (string.IsNullOrWhiteSpace(next) && j + 1 < lines.Count && (lines[j + 1].StartsWith(' ') || lines[j + 1].StartsWith('\t'))))
                    {
                        block.Add(next);
                        j++;
                    }
                    else
                        break;
                }

                switch (key.ToLowerInvariant())
                {
                    case "name":
                        skill.Name = ReadScalar(value, block);
                        break;
                    case "description":
                        skill.Description = ReadScalar(value, block);
                        break;
                    case "keywords":
                        skill.Keywords = ReadList(value, block);
                        break;
                    case "enabled":
                        skill.Enabled = !ReadScalar(value, block).Equals("false", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "source":
                        if (Enum.TryParse<SkillSource>(ReadScalar(value, block), true, out var src))
                            skill.Source = src;
                        break;
                    default:
                        // preserve unknown entries as-is
                        skill.ExtraFrontmatter.Add(line);
                        skill.ExtraFrontmatter.AddRange(block);
                        break;
                }
                i = j;
            }
        }

        private static string ReadScalar(string value, List<string> block)
        {
            // YAML block scalars: > (folded) or | (literal)
            if (value.Length > 0 && (value[0] == '>' || value[0] == '|'))
            {
                var folded = value[0] == '>';
                var parts = block.Select(b => b.Trim()).ToList();
                return folded
                    ? string.Join(" ", parts.Where(p => p.Length > 0)).Trim()
                    : string.Join("\n", parts).Trim();
            }
            // multi-line plain scalar
            if (block.Count > 0)
                value = (value + " " + string.Join(" ", block.Select(b => b.Trim()).Where(b => b.Length > 0))).Trim();
            return Unquote(value);
        }

        private static List<string> ReadList(string value, List<string> block)
        {
            var items = new List<string>();
            if (value.StartsWith('[') && value.EndsWith(']'))
                value = value[1..^1];

            if (value.Length > 0)
            {
                items.AddRange(value.Split(',').Select(v => Unquote(v.Trim())));
            }
            foreach (var b in block)
            {
                var t = b.Trim();
                if (t.StartsWith('-'))
                    items.Add(Unquote(t[1..].Trim()));
                else if (t.Length > 0)
                    items.AddRange(t.Split(',').Select(v => Unquote(v.Trim())));
            }
            return [.. items.Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        private static string Unquote(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                try
                {
                    return JsonConvert.DeserializeObject<string>(value) ?? string.Empty;
                }
                catch
                {
                    return value[1..^1];
                }
            }
            if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                return value[1..^1].Replace("''", "'");
            return value;
        }

        private static string WriteScalar(string value)
        {
            value = value.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
            var needsQuote = value.Length == 0
                || value.Contains(": ") || value.Contains(" #") || value.Contains('"')
                || "[]{}&*!|>'%@`,#-?:".Contains(value[0])
                || value != value.Trim()
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("null", StringComparison.OrdinalIgnoreCase);
            return needsQuote ? JsonConvert.SerializeObject(value) : value;
        }

        /// <summary>
        /// Builds the full SKILL.md file content.
        /// </summary>
        public string ToFileText()
        {
            var sb = new StringBuilder();
            sb.Append("---\n");
            sb.Append("name: ").Append(WriteScalar(Name)).Append('\n');
            sb.Append("description: ").Append(WriteScalar(Description)).Append('\n');
            if (Keywords.Count > 0)
                sb.Append("keywords: ").Append(WriteScalar(string.Join(", ", Keywords.Select(k => k.Replace(",", " "))))).Append('\n');
            if (!Enabled)
                sb.Append("enabled: false\n");
            if (Source != SkillSource.User)
                sb.Append("source: ").Append(Source.ToString().ToLowerInvariant()).Append('\n');
            foreach (var extra in ExtraFrontmatter)
                sb.Append(extra).Append('\n');
            sb.Append("---\n\n");
            sb.Append(Body.Replace("\r\n", "\n").Trim('\n')).Append('\n');
            return sb.ToString();
        }

        #endregion

        /// <summary>
        /// Basic validation. Returns an error message, or an empty string if the skill is valid.
        /// </summary>
        public string Validate(int maxContentLength)
        {
            if (string.IsNullOrEmpty(Name))
                return "The skill name is empty or contains no usable characters (use letters, digits and hyphens).";
            if (Name != Slugify(Name))
                return "The skill name must only contain lowercase letters, digits and single hyphens.";
            if (string.IsNullOrWhiteSpace(Description))
                return "The skill needs a description explaining what it does and when to use it.";
            if (Description.Length > MaxDescriptionLength)
                return $"The description is too long (max {MaxDescriptionLength} characters).";
            if (string.IsNullOrWhiteSpace(Body))
                return "The skill content is empty.";
            if (Body.Length > maxContentLength)
                return $"The skill content is too long ({Body.Length} characters, max {maxContentLength}).";
            return string.Empty;
        }

        public override string ToString() => Name;
    }
}
