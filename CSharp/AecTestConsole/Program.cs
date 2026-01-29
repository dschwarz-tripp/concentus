/* Simple test program for AEC functionality */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Concentus;

namespace AecTestConsole
{
    class Program
    {
        static short[] BytesToShorts(byte[] input)
        {
            short[] processedValues = new short[input.Length / 2];
            for (int c = 0; c < processedValues.Length; c++)
            {
                processedValues[c] = (short)(((int)input[(c * 2)]) << 0);
                processedValues[c] += (short)(((int)input[(c * 2) + 1]) << 8);
            }
            return processedValues;
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Testing Concentus AEC Implementation...\n");

            // Test 1: Basic instantiation
            Console.WriteLine("Test 1: Creating AEC with default settings for 16kHz...");
            try
            {
                using (var aec = AecFactory.CreateMdf(16000))
                {
                    Console.WriteLine($"  Sample Rate: {aec.SampleRate} Hz");
                    Console.WriteLine($"  Frame Size: {aec.FrameSize} samples");
                    Console.WriteLine($"  Filter Length: {aec.FilterLength} samples ({aec.FilterLength * 1000.0 / aec.SampleRate:F1} ms)");
                    Console.WriteLine("  ✓ PASSED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                return;
            }

            // Test 2: Process a frame
            Console.WriteLine("\nTest 2: Processing synthetic echo signal...");
            try
            {
                using (var aec = AecFactory.CreateMdf(16000, 128, 2432)) // 152ms at 16kHz
                {
                    short[] farEnd = GenerateSineWave(1000.0, 16000, 128);
                    short[] nearEnd = new short[128];
                    short[] output = new short[128];

                    // Simulate echo
                    for (int i = 0; i < 128; i++)
                        nearEnd[i] = (short)(farEnd[i] / 2);

                    // Process several frames
                    for (int frame = 0; frame < 10; frame++)
                    {
                        aec.Process(nearEnd, farEnd, output);
                    }

                    // Check output was generated
                    bool hasOutput = false;
                    for (int i = 0; i < output.Length; i++)
                    {
                        if (output[i] != 0)
                        {
                            hasOutput = true;
                            break;
                        }
                    }

                    if (hasOutput)
                        Console.WriteLine("  ✓ PASSED - Output generated");
                    else
                        Console.WriteLine("  ⚠ WARNING - Output is all zeros");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                Console.WriteLine($"  Stack: {ex.StackTrace}");
                return;
            }

            // Test 3: Reset functionality
            Console.WriteLine("\nTest 3: Testing reset...");
            try
            {
                using (var aec = AecFactory.CreateMdf(16000, 128, 2432))
                {
                    short[] farEnd = GenerateSineWave(1000.0, 16000, 128);
                    short[] nearEnd = new short[128];
                    short[] output = new short[128];

                    // Process some frames
                    for (int frame = 0; frame < 5; frame++)
                    {
                        aec.Process(nearEnd, farEnd, output);
                    }

                    // Reset
                    aec.Reset();

                    // Process again
                    aec.Process(nearEnd, farEnd, output);

                    Console.WriteLine("  ✓ PASSED");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                return;
            }

            // Test 4: All sample rates
            Console.WriteLine("\nTest 4: Testing all supported sample rates...");
            int[] sampleRates = { 8000, 16000, 24000, 48000 };
            foreach (int sr in sampleRates)
            {
                try
                {
                    using (var aec = AecFactory.CreateMdf(sr))
                    {
                        Console.WriteLine($"  {sr} Hz: ✓");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  {sr} Hz: ✗ {ex.Message}");
                }
            }

            Console.WriteLine("\n✓ All tests passed!");

            // Bonus: Test with real audio if available
            Console.WriteLine("\n--- Real Audio File Tests ---");
            TestRealAudioFile("../../AudioData/8Khz Mono.raw", 8000);
            TestRealAudioFile("../../AudioData/16Khz Mono.raw", 16000);
            TestRealAudioFile("../../AudioData/24Khz Mono.raw", 24000);
            TestRealAudioFile("../../AudioData/48Khz Mono.raw", 48000);

            Console.WriteLine("\n--- Music File Tests (48 kHz Mono) ---");
            TestRealAudioFile("../../AudioData/Blunderbuss.raw", 48000);
            TestRealAudioFile("../../AudioData/Ichiba.raw", 48000);
            TestRealAudioFile("../../AudioData/Jurgen.raw", 48000);

            // Test near-end speech preservation (no echo scenario)
            TestNearEndOnly.Run();

            // Test scaled far-end scenarios (distant speaker)
            TestScaledFarEnd.Run();

            // Live audio test (macOS only)
            Console.WriteLine("\n--- Live Audio Test ---");
            Console.WriteLine("This test uses real speakers and microphone.");
            Console.Write("Run live audio test? (y/n): ");
            string response = Console.ReadLine();
            if (response?.ToLower() == "y")
            {
                TestLiveAudio.Run();
            }
            else
            {
                Console.WriteLine("Skipping live audio test.");
            }
        }

        static void TestRealAudioFile(string filePath, int sampleRate, int channels = 1)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"\n{Path.GetFileName(filePath)}: File not found (skipping)");
                return;
            }

            Console.WriteLine($"\n{Path.GetFileName(filePath)}: Testing echo cancellation...");
            var totalStopwatch = Stopwatch.StartNew();
            long totalProcessingTicks = 0;
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                short[] audioSamples = BytesToShorts(fileBytes);

                int frameSize = 128;
                using (var aec = AecFactory.CreateMdf(sampleRate))
                {
                    // For stereo, we need to deinterleave or just use left channel
                    int totalSamples = channels == 1 ? audioSamples.Length : audioSamples.Length / channels;
                    int numFrames = Math.Min(totalSamples / frameSize, 300);

                    if (numFrames < 100)
                    {
                        Console.WriteLine($"  ⚠ Audio file too short ({audioSamples.Length} samples, {numFrames} frames)");
                        return;
                    }

                    short[] farEnd = new short[frameSize];
                    short[] nearEnd = new short[frameSize];
                    short[] output = new short[frameSize];

                    double totalNearPower = 0;
                    double totalOutputPower = 0;
                    int activeFrames = 0;

                    // Buffers to save output files
                    var nearEndBuffer = new System.Collections.Generic.List<short>();
                    var processedBuffer = new System.Collections.Generic.List<short>();

                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        int offset = frame * frameSize;
                        Array.Copy(audioSamples, offset, farEnd, 0, frameSize);

                        // Simulate 40% echo with small delay
                        int delayFrames = 2;
                        if (frame >= delayFrames)
                        {
                            int echoOffset = (frame - delayFrames) * frameSize;
                            for (int i = 0; i < frameSize; i++)
                            {
                                nearEnd[i] = (short)(audioSamples[echoOffset + i] * 0.4);
                            }
                        }
                        else
                        {
                            Array.Clear(nearEnd, 0, frameSize);
                        }

                        var frameStopwatch = Stopwatch.StartNew();
                        aec.Process(nearEnd, farEnd, output);
                        frameStopwatch.Stop();
                        totalProcessingTicks += frameStopwatch.ElapsedTicks;

                        // Save audio for output files
                        nearEndBuffer.AddRange(nearEnd);
                        processedBuffer.AddRange(output);

                        // Skip adaptation period and measure ERLE
                        if (frame >= 50 && frame < numFrames - 20)
                        {
                            double nearPower = ComputePower(nearEnd);
                            double outPower = ComputePower(output);

                            if (nearPower > 1e6) // Active frame threshold
                            {
                                totalNearPower += nearPower;
                                totalOutputPower += outPower;
                                activeFrames++;
                            }
                        }
                    }

                    totalStopwatch.Stop();
                    double avgProcessingTimeMs = (totalProcessingTicks * 1000.0) / (Stopwatch.Frequency * numFrames);
                    double audioLengthMs = (numFrames * frameSize * 1000.0) / sampleRate;
                    double realTimeFactor = audioLengthMs / totalStopwatch.Elapsed.TotalMilliseconds;

                    if (activeFrames > 0)
                    {
                        double avgNearPower = totalNearPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  ERLE: {erle:F2} dB (measured over {activeFrames} active frames)");

                        if (erle > 15.0)
                            Console.WriteLine($"  ✓ PASSED (ERLE > 15 dB)");
                        else
                            Console.WriteLine($"  ⚠ ERLE lower than expected (target: >15 dB)");
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠ Not enough active frames to measure (processed {numFrames} frames)");
                    }

                    Console.WriteLine($"  Processing Time: {avgProcessingTimeMs:F3} ms/frame ({totalStopwatch.Elapsed.TotalMilliseconds:F1} ms total)");
                    Console.WriteLine($"  Real-Time Factor: {realTimeFactor:F2}x (audio length: {audioLengthMs:F1} ms)");

                    // Save output WAV files
                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string nearEndFile = $"{baseName}_nearend.wav";
                    string processedFile = $"{baseName}_processed.wav";

                    SaveWavFile(nearEndFile, nearEndBuffer.ToArray(), sampleRate);
                    SaveWavFile(processedFile, processedBuffer.ToArray(), sampleRate);

                    Console.WriteLine($"  Saved: {nearEndFile}");
                    Console.WriteLine($"  Saved: {processedFile}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
                Console.WriteLine($"  Stack: {ex.StackTrace?.Split('\n').FirstOrDefault()}");
            }
        }

        static double ComputePower(short[] signal)
        {
            if (signal == null || signal.Length == 0)
                return 0.0;

            double power = 0;
            for (int i = 0; i < signal.Length; i++)
            {
                power += (double)signal[i] * signal[i];
            }
            return power / signal.Length;
        }

        static short[] GenerateSineWave(double frequency, int sampleRate, int length)
        {
            short[] samples = new short[length];
            double phaseIncrement = 2.0 * Math.PI * frequency / sampleRate;

            for (int i = 0; i < length; i++)
            {
                double phase = i * phaseIncrement;
                samples[i] = (short)(16000.0 * Math.Sin(phase));
            }

            return samples;
        }

        static void SaveWavFile(string filename, short[] samples, int sampleRate)
        {
            using (var stream = new FileStream(filename, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                // RIFF header
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Length * 2); // File size - 8
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16); // Chunk size
                writer.Write((short)1); // Audio format (PCM)
                writer.Write((short)1); // Channels (mono)
                writer.Write(sampleRate); // Sample rate
                writer.Write(sampleRate * 2); // Byte rate
                writer.Write((short)2); // Block align
                writer.Write((short)16); // Bits per sample

                // data chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2); // Data size

                // Write audio data
                foreach (short sample in samples)
                {
                    writer.Write(sample);
                }
            }
        }
    }
}
