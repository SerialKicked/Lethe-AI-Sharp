using NAudio.Wave;
using System;
using System.IO;

namespace AIToolkit.Audio
{
    /// <summary>
    /// Record audio from the microphone and save it to a file.
    /// </summary>
    public class AudioRecord : IDisposable
    {
        private readonly WaveInEvent waveIn;
        private WaveFileWriter? writer;
        private MemoryStream? memoryStream;

        public AudioRecord()
        {
            waveIn = new WaveInEvent();
            waveIn.DataAvailable += OnDataAvailable!;
            waveIn.RecordingStopped += OnRecordingStopped!;
        }

        public void StartRecording()
        {
            memoryStream = new MemoryStream();
            writer = new WaveFileWriter(memoryStream, waveIn.WaveFormat);
            waveIn.StartRecording();
        }

        public void StopRecording()
        {
            waveIn?.StopRecording();
        }

        public bool SaveRecording(string filePath, float threshold = 0.02f)
        {
            if (memoryStream == null)
                throw new InvalidOperationException("No recording to save.");

            // Reset the position to the beginning of the stream
            memoryStream.Position = 0;

            var hasSound = threshold <= 0 || ContainsSound(memoryStream, threshold);

            if (hasSound)
            {
                // Save the recording to the specified file
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                memoryStream.CopyTo(fileStream);
                return true;
            }
            else
            {
                // Clear the memory stream
                memoryStream.SetLength(0);
                return false;
            }
        }

        private static bool ContainsSound(Stream audioStream, float threshold)
        {
            // Reset the position to the beginning of the stream
            audioStream.Position = 0;

            using var reader = new WaveFileReader(audioStream);
            var sampleProvider = reader.ToSampleProvider();

            var buffer = new float[reader.WaveFormat.SampleRate];
            int samplesRead;

            while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < samplesRead; i++)
                {
                    float sample = Math.Abs(buffer[i]);
                    if (sample > threshold)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (writer != null)
            {
                writer.Write(e.Buffer, 0, e.BytesRecorded);
                writer.Flush();
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            writer?.Dispose();
            writer = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                waveIn?.Dispose();
                writer?.Dispose();
                memoryStream?.Dispose();
            }
        }
    }
}