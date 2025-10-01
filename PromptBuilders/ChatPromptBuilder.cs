using LetheAISharp.Files;
using LetheAISharp.LLM;
using CommunityToolkit.HighPerformance;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenAI;

namespace LetheAISharp
{
    internal class ChatPromptBuilder : IPromptBuilder
    {
        private readonly List<Message> _prompt = [];
        private OpenAI.JsonSchema? _currentSchema = null;
        private List<string> imagefilepath = [];

        public int Count => _prompt.Count;

        public int AddMessage(AuthorRole role, string message)
        {
            var msg = FormatSingleMessage(role, LLMEngine.User, LLMEngine.Bot, message);
            _prompt.Add(msg);
            return LLMEngine.GetTokenCount(msg.Content.ToString()) + 4;
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
                total += LLMEngine.GetTokenCount(message.Content.ToString()) + 4;
            }
            return total;
        }

        public int InsertMessage(int index, AuthorRole role, string message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(role, message);
            }
            var msg = FormatSingleMessage(role, LLMEngine.User, LLMEngine.Bot, message);
            _prompt.Insert(index, msg);
            return LLMEngine.GetTokenCount(msg.Content.ToString()) + 4;
        }

        public object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null)
        {
            if (imagefilepath.Count > 0)
            {
                var lastusermsg = _prompt.LastOrDefault(m => m.Role == Role.User);
                if (lastusermsg is not null)
                {
                    var message = lastusermsg.Content.ToString();

                    var idx = _prompt.IndexOf(lastusermsg);

                    var ctx = new List<Content>
                    {
                        message
                    };
                    foreach (var item in imagefilepath)
                    {
                        // determine data:image/extension based on file extension
                        
                        
                        ctx.Add(new(ContentType.ImageUrl, $"data:image/gif;base64,{ImageUtils.ImageToBase64(item, 1024)!}"));
                    }
                    _prompt[idx] = new Message(lastusermsg.Role, ctx, lastusermsg.Name);
                }
            }

            var chatrq = new ChatRequest(_prompt,
                topP: LLMEngine.Sampler.Top_p,
                frequencyPenalty: LLMEngine.Sampler.Rep_pen - 1,
                seed: LLMEngine.Sampler.Sampler_seed != -1 ? LLMEngine.Sampler.Sampler_seed : null,
                user: LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt ? LLMEngine.User.Name : null,
                stops: [.. LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot)],
                responseFormat: _currentSchema is not null ? OpenAI.TextResponseFormat.JsonSchema : OpenAI.TextResponseFormat.Auto,
                jsonSchema: _currentSchema,

                maxTokens: responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride,
                temperature: tempoverride >= 0 ? tempoverride : (LLMEngine.ForceTemperature >= 0) ? LLMEngine.ForceTemperature : LLMEngine.Sampler.Temperature);

            return chatrq;
        }

        public void Clear()
        {
            _prompt.Clear();
        }

        public int GetTokenCount(AuthorRole role, string message)
        {
            var msg = FormatSingleMessage(role, LLMEngine.User, LLMEngine.Bot, message);
            return LLMEngine.GetTokenCount(msg.Content.ToString());
        }

        public string PromptToText()
        {
            var sb = new StringBuilder();
            foreach (var message in _prompt)
            {
                if (message.Role == OpenAI.Role.User)
                    sb.AppendLine(LLMEngine.User.Name + ": " + message.Content.ToString());
                else if (message.Role == OpenAI.Role.Assistant)
                    sb.AppendLine(LLMEngine.Bot.Name + ": " + message.Content.ToString());
                else
                    sb.AppendLine("SYSTEM" + ": " + message.Content.ToString());
            }
            return sb.ToString();
        }

        private static Message FormatSingleMessage(AuthorRole role, BasePersona user, BasePersona bot, string prompt)
        {
            var realprompt = prompt;
            var addname = LLMEngine.NamesInPromptOverride ?? LLMEngine.Instruct.AddNamesToPrompt;

            // In group conversations, ALWAYS add names so the LLM knows which persona is speaking
            if (bot is GroupPersona)
                addname = true;

            if (role != AuthorRole.Assistant && role != AuthorRole.User)
                addname = false;
            string? selname = null;
            if (addname)
            {
                if (role == AuthorRole.Assistant)
                {
                    // For group conversations, use the current bot's name
                    var actualBot = bot is GroupPersona groupPersona ?
                        (groupPersona.CurrentBot ?? groupPersona.BotPersonas.FirstOrDefault() ?? bot) : bot;
                    realprompt = string.Format("{0}: {1}", actualBot.Name, prompt);
                    selname = actualBot.Name;
                }
                else if (role == AuthorRole.User)
                {
                    realprompt = string.Format("{0}: {1}", user.Name, prompt);
                    selname = user.Name;
                }
            }
            return new Message(TokenTools.InternalRoleToChatRole(role), bot.ReplaceMacros(realprompt, user), selname);
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

        public void VLM_ClearImages()
        {
            imagefilepath.Clear();
        }

        public void VLM_AddImage(string imagePath, int size = 1024)
        {
            if (File.Exists(imagePath) && !imagefilepath.Contains(imagePath))
                imagefilepath.Add(imagePath);
        }

        public int VLM_GetImageCount() => imagefilepath.Count;
    }
}
