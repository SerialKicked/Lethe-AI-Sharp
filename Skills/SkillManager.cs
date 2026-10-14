using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;

namespace LetheAISharp.Skills
{
    /// <summary>
    /// A skill returned by a search, with its relevance.
    /// </summary>
    public class SkillMatch(Skill skill, float score, float distance, bool keywordHit)
    {
        public Skill Skill { get; } = skill;
        /// <summary> Combined relevance score (higher is better). </summary>
        public float Score { get; } = score;
        /// <summary> Embedding distance between the query and the skill (2 if embeddings were not used). </summary>
        public float Distance { get; } = distance;
        /// <summary> True if the skill matched on name, keywords or description words. </summary>
        public bool KeywordHit { get; } = keywordHit;
    }

    /// <summary>
    /// Loads, stores, searches and saves skills. Available as <see cref="LLMEngine.SkillManager"/>.
    /// </summary>
    public partial class SkillManager
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, Skill> _skills = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> Minimum score for a result to be returned by an explicit search. </summary>
        public const float SearchMinScore = 0.15f;
        /// <summary> Minimum score for a result to be suggested automatically. </summary>
        public const float AutoMinScore = 0.4f;

        /// <summary> Raised after the library has been loaded or modified. </summary>
        public event Action? SkillsChanged;

        private static SkillSettings Options => LLMEngine.Settings.SkillOptions;

        /// <summary> Absolute path of the skill library folder. </summary>
        public string RootFolder => Path.GetFullPath(Options.Folder);

        /// <summary> All skills, sorted by name. </summary>
        public IReadOnlyList<Skill> All
        {
            get
            {
                lock (_lock)
                    return [.. _skills.Values.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
            }
        }

        /// <summary> Enabled skills, sorted by name. </summary>
        public IReadOnlyList<Skill> Enabled => [.. All.Where(s => s.Enabled)];

        public int Count
        {
            get { lock (_lock) return _skills.Count; }
        }

        #region *** Loading and saving ***

        /// <summary>
        /// (Re)load every skill from <see cref="RootFolder"/>. The folder is created if needed.
        /// </summary>
        public void Load()
        {
            var root = RootFolder;
            Directory.CreateDirectory(root);
            var loaded = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in Directory.GetDirectories(root))
            {
                var file = Path.Combine(dir, Skill.FileName);
                if (!File.Exists(file))
                    continue;
                try
                {
                    var skill = Skill.Parse(File.ReadAllText(file), Path.GetFileName(dir));
                    skill.FolderPath = dir;
                    if (string.IsNullOrEmpty(skill.Name))
                    {
                        LLMEngine.Logger?.LogWarning("[Skills] Ignoring skill without a usable name: {dir}", dir);
                        continue;
                    }
                    if (loaded.ContainsKey(skill.Name))
                    {
                        LLMEngine.Logger?.LogWarning("[Skills] Duplicate skill name '{name}' in {dir}, ignored", skill.Name, dir);
                        continue;
                    }
                    loaded[skill.Name] = skill;
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogWarning(ex, "[Skills] Failed to load skill from {dir}", dir);
                }
            }
            lock (_lock)
            {
                _skills.Clear();
                foreach (var kv in loaded)
                    _skills[kv.Key] = kv.Value;
            }
            OnChanged();
        }

        /// <summary>
        /// Get a skill by name. The name is matched exactly (case-insensitive), then by its slug form.
        /// </summary>
        public Skill? Get(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            lock (_lock)
            {
                if (_skills.TryGetValue(name.Trim(), out var skill))
                    return skill;
                if (_skills.TryGetValue(Skill.Slugify(name), out skill))
                    return skill;
            }
            return null;
        }

