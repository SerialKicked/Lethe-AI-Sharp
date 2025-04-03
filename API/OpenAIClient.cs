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

        public event EventHandler<OpenTokenResponse>? StreamingMessageReceived;
        private void RaiseOnStreamingResponse(OpenTokenResponse e) => StreamingMessageReceived?.Invoke(this, e);

        public OpenAI_APIClient()
        {
            var settings = new OpenAIClientSettings(LLMSystem.BackendUrl);
            API = new OpenAIClient(null, settings);
        }

        public virtual async Task<List<Model>> GetModelList()
        {
            var models = await API.ModelsEndpoint.GetModelsAsync();
            var lst = new List<Model>(models);
            return lst;
        }

        public virtual async Task<Model> GetModelInfo(string model)
        {
            var info = await API.ModelsEndpoint.GetModelDetailsAsync(model);
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

            // Count words and adjust for word length
            var words = text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
            var wordTokens = 0;

            foreach (var word in words)
            {
                // Longer words are typically broken into multiple tokens
                if (word.Length <= 4)
                    wordTokens += 1;  // Very short words are usually single tokens
                else if (word.Length <= 8)
                    wordTokens += 1;  // Medium-length words are often single tokens
                else if (word.Length <= 12)
                    wordTokens += 2;  // Longer words often break into 2 tokens
                else
                    wordTokens += word.Length / 5 + 1;  // Very long words may break into more tokens
            }

            // Count whitespace characters (they often become tokens)
            var spaceCount = text.Count(c => c == ' ');
            var newlineCount = text.Count(c => c == '\n' || c == '\r');
            var tabCount = text.Count(c => c == '\t');

            // Count special characters (punctuation, symbols, etc.)
            var specialCharCount = text.Count(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));

            // Some common patterns like "\n\n" might be single tokens
            var consecutiveNewlinesCount = CountPattern(text, "\n\n") + CountPattern(text, "\r\n\r\n");

            // Calculate token estimate
            // - Word tokens as calculated above
            // - Most spaces are tokenized separately, but not always
            // - Newlines and tabs are often separate tokens
            // - Special characters are often separate tokens
            // - Subtract duplicate counting for consecutive newlines
            var estimate = wordTokens + (spaceCount * 0.7) + newlineCount + tabCount +
                          specialCharCount - (consecutiveNewlinesCount * 0.5);

            // Round up and add a small safety margin
            return (int)Math.Ceiling(estimate) + 3;
        }

        protected static int CountPattern(string text, string pattern)
        {
            int count = 0;
            int i = 0;
            while ((i = text.IndexOf(pattern, i)) != -1)
            {
                count++;
                i += pattern.Length;
            }
            return count;
        }
    }
}
