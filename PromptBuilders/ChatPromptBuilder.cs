using LetheAISharp.Agent.Tools;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.LLM;
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
            if (LLMEngine.ToolCallsLoaded)
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

        private string GetResponseStart(BasePersona talker, bool? overridePrefill = null)
        {
            var addname = LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt;
            var res = string.Empty;
            if (addname && (!LLMEngine.Instruct.IsThinkFormat || LLMEngine.Settings.DisableThinking))
                res += talker.Name + ":";
            if (talker.IsUser)
                return res;
            var doprefill = overridePrefill ?? LLMEngine.Instruct.PrefillThinking;
            if (doprefill)
            {
                res += LLMEngine.Instruct.GetThinkPrefill();
                if (addname)
                {
                    res += $" It's {talker.Name} turn to talk.";
                }
            }
            return res;
        }

        public object PromptToQuery(AuthorRole responserole = AuthorRole.Assistant, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null, bool forceAltRoles = false)
        {
            // Let's make sure we don't overshoot token limits.
            var workingprompt = new List<SingleMessage>(_prompt);
            var total = GetTokenUsage(workingprompt);
            var max = LLMEngine.MaxContextLength - (responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride) - 15;
            while (total > max && workingprompt.Count > 1)
            {
                workingprompt.RemoveAt(1);
                total = GetTokenUsage(workingprompt);
            }

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

            var prefill = overridePrefill ?? LLMEngine.Instruct.PrefillThinking;
            if (LLMEngine.Client?.AllowPrefill == false)
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

            var dooverride = (LLMEngine.Client is LlamaCppAdapter) && LLMEngine.Settings.BackendLLamaCppAllowAllSamplers;
            double? temp = tempoverride >= 0 ? tempoverride : (LLMEngine.ForceTemperature >= 0) ? LLMEngine.ForceTemperature : LLMEngine.Sampler.Temperature;
            int? setseed = LLMEngine.Sampler.Sampler_seed != -1 ? LLMEngine.Sampler.Sampler_seed : null;
            if (dooverride)
            {
                temp = null;
                setseed = null;
            }

            if (LLMEngine.ToolCallsLoaded && _currentSchema is null)
            {
                var req = new ChatRequest(finalprompt,
                    tools: LLMEngine.ToolManager.GetToolList(),
                    toolChoice: "auto",
                    topP: dooverride ? null : LLMEngine.Sampler.Top_p,
                    frequencyPenalty: dooverride ? null : LLMEngine.Sampler.Rep_pen - 1,
                    seed: setseed,
                    user: LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt ? LLMEngine.User.Name : null,
                    stops: [.. LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot)],
                    responseFormat: TextResponseFormat.Auto,
                    parallelToolCalls: LLMEngine.Client?.SupportParallelToolCall ?? false,
                    maxTokens: responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride,
                    temperature: temp);

                req.chat_template_kwargs = new Dictionary<string, object>()
                    {
                        { "enable_thinking", !LLMEngine.Settings.DisableThinking }
                    };
                return req;
            }
            else
            {
                var req = new ChatRequest(finalprompt,
                    topP: dooverride ? null : LLMEngine.Sampler.Top_p,
                    frequencyPenalty: dooverride ? null : LLMEngine.Sampler.Rep_pen - 1,
                    seed: setseed,
                    user: LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt ? LLMEngine.User.Name : null,
                    stops: [.. LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot)],
                    responseFormat: _currentSchema is not null ? TextResponseFormat.JsonSchema : TextResponseFormat.Auto,
                    jsonSchema: _currentSchema,
                    parallelToolCalls: LLMEngine.Client?.SupportParallelToolCall ?? false,
                    maxTokens: responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride,
                    temperature: temp);
                req.chat_template_kwargs = new Dictionary<string, object>()
                    {
                        { "enable_thinking", !LLMEngine.Settings.DisableThinking }
                    };
                return req;
            }

        }

        public void Clear()
        {
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