        /// <summary>
        /// Returns a name that doesn't collide with an existing skill (appends -2, -3...).
        /// </summary>
        public string MakeUniqueName(string name)
        {
            var baseName = Skill.Slugify(name);
            if (baseName.Length == 0)
                baseName = "skill";
            var candidate = baseName;
            int i = 2;
            while (Get(candidate) != null || Directory.Exists(Path.Combine(RootFolder, candidate)))
            {
                var suffix = "-" + i++;
                var trimmed = baseName.Length + suffix.Length > Skill.MaxNameLength ? baseName[..(Skill.MaxNameLength - suffix.Length)] : baseName;
                candidate = trimmed + suffix;
            }
            return candidate;
        }

        /// <summary>
        /// Validate and save a skill to disk. If <paramref name="previousName"/> is provided and differs from the skill's current name, the skill is renamed.
        /// </summary>
        /// <returns>An error message, or an empty string on success.</returns>
        public string Save(Skill skill, string? previousName = null)
        {
            skill.Name = Skill.Slugify(skill.Name);
            skill.Description = skill.Description.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            skill.Body = skill.Body.Replace("\r\n", "\n");
            var err = skill.Validate(Options.MaxContentLength);
            if (err.Length > 0)
                return err;

            var root = RootFolder;
            Directory.CreateDirectory(root);
            var targetDir = Path.GetFullPath(Path.Combine(root, skill.Name));
            if (!IsInsideRoot(targetDir))
                return "Invalid skill name.";

            try
            {
                lock (_lock)
                {
                    var isNew = !_skills.ContainsKey(skill.Name) && (previousName == null || !_skills.ContainsKey(previousName));
                    if (isNew && _skills.Count >= Options.MaxSkillCount)
                        return $"The skill library is full ({Options.MaxSkillCount} skills).";

                    // rename handling
                    var renamed = !string.IsNullOrEmpty(previousName) && !previousName.Equals(skill.Name, StringComparison.OrdinalIgnoreCase);
                    if (renamed)
                    {
                        if (_skills.ContainsKey(skill.Name))
                            return $"A skill named '{skill.Name}' already exists.";
                        if (_skills.TryGetValue(previousName!, out var old))
                        {
                            var oldDir = old.FolderPath;
                            if (!string.IsNullOrEmpty(oldDir) && Directory.Exists(oldDir) && IsInsideRoot(oldDir) && !Directory.Exists(targetDir))
                                Directory.Move(oldDir, targetDir);
                            _skills.Remove(previousName!);
                        }
                    }

                    Directory.CreateDirectory(targetDir);
                    var file = Path.Combine(targetDir, Skill.FileName);
                    var text = skill.ToFileText();
                    if (File.Exists(file))
                    {
                        var existing = File.ReadAllText(file);
                        if (existing != text)
                            File.Copy(file, file + ".bak", true);
                    }
                    var tmp = file + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    File.Move(tmp, file, true);

                    skill.FolderPath = targetDir;
                    skill.Embedding = [];
                    skill.EmbeddedText = string.Empty;
                    _skills[skill.Name] = skill;
                }
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[Skills] Failed to save skill {name}", skill.Name);
                return "Failed to save the skill: " + ex.Message;
            }
            OnChanged();
            return string.Empty;
        }

        /// <summary>
        /// Delete a skill and its folder.
        /// </summary>
        /// <returns>An error message, or an empty string on success.</returns>
        public string Delete(string name)
        {
            Skill? skill;
            lock (_lock)
            {
                skill = Get(name);
                if (skill == null)
                    return $"Skill '{name}' not found.";
                try
                {
                    if (!string.IsNullOrEmpty(skill.FolderPath) && Directory.Exists(skill.FolderPath))
                    {
                        if (!IsInsideRoot(skill.FolderPath))
                            return "Refusing to delete a folder outside of the skill library.";
                        Directory.Delete(skill.FolderPath, true);
                    }
                    _skills.Remove(skill.Name);
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogError(ex, "[Skills] Failed to delete skill {name}", name);
                    return "Failed to delete the skill: " + ex.Message;
                }
            }
            OnChanged();
            return string.Empty;
        }

