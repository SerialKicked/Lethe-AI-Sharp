using AIToolkit.LLM;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit.Files
{
    /// <summary>
    /// Represents a configurable instruction formatting system for constructing prompts and messages used in
    /// conversational AI models. This class is relevant for Text Completion backends (KoboldAPI) models, while 
    /// Chat Completetion backends (OpenAI) handles the formatting internally.
    /// </summary>
    /// <remarks>This class provides properties and methods to define the structure of prompts for instruction models (all models, nowadays), 
    /// and messages exchanged between users, assistants, and the system. It supports customization of message delimiters, 
    /// thinking prompts, and stopping sequences, among other features.
    /// </remarks>
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

        /// <summary>
        /// BoS token, used by some models to indicate the beginning the whole prompt. Can usually be left empty.
        /// </summary>
        public string BoSToken { get; set; } = string.Empty;

        /// <summary>
        /// User message start sequence. Inserted just before the user message.
        /// </summary>
        public string UserStart { get; set; } = string.Empty;

        /// <summary>
        /// User message end sequence. Inserted just after the user message.
        /// </summary>
        public string UserEnd { get; set; } = string.Empty;

        /// <summary>
        /// Bot message start sequence. Inserted just before the bot message.
        /// </summary>
        public string BotStart { get; set; } = string.Empty;

        /// <summary>
        /// Bot message end sequence. Inserted just after the bot message.
        /// </summary>
        public string BotEnd { get; set; } = string.Empty;

        /// <summary>
        /// Force the bot to end generation when encountering this sequence. Contrary to BotEnd, this one won't be added to the prompt.
        /// This is not a commonly used feature, and can usually be left empty.
        /// </summary>
        public string StopSequence { get; set; } = string.Empty;

        /// <summary>
        /// Start sequence for the main system prompt. Inserted just before the prompt's content.
        /// </summary>
        public string SysPromptStart { get; set; } = string.Empty;

        /// <summary>
        /// End sequence for the main system prompt. Inserted just after the prompt's content.
        /// /summary>
        public string SysPromptEnd { get; set; } = string.Empty;

        /// <summary>
        /// Start sequence for the system messages that are inserted in the chatlog. 
        /// Outside of rare models with weird instruction formats, this should usually be the same as SysPromptStart.
        /// </summary>
        public string SystemStart { get; set; } = string.Empty;
        
        /// <summary>
        /// End sequence for the system messages that are inserted in the chatlog. 
        /// Outside of rare models with weird instruction formats, this should usually be the same as SysPromptEnd.
        /// </summary>
        public string SystemEnd { get; set; } = string.Empty;

        /// <summary>
        /// Toggle to add the user and bot names before their respective messages in the prompt. May help some models with role recognition.
        /// </summary>
        public bool AddNamesToPrompt { get; set; } = true;

        /// <summary>
        /// Insert a new line between messages in the prompt. Depends on the instruction format. Some models may like it, while others may not.
        /// </summary>
        public bool NewLinesBetweenMessages { get; set; } = false;

        /// <summary>
        /// Some badly trained models may require additional stopping strings to properly end generation. This is where you do that.
        /// </summary>
        public List<string> StopStrings { get; set; } = [];

        /// <summary>
        /// Start sequence for the thinking prompt block. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        public string ThinkingStart { get; set; } = string.Empty;

        /// <summary>
        /// End sequence for the thinking prompt block. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        public string ThinkingEnd { get; set; } = string.Empty;

        /// <summary>
        /// Force the thinking prompt to start with a specific thought. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        public string ThinkingForcedThought { get; set; } = string.Empty;

        /// <summary>
        /// Some badly trained CoT models need to have the thinking prompt prefilled to properly work. This toggle enables that.
        /// </summary>
        public bool PrefillThinking { get; set; } = false;

        /// <summary>
        /// Attempt to insert the RAG entries in the thinking prompt instead of the main prompt. Only relevant for CoT (or so-called thinking) models.
        /// Highly experimental.
        /// </summary>
        public bool ForceRAGToThinkingPrompt { get; set; } = false;

        [JsonIgnore] private bool RealAddNameToPrompt => LLMSystem.NamesInPromptOverride ?? AddNamesToPrompt;

        public string GetThinkPrefill()
        {
            var res = string.Empty;
            if (PrefillThinking && !string.IsNullOrEmpty(ThinkingStart))
            {
                res = ThinkingStart;
                if (!string.IsNullOrWhiteSpace(ThinkingForcedThought))
                    res += LLMSystem.ReplaceMacros(ThinkingForcedThought);

                if (LLMSystem.Settings.RAGMoveToThinkBlock && LLMSystem.dataInserts.Count > 0)
                {
                    if (!res.EndsWith(LLMSystem.NewLine))
                        res += LLMSystem.NewLine;

                    if (LLMSystem.Settings.DisableThinking)
                    {
                        // Better formatting to make it easier to read as it won't interfere with the thinking process
                        res += LLMSystem.NewLine + "The following information might be relevant to the conversation:" + LLMSystem.NewLine;
                        foreach (var insert in LLMSystem.dataInserts)
                        {
                            if (insert?.Location > -1)
                            {
                                res += "- " + LLMSystem.ReplaceMacros(insert.Content).RemoveNewLines().CleanupAndTrim() + LLMSystem.NewLine;
                            }
                        }
                        res += LLMSystem.NewLine;
                    }
                    else
                    {
                        // Raw information in paragraphs to mimick thinking, making it easier for the bot to continue from there.
                        foreach (var insert in LLMSystem.dataInserts)
                        {
                            if (insert?.Location > -1)
                            {
                                res += LLMSystem.ReplaceMacros(insert.Content).RemoveNewLines().CleanupAndTrim() + LLMSystem.NewLine + LLMSystem.NewLine;
                            }
                        }
                    }

                }
                if (LLMSystem.Settings.DisableThinking)
                    res += LLMSystem.NewLine + ThinkingEnd + LLMSystem.NewLine;
            }
            return res;
        }

        public string GetResponseStart(BasePersona bot)
        {
            var res = LLMSystem.ReplaceMacros(BotStart);
            if (RealAddNameToPrompt)
                res += bot.Name + ":";
            if (PrefillThinking)
                res += GetThinkPrefill();
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

            // In group conversations, ALWAYS add names so the LLM knows which persona is speaking
            var addname = (bot is GroupPersona) ? true : RealAddNameToPrompt;

            if (addname)
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
            var res = string.IsNullOrEmpty(ThinkingStart) ? [LLMSystem.NewLine + user.Name + ":", LLMSystem.NewLine + bot.Name + ":"] : new List<string>();

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
            if (LLMSystem.Settings.StopGenerationOnFirstParagraph)
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
