using AIToolkit.API;
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
            var msg = LLMSystem.Instruct.FormatSinglePrompt(role, LLMSystem.User, LLMSystem.Bot, message);
            var res = LLMSystem.GetTokenCount(msg);
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

        public object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1)
        {
            var fullquery = (string)GetFullPrompt();

            if (responserole == AuthorRole.User)
            {
                fullquery += LLMSystem.Instruct.GetUserStart(LLMSystem.User);
            }
            else
            {
                fullquery += LLMSystem.Instruct.GetResponseStart(LLMSystem.Bot);
            }
            fullquery = fullquery.TrimEnd();

            GenerationInput genparams = LLMSystem.Sampler.GetCopy();
            if (tempoverride >= 0)
                genparams.Temperature = tempoverride;
            else if (LLMSystem.ForceTemperature >= 0)
                genparams.Temperature = LLMSystem.ForceTemperature;
            genparams.Max_context_length = LLMSystem.MaxContextLength;
            genparams.Max_length = responseoverride == -1 ? LLMSystem.MaxReplyLength : responseoverride;
            genparams.Stop_sequence = LLMSystem.Instruct.GetStoppingStrings(LLMSystem.User, LLMSystem.Bot);
            genparams.Prompt = fullquery;
            genparams.Images = [.. LLMSystem.vlm_pictures];
            return genparams;
        }

        public int InsertMessage(int index, AuthorRole role, string message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(role, message);
            }

            var msg = LLMSystem.Instruct.FormatSinglePrompt(role, LLMSystem.User, LLMSystem.Bot, message);
            var res = LLMSystem.GetTokenCount(msg);
            _prompt.Insert(index, LLMSystem.Instruct.FormatSinglePrompt(role, LLMSystem.User, LLMSystem.Bot, message));
            return res;
        }

        public void ResetPrompt()
        {
            _prompt.Clear();
        }

        public int GetTokenUsage()
        {
            return LLMSystem.GetTokenCount((string)GetFullPrompt());
        }

        public int GetTokenCount(AuthorRole role, string message)
        {
            var msg = LLMSystem.Instruct.FormatSinglePrompt(role, LLMSystem.User, LLMSystem.Bot, message);
            return LLMSystem.GetTokenCount(msg);
        }

        public string PromptToText()
        {

            return (string)GetFullPrompt();
        }
    }
}