        /// <summary>
        /// Import a skill from a markdown file or from a folder containing a SKILL.md file.
        /// Imported skills are added disabled until the user reviews them.
        /// </summary>
        /// <returns>The imported skill, or null with an error message.</returns>
        public Skill? Import(string path, out string error)
        {
            error = string.Empty;
            try
            {
                string file;
                string fallback;
                if (Directory.Exists(path))
                {
                    file = Path.Combine(path, Skill.FileName);
                    fallback = Path.GetFileName(path.TrimEnd('\\', '/'));
                }
                else
                {
                    file = path;
                    fallback = Path.GetFileNameWithoutExtension(path);
                    if (Path.GetFileName(path).Equals(Skill.FileName, StringComparison.OrdinalIgnoreCase))
                        fallback = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(path)) ?? fallback);
                }
                if (!File.Exists(file))
                {
                    error = "No SKILL.md or markdown file found.";
                    return null;
                }
                var info = new FileInfo(file);
                if (info.Length > Options.MaxContentLength * 4L)
                {
                    error = "The file is too large to be a skill.";
                    return null;
                }
                var skill = Skill.Parse(File.ReadAllText(file), fallback);
                if (string.IsNullOrWhiteSpace(skill.Description))
                {
                    // fall back to the first non-empty, non-heading line of the body
                    var firstLine = skill.Body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? string.Empty;
                    skill.Description = firstLine.Length > 300 ? firstLine[..300] : firstLine;
                }
                skill.Name = MakeUniqueName(skill.Name.Length > 0 ? skill.Name : fallback);
                skill.Source = SkillSource.Imported;
                skill.Enabled = false;
                skill.FolderPath = string.Empty;
                error = Save(skill);
                return error.Length == 0 ? skill : null;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private bool IsInsideRoot(string path)
        {
            var root = RootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full.Length > root.Length;
        }

        private void OnChanged()
        {
            LLMEngine.InvalidatePromptCache();
            SkillsChanged?.Invoke();
        }

        #endregion

        #region *** System prompt index ***

        /// <summary>
        /// Builds the list of enabled skills (one line each) shown to the model. Sorted alphabetically so the text stays stable between requests.
        /// </summary>
        public string BuildIndex()
        {
            var enabled = Enabled;
            if (enabled.Count == 0)
                return string.Empty;
            var sb = new StringBuilder();
            var max = Math.Max(1, Options.MaxIndexEntries);
            var maxdesc = Math.Max(40, Options.MaxIndexDescriptionLength);
            foreach (var skill in enabled.Take(max))
            {
                var desc = skill.Description;
                if (desc.Length > maxdesc)
                    desc = desc[..maxdesc].TrimEnd() + "...";
                sb.Append("- ").Append(skill.Name).Append(": ").Append(desc).Append('\n');
            }
            if (enabled.Count > max)
                sb.Append($"- ({enabled.Count - max} more skills are not listed, use Skill_Search to find them)\n");
            return sb.ToString().TrimEnd('\n');
        }

        #endregion

