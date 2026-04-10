using CommunityToolkit.HighPerformance;
using LetheAISharp.LLM;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.Files
{
    /// <summary>
    /// Represents a configurable instruction formatting system for constructing prompts and messages used in
    /// conversational AI models. This class is mostly relevant for Text Completion backends (KoboldAPI). While 
    /// Chat completion backends (OpenAI, llama.cpp) handle the formatting internally, using the correct format
    /// will ensure better token counting accuracy and better compatibility with the model.
    /// </summary>
    /// <remarks>
    /// Functions in this class are set as public for testing and experimentation purpose.
    /// However the end user should rely on the <see cref="IPromptBuilder"/> to generate correct prompts.
    /// use: LLMEngine.GetPromptBuilder() to get a backend agnostic prompt builder.
    /// </remarks>
    public class InstructFormat : BaseFile
    {
        public static readonly string[] Properties = [
            "SystemPrompt",
            "SystemStart", "SystemEnd",
            "UserStart", "UserEnd",
            "BotStart", "BotEnd",
            "BotStartOverride", "BotEndOverride",
            "BoSToken", "StopSequence",
            "ThinkingStart", "ThinkingEnd",
            "ThinkingForcedThought",
            "PrefillThinking",
            "ForceRAGToThinkingPrompt",
            "NewLinesBetweenMessages",
            "NoInstructInStopString",
            "StopStrings", "GroupThinkingPrefix"
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
        /// Start sequence for the system messages. 
        /// </summary>
        public string SystemStart { get; set; } = string.Empty;
        
        /// <summary>
        /// End sequence for the system messages. 
        /// </summary>
        public string SystemEnd { get; set; } = string.Empty;

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

        /// <summary>
        /// Overrides the entirety of the generated bot header with this post generation. This is useful for some modern CoT systems with multi channel formatting.
        /// This will only impact the older messages, not the one being currently generated. 
        /// </summary>
        // Example: 
        // with GPT OSS The bot generates the following output (including thinkinking)
        // <|start|>assistant<|channel|>analysis<|message|>{thinking block}<|end|><|start|>assistant<|channel|>final<|message|>{actual message} {{EOS_Token}}
        // we don't want to store all that as is, this is where BotStartOverride and BotEndOverride come into play, it'll store the actual message between those tags instead
        // [BotStartOverride]{actual message}[BotEndOverride]
        // <|start|>assistant<|channel|>final<|message|>{actual message}<|end|>
        public string BotStartOverride { get; set; } = string.Empty;

        /// <summary>
        /// Overrides the entirety of the generated bot footer with this post generation. This is useful for some modern CoT systems with multi channel formatting.
        /// This will only impact the older messages, not the one being currently generated. 
        /// </summary>
        public string BotEndOverride { get; set; } = string.Empty;

        /// <summary>
        /// Normally all Bot/User start and end tags are added to stop strings as "security", however this is not how some instruct format work. 
        /// Setting this to true disables the behavior
        /// </summary>
        public bool NoInstructInStopString { get; set; } = false;

        public string GroupThinkingPrefix { get; set; } = string.Empty;

        [JsonIgnore] internal bool RealAddNameToPrompt => LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt;
        [JsonIgnore] public bool IsThinkFormat => !string.IsNullOrEmpty(ThinkingEnd);

        public string GetThinkPrefill()
        {
            var res = string.Empty;

            if (PrefillThinking && !string.IsNullOrEmpty(ThinkingStart) && (LLMEngine.Settings.BackendChatAllowPrefill ?? LLMEngine.Client?.AllowPrefill == true))
            {
                res = ThinkingStart;
                if (!string.IsNullOrWhiteSpace(ThinkingForcedThought))
                    res += LLMEngine.Bot.ReplaceMacros(ThinkingForcedThought);

                if (LLMEngine.IsGroupConversation && !string.IsNullOrEmpty(GroupThinkingPrefix) && LLMEngine.Settings.GroupChatInfoThinkingBlock && !LLMEngine.Settings.DisableThinking)
                {
                    res += LLMEngine.Bot.ReplaceMacros(GroupThinkingPrefix);
                }

                if (LLMEngine.Settings.RAGMoveToThinkBlock && LLMEngine.dataInserts.Count > 0)
                {
                    if (!res.EndsWith(LLMEngine.NewLine))
                        res += LLMEngine.NewLine;

                    if (LLMEngine.Settings.DisableThinking)
                    {
                        // Better formatting to make it easier to read as it won't interfere with the thinking process
                        res += LLMEngine.NewLine + "The following information might be relevant to the conversation:" + LLMEngine.NewLine;
                        foreach (var insert in LLMEngine.dataInserts)
                        {
                            if (insert?.Location > -1)
                            {
                                res += "- " + LLMEngine.Bot.ReplaceMacros(insert.ToContent()).RemoveNewLines().CleanupAndTrim() + LLMEngine.NewLine;
                            }
                        }
                        res += LLMEngine.NewLine;
                    }
                    else
                    {
                        // Raw information in paragraphs to mimick thinking, making it easier for the bot to continue from there.
                        foreach (var insert in LLMEngine.dataInserts)
                        {
                            if (insert?.Location > -1)
                            {
                                res += LLMEngine.Bot.ReplaceMacros(insert.ToContent()).RemoveNewLines().CleanupAndTrim() + LLMEngine.NewLine + LLMEngine.NewLine;
                            }
                        }
                    }

                }
                if (LLMEngine.Settings.DisableThinking)
                    res += LLMEngine.NewLine + ThinkingEnd + LLMEngine.NewLine;
            }
            return res;
        }

        public string GetResponseStart(BasePersona talker, bool? overridePrefill = null)
        {
            var doprefill = overridePrefill ?? PrefillThinking;
            if (talker.IsUser)
            {
                var userres = talker.ReplaceMacros(UserStart);
                if (RealAddNameToPrompt)
                    userres += talker.Name + ":";
                return userres;
            }
            var res = talker.ReplaceMacros(BotStart);
            if (LLMEngine.Settings.DisableThinking && doprefill && RealAddNameToPrompt && IsThinkFormat)
            {
                res += GetThinkPrefill();
                res += talker.Name + ":";
                return res;
            }
            if ((RealAddNameToPrompt && (!IsThinkFormat || LLMEngine.Settings.DisableThinking)) || 
                (LLMEngine.NamesInPromptBotOnlyOverride == true && !talker.IsUser))
                res += talker.Name + ":";
            if (doprefill)
                res += GetThinkPrefill();
            return res;
        }

        public string FormatSingleMessage(SingleMessage message)
        {
            var realprompt = message.Message;
            if (message.Role == AuthorRole.Assistant && message.ToolCalls?.Count > 0 && string.IsNullOrEmpty(message.Message))
            {
                realprompt = message.ToolCallToString();
            }
            else if ((LLMEngine.Bot is GroupPersonaBase) || RealAddNameToPrompt || message.Bot != LLMEngine.Bot || message.User != LLMEngine.User)
            {
                if (message.Role == AuthorRole.Assistant)
                    realprompt = string.Format("{0}: {1}", message.Bot.Name, message.Message);
                else if (message.Role == AuthorRole.User)
                    realprompt = string.Format("{0}: {1}", message.User.Name, message.Message);
            }
            switch (message.Role)
            {
                case AuthorRole.Unknown:
                    realprompt = "[" + message.Bot.ReplaceMacros(realprompt, message.User) + "]";
                    break;
                case AuthorRole.System:
                case AuthorRole.SysPrompt:
                    realprompt = message.Bot.ReplaceMacros(SystemStart + realprompt + SystemEnd, message.User);
                    break;
                case AuthorRole.User:
                    realprompt = message.Bot.ReplaceMacros(UserStart + realprompt + UserEnd, message.User);
                    break;
                case AuthorRole.Assistant:
                    var start = string.IsNullOrEmpty(BotStartOverride) ? BotStart : BotStartOverride;
                    var end = string.IsNullOrEmpty(BotEndOverride) ? BotEnd : BotEndOverride;
                    realprompt = message.Bot.ReplaceMacros(start + realprompt + end, message.User);
                    break;
                default:
                    break;
            }
            if (NewLinesBetweenMessages)
                realprompt += LLMEngine.NewLine;
            return realprompt;
        }

        public List<string> GetStoppingStrings(BasePersona user, BasePersona bot)
        {
            var res = string.IsNullOrEmpty(ThinkingStart) ? [LLMEngine.NewLine + user.Name + ":", LLMEngine.NewLine + bot.Name + ":"] : new List<string>();

            //if (!string.IsNullOrEmpty(BotStart))
            //    res.Add(BotStart);
            if (!string.IsNullOrEmpty(BotEnd) && !NoInstructInStopString)
                res.Add(BotEnd);
            if (!string.IsNullOrEmpty(SystemStart) && !NoInstructInStopString)
                res.Add(SystemStart);
            if (!string.IsNullOrEmpty(SystemEnd) && !NoInstructInStopString)
                res.Add(SystemEnd);
            if (!string.IsNullOrEmpty(UserStart) && !NoInstructInStopString)
                res.Add(UserStart);
            if (!string.IsNullOrEmpty(UserEnd) && !NoInstructInStopString)
                res.Add(UserEnd);
            if (!string.IsNullOrEmpty(StopSequence))
                res.Add(StopSequence);
            res.AddRange(StopStrings);
            if (LLMEngine.Settings.StopGenerationOnFirstParagraph)
                res.Add(LLMEngine.NewLine);

            // Remove duplicates from the list
            res = [.. res.Distinct()];
            if (IsThinkFormat)
            {
                if (!string.IsNullOrEmpty(ThinkingEnd) && (ThinkingEnd == BotEnd || ThinkingEnd == UserEnd))
                {
                    res.Remove(ThinkingEnd);
                }
            }


            return res;
        }

        public bool IsThinkingPrompt(string prompt)
        {
            if (string.IsNullOrEmpty(ThinkingStart) || string.IsNullOrEmpty(ThinkingEnd) || string.IsNullOrEmpty(prompt))
                return false;
            return prompt.Contains(LLMEngine.Instruct.ThinkingStart) && !prompt.Contains(LLMEngine.Instruct.ThinkingEnd);
        }
    }
}
