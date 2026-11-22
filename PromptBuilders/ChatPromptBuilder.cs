using LetheAISharp.Agent.Tools;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using LLama.Sampling;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LetheAISharp
{
    internal class ChatPromptBuilder : IPromptBuilder
    {
        private readonly List<SingleMessage> _prompt = [];
        private OpenAI.JsonSchema? _currentSchema = null;
        private List<string> imagefilepath = [];
        
        public object? LastQuery { get; set; }

        public int Count => _prompt.Count;

        public int AddMessage(AuthorRole role, string message)
        {
            var single = new SingleMessage(role, message);
            _prompt.Add(single);
            return GetTokenCount(single);
        }

        public int AddMessage(SingleMessage message)
        {
            _prompt.Add(message);
            if (_prompt.Count == 1 && message.Role == AuthorRole.System)
            {
                var testbuddy = message.Clone();
                var modified = LLMEngine.Instruct.UpdateSysPromptForThinking(testbuddy);
                if (modified)
                    return GetTokenCount(testbuddy);
            }
            var cost = GetTokenCount(message);
            return cost;
        }

        public object GetFullPrompt()
        {
            return _prompt;
        }

        public int GetTokenUsage()
        {
            return GetTokenUsage(_prompt);
        }

        private int GetTokenUsage(List<SingleMessage> messages)
        {

            var total = 0;

            // Some backends render the tool definitions themselves when counting (llama.cpp's
            // /apply-template does), in which case their cost is already inside CountMessageTokens and
            // adding a local estimate on top would inflate the count and trim the prompt for no reason.
            var toolsAlreadyCounted = LLMEngine.Client is LlamaCppAdapter llama && llama.CountsToolDefinitions;

            if (LLMEngine.ToolCallsLoaded && !toolsAlreadyCounted)
                total += LLMEngine.ToolManager.EstimatedTokenCost();

            if (LLMEngine.SupportsVision)
            {
                var imgcnt = messages.Where(m => m.HasSendableImage()).ToList().Count;
                if (LLMEngine.Settings.MaxImageCount > 0 && imgcnt > LLMEngine.Settings.MaxImageCount)
                    imgcnt = LLMEngine.Settings.MaxImageCount;
                total += imgcnt * (LLMEngine.Settings.ImageEmbeddingSize + 4);
            }

            if (LLMEngine.Client is not null)
            {
                return total + LLMEngine.Client.CountMessageTokens(messages);
            }

            foreach (var message in messages)
            {
                total += GetTokenCount(message, false) + 2;
            }
            return total;
        }

        public object? RegenLastQuery()
        {
            if (LastQuery is null || LastQuery is not ChatRequest req)
                return null;
            double temp = (LLMEngine.ForceTemperature >= 0) ? LLMEngine.ForceTemperature : LLMEngine.Sampler.Temperature;
            int? setseed = LLMEngine.Sampler.Sampler_seed != -1 ? LLMEngine.Sampler.Sampler_seed : LLMEngine.RNG.Next(int.MaxValue);

            req.Seed = setseed;
            req.TopP = LLMEngine.Sampler.Top_p;
            req.FrequencyPenalty = LLMEngine.Client is OpenAIAdapter ? LLMEngine.Sampler.Rep_pen - 1 : null;
            req.ImportFromGenerationInput(LLMEngine.Sampler);
            return req;
        }


        public int InsertMessage(int index, AuthorRole role, string message)
        {
            var single = new SingleMessage(role, message);
            if (index == _prompt.Count)
            {
                return AddMessage(single);
            }
            _prompt.Insert(index, single);
            return GetTokenCount(single);
        }

        public int InsertMessage(int index, SingleMessage message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(message);
            }
            _prompt.Insert(index, message);
            var cost = GetTokenCount(message);
            return cost;
        }

        private string GetResponseStart(BasePersona talker)
        {
            var addthink = LLMEngine.Instruct.PrefillThinking;
            var addname = (LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt);

            if (!talker.IsUser && LLMEngine.Settings.DisableThinking && addthink && addname && LLMEngine.Instruct.IsThinkFormat)
            {
                var simpleres = LLMEngine.Instruct.GetThinkPrefill();
                simpleres += talker.Name + ":";
                return simpleres;
            }
            var res = string.Empty;
            if (addname && (!LLMEngine.Instruct.IsThinkFormat || LLMEngine.Settings.DisableThinking))
                res += talker.Name + ":";
            if (talker.IsUser)
                return res;

            res += LLMEngine.Instruct.GetThinkPrefill();
            return res;
        }

        public object PromptToQuery(AuthorRole responserole = AuthorRole.Assistant, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null, bool forceAltRoles = false)
        {
            // Let's make sure we don't overshoot token limits.
            var workingprompt = new List<SingleMessage>(_prompt);
            var think = LLMEngine.Settings.DisableThinking;
            try
            {
                if (_currentSchema is not null)
                {
                    LLMEngine.Settings.DisableThinking = true;
                    LLMEngine.NamesInPromptOverride = false;
                    // Structured output suppresses the assistant generation prefix; the token count has to
                    // render the same way, so publish it before GetTokenUsage runs.
                    LLMEngine.AddGenerationPromptOverride = false;
                }

                if (LLMEngine.Settings.ToolCallChainLimit > 0 && workingprompt.Count > LLMEngine.Settings.ToolCallChainLimit)
                {
                    int chainCount = 0;
                    int foundID = -1;

                    for (int i = workingprompt.Count - 1; i >= 0; i--)
                    {
                        var msg = workingprompt[i];

                        // Count only tool call roots (assistant messages with tool calls)
                        if (msg.Role == AuthorRole.Assistant && msg.ToolCalls.Count > 0)
                        {
                            chainCount++;
                            if (chainCount > LLMEngine.Settings.ToolCallChainLimit)
                            {
                                foundID = i;
                                break;
                            }
                        }
                    }

                    if (foundID >= 0)
                    {
                        // Remove ONLY tool-related messages before foundID
                        workingprompt = [.. workingprompt.Where((m, idx) =>
                            !(idx < foundID && (m.Role == AuthorRole.Tool || (m.Role == AuthorRole.Assistant && m.ToolCalls.Count > 0))))];
                    }
                }
                LLMEngine.Instruct.UpdateSysPromptForThinking(workingprompt[0]);

                var max = LLMEngine.MaxContextLength - (responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride) - 15;

                // Exact path: the backend counts the fully-built request itself (chat template, tool
                // definitions and image expansion included), so trimming matches what the server will
                // actually process - no estimate drift. Currently llama.cpp only.
                if (LLMEngine.Client is { SupportsRequestTokenCount: true } exactClient)
                {
                    try
                    {
                        var exact = FitToContext(workingprompt, max, TrimCountMode.Exact, candidate =>
                        {
                            var request = BuildChatRequest(candidate, tempoverride, responseoverride, overridePrefill);
                            return (exactClient.CountRequestTokensSync(request), request);
                        });
                        return exact.Request!;
                    }
                    catch (Exception ex)
                    {
                        // The endpoint-missing case already disabled the capability inside the adapter
                        // (and logged why); anything else (a payload the server rejects, a transient
                        // failure) falls back to the estimate-based path for this query only.
                        LLMEngine.Logger?.LogWarning(ex, "[PromptBuilder] Exact request token count failed; falling back to estimate-based trimming.");
                    }
                }

                var fitted = FitToContext(workingprompt, max, TrimCountMode.Estimate, candidate => (GetTokenUsage(candidate), null));
                return BuildChatRequest(fitted.Prompt, tempoverride, responseoverride, overridePrefill);
            }
            finally
            {
                LLMEngine.Settings.DisableThinking = think;
                LLMEngine.NamesInPromptOverride = null;
                LLMEngine.AddGenerationPromptOverride = null;
            }
        }

        /// <summary>
        /// Converts <paramref name="workingprompt"/> into the final <see cref="ChatRequest"/>: message
        /// conversion, image pruning, prefill, sampler import, tool definitions and template kwargs.
        /// This is the single construction site for the outgoing request, so an exact token count taken
        /// on its result always describes the payload generation will actually send.
        /// </summary>
        private ChatRequest BuildChatRequest(List<SingleMessage> workingprompt, double tempoverride, int responseoverride, bool? overridePrefill)
        {
            var finalprompt = new List<Message>(workingprompt.ConvertAll(m => m.ToChatCompletion()));
            var cleanimages = !LLMEngine.SupportsVision || LLMEngine.Settings.MaxImageCount > 0;
            var maxallowed = LLMEngine.SupportsVision ? LLMEngine.Settings.MaxImageCount : 0;

            // Merge system messages (except the first) into the next user message if setup
            if (LLMEngine.Settings.MergeSystemMessageInUserMessage)
            {
                for (int i = finalprompt.Count - 1; i > 0; i--)
                {
                    var mess = finalprompt[i];
                    if (mess.Role == Role.System)
                    {
                        if (i + 1 < finalprompt.Count && finalprompt[i + 1].Role == Role.User)
                        {
                            var next = finalprompt[i + 1];
                            if (next.Content is List<Content> lst)
                            {
                                lst[0] = new Content(ContentType.Text, "[System Note] " + mess.Content.ToString() + "[/System Note]" + LLMEngine.NewLine + lst[0].Text);
                            }
                            else
                            {
                                next.Content = "[System Note] " + mess.Content.ToString() + "[/System Note]" + LLMEngine.NewLine + next.Content.ToString();                            
                            }
                            finalprompt.RemoveAt(i);
                        }
                    }
                }
            }

            if (cleanimages)
            {
                // traverse the list and remove oldest images until we are within the limit
                var count = 0;
                for (int i = finalprompt.Count - 1; i >= 0; i--)
                {
                    var mess = finalprompt[i];
                    if (mess.Content is List<Content> lst)
                    {
                        var hasimage = false;
                        foreach (var content in lst)
                        {
                            if (content.Type == ContentType.ImageUrl)
                            {
                                hasimage = true;
                                break;
                            }
                        }
                        if (hasimage)
                        {
                            count++;
                            if (count > LLMEngine.Settings.MaxImageCount)
                            {
                                lst.RemoveAll(c => c.Type == ContentType.ImageUrl);
                            }
                        }
                    }
                }
            }

            var DoFrequence = LLMEngine.Client is OpenAIAdapter;
            var prefill = overridePrefill ?? (LLMEngine.Instruct.PrefillThinking || LLMEngine.IsGroupConversation);

            if (LLMEngine.Client?.AllowPrefill == false && LLMEngine.Settings.BackendChatAllowPrefill != true)
                prefill = false;
            // prefilling is not available when using tool calls in prompt or when a structured output schema is set,
            // as it would interfere with the format of the response
            if (prefill && !LLMEngine.ToolCallsLoaded && _currentSchema is null)
            {
                var info = GetResponseStart(LLMEngine.Bot);
                if (!string.IsNullOrWhiteSpace(info))
                {
                    finalprompt.Add(new Message(role: Role.Assistant, content: info, name: "prefix"));
                }
            }

            double temp = tempoverride >= 0 ? tempoverride : (LLMEngine.ForceTemperature >= 0) ? LLMEngine.ForceTemperature : LLMEngine.Sampler.Temperature;
            int? setseed = LLMEngine.Sampler.Sampler_seed != -1 ? LLMEngine.Sampler.Sampler_seed : LLMEngine.RNG.Next(int.MaxValue);

            if (LLMEngine.ToolCallsLoaded && _currentSchema is null)
            {
                var req = new ChatRequest(finalprompt,
                    tools: LLMEngine.ToolManager.GetToolList(),
                    model: LLMEngine.Settings.OpenAIModelID,
                    toolChoice: "auto",
                    topP: LLMEngine.Sampler.Top_p,
                    frequencyPenalty: DoFrequence ? LLMEngine.Sampler.Rep_pen - 1 : null,
                    seed: setseed,
                    user: LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt ? LLMEngine.User.Name : null,
                    stops: [.. LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot)],
                    responseFormat: TextResponseFormat.Auto,
                    parallelToolCalls: LLMEngine.Client?.SupportParallelToolCall ?? false,
                    maxTokens: responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride,
                    temperature: temp)
                {
                    chat_template_kwargs = new Dictionary<string, object>()
                        {
                            { "enable_thinking", !LLMEngine.Settings.DisableThinking }
                        }
                };
                req.ImportFromGenerationInput(LLMEngine.Sampler);
                return req;
            }
            else
            {
                var req = new ChatRequest(finalprompt,
                    topP: LLMEngine.Sampler.Top_p,
                    model: LLMEngine.Settings.OpenAIModelID,
                    frequencyPenalty: DoFrequence ? LLMEngine.Sampler.Rep_pen - 1 : null,
                    seed: setseed,
                    user: LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt ? LLMEngine.User.Name : null,
                    stops: [.. LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot)],
                    responseFormat: _currentSchema is not null ? TextResponseFormat.JsonSchema : TextResponseFormat.Auto,
                    jsonSchema: _currentSchema,
                    parallelToolCalls: LLMEngine.Client?.SupportParallelToolCall ?? false,
                    maxTokens: responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride,
                    temperature: temp);
                req.chat_template_kwargs = new Dictionary<string, object>();
                if (_currentSchema is not null)
                {
                    req.chat_template_kwargs["add_generation_prompt"] = false;
                    req.chat_template_kwargs["enable_thinking"] = false;
                    req.add_generation_prompt = false;
                }
                else
                {
                    req.chat_template_kwargs["enable_thinking"] = !LLMEngine.Settings.DisableThinking;
                    req.add_generation_prompt = null;
                }
                req.ImportFromGenerationInput(LLMEngine.Sampler);
                return req;
            }
        }

        /// <summary>
        /// Which counter a <see cref="FitToContext"/> run used. Warm-start data is only reused with the
        /// same counter, since the exact and estimate-based counts are not on the same scale.
        /// </summary>
        private enum TrimCountMode { Exact, Estimate }

        // Warm-start data from the last successful fit of this builder instance (the engine keeps one
        // builder for the whole chat, so this carries over from turn to turn). It is only a hint used to
        // place the first count; every returned prompt is still verified by a real count. Not reset by
        // Clear() on purpose.
        private bool _warmValid;
        private TrimCountMode _warmMode;
        private int _warmTotal;
        private int _warmEstimate;
        private double _warmSlope;

        /// <summary>
        /// Finds the prompt to send: <paramref name="source"/> with the fewest oldest messages removed
        /// (index 0, the system prompt, is always kept) whose count by <paramref name="counter"/> fits
        /// within <paramref name="max"/>.
        /// </summary>
        /// <remarks>
        /// <para>A cut <c>k</c> removes the <c>k</c> oldest messages after the system prompt, plus any tool
        /// result left orphaned at index 1 (some chat templates reject a leading tool message). Removing more
        /// messages never makes the prompt bigger, so the search keeps two bounds: the largest cut known to
        /// be over the limit and the smallest cut known to fit. It stops when they are adjacent (keeping one
        /// more message would not fit: the tightest result possible), or when the fitting cut leaves at most
        /// <see cref="LLMSettings.PromptTrimLeeway"/> tokens unused.</para>
        /// <para>Counts are expensive (a full template + tokenization round-trip on llama.cpp), so each next
        /// cut is predicted from cheap local per-message estimates, corrected by the ratio measured between
        /// real counts. The last fit of the previous turn places the first count, which usually lands on the
        /// answer directly. When a guess fails to halve the search range, the next one is a plain bisection,
        /// which bounds the number of counts to about twice log2 of the message count.</para>
        /// </remarks>
        /// <returns>The chosen prompt, its count, and the request the counter built for it (null when the counter builds none).</returns>
        private (List<SingleMessage> Prompt, int Total, ChatRequest? Request) FitToContext(
            List<SingleMessage> source, int max, TrimCountMode mode, Func<List<SingleMessage>, (int Total, ChatRequest? Request)> counter)
        {
            var n = source.Count;
            var leeway = Math.Clamp(LLMEngine.Settings.PromptTrimLeeway, 0, 512);
            // Predictions aim slightly under the limit so a guess tends to fit. This only places the counts:
            // the stop conditions alone decide how tight the result is.
            var target = max - (leeway > 0 ? leeway / 2 : 32);
            var watch = Stopwatch.StartNew();

            // prefix[k] = local estimate of the k oldest removable messages, estimateTotal = whole prompt.
            int[]? prefix = null;
            var estimateTotal = 0;
            void EnsureEstimates()
            {
                if (prefix is not null)
                    return;
                prefix = new int[n];
                estimateTotal = EstimateLocalTokens(source[0]);
                for (int i = 1; i < n; i++)
                {
                    var cost = EstimateLocalTokens(source[i]);
                    prefix[i] = prefix[i - 1] + cost;
                    estimateTotal += cost;
                }
            }

            int Normalize(int k)
            {
                if (k <= 0)
                    return 0;
                if (k > n - 1)
                    k = n - 1;
                while (1 + k < n && source[1 + k].Role == AuthorRole.Tool)
                    k++;
                return k;
            }

            List<SingleMessage> Materialize(int k)
            {
                if (k == 0)
                    return new List<SingleMessage>(source);
                var list = new List<SingleMessage>(n - k) { source[0] };
                list.AddRange(source.GetRange(1 + k, n - 1 - k));
                return list;
            }

            // Smallest cut whose removed estimate reaches the requested amount.
            int FindCut(double need)
            {
                if (need <= 0 || n <= 1)
                    return 0;
                if (prefix![n - 1] < need)
                    return n - 1;
                int a = 0, b = n - 1;
                while (a < b)
                {
                    var m = (a + b) / 2;
                    if (prefix[m] >= need)
                        b = m;
                    else
                        a = m + 1;
                }
                return a;
            }

            // Real tokens per estimated token between two counted cuts.
            double? Slope(int ka, int ta, int kb, int tb)
            {
                if (ka == kb)
                    return null;
                if (ka > kb)
                {
                    (ka, kb) = (kb, ka);
                    (ta, tb) = (tb, ta);
                }
                var removed = prefix![kb] - prefix[ka];
                if (removed <= 0)
                    return null;
                var s = (ta - tb) / (double)removed;
                if (double.IsNaN(s) || s <= 0)
                    return null;
                return Math.Clamp(s, 0.05, 20);
            }

            double? slope = null;
            var k = 0;
            if (_warmValid && _warmMode == mode && n > 1)
            {
                // Predict this prompt from the last fit: real ~ warmTotal + slope * (estimate - warmEstimate).
                EnsureEstimates();
                slope = _warmSlope;
                k = FindCut((estimateTotal - _warmEstimate) + ((_warmTotal - target) / _warmSlope));
            }

            int lo = -1, loTotal = 0;
            ChatRequest? loRequest = null;
            int hi = -1, hiTotal = 0;
            ChatRequest? hiRequest = null;
            int lastK = -1, lastTotal = 0, prevK = -1, prevTotal = 0;
            int lastStep = 0, overStreak = 0, fitStreak = 0;
            var prevWidth = int.MaxValue;
            var counts = 0;
            var maxCounts = 6 + (2 * (int)Math.Ceiling(Math.Log2(n + 1)));

            while (true)
            {
                k = Normalize(k);
                if (lastK >= 0)
                    lastStep = Math.Abs(k - lastK);
                var (total, request) = counter(Materialize(k));
                counts++;
                prevK = lastK;
                prevTotal = lastTotal;
                lastK = k;
                lastTotal = total;

                if (total <= max)
                {
                    if (hi < 0 || k < hi)
                    {
                        hi = k;
                        hiTotal = total;
                        hiRequest = request;
                    }
                    fitStreak = lo < 0 ? fitStreak + 1 : 0;
                    overStreak = 0;
                }
                else
                {
                    if (k > lo)
                    {
                        lo = k;
                        loTotal = total;
                        loRequest = request;
                    }
                    overStreak = hi < 0 ? overStreak + 1 : 0;
                    fitStreak = 0;
                }

                if (hi >= 0)
                {
                    if (hi == 0 || max - hiTotal <= leeway)
                        break;
                    // Adjacent bounds (or a non-monotonic count): no cut in between is left to try.
                    if (lo >= 0 && Normalize(lo + 1) >= hi)
                        break;
                }
                else if (lo >= n - 1)
                {
                    break;
                }
                if (counts >= maxCounts)
                {
                    // Safety net only. If nothing fits yet, the largest cut is the last thing worth trying.
                    if (hi < 0 && lo < n - 1)
                    {
                        k = n - 1;
                        continue;
                    }
                    break;
                }

                EnsureEstimates();
                if (lo >= 0 && hi >= 0)
                    slope = Slope(lo, loTotal, hi, hiTotal) ?? slope;
                else if (prevK >= 0)
                    slope = Slope(prevK, prevTotal, lastK, lastTotal) ?? slope;
                slope ??= Math.Clamp(lastTotal / (double)Math.Max(1, estimateTotal - prefix![lastK]), 0.05, 20);

                var next = FindCut(prefix![lastK] + ((lastTotal - target) / slope.Value));
                if (lo >= 0 && hi >= 0)
                {
                    var width = hi - lo;
                    if (width * 2 > prevWidth)
                        next = lo + (width / 2);
                    prevWidth = width;
                    next = Math.Clamp(next, lo + 1, hi - 1);
                    if (Normalize(next) >= hi)
                        next = lo + 1;
                }
                else if (hi < 0)
                {
                    // Still over everywhere: move forward, and at least double the step once guesses keep falling short.
                    var minStep = overStreak >= 2 ? Math.Max(1, lastStep * 2) : 1;
                    next = Math.Min(Math.Max(next, lo + minStep), n - 1);
                }
                else
                {
                    // Fits with too much room and nothing known to be over: move back, same doubling rule.
                    var minStep = fitStreak >= 2 ? Math.Max(1, lastStep * 2) : 1;
                    next = Math.Max(Math.Min(next, hi - minStep), 0);
                    // A cut landing in a run of tool results normalizes forward; step back until it is a new cut.
                    while (next > 0 && Normalize(next) >= hi)
                        next--;
                }
                k = next;
            }

            watch.Stop();
            var fits = hi >= 0;
            var finalK = fits ? hi : lo;
            var finalTotal = fits ? hiTotal : loTotal;
            var finalRequest = fits ? hiRequest : loRequest;

            if (fits)
            {
                EnsureEstimates();
                _warmValid = true;
                _warmMode = mode;
                _warmTotal = hiTotal;
                _warmEstimate = estimateTotal - prefix![hi];
                _warmSlope = slope ?? Math.Clamp(hiTotal / (double)Math.Max(1, _warmEstimate), 0.05, 20);
            }
            else
            {
                LLMEngine.Logger?.LogWarning("[PromptBuilder] The prompt does not fit in the context even after trimming: {Total} tokens for a limit of {Max}.", finalTotal, max);
            }

            if (finalK > 0 || counts > 1)
            {
                LLMEngine.Logger?.LogInformation("[PromptBuilder] Context fit ({Mode}): removed {Removed} of {Count} messages, {Total}/{Max} tokens, {Counts} count(s) in {Ms} ms.",
                    mode, finalK, n, finalTotal, max, counts, watch.ElapsedMilliseconds);
            }

            return (Materialize(finalK), finalTotal, finalRequest);
        }

        /// <summary>
        /// Cheap, local (no backend call) per-message token estimate. Only used to predict where to cut
        /// in <see cref="FitToContext"/>; the authoritative count is always the full-prompt count.
        /// </summary>
        private static int EstimateLocalTokens(SingleMessage message)
        {
            var text = message.ToTextCompletion();
            var total = TokenTools.CountTokens(text);

            if (message.Role == AuthorRole.Assistant && message.ToolCalls.Count > 0)
                total += TokenTools.CountTokens(message.ToolCallToString());

            // Structural per-message overhead (role headers/delimiters) the plain text count misses.
            return total + 8;
        }

        public void Clear()
        {
            LastQuery = null;
            _prompt.Clear();
        }

        public int GetTokenCount(AuthorRole role, string message)
        {
            return GetTokenCount(new SingleMessage(role, message));
        }

        public int GetTokenCount(SingleMessage message, bool countImages = true)
        {
            var total = LLMEngine.GetTokenCount(message.ToTextCompletion());

            if (LLMEngine.SupportsVision && countImages && message.HasSendableImage())
            {
                total += LLMEngine.Settings.ImageEmbeddingSize;
            }
            if (message.Role == AuthorRole.Assistant && message.ToolCalls.Count > 0)
            {
                total += LLMEngine.GetTokenCount(message.ToolCallToString());
            }
            return total;   
        }

        public string PromptToText()
        {
            var sb = new StringBuilder();
            foreach (var message in _prompt)
            {
                sb.Append(message.ToTextCompletion());
            }
            return sb.ToString();
        }

        public async Task SetStructuredOutput<ClassToConvert>()
        {
            _currentSchema = typeof(ClassToConvert);
            await Task.Delay(1).ConfigureAwait(false);
        }

        public async Task SetStructuredOutput(object classToConvert)
        {
            _currentSchema = classToConvert.GetType();
            await Task.Delay(1).ConfigureAwait(false);
        }

        public void UnsetStructuredOutput()
        {
            _currentSchema = null;
        }
    }
}
