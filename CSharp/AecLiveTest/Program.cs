using System;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using Concentus;

namespace AecLiveTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Acoustic Echo Cancellation Live Test ===\n");

            // Configuration
            int sampleRate = 16000;
            int durationSeconds = 10;
            int frameSize = 128;
            int tailLength = 1024; // ~64ms at 16kHz

            // Parse command line arguments
            if (args.Length > 0 && int.TryParse(args[0], out int duration))
            {
                durationSeconds = duration;
            }

            Console.WriteLine($"Sample Rate: {sampleRate} Hz");
            Console.WriteLine($"Duration: {durationSeconds} seconds");
            Console.WriteLine($"Frame Size: {frameSize} samples ({frameSize * 1000.0 / sampleRate:F1} ms)");
            Console.WriteLine($"Tail Length: {tailLength} samples ({tailLength * 1000.0 / sampleRate:F1} ms)\n");

            // Output files
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string micFile = $"mic_original_{timestamp}.wav";
            string speakerFile = $"speaker_reference_{timestamp}.wav";
            string processedFile = $"mic_processed_{timestamp}.wav";

            Console.WriteLine($"\nOutput Files:");
            Console.WriteLine($"  Original Mic: {micFile}");
            Console.WriteLine($"  Speaker Reference: {speakerFile}");
            Console.WriteLine($"  Processed (AEC applied): {processedFile}");

            Console.WriteLine("\n=== Starting Audio Capture ===");
            Console.WriteLine("Instructions:");
            Console.WriteLine("1. Play some audio (music, video, etc.) through your speakers");
            Console.WriteLine("2. Speak into your microphone while audio is playing");
            Console.WriteLine("3. Recording will stop automatically after the configured duration\n");
            Console.WriteLine("Press ENTER to start recording...");
            Console.ReadLine();

            try
            {
                IAudioCapture capture;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    capture = new MacOSAudioCapture();
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
#if WINDOWS
                    capture = new WindowsAudioCapture();
#else
                    throw new PlatformNotSupportedException("Windows audio requires building on Windows with NAudio");
#endif
                }
                else
                {
                    throw new PlatformNotSupportedException($"Platform {RuntimeInformation.OSDescription} is not yet supported");
                }

                RunLiveAecTest(capture, sampleRate, durationSeconds, frameSize, tailLength,
                              micFile, speakerFile, processedFile);

                Console.WriteLine("\n=== Recording Complete ===");
                Console.WriteLine($"Files saved:");
                Console.WriteLine($"  {Path.GetFullPath(micFile)}");
                Console.WriteLine($"  {Path.GetFullPath(speakerFile)}");
                Console.WriteLine($"  {Path.GetFullPath(processedFile)}");
                Console.WriteLine("\nCompare the 'original' and 'processed' files to hear the echo cancellation effect.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void RunLiveAecTest(IAudioCapture capture, int sampleRate, int durationSeconds,
                                   int frameSize, int tailLength,
                                   string micFile, string speakerFile, string processedFile)
        {
            // Create AEC instance
            var aec = AecFactory.CreateMdf(sampleRate, frameSize, tailLength);

            // Buffers for audio data
            var micBuffer = new MemoryStream();
            var speakerBuffer = new MemoryStream();
            var processedBuffer = new MemoryStream();

            var micWriter = new SimpleWaveWriter(micBuffer, sampleRate);
            var speakerWriter = new SimpleWaveWriter(speakerBuffer, sampleRate);
            var processedWriter = new SimpleWaveWriter(processedBuffer, sampleRate);

            short[] micFrame = new short[frameSize];
            short[] speakerFrame = new short[frameSize];
            short[] outputFrame = new short[frameSize];

            int micFramePos = 0;
            int speakerFramePos = 0;

            object lockObj = new object();
            int framesProcessed = 0;
            int totalFrames = (int)(sampleRate * durationSeconds / (double)frameSize);

            // Set up audio capture handlers
            capture.OnMicrophoneData = (data) =>
            {
                lock (lockObj)
                {
                    foreach (short sample in data)
                    {
                        if (micFramePos >= frameSize)
                        {
                            // Process full frame
                            aec.Process(micFrame, speakerFrame, outputFrame);

                            // Write to files
                            micWriter.WriteSamples(micFrame);
                            speakerWriter.WriteSamples(speakerFrame);
                            processedWriter.WriteSamples(outputFrame);

                            framesProcessed++;
                            if (framesProcessed % 50 == 0)
                            {
                                int progress = (int)(100.0 * framesProcessed / totalFrames);
                                Console.Write($"\rRecording: {progress}% ({framesProcessed}/{totalFrames} frames)");
                            }

                            micFramePos = 0;
                            Array.Clear(speakerFrame, 0, frameSize);
                            speakerFramePos = 0;
                        }

                        micFrame[micFramePos++] = sample;
                    }
                }
            };

            capture.OnSpeakerData = (data) =>
            {
                lock (lockObj)
                {
                    foreach (short sample in data)
                    {
                        if (speakerFramePos < frameSize)
                        {
                            speakerFrame[speakerFramePos++] = sample;
                        }
                    }
                }
            };

            // Start recording
            capture.Start(sampleRate, durationSeconds);

            // Wait for completion
            DateTime startTime = DateTime.Now;
            while ((DateTime.Now - startTime).TotalSeconds < durationSeconds && framesProcessed < totalFrames)
            {
                Thread.Sleep(100);
            }

            // Stop recording
            capture.Stop();

            Console.WriteLine($"\rRecording: 100% ({framesProcessed}/{totalFrames} frames) - Complete!");

            // Cleanup
            Thread.Sleep(100);
            capture.Dispose();

            // Finalize wave files
            micWriter.Complete();
            speakerWriter.Complete();
            processedWriter.Complete();

            // Save to disk
            File.WriteAllBytes(micFile, micBuffer.ToArray());
            File.WriteAllBytes(speakerFile, speakerBuffer.ToArray());
            File.WriteAllBytes(processedFile, processedBuffer.ToArray());

            aec.Dispose();
        }

        static byte[] ShortsToBytes(short[] shorts)
        {
            byte[] bytes = new byte[shorts.Length * 2];
            for (int i = 0; i < shorts.Length; i++)
            {
                bytes[i * 2] = (byte)(shorts[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((shorts[i] >> 8) & 0xFF);
            }
            return bytes;
        }
    }

    // Cross-platform audio capture interface
    interface IAudioCapture : IDisposable
    {
        Action<short[]> OnMicrophoneData { get; set; }
        Action<short[]> OnSpeakerData { get; set; }
        void Start(int sampleRate, int durationSeconds);
        void Stop();
    }

#if WINDOWS
    // Windows implementation using NAudio
    class WindowsAudioCapture : IAudioCapture
    {
        private NAudio.Wave.WaveInEvent waveIn;
        private NAudio.CoreAudioApi.WasapiLoopbackCapture loopbackCapture;
        
        public Action<short[]> OnMicrophoneData { get; set; }
        public Action<short[]> OnSpeakerData { get; set; }

        public void Start(int sampleRate, int durationSeconds)
        {
            var waveFormat = new NAudio.Wave.WaveFormat(sampleRate, 16, 1);
            
            // Microphone capture
            waveIn = new NAudio.Wave.WaveInEvent
            {
                WaveFormat = waveFormat,
                BufferMilliseconds = 20
            };

            waveIn.DataAvailable += (sender, e) =>
            {
                short[] samples = new short[e.BytesRecorded / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                }
                OnMicrophoneData?.Invoke(samples);
            };

            // Speaker loopback
            try
            {
                loopbackCapture = new NAudio.CoreAudioApi.WasapiLoopbackCapture();
                loopbackCapture.DataAvailable += (sender, e) =>
                {
                    int channels = loopbackCapture.WaveFormat.Channels;
                    short[] samples = new short[e.BytesRecorded / 2 / channels];
                    
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int sum = 0;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            int idx = (i * channels + ch) * 2;
                            sum += (short)(e.Buffer[idx] | (e.Buffer[idx + 1] << 8));
                        }
                        samples[i] = (short)(sum / channels);
                    }
                    OnSpeakerData?.Invoke(samples);
                };
                loopbackCapture.StartRecording();
                Console.WriteLine("Using WASAPI loopback for speaker capture");
            }
            catch
            {
                Console.WriteLine("Speaker loopback not available");
            }

            waveIn.StartRecording();
        }

        public void Stop()
        {
            waveIn?.StopRecording();
            loopbackCapture?.StopRecording();
        }

        public void Dispose()
        {
            waveIn?.Dispose();
            loopbackCapture?.Dispose();
        }
    }
