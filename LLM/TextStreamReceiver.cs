using System;
using System.Collections.Generic;
using System.Text;

namespace LetheAISharp.LLM
{
    /// <summary>
    /// Dedicted class to handle the complexity of receiving a stream of tokens that may contain interleaved "thinking" and "talking" segments, 
    /// as denoted by special tags in the stream. This is necessary because some LLMs may emit "thinking" tokens (e.g. internal reasoning steps) interleaved with "talking" tokens 
    /// (the actual response to be shown to the user), and we want to be able to separate these cleanly without risking cutting off tags in the middle.
    /// </summary>
    internal class TextStreamReceiver
    {
        private InferenceChannel currentState = InferenceChannel.Thinking;
        private StringBuilder _streamBuffer = new();
        private StringBuilder thinkingBuffer = new();
        private StringBuilder talkingBuffer = new();

        private string StartThinkingToken => LLMEngine.Instruct.ThinkingStart.Replace("\n", "");
        private string EndThinkingToken => LLMEngine.Instruct.ThinkingEnd.Replace("\n", "");

        public InferenceChannel FeedToken(string token)
        {
            _streamBuffer.Append(token);
            // Handle case where there's no thinking tags at all — just flush directly to talking
            if (string.IsNullOrEmpty(LLMEngine.Instruct.ThinkingStart) || string.IsNullOrEmpty(LLMEngine.Instruct.ThinkingEnd) || LLMEngine.Settings.DisableThinking)
            {
                talkingBuffer.Append(token);
                _streamBuffer.Clear();
                return InferenceChannel.Text;            
            }

            // Cache once — each property access calls Replace() which allocates a new string
            ReadOnlySpan<char> startTag = StartThinkingToken;
            ReadOnlySpan<char> endTag = EndThinkingToken;

            // Materialize _streamBuffer once (it holds at most tag.Length-1 chars between calls).
            // All subsequent slicing operates on ReadOnlySpan<char> — zero heap allocations.
            ReadOnlySpan<char> buf = _streamBuffer.ToString();
            while (true)
            {
                if (currentState == InferenceChannel.Thinking)
                {
                    var closeIdx = buf.IndexOf(endTag, StringComparison.Ordinal);
                    if (closeIdx >= 0)
                    {
                        // Dump everything before the close tag into thinking
                        thinkingBuffer.Append(buf[..closeIdx]);
                        buf = buf[(closeIdx + endTag.Length)..];
                        currentState = InferenceChannel.Text;
                        // Don't break — there might be more to process in the remainder
                    }
                    else
                    {
                        // No close tag yet — safe to flush everything except
                        // a tail that could be a partial close tag
                        var safeLen = SafeFlushLength(buf, endTag);
                        if (safeLen > 0)
                            thinkingBuffer.Append(buf[..safeLen]);
                        buf = buf[safeLen..];
                        break;
                    }
                }
                else
                {
                    var openIdx = buf.IndexOf(startTag, StringComparison.Ordinal);
                    if (openIdx >= 0)
                    {
                        talkingBuffer.Append(buf[..openIdx]);
                        buf = buf[(openIdx + startTag.Length)..];
                        currentState = InferenceChannel.Thinking;
                    }
                    else
                    {
                        var safeLen = SafeFlushLength(buf, startTag);
                        if (safeLen > 0)
                            talkingBuffer.Append(buf[..safeLen]);
                        buf = buf[safeLen..];
                        break;
                    }
                }
            }
            _streamBuffer.Clear();
            _streamBuffer.Append(buf);
            return currentState;
        }


        // Returns how many chars from the start of buf can be safely flushed
        // without risking cutting off a partial match of tag at the end
        private static int SafeFlushLength(ReadOnlySpan<char> buf, ReadOnlySpan<char> tag)
        {
            // Walk back from the end to find the longest suffix that is a prefix of tag
            for (int suffixLen = Math.Min(tag.Length - 1, buf.Length); suffixLen > 0; suffixLen--)
            {
                if (tag.StartsWith(buf[^suffixLen..], StringComparison.Ordinal))
                    return buf.Length - suffixLen;
            }
            return buf.Length;
        }

        public void ForceFeed(InferenceChannel target, string content)
        {
            if (target == InferenceChannel.Thinking)
            {
                thinkingBuffer.Append(content);
            }
            else
            {
                talkingBuffer.Append(content);
            }
        }

        /// <summary>
        /// Routes a content delta directly to the requested channel and synchronizes the
        /// internal state machine so subsequent inline-tag parsing is consistent with the
        /// last channel we wrote to. Use this when a backend signals the channel through
        /// a separate JSON field (e.g. <c>reasoning_content</c>) rather than through inline
        /// <c>&lt;think&gt;</c> tags. After this call, <see cref="FeedToken"/> will look for
        /// the transition tag of the opposite direction.
        /// </summary>
        public void FeedToChannel(InferenceChannel target, string content)
        {
            ForceFeed(target, content);
            _streamBuffer.Clear();
            currentState = target;
        }

        public (string ThinkContent, string TalkContent) GetCurrentBuffers()
        {
            if (string.IsNullOrEmpty(StartThinkingToken))
                return (string.Empty, talkingBuffer.ToString());
            var think = thinkingBuffer.ToString();
            if (think.Length > 0)
                think = think.Replace(StartThinkingToken, string.Empty);
            if (think.Length > 0)
                think = think.Replace(EndThinkingToken, string.Empty);

            var talk = talkingBuffer.ToString();
            if (talk.Length > 0)
                talk = talk.Replace(StartThinkingToken, string.Empty);
            if (talk.Length > 0)
                talk = talk.Replace(EndThinkingToken, string.Empty);
            return (think, talk);
        }

        public string GetFormattedText()
        {
            if (string.IsNullOrEmpty(StartThinkingToken))
                return talkingBuffer.ToString();

            var think = thinkingBuffer.ToString();
            if (think.Length > 0)
                think = think.Replace(StartThinkingToken, string.Empty);
            if (think.Length > 0)
                think = think.Replace(EndThinkingToken, string.Empty);

            var talk = talkingBuffer.ToString();
            if (talk.Length > 0)
                talk = talk.Replace(StartThinkingToken, string.Empty);
            if (talk.Length > 0)
                talk = talk.Replace(EndThinkingToken, string.Empty);

            if (string.IsNullOrEmpty(think) || string.IsNullOrEmpty(StartThinkingToken) || string.IsNullOrEmpty(EndThinkingToken))
                return talk;
            return $"{LLMEngine.Instruct.ThinkingStart}{think}{LLMEngine.Instruct.ThinkingEnd}{talk}";
        }

        public void Reset()
        {
            _streamBuffer.Clear();
            thinkingBuffer.Clear();
            talkingBuffer.Clear();
            currentState = InferenceChannel.Thinking;
        }
    }
}
