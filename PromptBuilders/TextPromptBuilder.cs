using CommunityToolkit.HighPerformance;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp
{
    internal class TextPromptBuilder : IPromptBuilder
    {
        private readonly List<string> _prompt = [];
        private string grammar = string.Empty;
        public int Count => _prompt.Count;

        public int AddMessage(AuthorRole role, string message)
        {
            var msg = LLMEngine.Instruct.FormatSinglePrompt(role, LLMEngine.User, LLMEngine.Bot, message);
            var res = LLMEngine.GetTokenCount(msg);
            _prompt.Add(msg);
            return res;
        }

        public object GetFullPrompt()
        {
            var fullprompt = new StringBuilder();
            foreach (var prompt in _prompt) 
            {
                fullprompt.Append(prompt);
            }
            return fullprompt.ToString();
        }

        public async Task SetStructuredOutput(object? classToConvert)
        {
            // Highest priority: extractable => let it provide grammar (handles caching/special cases)
            if (classToConvert is ILLMExtractableBase extract)
            {
                grammar = await extract.GetGrammar().ConfigureAwait(false);
                return;
            }

            // If a Type representing a class was provided
            Type? targetType = classToConvert as Type;
            if (targetType is null && classToConvert is not null)
            {
                var rt = classToConvert.GetType();
                if (rt.IsClass) targetType = rt;
            }

            if (targetType is not null && targetType.IsClass)
            {
                grammar = await InvokeEngineGetGrammarForType(targetType).ConfigureAwait(false);
                return;
            }

            // Fallback: nothing to set
            grammar = string.Empty;
        }

        // Keep interface compatibility without adding another public method
        public async Task SetStructuredOutput<ClassToConvert>()
        {
            // This blocks intentionally to respect the IPromptBuilder signature
            await SetStructuredOutput(typeof(ClassToConvert));
        }

        private static async Task<string> InvokeEngineGetGrammarForType(Type type)
        {
            var mi = typeof(LLMEngine).GetMethod(nameof(LLMEngine.GetGrammar), BindingFlags.Public | BindingFlags.Static);
            if (mi == null) return string.Empty;

            var generic = mi.MakeGenericMethod(type);
            var task = (Task<string>)generic.Invoke(null, null)!;
            return await task.ConfigureAwait(false);
        }

        public void UnsetStructuredOutput()
        {
            grammar = string.Empty;
        }


        public object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null)
        {
            var fullquery = (string)GetFullPrompt();

            if (responserole == AuthorRole.User)
            {
                fullquery += LLMEngine.Instruct.GetResponseStart(LLMEngine.User, overridePrefill);
            }
            else
            {
                fullquery += LLMEngine.Instruct.GetResponseStart(LLMEngine.Bot, overridePrefill);
            }
            fullquery = fullquery.TrimEnd();

            GenerationInput genparams = LLMEngine.Sampler.GetCopy();
            if (tempoverride >= 0)
                genparams.Temperature = tempoverride;
            else if (LLMEngine.ForceTemperature >= 0)
                genparams.Temperature = LLMEngine.ForceTemperature;
            genparams.Max_context_length = LLMEngine.MaxContextLength;
            genparams.Max_length = responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride;
            genparams.Stop_sequence = LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot);
            genparams.Prompt = fullquery;
            genparams.Images = [.. LLMEngine.vlm_pictures];
            if (!string.IsNullOrWhiteSpace(grammar))
                genparams.Grammar = grammar;
            return genparams;
        }

        public int InsertMessage(int index, AuthorRole role, string message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(role, message);
            }

            var msg = LLMEngine.Instruct.FormatSinglePrompt(role, LLMEngine.User, LLMEngine.Bot, message);
            var res = LLMEngine.GetTokenCount(msg);
            _prompt.Insert(index, LLMEngine.Instruct.FormatSinglePrompt(role, LLMEngine.User, LLMEngine.Bot, message));
            return res;
        }

        public void Clear()
        {
            _prompt.Clear();
        }

        public int GetTokenUsage()
        {
            return LLMEngine.GetTokenCount((string)GetFullPrompt());
        }

        public int GetTokenCount(AuthorRole role, string message)
        {
            var msg = LLMEngine.Instruct.FormatSinglePrompt(role, LLMEngine.User, LLMEngine.Bot, message);
            return LLMEngine.GetTokenCount(msg);
        }

        public string PromptToText()
        {

            return (string)GetFullPrompt();
        }
    }
}
