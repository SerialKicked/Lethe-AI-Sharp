using AIToolkit.API;
using AIToolkit.Files;
using AIToolkit.LLM;
using CommunityToolkit.HighPerformance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit
{
    internal class TextPromptBuilder : IPromptBuilder
    {
        private readonly List<string> _prompt = [];
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