#endif

    // macOS implementation using CoreAudio
    class MacOSAudioCapture : IAudioCapture
    {
        private const string AudioToolboxFramework = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
        private const string CoreAudioFramework = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";

        public Action<short[]> OnMicrophoneData { get; set; }
        public Action<short[]> OnSpeakerData { get; set; }

        private Thread captureThread;
        private volatile bool isRecording;
        private IntPtr audioQueue = IntPtr.Zero;

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueNewInput(
            ref AudioStreamBasicDescription format,
            AudioQueueInputCallback callback,
            IntPtr userData,
            IntPtr runLoop,
            IntPtr runLoopMode,
            uint flags,
            out IntPtr audioQueue);

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueAllocateBuffer(
            IntPtr audioQueue,
            uint bufferByteSize,
            out IntPtr buffer);

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueEnqueueBuffer(
            IntPtr audioQueue,
            IntPtr buffer,
            uint numPacketDescs,
            IntPtr packetDescs);

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

        [DllImport(AudioToolboxFramework)]
        private static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);

        private delegate void AudioQueueInputCallback(
            IntPtr userData,
            IntPtr audioQueue,
            IntPtr buffer,
            IntPtr startTime,
            uint numPackets,
            IntPtr packetDesc);

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioStreamBasicDescription
        {
            public double mSampleRate;
            public uint mFormatID;
            public uint mFormatFlags;
            public uint mBytesPerPacket;
            public uint mFramesPerPacket;
            public uint mBytesPerFrame;
            public uint mChannelsPerFrame;
            public uint mBitsPerChannel;
            public uint mReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioQueueBuffer
        {
            public uint mAudioDataBytesCapacity;
            public IntPtr mAudioData;
            public uint mAudioDataByteSize;
            public IntPtr mUserData;
            public uint mPacketDescriptionCapacity;
            public IntPtr mPacketDescriptions;
            public uint mPacketDescriptionCount;
        }

        public void Start(int sampleRate, int durationSeconds)
        {
            Console.WriteLine("Starting macOS CoreAudio capture...");
            isRecording = true;

            captureThread = new Thread(() =>
            {
                try
                {
                    // Set up audio format (16-bit PCM mono)
                    var format = new AudioStreamBasicDescription
                    {
                        mSampleRate = sampleRate,
                        mFormatID = 0x6C70636D, // 'lpcm' - Linear PCM
                        mFormatFlags = 12, // kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked
                        mBytesPerPacket = 2,
                        mFramesPerPacket = 1,
                        mBytesPerFrame = 2,
                        mChannelsPerFrame = 1,
                        mBitsPerChannel = 16,
                        mReserved = 0
                    };

                    // Callback for audio data
                    AudioQueueInputCallback callback = (userData, queue, buffer, startTime, numPackets, packetDesc) =>
                    {
                        if (!isRecording) return;

                        var bufferStruct = Marshal.PtrToStructure<AudioQueueBuffer>(buffer);
                        int sampleCount = (int)(bufferStruct.mAudioDataByteSize / 2);

                        if (sampleCount > 0)
                        {
                            short[] samples = new short[sampleCount];
                            Marshal.Copy(bufferStruct.mAudioData, samples, 0, sampleCount);
                            OnMicrophoneData?.Invoke(samples);
                        }

                        // Re-enqueue buffer for next capture
                        AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
                    };

                    // Create audio queue
                    int result = AudioQueueNewInput(
                        ref format,
                        callback,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        0,
                        out audioQueue);

                    if (result != 0 || audioQueue == IntPtr.Zero)
                    {
                        throw new Exception($"Failed to create audio queue: {result}");
                    }

                    // Allocate and enqueue buffers
                    uint bufferSize = (uint)(sampleRate * 2 * 0.02); // 20ms buffer
                    for (int i = 0; i < 3; i++)
                    {
                        IntPtr bufferPtr;
                        result = AudioQueueAllocateBuffer(audioQueue, bufferSize, out bufferPtr);
                        if (result != 0)
                        {
                            throw new Exception($"Failed to allocate buffer: {result}");
                        }
                        AudioQueueEnqueueBuffer(audioQueue, bufferPtr, 0, IntPtr.Zero);
                    }

                    // Start recording
                    result = AudioQueueStart(audioQueue, IntPtr.Zero);
                    if (result != 0)
                    {
                        throw new Exception($"Failed to start audio queue: {result}");
                    }

                    Console.WriteLine("CoreAudio recording started (microphone only)");
                    Console.WriteLine("Note: Speaker loopback not implemented for macOS");

                    // Keep thread alive while recording
                    while (isRecording)
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"CoreAudio error: {ex.Message}");
                    isRecording = false;
                }
            });

            captureThread.Start();
        }

        public void Stop()
        {
            isRecording = false;
            if (audioQueue != IntPtr.Zero)
            {
                AudioQueueStop(audioQueue, true);
            }
            captureThread?.Join(1000);
        }

        public void Dispose()
        {
            Stop();
            if (audioQueue != IntPtr.Zero)
            {
                AudioQueueDispose(audioQueue, true);
                audioQueue = IntPtr.Zero;
            }
        }
    }

    // Simple WAV file writer
    class SimpleWaveWriter
    {
        private MemoryStream stream;
        private int sampleRate;
        private long dataStartPos;

        public SimpleWaveWriter(MemoryStream stream, int sampleRate)
        {
            this.stream = stream;
            this.sampleRate = sampleRate;
            WriteWaveHeader();
        }

        private void WriteWaveHeader()
        {
            // Write placeholder header (will update at end)
            stream.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"), 0, 4);
            stream.Write(BitConverter.GetBytes(0), 0, 4); // File size - 8 (placeholder)
            stream.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, 4);

            // fmt chunk
            stream.Write(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, 4);
            stream.Write(BitConverter.GetBytes(16), 0, 4); // Chunk size
            stream.Write(BitConverter.GetBytes((short)1), 0, 2); // Audio format (PCM)
            stream.Write(BitConverter.GetBytes((short)1), 0, 2); // Channels
            stream.Write(BitConverter.GetBytes(sampleRate), 0, 4); // Sample rate
            stream.Write(BitConverter.GetBytes(sampleRate * 2), 0, 4); // Byte rate
            stream.Write(BitConverter.GetBytes((short)2), 0, 2); // Block align
            stream.Write(BitConverter.GetBytes((short)16), 0, 2); // Bits per sample

            // data chunk
            stream.Write(System.Text.Encoding.ASCII.GetBytes("data"), 0, 4);
            stream.Write(BitConverter.GetBytes(0), 0, 4); // Data size (placeholder)
            dataStartPos = stream.Position;
        }

        public void WriteSamples(short[] samples)
        {
            foreach (short sample in samples)
            {
                stream.WriteByte((byte)(sample & 0xFF));
                stream.WriteByte((byte)((sample >> 8) & 0xFF));
            }
        }

        public void Complete()
        {
            long dataSize = stream.Position - dataStartPos;
            long fileSize = stream.Position - 8;

            // Update file size
            stream.Seek(4, SeekOrigin.Begin);
            stream.Write(BitConverter.GetBytes((int)fileSize), 0, 4);

            // Update data chunk size
            stream.Seek(40, SeekOrigin.Begin);
            stream.Write(BitConverter.GetBytes((int)dataSize), 0, 4);

            stream.Seek(0, SeekOrigin.End);
        }
    }
}
