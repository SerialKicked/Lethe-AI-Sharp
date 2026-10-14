using OpenAI;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace LetheAISharp.Files.Tests
{
    /// <summary>
    /// Manual tests for the tool-chain context trimming (<see cref="TokenTools.TrimToolContext"/>) and the
    /// per-round budget helper. Call <see cref="RunAllTests"/> from a throwaway console project.
    /// </summary>
    public static class ToolChainTrimTest
    {
        private static int _failures;

        public static void RunAllTests()
        {
            _failures = 0;
            Console.WriteLine("=== Tool chain trim tests ===");

            NothingEvictedWhenItFits();
            OldHistoryIsEvictedFirstAndUserMessageIsPinned();
            OlderRoundsAreDroppedAtomically();
            ReportsWhenPinnedMessagesDoNotFit();
            EstimatorCountsToolCallArguments();

            Console.WriteLine(_failures == 0 ? "All tool chain trim tests passed." : $"{_failures} tool chain trim test(s) FAILED.");
        }

        private static string Filler(int tokens) => new('x', tokens * 4);

        private static Message Round(string id, int resultTokens, out Message toolResult)
        {
            var call = new ToolCall(id, "do_something", JsonValue.Create("{\"q\":\"value\"}"));
            toolResult = new Message(call, Filler(resultTokens));
            return new Message(Role.Assistant, new List<ToolCall> { call }, string.Empty);
        }

        private static void Check(bool condition, string name)
        {
            if (condition)
            {
                Console.WriteLine($"  OK   {name}");
            }
            else
            {
                _failures++;
                Console.WriteLine($"  FAIL {name}");
            }
        }

        private static void NothingEvictedWhenItFits()
        {
            var messages = new List<Message>
            {
                new(Role.System, "sys"),
                new(Role.User, "old question"),
                new(Role.Assistant, "old answer"),
                new(Role.User, "current question")
            };
            var assistant = Round("a", 10, out var tool);
            var res = TokenTools.TrimToolContext(messages, [assistant, tool], null, 10000, 0, out var fits);
            Check(fits && res.Count == 6, "nothing is evicted below the budget");
        }

        private static void OldHistoryIsEvictedFirstAndUserMessageIsPinned()
        {
            var messages = new List<Message>
            {
                new(Role.System, "sys"),
                new(Role.User, Filler(500)),
                new(Role.Assistant, Filler(500)),
                new(Role.User, "current question")
            };
            var assistant = Round("a", 300, out var tool);
            var res = TokenTools.TrimToolContext(messages, [assistant, tool], null, 700, 0, out var fits);
            Check(fits, "fits after evicting old history");
            Check(res[0].Role == Role.System, "system prompt kept");
            Check(res.Any(m => m.Role == Role.User && m.Content?.ToString() == "current question"), "current user message kept");
            Check(res.Count == 4 && res[1].Role == Role.User, "old history removed, round kept");
        }

        private static void OlderRoundsAreDroppedAtomically()
        {
            var messages = new List<Message>
            {
                new(Role.System, "sys"),
                new(Role.User, "current question")
            };
            var toadd = new List<Message>();
            for (int i = 0; i < 4; i++)
            {
                toadd.Add(Round("c" + i, 300, out var tool));
                toadd.Add(tool);
            }
            // Budget for roughly two rounds out of four.
            var res = TokenTools.TrimToolContext(messages, toadd, null, 750, 0, out var fits);
            Check(fits, "fits after dropping older rounds");
            Check(res[1].Role == Role.User, "user message still right after the system prompt");
            Check(res.Count == 2 + 4, "two newest rounds kept (assistant + tool each)");
            var orphan = false;
            for (int i = 2; i < res.Count; i += 2)
            {
                if (res[i].Role != Role.Assistant || res[i + 1].Role != Role.Tool || res[i + 1].ToolCallId != res[i].ToolCalls![0].Id)
                    orphan = true;
            }
            Check(!orphan, "no orphaned tool message");
            Check(res[^1].ToolCallId == "c3", "newest round kept");
        }

        private static void ReportsWhenPinnedMessagesDoNotFit()
        {
            var messages = new List<Message>
            {
                new(Role.System, Filler(400)),
                new(Role.User, Filler(400))
            };
            var assistant = Round("a", 400, out var tool);
            var res = TokenTools.TrimToolContext(messages, [assistant, tool], null, 500, 0, out var fits);
            Check(!fits, "reports that it cannot fit");
            Check(res.Count == 4, "pinned messages and the newest round are never evicted");
        }

        private static void EstimatorCountsToolCallArguments()
        {
            var call = new ToolCall("x", "do_something", JsonValue.Create(Filler(400)));
            var withArgs = new Message(Role.Assistant, new List<ToolCall> { call }, string.Empty);
            var without = new Message(Role.Assistant, string.Empty);
            var delta = TokenTools.CountTokens([withArgs]) - TokenTools.CountTokens([without]);
            Check(delta >= 390, "tool call arguments are counted by the estimator");
        }
    }
}