        #region *** Search ***

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","and","for","with","that","this","from","have","you","your","are","was","were","not","but","can","will","what",
            "when","how","use","using","used","into","about","then","than","them","they","their","there","here","just","like",
            "need","want","please","make","get","let","has","had","its","our","out","all","any","some","also","does","did",
            "should","would","could","skill","task","help"
        };

        [GeneratedRegex(@"[\p{L}\p{N}]+")]
        private static partial Regex WordRegex();

        private static HashSet<string> Words(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in WordRegex().Matches(text))
            {
                if (m.Value.Length >= 3 && !StopWords.Contains(m.Value))
                    set.Add(Stem(m.Value));
            }
            return set;
        }

        /// <summary> Very light plural handling so 'errors' matches 'error'. </summary>
        private static string Stem(string word)
        {
            if (word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
                return word[..^1];
            return word;
        }

        private static bool ContainsPhrase(string lowerQuery, string phrase)
        {
            phrase = phrase.Trim().ToLowerInvariant();
            if (phrase.Length == 0)
                return false;
            return Regex.IsMatch(lowerQuery, @"(?<![\p{L}\p{N}])" + Regex.Escape(phrase) + @"(?:e?s)?(?![\p{L}\p{N}])");
        }

        private static float KeywordScore(string lowerQuery, HashSet<string> queryWords, Skill skill)
        {
            float score = 0;
            foreach (var kw in skill.Keywords)
            {
                if (ContainsPhrase(lowerQuery, kw))
                    score += 1.0f;
            }
            var namePhrase = skill.Name.Replace('-', ' ');
            if (ContainsPhrase(lowerQuery, namePhrase))
                score += 1.5f;
            else
            {
                foreach (var part in skill.Name.Split('-'))
                {
                    if (part.Length >= 3 && !StopWords.Contains(part) && queryWords.Contains(Stem(part)))
                        score += 0.4f;
                }
            }
            var descWords = Words(skill.Description);
            int overlap = queryWords.Count(w => descWords.Contains(w));
            score += Math.Min(0.45f, overlap * 0.1f);
            return score;
        }

        /// <summary>
        /// Search the enabled skills using keywords and, if available, embedding similarity.
        /// </summary>
        /// <param name="query">Text to look for (a request, a few keywords, a user message...).</param>
        /// <param name="max">Maximum number of results.</param>
        /// <param name="minScore">Minimum relevance score. Use <see cref="SearchMinScore"/> or <see cref="AutoMinScore"/>.</param>
        public async Task<List<SkillMatch>> SearchAsync(string query, int max, float minScore = SearchMinScore, CancellationToken ct = default)
        {
            var candidates = Enabled;
            if (candidates.Count == 0 || string.IsNullOrWhiteSpace(query) || max <= 0)
                return [];

            var lowerQuery = query.ToLowerInvariant();
            var queryWords = Words(query);

            float[] queryEmbed = [];
            var useEmbeddings = Options.UseEmbeddings && LLMEngine.Settings.RAGEnabled;
            if (useEmbeddings)
            {
                try
                {
                    queryEmbed = await EmbedTools.EmbeddingText(query).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LLMEngine.Logger?.LogWarning(ex, "[Skills] Embedding of the query failed, using keywords only");
                }
            }

            var results = new List<SkillMatch>();
            var cutoff = Math.Max(0.05f, Options.SimilarityCutoff);
            foreach (var skill in candidates)
            {
                ct.ThrowIfCancellationRequested();
                var kw = KeywordScore(lowerQuery, queryWords, skill);
                float dist = 2f;
                if (queryEmbed.Length > 0)
                {
                    try
                    {
                        var text = skill.GetEmbedText();
                        if (skill.Embedding.Length == 0 || skill.EmbeddedText != text)
                        {
                            skill.Embedding = await EmbedTools.EmbeddingText(text).ConfigureAwait(false);
                            skill.EmbeddedText = text;
                        }
                        dist = EmbedTools.GetDistance(queryEmbed, skill.Embedding);
                    }
                    catch (Exception ex)
                    {
                        LLMEngine.Logger?.LogWarning(ex, "[Skills] Embedding of skill {name} failed", skill.Name);
                    }
                }
                var sim = dist <= cutoff ? 1f - (dist / cutoff) : 0f;
                var score = kw + sim;
                if (score >= minScore)
                    results.Add(new SkillMatch(skill, score, dist, kw > 0));
            }
            return [.. results.OrderByDescending(r => r.Score).Take(max)];
        }

        /// <summary>
        /// Synchronous version of <see cref="SearchAsync"/>, for callers that can't await (such as context plugins).
        /// </summary>
        public List<SkillMatch> Search(string query, int max, float minScore = SearchMinScore)
        {
            return Task.Run(() => SearchAsync(query, max, minScore)).GetAwaiter().GetResult();
        }

        #endregion
    }
}
