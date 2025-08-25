using AIToolkit.LLM;
using CommunityToolkit.HighPerformance;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit
{
    internal class ChatPromptBuilder : IPromptBuilder
    {
        private readonly List<Message> _prompt = [];

        public int Count => _prompt.Count;

        public int AddMessage(AuthorRole role, string message)
        {
            var msg = LLMSystem.FormatSingleMessage(role, LLMSystem.User, LLMSystem.Bot, message);
            _prompt.Add(msg);
            return LLMSystem.GetTokenCount(msg.Content.ToString()) + 4;
        }

        public object GetFullPrompt()
        {
            return _prompt;
        }

        public int GetTokenUsage()
        {
            var total = 0;
            foreach (var message in _prompt)
            {
                total += LLMSystem.GetTokenCount(message.Content.ToString()) + 4;
            }
            return total;
        }

        public int InsertMessage(int index, AuthorRole role, string message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(role, message);
            }
            var msg = LLMSystem.FormatSingleMessage(role, LLMSystem.User, LLMSystem.Bot, message);
            _prompt.Insert(index, msg);
            return LLMSystem.GetTokenCount(msg.Content.ToString()) + 4;
        }

        public object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1)
        {
            var chatrq = new ChatRequest(_prompt,
                topP: LLMSystem.Sampler.Top_p,
                frequencyPenalty: LLMSystem.Sampler.Rep_pen - 1,
                seed: LLMSystem.Sampler.Sampler_seed != -1 ? LLMSystem.Sampler.Sampler_seed : null,
                user: LLMSystem.NamesInPromptOverride ?? LLMSystem.Instruct.AddNamesToPrompt ? LLMSystem.User.Name : null,
                stops: [.. LLMSystem.Instruct.GetStoppingStrings(LLMSystem.User, LLMSystem.Bot)],
                maxTokens: responseoverride == -1 ? LLMSystem.Settings.MaxReplyLength : responseoverride,
                temperature: tempoverride >= 0 ? tempoverride : (LLMSystem.ForceTemperature >= 0) ? LLMSystem.ForceTemperature : LLMSystem.Sampler.Temperature);
            return chatrq;
        }

        public void ResetPrompt()
        {
            _prompt.Clear();
        }

        public int GetTokenCount(AuthorRole role, string message)
        {
            var msg = LLMSystem.FormatSingleMessage(role, LLMSystem.User, LLMSystem.Bot, message);
            return LLMSystem.GetTokenCount(msg.Content.ToString());
        }

        public string PromptToText()
        {
            var sb = new StringBuilder();
            foreach (var message in _prompt)
            {
                if (message.Role == OpenAI.Role.User)
                    sb.AppendLine(LLMSystem.User.Name + ": " + message.Content.ToString());
                else if (message.Role == OpenAI.Role.Assistant)
                    sb.AppendLine(LLMSystem.Bot.Name + ": " + message.Content.ToString());
                else
                    sb.AppendLine("SYSTEM" + ": " + message.Content.ToString());
            }
            return sb.ToString();
        }
    }
}
