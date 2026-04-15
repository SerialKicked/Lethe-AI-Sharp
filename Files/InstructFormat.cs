using CommunityToolkit.HighPerformance;
using LetheAISharp.LLM;
using Newtonsoft.Json;
using OpenAI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        [Description("BoS token, used by some models to indicate the beginning the whole prompt. Can be left empty, or handled by the backend server.")]
        public string BoSToken { get; set; } = string.Empty;
        
        #region ** Message Roles **

        /// <summary>
        /// User message start sequence. Inserted just before the user message.
        /// </summary>
        [Description("Prefix to user messages: inserted just before the user message.")]
        public string UserStart { get; set; } = string.Empty;

        /// <summary>
        /// User message end sequence. Inserted just after the user message.
        /// </summary>
        [Description("Suffix to user messages: inserted just after the user message.")]
        public string UserEnd { get; set; } = string.Empty;

        /// <summary>
        /// Bot message start sequence. Inserted just before the bot message.
        /// </summary>
        [Description("Prefix to bot messages: inserted just before the bot message.")]
        public string BotStart { get; set; } = string.Empty;

        /// <summary>
        /// Bot message end sequence. Inserted just after the bot message.
        /// </summary>
        [Description("Suffix to bot messages: inserted just after the bot message.")]
        public string BotEnd { get; set; } = string.Empty;

        /// <summary>
        /// Start sequence for the system messages. 
        /// </summary>
        [Description("Prefix to system prompt and messages: inserted just before the system prompt or message.")]
        public string SystemStart { get; set; } = string.Empty;

        /// <summary>
        /// End sequence for the system messages. 
        /// </summary>
        [Description("Suffix to system prompt and messages: inserted just after the system prompt or message.")]
        public string SystemEnd { get; set; } = string.Empty;

        #endregion


        #region ** Thinking / CoT related **

        /// <summary>
        /// Start sequence for the thinking prompt block. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        [Description("Start sequence for the thinking prompt block. Only relevant for CoT (thinking) models.")]
        public string ThinkingStart { get; set; } = string.Empty;

        /// <summary>
        /// End sequence for the thinking prompt block. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        [Description("End sequence for the thinking prompt block. Only relevant for CoT (thinking) models.")]
        public string ThinkingEnd { get; set; } = string.Empty;

        /// <summary>
        /// Tells whether an empty think block is prefilled to bot response when thinking is disabled (Like with Gemma4).
        /// </summary>
        [Description("Tells whether an empty think block is prefilled to bot response when thinking is disabled (Like with Gemma4).")]
        public bool RequireEmptyThinkBlockWhenThinkingDisabled { get; set; } = false;

        /// <summary>
        /// Force the thinking prompt to start with a specific thought. Only relevant for CoT (or so-called thinking) models.
        /// </summary>
        [Description("Force the thinking prompt to start with a specific thought. Only relevant for CoT (thinking) models in text completion mode. \n\n" +
            "Leave empty unless you know what you're doing.")]
        public string ThinkingForcedThought { get; set; } = string.Empty;

        /// <summary>
        /// Some badly trained CoT models need to have the thinking prompt prefilled to properly work. This toggle enables that.
        /// </summary>
        [Description("Some thinking models need to have the think token prefilled to have the feature enabled. This toggle enables that.\n\n" +
            "This is only relevant in text completion mode.")]
        public bool PrefillThinking { get; set; } = false;

        /// <summary>
        /// Gets or sets the prefix inserted into the chain-of-thought prompt to specify the persona for group chat
        /// scenarios.
        /// </summary>
        /// <remarks>This property is relevant only when using text completion models in group chat mode.
        /// It helps guide the model to roleplay as the intended persona within the generated
        /// chain-of-thought.</remarks>
        [Description("When in group chat, using a thinking / CoT model, this will be inserted inside the CoT to tell the model which persona it's meant to roleplay.\n\n" +
            "This is only relevant in text completion mode.")]
        public string GroupThinkingPrefix { get; set; } = string.Empty;

        /// <summary>
        /// If thinking is enabled (and this is a CoT model), this will be added at the very start of the system prompt. 
        /// </summary>
        [Description("If thinking is enabled (and this is a CoT model), this will be added at the very start of the system prompt.")]
        public string ThinkingSystemPromptPrefix { get; set; } = string.Empty;

        /// <summary>
        /// If thinking is enabled (and this is a CoT model), this will be added at the very end of the system prompt. 
        /// </summary>
        [Description("If thinking is enabled (and this is a CoT model), this will be added at the very end of the system prompt.")]
        public string ThinkingSystemPromptSuffix { get; set; } = string.Empty;

        #endregion


        /// <summary>
        /// Force the bot to end generation when encountering this sequence. Contrary to BotEnd, this one won't be added to the prompt.
        /// This is not a commonly used feature, and can usually be left empty.
        /// </summary>
        [Description("Force the bot to end generation when encountering this sequence. Contrary to BotEnd, this one won't be added to the prompt.\n\n" +
            "This is not a commonly used feature, and can usually be left empty.")]
        public string StopSequence { get; set; } = string.Empty;

        /// <summary>
        /// Some badly trained models may require additional stopping strings to properly end generation. This is where you do that.
        /// </summary>
        [Description("Some badly trained models may require additional stopping strings to properly end generation. This is where you do that.\n\n" +
            "Leave empty otherwise.")]
        public List<string> StopStrings { get; set; } = [];

        /// <summary>
        /// Insert a new line between messages in the prompt. Depends on the instruction format. Some models may like it, while others may not.
        /// </summary>
        [Description("Insert a new line between messages in the prompt. Depends on the instruction format. Some models may like it, while others may not.")]
        public bool NewLinesBetweenMessages { get; set; } = false;

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
        [Description("Overrides the entirety of the generated bot header with this post generation. This is useful for some modern CoT systems with multi channel formatting.\n\n" +
            "This will only impact the older messages, not the one being currently generated.")]
        public string BotStartOverride { get; set; } = string.Empty;

        /// <summary>
        /// Overrides the entirety of the generated bot footer with this post generation. This is useful for some modern CoT systems with multi channel formatting.
        /// This will only impact the older messages, not the one being currently generated. 
        /// </summary>
        [Description("Overrides the entirety of the generated bot footer with this post generation. This is useful for some modern CoT systems with multi channel formatting.\n\n" +
            "This will only impact the older messages, not the one being currently generated.")]
        public string BotEndOverride { get; set; } = string.Empty;

        /// <summary>
        /// Normally all Bot/User start and end tags are added to stop strings as "security", however this is not how some instruct format work. 
        /// Setting this to true disables the behavior
        /// </summary>
        [Description("Normally all Bot/User start and end tags are added to stop strings as \"security\", however this is not how some instruct format work.\n\n" +
            "Setting this to true disables the behavior. Leave to false unless you know what you're doing.")]
        public bool NoInstructInStopString { get; set; } = false;

        [JsonIgnore] internal bool RealAddNameToPrompt => LLMEngine.NamesInPromptOverride ?? LLMEngine.Settings.AddNamesToPrompt;
        [JsonIgnore] public bool IsThinkFormat => !string.IsNullOrEmpty(ThinkingEnd);

        public string GetThinkPrefill()
        {
            var res = string.Empty;
            if (IsThinkFormat && LLMEngine.Settings.DisableThinking)
            {
                if (RequireEmptyThinkBlockWhenThinkingDisabled)
                {
                    res = ThinkingStart + ThinkingEnd;
                }
                return res;
            }
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
            foreach (var item in StopStrings)
            {
                if (!string.IsNullOrWhiteSpace(item))
                    res.Add(item);
            }
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

        public bool UpdateSysPromptForThinking(SingleMessage sysPrompt)
        {
            if (!IsThinkFormat || LLMEngine.Settings.DisableThinking || sysPrompt.Role != AuthorRole.System)
                return false;
            if (string.IsNullOrWhiteSpace(ThinkingSystemPromptPrefix) && string.IsNullOrWhiteSpace(ThinkingSystemPromptSuffix))
                return false;
            if (!string.IsNullOrWhiteSpace(ThinkingSystemPromptPrefix) && sysPrompt.Message.StartsWith(ThinkingSystemPromptPrefix))
                return false;
            if (!string.IsNullOrWhiteSpace(ThinkingSystemPromptSuffix) && sysPrompt.Message.EndsWith(ThinkingSystemPromptSuffix))
                return false;

            var rawprompt = sysPrompt.Message;
            if (!string.IsNullOrWhiteSpace(ThinkingSystemPromptPrefix))
            {
                rawprompt = ThinkingSystemPromptPrefix + rawprompt;
            }
            if (!string.IsNullOrWhiteSpace(ThinkingSystemPromptSuffix))
            {
                rawprompt += Environment.NewLine + ThinkingSystemPromptSuffix;
            }
            sysPrompt.Message = rawprompt;
            return true;
        }
    }
}
