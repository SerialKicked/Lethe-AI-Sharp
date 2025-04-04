using AIToolkit.LLM;
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

namespace AIToolkit.API
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
            var settings = new OpenAIClientSettings(LLMSystem.BackendUrl);
            API = new OpenAIClient(new OpenAIAuthentication("123"), settings, _httpClient);
        }

        public virtual async Task<List<Model>> GetModelList()
        {
            var models = await API.ModelsEndpoint.GetModelsAsync();
            var lst = new List<Model>(models);
            return lst;
        }

        public virtual async Task<Model> GetModelInfo(string model)
        {
            var models = await API.ModelsEndpoint.GetModelsAsync();
            var lst = new List<Model>(models);
            var info = await API.ModelsEndpoint.GetModelDetailsAsync(lst[0]);
            return info;
        }

        public virtual async Task<string> GetBackendInfo()
        {
            return "OpenAI Compatible Backend";
        }

        public virtual async Task StreamChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var cumulativeDelta = string.Empty;
            await foreach (var partialResponse in API.ChatEndpoint.StreamCompletionEnumerableAsync(request, cancellationToken: cancellationToken))
            {
                foreach (var choice in partialResponse.Choices.Where(choice => choice.Delta?.Content != null))
                {
                    cumulativeDelta += choice.Delta.Content;
                    RaiseOnStreamingResponse(new OpenTokenResponse
                    {
                        Token = choice.Delta.Content,
                        FinishReason = choice.FinishReason
                    });
                }
            }
            LLMSystem.Logger?.LogInformation($"[OpenAI API] Final response: {cumulativeDelta}");
        }

        public virtual async Task<Choice> ChatCompletion(ChatRequest request, CancellationToken cancellationToken = default)
        {
            var response = await API.ChatEndpoint.GetCompletionAsync(request);
            return response.FirstChoice;
        }

        /// <summary>
        /// Estimates the number of tokens in a string using a character-based approximation.
        /// </summary>
        /// <param name="text">Text to count tokens for</param>
        /// <returns>Estimated token count</returns>
        public virtual int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return text.Length / 4;
        }
    }
}
