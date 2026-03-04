using LetheAISharp.LLM;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LetheAISharp.API
{
    /// <summary>
    /// Encapsulates tool execution logic for OpenAI-compatible backends.
    /// Provides real-time events before and after each tool invocation.
    /// </summary>
    public class ToolExecutor
    {
        /// <summary>Fired before each individual tool invocation.</summary>
        public event EventHandler<ToolCallRecord>? ToolCallStarted;

        /// <summary>Fired after each individual tool invocation completes (success or failure).</summary>
        public event EventHandler<ToolCallRecord>? ToolCallCompleted;

        /// <summary>
        /// Executes all tool calls from a completed streaming response.
        /// </summary>
        /// <param name="toolCalls">The tool calls requested by the model.</param>
        /// <param name="round">Zero-based tool-calling round index.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A tuple of tool response messages and their corresponding records.</returns>
        public async Task<(List<OpenAI.Chat.Message> toolMessages, List<ToolCallRecord> records)> ExecuteToolCalls(
            IReadOnlyList<OpenAI.ToolCall> toolCalls,
            int round,
            CancellationToken ct)
        {
            var toolMessages = new List<OpenAI.Chat.Message>();
            var records = new List<ToolCallRecord>();

            foreach (var toolcall in toolCalls)
            {
                var startRecord = new ToolCallRecord
                {
                    CallId = toolcall.Id ?? string.Empty,
                    FunctionName = toolcall.Function?.Name ?? string.Empty,
                    ArgumentsJson = toolcall.Function?.Arguments?.ToJsonString() ?? string.Empty
                };
                ToolCallStarted?.Invoke(this, startRecord);

                string functionResult;
                bool success;
                var sw = Stopwatch.StartNew();
                try
                {
                    functionResult = (await toolcall.InvokeFunctionAsync<string>(ct).ConfigureAwait(false)) ?? string.Empty;
                    success = true;
                }
                catch (Exception ex)
                {
                    functionResult = $"Error: {ex.Message}";
                    success = false;
                }
                sw.Stop();

                var completedRecord = new ToolCallRecord
                {
                    CallId = startRecord.CallId,
                    FunctionName = startRecord.FunctionName,
                    ArgumentsJson = startRecord.ArgumentsJson,
                    ResultJson = success ? functionResult : string.Empty,
                    Error = success ? null : functionResult,
                    Success = success,
                    Duration = sw.Elapsed,
                    Round = round
                };
                ToolCallCompleted?.Invoke(this, completedRecord);
                records.Add(completedRecord);
                toolMessages.Add(new OpenAI.Chat.Message(toolcall, functionResult));
            }

            return (toolMessages, records);
        }

        /// <summary>
        /// Builds a new <see cref="ChatRequest"/> with the updated message list after tool execution.
        /// </summary>
        /// <param name="current">The original request.</param>
        /// <param name="assistantMessage">The assistant message containing the tool_calls.</param>
        /// <param name="toolMessages">The tool result messages to append.</param>
        /// <returns>A new <see cref="ChatRequest"/> preserving all original parameters.</returns>
        public static ChatRequest RebuildRequest(ChatRequest current, OpenAI.Chat.Message assistantMessage, List<OpenAI.Chat.Message> toolMessages)
        {
            var updatedMessages = new List<OpenAI.Chat.Message>(current.Messages)
            {
                assistantMessage
            };
            updatedMessages.AddRange(toolMessages);

            return new ChatRequest(
                messages: updatedMessages,
                tools: current.Tools,
                toolChoice: "auto",
                model: current.Model,
                frequencyPenalty: current.FrequencyPenalty,
                maxTokens: current.MaxCompletionTokens,
                presencePenalty: current.PresencePenalty,
                responseFormat: current.ResponseFormat,
                seed: current.Seed,
                stops: current.Stops,
                temperature: current.Temperature,
                topP: current.TopP,
                jsonSchema: current.ResponseFormatObject?.JsonSchema,
                user: current.User
            );
        }
    }
}
