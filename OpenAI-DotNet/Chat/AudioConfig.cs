// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace OpenAI.Chat
{
    public sealed class AudioConfig
    {
        public AudioConfig() { }

        public AudioConfig(Voice? voice, AudioFormat format = AudioFormat.Pcm16)
        {
            Voice = string.IsNullOrWhiteSpace(voice?.Id) ? OpenAI.Voice.Alloy : voice;
            Format = format;
        }

        [JsonInclude]
        [JsonPropertyName("voice")]
        public string Voice { get; set; } = OpenAI.Voice.Alloy;

        [JsonInclude]
        [JsonPropertyName("format")]
        public AudioFormat Format { get; set; }

        public static implicit operator AudioConfig(Voice voice) => new(voice);
    }
}
