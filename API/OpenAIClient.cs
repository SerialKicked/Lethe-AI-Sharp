using LetheAISharp.LLM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LetheAISharp.API
{

    public class OpenTokenResponse
    {
        public string Token { get; set; } = string.Empty;
        public string? FinishReason { get; set; } = string.Empty;
    }   

    public class OpenAI_APIClient
    {
        private OpenAIClient API { get; set; }
        private HttpClient _httpClient { get; set; }

        public event EventHandler<OpenTokenResponse>? StreamingMessageReceived;
        private void RaiseOnStreamingResponse(OpenTokenResponse e) => StreamingMessageReceived?.Invoke(this, e);

        public OpenAI_APIClient(HttpClient httpclient)
        {
            _httpClient = httpclient;
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.Timeout = TimeSpan.FromMinutes(2);
            var settings = new OpenAISettings(LLMEngine.Settings.BackendUrl);
            API = new OpenAIClient(new OpenAIAuthentication("123"), settings, _httpClient);
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
            var nostopfix = true; // some backends don't return "stop" at the end of completion. It handles this case.
            try
            {
                await foreach (var partialResponse in API.ChatEndpoint.StreamCompletionEnumerableAsync(request, cancellationToken: cancellationToken))
                {
                    foreach (var choice in partialResponse.Choices.Where(choice => choice.Delta?.Content != null))
                    {
                        cumulativeDelta += choice.Delta.Content;
                        if (!string.IsNullOrEmpty(choice.FinishReason))
                        {
                            nostopfix = false;
                        }
                        RaiseOnStreamingResponse(new OpenTokenResponse
                        {
                            Token = choice.Delta.Content,
                            FinishReason = choice.FinishReason
                        });
                    }
                }
                if (nostopfix)
                {
                    RaiseOnStreamingResponse(new OpenTokenResponse
                    {
                        Token = "",
                        FinishReason = "stop"
                    });
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                RaiseOnStreamingResponse(new OpenTokenResponse
                {
                    Token = $" [Error Streaming Message: {ex.Message}] " + LLMEngine.NewLine + LLMEngine.NewLine + "This is likely an issue with the Jinja chat template used by this model. It might not support some of w(AI)fu's features or it can just be incorrect." + LLMEngine.NewLine + LLMEngine.NewLine + "You can either:" + LLMEngine.NewLine + "- Edit the Jinja chat template in your backend." + LLMEngine.NewLine + "- Use a different model." + LLMEngine.NewLine + "- Use a text completion backend like KoboldCpp.",
                    FinishReason = "error"
                });
            }

            // CA2254 fix: Use a constant message template and pass cumulativeDelta as an argument
            LLMEngine.Logger?.LogInformation("[OpenAI API] Final response: {CumulativeDelta}", cumulativeDelta);
        }

        public virtual async Task<Choice> ChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var response = await API.ChatEndpoint.GetCompletionAsync(request, cancellationToken).ConfigureAwait(false);
            return response.FirstChoice;
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
