using AIToolkit.LLM;
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
    public class OpenAI_APIClient
    {
        private OpenAIClient API;

        public OpenAI_APIClient()
        {
            var settings = new OpenAIClientSettings(LLMSystem.BackendUrl);
            API = new OpenAIClient(null, settings);
        }

        public async Task<List<Model>> GetModelList()
        {
            var models = await API.ModelsEndpoint.GetModelsAsync();
            var lst = new List<Model>(models);
            return lst;
        }

        public async Task<Model> GetModelInfo(string model)
        {
            var info = await API.ModelsEndpoint.GetModelDetailsAsync(model);
            return info;
        }

        public async Task<Choice> ChatCompletionTest()
        {
            var messages = new List<Message>
            {
                new Message(Role.System, "You are a helpful assistant."),
                new Message(Role.User, "What is the capital of France?")
            };

            var chatRequest = new ChatRequest(messages, maxTokens: LLMSystem.MaxReplyLength, temperature: LLMSystem.Sampler.Temperature);
            var response = await API.ChatEndpoint.GetCompletionAsync(chatRequest);
            return response.FirstChoice;
        }

    }
}
