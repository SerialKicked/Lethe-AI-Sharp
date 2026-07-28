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
                var imgcnt = messages.Where(m => !string.IsNullOrEmpty(m.ImagePath) && File.Exists(m.ImagePath)).ToList().Count;
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
                        var request = BuildChatRequest(workingprompt, tempoverride, responseoverride, overridePrefill);
                        var total = exactClient.CountRequestTokensSync(request);
                        var maxIterations = workingprompt.Count + 4;
                        var iteration = 0;
                        while (total > max && workingprompt.Count > 1)
                        {
                            RemoveTrimBatch(workingprompt, total - max);
                            request = BuildChatRequest(workingprompt, tempoverride, responseoverride, overridePrefill);
                            total = exactClient.CountRequestTokensSync(request);
                            if (++iteration > maxIterations)
                                break;
                        }
                        return request;
                    }
                    catch (Exception ex)
                    {
                        // The endpoint-missing case already disabled the capability inside the adapter
                        // (and logged why); anything else (a payload the server rejects, a transient
                        // failure) falls back to the estimate-based path for this query only.
                        LLMEngine.Logger?.LogWarning(ex, "[PromptBuilder] Exact request token count failed; falling back to estimate-based trimming.");
                    }
                }

                var esttotal = GetTokenUsage(workingprompt);
                TrimToFit(workingprompt, ref esttotal, max);
                return BuildChatRequest(workingprompt, tempoverride, responseoverride, overridePrefill);
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
        /// Trims the oldest messages (preserving index 0, the system prompt) until the accurate token
        /// usage of <paramref name="workingprompt"/> fits within <paramref name="max"/>.
        /// </summary>
        /// <remarks>
        /// The naive approach re-counts the full prompt after every single removal, which is extremely
        /// slow on backends whose token-counting is an HTTP round-trip (e.g. llama.cpp). Instead we use a
        /// cheap local per-message estimate to decide how many messages to drop in one batch, then perform
        /// a single accurate re-count to verify. The batch is intentionally conservative (under-drops) so
        /// that we never trim more context than necessary; if the estimate was too optimistic we simply
        /// loop again, which converges in one or two accurate counts instead of N.
        /// </remarks>
        private void TrimToFit(List<SingleMessage> workingprompt, ref int total, int max)
        {
            // Fast path: nothing to do.
            if (total <= max || workingprompt.Count <= 1)
                return;

            // Guard against pathological loops (e.g. a single message that is itself larger than max).
            var safety = 0;
            while (total > max && workingprompt.Count > 1)
            {
                RemoveTrimBatch(workingprompt, total - max);

                // One accurate count to verify the batch. If we under-dropped, the outer loop runs again.
                total = GetTokenUsage(workingprompt);

                if (++safety > workingprompt.Count + 4)
                    break;
            }
        }

        /// <summary>
        /// Removes one batch of the oldest messages from <paramref name="workingprompt"/> (preserving
        /// index 0, the system prompt, and never removing the newest message) whose combined cheap local
        /// estimate covers <paramref name="excess"/> tokens. The batch deliberately under-drops: any
        /// shortfall is caught by the accurate re-count the caller performs afterwards. Also drops any
        /// orphaned tool-result message exposed at index 1, so backends whose chat templates reject a
        /// leading tool message do not error out.
        /// </summary>
        private static void RemoveTrimBatch(List<SingleMessage> workingprompt, int excess)
        {
            // Walk from the oldest removable message (index 1) forward, accumulating cheap local
            // estimates until we've covered the excess. We deliberately stop as soon as the estimate
            // meets the excess (conservative / under-drop) rather than padding it, because any
            // shortfall is caught by the accurate re-count the caller performs afterwards.
            var removeCount = 0;
            var estRemoved = 0;
            for (int i = 1; i < workingprompt.Count && estRemoved < excess; i++)
            {
                estRemoved += EstimateLocalTokens(workingprompt[i]);
                removeCount++;
            }

            // Always remove at least one message so we make progress even if the local estimate is 0.
            if (removeCount == 0)
                removeCount = 1;

            // Never remove the final (newest) message; keep at least the system prompt + one message.
            var maxRemovable = workingprompt.Count - 2;
            if (maxRemovable < 1)
                maxRemovable = 1;
            if (removeCount > maxRemovable)
                removeCount = maxRemovable;

            workingprompt.RemoveRange(1, removeCount);

            // Guard against leaving an orphaned tool-result message as the first post-system message.
            while (workingprompt.Count > 1 && workingprompt[1].Role == AuthorRole.Tool)
            {
                workingprompt.RemoveAt(1);
            }
        }

        /// <summary>
        /// Cheap, local (no backend call) per-message token estimate used only to decide how many
        /// messages to drop in a single trim batch. This is intentionally an over-estimate per message so
        /// the batch tends to be conservative; the authoritative count is always the full-prompt count.
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

            if (LLMEngine.SupportsVision && countImages && !string.IsNullOrEmpty(message.ImagePath) && File.Exists(message.ImagePath))
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
