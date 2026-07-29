using CommunityToolkit.HighPerformance;
using LetheAISharp.API;
using LetheAISharp.Files;
using LetheAISharp.LLM;
using LLama.Native;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static LetheAISharp.Files.Tests.GbnfConverterTest;

namespace LetheAISharp
{
    internal class TextPromptBuilder : IPromptBuilder
    {
        private List<string> vlm_pictures = [];
        private readonly List<SingleMessage> _prompt = [];
        private string grammar = string.Empty;

        public int Count => _prompt.Count;

        public object? LastQuery { get; set; }

        public int AddMessage(AuthorRole role, string message)
        {
            return AddMessage(new SingleMessage(role, message));
        }

        public int AddMessage(SingleMessage message)
        {
            if (message.Role == AuthorRole.Tool || (message.Role == AuthorRole.Assistant && message.ToolCalls?.Count > 0 && string.IsNullOrWhiteSpace(message.Message)))
                return 1;
            _prompt.Add(message);

            if (_prompt.Count == 1 && message.Role == AuthorRole.System)
            {
                var testbuddy = message.Clone();
                var modified = LLMEngine.Instruct.UpdateSysPromptForThinking(testbuddy);
                if (modified)
                    return LLMEngine.GetTokenCount(testbuddy.ToTextCompletion());
            }
            return LLMEngine.GetTokenCount(message.ToTextCompletion());
        }

        public object GetFullPrompt()
        {
            var fullprompt = new StringBuilder();
            for (int i = 0; i < _prompt.Count; i++)
            {
                var prompt = _prompt[i];
                if (i == 0 && prompt.Role == AuthorRole.System)
                {
                    var testbuddy = prompt.Clone();
                    var modified = LLMEngine.Instruct.UpdateSysPromptForThinking(testbuddy);
                    if (modified)
                    {
                        fullprompt.Append(testbuddy.ToTextCompletion());
                        continue;
                    }
                }
                fullprompt.Append(prompt.ToTextCompletion());
            }

            return fullprompt.ToString();
        }

        public async Task SetStructuredOutput(object classToConvert)
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

        public object? RegenLastQuery()
        {
            if (LastQuery is null || LastQuery is not GenerationInput req)
                return null;
            GenerationInput genparams = LLMEngine.Sampler.GetCopy();
            genparams.Temperature = (LLMEngine.ForceTemperature >= 0) ? LLMEngine.ForceTemperature : LLMEngine.Sampler.Temperature;
            genparams.Max_context_length = req.Max_context_length;
            genparams.Max_length = req.Max_length;
            genparams.Stop_sequence = req.Stop_sequence;
            genparams.Prompt = req.Prompt;
            genparams.Images = req.Images;
            genparams.Grammar =req.Grammar;
            return genparams;
        }


        public object PromptToQuery(AuthorRole responserole = AuthorRole.Assistant, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null, bool forceAltRoles = false)
        {
            var think = LLMEngine.Settings.DisableThinking;
            if (!string.IsNullOrWhiteSpace(grammar))
            {
                LLMEngine.Settings.DisableThinking = true;
                LLMEngine.NamesInPromptOverride = false;
            }

            string fullquery;
            if (!forceAltRoles)
            {
                fullquery = (string)GetFullPrompt();
            }
            else 
            {
                // Use alternate roles for group conversations so it needs to end with User if responserole is Assistant
                var fullprompt = new StringBuilder();
                var currentrole = AuthorRole.User; // responserole == AuthorRole.Assistant ? AuthorRole.User : AuthorRole.Assistant;
                // let's go in reverse to flip roles
                for (int i = _prompt.Count - 1; i >= 0; i--)
                {
                    var prompt = _prompt[i];
                    // System prompts are always added as-is
                    if (prompt.Role == AuthorRole.System)
                    {
                        // if it's the system prompt (first one)
                        if (i == 0)
                        {
                            var testbuddy = prompt.Clone();
                            if (LLMEngine.Instruct.UpdateSysPromptForThinking(testbuddy))
                            {
                                fullprompt.Insert(0, testbuddy.ToTextCompletion());
                                continue;
                            }
                        }
                        fullprompt.Insert(0, prompt.ToTextCompletion());
                        continue;
                    }
                    var roleToUse = currentrole;
                    
                    var userID = prompt.UserID;
                    var charID = prompt.CharID;

                    // If the original message was Assistant but we are flipping roles, we need to swap user and bot personas
                    if (prompt.Role == AuthorRole.Assistant && roleToUse == AuthorRole.User)
                    {
                        userID = prompt.CharID;
                        charID = prompt.UserID;
                    }
                    else if (prompt.Role == AuthorRole.User && roleToUse == AuthorRole.Assistant)
                    {
                        userID = prompt.CharID;
                        charID = prompt.UserID;
                    }
                    fullprompt.Insert(0, new SingleMessage(roleToUse, DateTime.Now, prompt.Message, charID, userID).ToTextCompletion());
                    // flip role for next message
                    currentrole = currentrole == AuthorRole.User ? AuthorRole.Assistant : AuthorRole.User;
                }
                fullquery = fullprompt.ToString();
            }

