using LetheAISharp.LLM;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using OpenAI.Responses;
using OpenAI.Threads;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.API
{

    public class OpenTokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public string? FinishReason { get; set; } = string.Empty;
        /// <summary>
        /// Tool call records accumulated during any tool-calling rounds that preceded this completion.
        /// Populated only on the final completion event (when FinishReason is non-empty).
        /// </summary>
        public List<ToolCallRecord>? ToolCallRecords { get; set; }
        /// <summary>
        /// Chain-of-thought / reasoning content delivered in a separate streaming field
        /// (the <c>reasoning_content</c> JSON property emitted by llama.cpp / vLLM and other
        /// OpenAI-compatible backends). When non-empty, the engine routes it directly to
        /// the thinking channel rather than the visible response channel.
        /// </summary>
        public string? ReasoningToken { get; set; }
    }

    public class OpenAI_APIClient : HttpClientBase
    {
        protected OpenAIClient API { get; set; }

        public int NetworkTimeoutSeconds
        {
            get => (int)_httpClient!.Timeout.TotalSeconds;
            set => _httpClient!.Timeout = TimeSpan.FromSeconds(value);
        }

        public event EventHandler<OpenTokenResponse>? StreamingMessageReceived;
        protected void RaiseOnStreamingResponse(OpenTokenResponse e) => StreamingMessageReceived?.Invoke(this, e);

        public OpenAI_APIClient(HttpClient httpclient)
        {
            _httpClient = httpclient;
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            var settings = new OpenAISettings(LLMEngine.Settings.BackendUrl);
            API = new OpenAIClient(new OpenAIAuthentication(LLMEngine.Settings.OpenAIKey), settings, _httpClient);
        }

        public virtual async Task<List<Model>> GetModelList()
        {
            var models = await API.ModelsEndpoint.GetModelsAsync().ConfigureAwait(false);
            var lst = new List<Model>(models);
            return lst;
        }

        public virtual async Task<Model> GetModelInfo(string? model = null)
        {
            if (model is null)
            {
                var models = await API.ModelsEndpoint.GetModelsAsync().ConfigureAwait(false);
                var lst = new List<Model>(models);
                if (lst.Count == 0)
                {
                    throw new Exception("No models found in the backend.");
                }
                return lst[0];
            }
            var info = await API.ModelsEndpoint.GetModelDetailsAsync(model).ConfigureAwait(false);
            return info;
        }

        public virtual async Task<string> GetBackendInfo()
        {
            return await Task.FromResult("OpenAI Compatible Backend").ConfigureAwait(false);
        }

        public virtual async Task StreamChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var cumulativeDelta = string.Empty;
            //var nostopfix = true; // some backends don't return "stop" at the end of completion. It handles this case.
            var toolCallRecords = new List<ToolCallRecord>();
            var toolRound = 0;
            int maxToolRounds = LLMEngine.Settings.ToolCallLimit <= 0 ? int.MaxValue : LLMEngine.Settings.ToolCallLimit;
            var currentRequest = request;
            try
            {
                bool continueLoop = true;
                while (continueLoop)
                {
                    continueLoop = false;
                    await foreach (var partialResponse in API.ChatEndpoint.StreamCompletionEnumerableAsync(currentRequest, cancellationToken: cancellationToken))
                    {
                        // Flush any reasoning content that arrived in the final assistant Message
                        // (typically seen together with tool_calls) so the engine can capture it
                        // in the thinking channel before we mutate the request and re-issue.
                        var pendingReasoning = partialResponse.FirstChoice.Message?.ReasoningContent;
                        if (!string.IsNullOrEmpty(pendingReasoning))
                        {
                            RaiseOnStreamingResponse(new OpenTokenResponse
                            {
                                Token = string.Empty,
                                ReasoningToken = pendingReasoning
                            });
                        }

                        // Handle tool_calls
                        if (partialResponse.FirstChoice.FinishReason == "tool_calls" && partialResponse.FirstChoice.Message?.ToolCalls != null)
                        {
                            var toolmsgs = new List<OpenAI.Chat.Message>();
                            foreach (var toolcall in partialResponse.FirstChoice.Message.ToolCalls)
                            {
                                string functionResult;
                                bool success;
                                var sw = Stopwatch.StartNew();
                                try
                                {
                                    var allowed = true;
                                    if (LLMEngine.ToolCallConfirmation != null && (LLMEngine.ToolManager.RequiresConfirmation(toolcall.Function?.Name ?? string.Empty) || LLMEngine.Settings.ToolCallsAlwaysManualConfirm))
                                    {
                                        // that's an UI call, so no ConfigureAwait(false) here, otherwise we might crash things at the UI level
                                        allowed = await LLMEngine.ToolCallConfirmation(toolcall.Function?.Name ?? string.Empty, toolcall.Function?.Arguments?.ToJsonString() ?? string.Empty);
                                    }
                                    if (toolRound >= maxToolRounds)
                                    {
                                        functionResult = "Max amount of tool calls reached. You must finish your thoughts and produce the response now.";
                                        success = true;
                                    }
                                    else if (!allowed)
                                    {
                                        functionResult = "This tool call was denied by the user.";
                                        success = false;
                                    }
                                    else
                                    {
                                        functionResult = (await toolcall.InvokeFunctionAsync<string>(cancellationToken)) ?? string.Empty;
                                        success = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    functionResult = $"Error: {ex.Message}";
                                    success = false;
                                }
                                sw.Stop();
                                LLMEngine.Logger?.LogInformation("[OpenAI API] Tool Call: {Name}", toolcall.Function?.Name);
                                toolCallRecords?.Add(new ToolCallRecord
                                {
                                    CallId = toolcall.Id ?? string.Empty,
                                    FunctionName = toolcall.Function?.Name ?? string.Empty,
                                    ArgumentsJson = toolcall.Function?.Arguments?.ToJsonString() ?? string.Empty,
                                    ResultJson = success ? functionResult : string.Empty,
                                    Error = success ? null : functionResult,
                                    Success = success,
                                    Duration = sw.Elapsed
                                });
                                toolmsgs.Add(new OpenAI.Chat.Message(toolcall, functionResult));
                            }

                            // Build updated message list: original messages + assistant tool-call message + tool results.
                            // The assistant message is included as-is (thinking/CoT content, if any, is preserved
                            // since Message properties are not publicly mutable in this library version).
                            var toadd = new List<OpenAI.Chat.Message>()
                            {
                                partialResponse.FirstChoice.Message
                            };
                            toadd.AddRange(toolmsgs);
                            // Only evict old messages when we actually approach the context budget; otherwise let
                            // the conversation grow. A generous margin absorbs the vagueness of the fast estimate.
                            var trimMargin = Math.Max(LLMEngine.MaxContextLength / 8, 512);
                            var maxContextTokens = LLMEngine.MaxContextLength - LLMEngine.Settings.MaxReplyLength - trimMargin;
                            var updatedMessages = TokenTools.TrimToolContextIfNeeded(currentRequest.Messages, toadd, LLMEngine.Instruct, maxContextTokens);
                            currentRequest.Messages = updatedMessages;
                            currentRequest.ToolChoice = toolRound < maxToolRounds ? "auto" : "none";
                            if (toolRound >= maxToolRounds)
                                currentRequest.Tools = null;
                            LLMEngine.StreamingTextProgress.Clear();
                            // keep this here, otherwise the model doesn't receive a warning that tool calls have ended, and it'll imagine them instead.
                            toolRound++;
                            // --
                            continueLoop = true;
                            break; // exit foreach — re-enter while loop (or stop if limit reached)
                        }
                        else if (partialResponse.FirstChoice.Delta?.Content != null || !string.IsNullOrEmpty(partialResponse.FirstChoice.Delta?.ReasoningContent))
                        {
                            var delta = partialResponse.FirstChoice.Delta;
                            var hasFinishReason = !string.IsNullOrEmpty(partialResponse.FirstChoice.FinishReason);
                            var finishReason = partialResponse.FirstChoice.FinishReason;
                            var toolRecords = hasFinishReason && toolCallRecords?.Count > 0 ? toolCallRecords : null;

                            // Reasoning (thinking) content from the separate reasoning_content field.
                            // Routed directly to the engine's thinking channel — does NOT pass through
                            // the inline-tag splitter.
                            if (!string.IsNullOrEmpty(delta.ReasoningContent))
                            {
                                RaiseOnStreamingResponse(new OpenTokenResponse
                                {
                                    Token = string.Empty,
                                    ReasoningToken = delta.ReasoningContent,
                                    FinishReason = finishReason,
                                    ToolCallRecords = toolRecords
                                });
                            }

                            // Visible content from the content field.
                            if (!string.IsNullOrEmpty(delta.Content))
                            {
                                cumulativeDelta += delta.Content;
                                RaiseOnStreamingResponse(new OpenTokenResponse
                                {
                                    Token = delta.Content,
                                    FinishReason = finishReason,
                                    ToolCallRecords = toolRecords
                                });
                            }

                            if (hasFinishReason && (finishReason == "stop" || finishReason == "length"))
                                break;
                        }
                        else if (!string.IsNullOrEmpty(partialResponse.FirstChoice.FinishReason) && partialResponse.FirstChoice.FinishReason != "null")
                        {
                            if (partialResponse.FirstChoice.FinishReason != "tool_calls" || toolCallRecords?.Count > 0)
                            {
                                RaiseOnStreamingResponse(new OpenTokenResponse
                                {
                                    Token = "",
                                    FinishReason = partialResponse.FirstChoice.FinishReason,
                                    ToolCallRecords = toolCallRecords?.Count > 0 ? toolCallRecords : null
                                });
                                if (partialResponse.FirstChoice.FinishReason == "tool_calls" && toolCallRecords?.Count > 0)
                                {
                                    toolCallRecords.Clear();
                                }
                            }
                            if (partialResponse.FirstChoice.FinishReason == "stop" || partialResponse.FirstChoice.FinishReason == "length")
                                break;
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $" [Error Streaming Message: {ex.Message}] " + LLMEngine.NewLine + LLMEngine.NewLine + "This is likely an issue with the Jinja chat template used by this model. It might not support some of Lethe AI features (namely system messages in the prompt) or it can just be incorrect." + LLMEngine.NewLine + LLMEngine.NewLine + "You can either:" + LLMEngine.NewLine + "- Edit the Jinja chat template in your backend." + LLMEngine.NewLine + "- Use a different model." + LLMEngine.NewLine + "- Use a text completion backend like KoboldCpp.",
                    FinishReason = "error"
                });
            }
            catch (OperationCanceledException ex)
            {
                LLMEngine.Logger?.LogInformation(ex, "[OpenAI API] Message stream stopped by user: {Message}", ex.Message);
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $"The response was manually interrupted or cancelled. ({ex.Message})",
                    FinishReason = "error"
                });
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[OpenAI API] Error during streaming chat completion: {Message}", ex.Message);
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $"Error during streaming chat completion: {ex.Message}",
                    FinishReason = "error"
                });
            }

            // CA2254 fix: Use a constant message template and pass cumulativeDelta as an argument
            LLMEngine.Logger?.LogInformation("[OpenAI API] Final response: {CumulativeDelta}", cumulativeDelta);
        }

        public virtual async Task<Choice?> ChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await API.ChatEndpoint.GetCompletionAsync(request, cancellationToken).ConfigureAwait(false);
                return response?.FirstChoice;
            }
            catch (TaskCanceledException)
            {
                LLMEngine.Logger?.LogInformation("[OpenAI API] ChatCompletion Canceled");
                return null;
            }
            catch (Exception ex)
            {
                LLMEngine.Logger?.LogError(ex, "[OpenAI API] Error during chat completion: {Message}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Estimates the number of tokens in a string using a character-based approximation.
        /// </summary>
        /// <param name="text">Text to count tokens for</param>
        /// <returns>Estimated token count</returns>
        public virtual int CountTokens(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : TokenTools.CountTokens(text);
        }
    }
}
