using AIToolkit.Files;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit
{
    /// <summary>
    /// Backend-agnostic prompt builder interface.
    /// This is the main way to build prompts for LLM queries.
    /// </summary>
    public interface IPromptBuilder
    {
        /// <summary>
        /// Returns the number of messages currently in the prompt
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Clear the prompt of all messages
        /// </summary>
        void Clear();

        /// <summary>
        /// Adds a message to the system with the specified author role.
        /// </summary>
        /// <remarks> Macros like {{time}}, {{user}} and so on, are automatically converted to text </remarks>
        /// <param name="role">The role of the author, indicating the context of the message sender.</param>
        /// <param name="message">The content of the message to be added. Cannot be null or empty.</param>
        /// <returns>tokens used by this message</returns>
        int AddMessage(AuthorRole role, string message);

        /// <summary>
        /// Inserts a message at the specified index in the prompt with the given author role.
        /// </summary>
        /// <param name="index">index to insert the message to</param>
        /// <param name="role">The role of the author, indicating the context of the message sender.</param>
        /// <param name="message">The content of the message to be added. Cannot be null or empty.</param>
        /// <returns>tokens used by this message</returns>
        int InsertMessage(int index, AuthorRole role, string message);

        /// <summary>
        /// Retrieve the full prompt. The exact format depends on the backend.
        /// </summary>
        /// <returns>a string for text completion. a List<Message> for chat completion </returns>
        object GetFullPrompt();

        /// <summary>
        /// Converts the current prompt to a query object suitable for sending to the LLM backend.
        /// </summary>
        /// <param name="responserole">Expected role for the response (should be Assistant normally)</param>
        /// <param name="tempoverride">Override the temperature set in LLMEngine.Sampler</param>
        /// <param name="responseoverride">Override the maximum response size</param>
        /// <param name="overridePrefill">Override the prefill setting for CoT models</param>
        /// <returns></returns>
        object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null);

        /// <summary>
        /// Retrieves the total number of tokens used by the current prompt.
        /// </summary>
        /// <returns>The total count of tokens used.</returns>
        int GetTokenUsage();

        /// <summary>
        /// Calculates the number of tokens in the specified message based on the given author role.
        /// </summary>
        /// <remarks>The token count may vary depending on the role provided, as different roles may apply
        /// different tokenization rules. The potential macros in message are automatically converted.</remarks>
        /// <param name="role">The role of the author, which may influence tokenization rules.</param>
        /// <param name="message">The message to analyze.</param>
        /// <returns>The total number of tokens in the message.</returns>
        int GetTokenCount(AuthorRole role, string message);

        /// <summary>
        /// Converts the current prompt into its textual representation.
        /// </summary>
        /// <returns>A string containing the text representation of the prompt. The returned string will be 
        /// empty if the prompt has no content.</returns>
        string PromptToText();

        /// <summary>
        /// Only relevant to text completion. Calculates the token overread for the responsse start block / prefill.
        /// </summary>
        /// <param name="talker">the Bot or the User supposed to talk</param>
        /// <returns>Tokens used</returns>
        int GetResponseTokenCount(BasePersona talker) => LLMEngine.GetTokenCount(LLMEngine.Instruct.GetResponseStart(talker));
    }
}