            if (responserole == AuthorRole.User)
            {
                if (!forceAltRoles)
                    fullquery += LLMEngine.Instruct.GetResponseStart(LLMEngine.User, overridePrefill);
                else
                {
                    LLMEngine.Settings.DisableThinking = true;
                    LLMEngine.NamesInPromptOverride = true;
                    fullquery += LLMEngine.Instruct.GetResponseStart(LLMEngine.User, overridePrefill, true);
                }
            }
            else
            {
                fullquery += LLMEngine.Instruct.GetResponseStart(LLMEngine.Bot, overridePrefill);
            }

            vlm_pictures = [];
            if (LLMEngine.Client?.SupportsVision ?? false)
            {
                var left = LLMEngine.Settings.MaxImageCount == 0 ? int.MaxValue : LLMEngine.Settings.MaxImageCount;
                for (int i = _prompt.Count - 1; i >= 0; i--)
                {
                    if (_prompt[i].Role == AuthorRole.User && _prompt[i].ImagePaths.Count > 0 && _prompt[i].ImagePaths.Any(File.Exists))
                    {
                        var res = ImageUtils.ImageToBase64(_prompt[i].ImagePaths.First(File.Exists), LLMEngine.Settings.ImageResolution);
                        if (res is not null)
                        {
                            vlm_pictures.Insert(0, res);
                            left--;
                            if (left <= 0)
                                break;
                        }
                    }
                }
            }
            if (!LLMEngine.Settings.BackendHandlesBoSToken && !string.IsNullOrWhiteSpace(LLMEngine.Instruct.BoSToken))
            {
                fullquery = LLMEngine.Instruct.BoSToken + fullquery;
            }

            GenerationInput genparams = LLMEngine.Sampler.GetCopy();
            if (tempoverride >= 0)
                genparams.Temperature = tempoverride;
            else if (LLMEngine.ForceTemperature >= 0)
                genparams.Temperature = LLMEngine.ForceTemperature;
            genparams.Max_context_length = LLMEngine.MaxContextLength;
            genparams.Max_length = responseoverride == -1 ? LLMEngine.Settings.MaxReplyLength : responseoverride;
            genparams.Stop_sequence = LLMEngine.Instruct.GetStoppingStrings(LLMEngine.User, LLMEngine.Bot);
            genparams.Prompt = fullquery;
            genparams.Images = [.. vlm_pictures];
            if (!string.IsNullOrWhiteSpace(grammar))
                genparams.Grammar = grammar;

            LLMEngine.Settings.DisableThinking = think;
            LLMEngine.NamesInPromptOverride = null;

            return genparams;
        }

        public int InsertMessage(int index, AuthorRole role, string message)
        {
            return InsertMessage(index, new SingleMessage(role, message));
        }

        public int InsertMessage(int index, SingleMessage message)
        {
            if (index == _prompt.Count)
            {
                return AddMessage(message);
            }
            if (index > _prompt.Count)
                return -1;
            if (message.Role == AuthorRole.Tool || (message.Role == AuthorRole.Assistant && message.ToolCalls?.Count > 0 && string.IsNullOrWhiteSpace(message.Message)))
                return 1;

            _prompt.Insert(index, message);
            return LLMEngine.GetTokenCount(message.ToTextCompletion());
        }

        public void Clear()
        {
            LastQuery = null;
            _prompt.Clear();
        }

        public int GetTokenUsage()
        {
            return LLMEngine.GetTokenCount((string)GetFullPrompt()) + vlm_pictures.Count * LLMEngine.Settings.ImageEmbeddingSize;
        }

        public int GetTokenCount(AuthorRole role, string message) => GetTokenCount(new SingleMessage(role, message), false);

        public int GetTokenCount(SingleMessage message, bool countImages = true)
        {
            if (message.Role == AuthorRole.Tool || (message.Role == AuthorRole.Assistant && message.ToolCalls?.Count > 0 && string.IsNullOrWhiteSpace(message.Message)))
                return 1;

            var realmessage = message.ToTextCompletion();

            if (string.IsNullOrEmpty(realmessage))
                return 0;
            else if (LLMEngine.Client == null || LLMEngine.Status != SystemStatus.Ready || realmessage.Length > LLMEngine.MaxContextLength * 8)
                return TokenTools.CountTokens(realmessage);

            try
            {
                return LLMEngine.GetTokenCount(realmessage);
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "Failed to count tokens. Falling back to failsafe");
                return TokenTools.CountTokens(realmessage);
            }
        }

        public string PromptToText()
        {
            return (string)GetFullPrompt();
        }
    }
}
