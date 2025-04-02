using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIToolkit.LLM;

namespace AIToolkit.Files
{
    public class InstructFormat : BaseFile
    {
        public static readonly string[] Properties = [
            "SystemPrompt",
            "SysPromptStart", "SysPromptEnd",
            "SystemStart", "SystemEnd",
            "UserStart", "UserEnd",
            "BotStart", "BotEnd",
            "BoSToken", "StopSequence",
            "ThinkingStart", "ThinkingEnd",
            "ThinkingForcedThought",
            "PrefillThinking",
            "ForceRAGToThinkingPrompt",
            "AddNamesToPrompt",
            "NewLinesBetweenMessages",
            "StopStrings"
            ];
        public string BoSToken { get; set; } = string.Empty;
        public string SystemStart { get; set; } = string.Empty;
        public string SystemEnd { get; set; } = string.Empty;
        public string UserStart { get; set; } = string.Empty;
        public string UserEnd { get; set; } = string.Empty;
        public string BotStart { get; set; } = string.Empty;
        public string BotEnd { get; set; } = string.Empty;
        public string StopSequence { get; set; } = string.Empty;
        public string SysPromptStart { get; set; } = string.Empty;
        public string SysPromptEnd { get; set; } = string.Empty;
        public bool AddNamesToPrompt { get; set; } = true;
        public bool NewLinesBetweenMessages { get; set; } = false;
        public List<string> StopStrings { get; set; } = [];
        public string ThinkingStart { get; set; } = string.Empty;
        public string ThinkingEnd { get; set; } = string.Empty;
        public string ThinkingForcedThought { get; set; } = string.Empty;
        public bool PrefillThinking { get; set; } = false;
        public bool ForceRAGToThinkingPrompt { get; set; } = false;

        [JsonIgnore] private bool RealAddNameToPrompt => LLMSystem.NamesInPromptOverride ?? AddNamesToPrompt;

        public string GetResponseStart(BasePersona bot)
        {
            var res = LLMSystem.ReplaceMacros(BotStart);
            if (RealAddNameToPrompt)
                res += bot.Name + ":";
            if (PrefillThinking)
            {
                res += ThinkingStart;
                if (!string.IsNullOrWhiteSpace(ThinkingForcedThought))
                    res += ThinkingForcedThought;
            }
            return res;
        }

        public string GetUserStart(BasePersona user)
        {
            var res = LLMSystem.ReplaceMacros(UserStart);
            if (RealAddNameToPrompt)
                res += user.Name + ":";
            return res;
        }

        public string FormatSinglePromptNoUserInfo(AuthorRole role, string userName, BasePersona bot, string prompt)
        {
            var realprompt = prompt;
            if (RealAddNameToPrompt)
            {
                if (role == AuthorRole.Assistant)
                    realprompt = string.Format("{0}: {1}", bot.Name, prompt);
                else if (role == AuthorRole.User)
                    realprompt = string.Format("{0}: {1}", userName, prompt);
            }
            switch (role)
            {
                case AuthorRole.Unknown:
                    realprompt = "[" + LLMSystem.ReplaceMacros(realprompt, userName, bot) + "]";
                    break;
                case AuthorRole.System:
                    realprompt =  LLMSystem.ReplaceMacros(SystemStart + realprompt + SystemEnd, userName, bot) ;
                    break;
                case AuthorRole.User:
                    realprompt = LLMSystem.ReplaceMacros(UserStart + realprompt + UserEnd, userName, bot);
                    break;
                case AuthorRole.Assistant:
                    realprompt = LLMSystem.ReplaceMacros(BotStart + realprompt + BotEnd, userName, bot);
                    break;
                case AuthorRole.SysPrompt:
                    realprompt = LLMSystem.ReplaceMacros(SysPromptStart + realprompt + SysPromptEnd, userName, bot);
                    break;
                default:
                    break;
            }
            if (NewLinesBetweenMessages)
                realprompt += LLMSystem.NewLine;
            return realprompt;
        }

        public string FormatSinglePrompt(AuthorRole role, BasePersona user, BasePersona bot, string prompt)
        {
            var realprompt = prompt;
            if (RealAddNameToPrompt)
            {
                if (role == AuthorRole.Assistant)
                    realprompt = string.Format("{0}: {1}", bot.Name, prompt);
                else if (role == AuthorRole.User)
                    realprompt = string.Format("{0}: {1}", user.Name, prompt);
            }
            switch (role)
            {
                case AuthorRole.Unknown:
                    realprompt = "[" + LLMSystem.ReplaceMacros(realprompt, user, bot) + "]";
                    break;
                case AuthorRole.System:
                    realprompt = LLMSystem.ReplaceMacros(SystemStart + realprompt + SystemEnd, user, bot);
                    break;
                case AuthorRole.User:
                    realprompt = LLMSystem.ReplaceMacros(UserStart + realprompt + UserEnd, user, bot);
                    break;
                case AuthorRole.Assistant:
                    realprompt = LLMSystem.ReplaceMacros(BotStart + realprompt + BotEnd, user, bot);
                    break;
                case AuthorRole.SysPrompt:
                    realprompt = LLMSystem.ReplaceMacros(SysPromptStart + realprompt + SysPromptEnd, user, bot);
                    break;
                default:
                    break;
            }
            if (NewLinesBetweenMessages)
                realprompt += LLMSystem.NewLine;
            return realprompt;
        }

        public string FormatSingleMessage(SingleMessage message)
        {
            return FormatSinglePrompt(message.Role, message.User, message.Bot, message.Message);
        }

        public List<string> GetStoppingStrings(BasePersona user, BasePersona bot)
        {
            var res = new List<string>() { LLMSystem.NewLine + user.Name + ":", LLMSystem.NewLine + bot.Name + ":" };

            if (!string.IsNullOrEmpty(BotStart))
                res.Add(BotStart);
            if (!string.IsNullOrEmpty(BotEnd))
                res.Add(BotEnd);
            if (!string.IsNullOrEmpty(SystemStart))
                res.Add(SystemStart);
            if (!string.IsNullOrEmpty(SystemEnd))
                res.Add(SystemEnd);
            if (!string.IsNullOrEmpty(UserStart))
                res.Add(UserStart);
            if (!string.IsNullOrEmpty(UserEnd))
                res.Add(UserEnd);
            if (!string.IsNullOrEmpty(StopSequence))
                res.Add(StopSequence);
            res.AddRange(StopStrings);
            if (LLMSystem.StopGenerationOnFirstParagraph)
                res.Add(LLMSystem.NewLine);

            // Remove duplicates from the list
            res = [.. res.Distinct()];

            return res;
        }

        public bool IsThinkingPrompt(string prompt)
        {
            if (string.IsNullOrEmpty(ThinkingStart) || string.IsNullOrEmpty(ThinkingEnd) || string.IsNullOrEmpty(prompt))
                return false;
            return prompt.Contains(LLMSystem.Instruct.ThinkingStart) && !prompt.Contains(LLMSystem.Instruct.ThinkingEnd);
        }
    }
}
