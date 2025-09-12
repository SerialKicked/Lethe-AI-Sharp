using AIToolkit.Files;
using AIToolkit.LLM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIToolkit
{
    public interface IPromptBuilder
    {
        int Count { get; }

        void Clear();

        int AddMessage(AuthorRole role, string message);

        int InsertMessage(int index, AuthorRole role, string message);

        object GetFullPrompt();

        object PromptToQuery(AuthorRole responserole, double tempoverride = -1, int responseoverride = -1, bool? overridePrefill = null);

        int GetTokenUsage();

        int GetTokenCount(AuthorRole role, string message);

        string PromptToText();

        int GetResponseTokenCount(BasePersona talker) => LLMEngine.GetTokenCount(LLMEngine.Instruct.GetResponseStart(talker));
    }
}
