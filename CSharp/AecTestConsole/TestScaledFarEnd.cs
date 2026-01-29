/* Test scenarios with scaled (reduced gain) far-end signal */

using System;
using System.Collections.Generic;
using System.IO;
using Concentus;

namespace AecTestConsole
{
    /// <summary>
    /// Tests echo cancellation when far-end signal has lower gain than near-end,
    /// simulating a far-away speaker scenario.
    /// </summary>
    internal static class TestScaledFarEnd
    {
        public static void Run()
        {
            Console.WriteLine("\n=== Testing Scaled Far-End Scenarios (Distant Speaker) ===");
            Console.WriteLine("These tests simulate far-end audio at lower gain than near-end,");
            Console.WriteLine("as would occur with a distant speaker or low volume playback.\n");

            // Test 1: 50% far-end gain with synthetic signal
            TestScaledSynthetic();

            // Test 2: 25% far-end gain with synthetic signal
            TestVeryScaledSynthetic();

            // Test 3: Scaled far-end with real audio
            TestScaledRealAudio("../../AudioData/16Khz Mono.raw", 16000, 0.3);
            TestScaledRealAudio("../../AudioData/48Khz Mono.raw", 48000, 0.3);
        }

        private static void TestScaledSynthetic()
        {
            Console.WriteLine("Test: Scaled Far-End - 50% Gain (Synthetic)");
            try
            {
                using (var aec = AecFactory.CreateMdf(16000, 128, 2432))
                {
                    int frameSize = 128;
                    int numFrames = 100;

                    short[] farEnd = new short[frameSize];
                    short[] nearEnd = new short[frameSize];
                    short[] output = new short[frameSize];

                    double totalEchoPower = 0;
                    double totalOutputPower = 0;
                    int activeFrames = 0;

                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        // Generate 1kHz sine wave at moderate level
                        for (int i = 0; i < frameSize; i++)
                        {
                            double phase = 2.0 * Math.PI * 1000.0 * (frame * frameSize + i) / 16000.0;
                            double sample = 8000.0 * Math.Sin(phase); // Half amplitude
                            farEnd[i] = (short)sample;

                            // Echo at 50% of far-end (so 25% of max amplitude)
                            nearEnd[i] = (short)(sample * 0.5);
                        }

                        aec.Process(nearEnd, farEnd, output);

                        // Measure after adaptation period
                        if (frame >= 30 && frame < numFrames - 10)
                        {
                            double echoPower = ComputePower(nearEnd);
                            double outPower = ComputePower(output);

                            if (echoPower > 1e6)
                            {
                                totalEchoPower += echoPower;
                                totalOutputPower += outPower;
                                activeFrames++;
                            }
                        }
                    }

                    if (activeFrames > 0)
                    {
                        double avgEchoPower = totalEchoPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgEchoPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: 50% (scaled)");
                        Console.WriteLine($"  Echo Level: 25% of max amplitude");
                        Console.WriteLine($"  ERLE: {erle:F2} dB");

                        if (erle > 10.0)
                            Console.WriteLine($"  ✓ PASSED (ERLE > 10 dB with scaled far-end)");
                        else
                            Console.WriteLine($"  ⚠ Lower than target (>10 dB)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            }
        }

        private static void TestVeryScaledSynthetic()
        {
            Console.WriteLine("\nTest: Very Scaled Far-End - 25% Gain (Synthetic)");
            try
            {
                using (var aec = AecFactory.CreateMdf(16000, 128, 2432))
                {
                    int frameSize = 128;
                    int numFrames = 100;

                    short[] farEnd = new short[frameSize];
                    short[] nearEnd = new short[frameSize];
                    short[] output = new short[frameSize];

                    double totalEchoPower = 0;
                    double totalOutputPower = 0;
                    int activeFrames = 0;

                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        // Generate 1kHz sine wave at low level (25%)
                        for (int i = 0; i < frameSize; i++)
                        {
                            double phase = 2.0 * Math.PI * 1000.0 * (frame * frameSize + i) / 16000.0;
                            double sample = 4000.0 * Math.Sin(phase); // Quarter amplitude
                            farEnd[i] = (short)sample;

                            // Echo at 50% of far-end (so 12.5% of max amplitude)
                            nearEnd[i] = (short)(sample * 0.5);
                        }

                        aec.Process(nearEnd, farEnd, output);

                        // Measure after adaptation period
                        if (frame >= 30 && frame < numFrames - 10)
                        {
                            double echoPower = ComputePower(nearEnd);
                            double outPower = ComputePower(output);

                            if (echoPower > 1e5) // Lower threshold for scaled signal
                            {
                                totalEchoPower += echoPower;
                                totalOutputPower += outPower;
                                activeFrames++;
                            }
                        }
                    }

                    if (activeFrames > 0)
                    {
                        double avgEchoPower = totalEchoPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgEchoPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: 25% (very scaled)");
                        Console.WriteLine($"  Echo Level: 12.5% of max amplitude");
                        Console.WriteLine($"  ERLE: {erle:F2} dB");

                        if (erle > 8.0)
                            Console.WriteLine($"  ✓ PASSED (ERLE > 8 dB with very scaled far-end)");
                        else
                            Console.WriteLine($"  ⚠ Lower than target (>8 dB)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            }
        }

        private static void TestScaledRealAudio(string filePath, int sampleRate, double farEndGain)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"\n{Path.GetFileName(filePath)}: File not found (skipping scaled test)");
                return;
            }

            Console.WriteLine($"\nTest: {Path.GetFileName(filePath)} - {farEndGain * 100:F0}% Far-End Gain");
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                short[] audioSamples = BytesToShorts(fileBytes);

                int frameSize = 128;
                using (var aec = AecFactory.CreateMdf(sampleRate))
                {
                    int numFrames = Math.Min(audioSamples.Length / frameSize, 300);

                    if (numFrames < 100)
                    {
                        Console.WriteLine($"  ⚠ Audio file too short");
                        return;
                    }

                    short[] farEnd = new short[frameSize];
                    short[] nearEnd = new short[frameSize];
                    short[] output = new short[frameSize];

                    double totalNearPower = 0;
                    double totalOutputPower = 0;
                    int activeFrames = 0;

                    var nearEndBuffer = new List<short>();
                    var processedBuffer = new List<short>();

                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        int offset = frame * frameSize;

                        // Scale far-end to simulate distant speaker
                        for (int i = 0; i < frameSize; i++)
                        {
                            farEnd[i] = (short)(audioSamples[offset + i] * farEndGain);
                        }

                        // Simulate 40% echo with delay (of the scaled far-end)
                        int delayFrames = 2;
                        if (frame >= delayFrames)
                        {
                            int echoOffset = (frame - delayFrames) * frameSize;
                            for (int i = 0; i < frameSize; i++)
                            {
                                nearEnd[i] = (short)(audioSamples[echoOffset + i] * farEndGain * 0.4);
                            }
                        }
                        else
                        {
                            Array.Clear(nearEnd, 0, frameSize);
                        }

                        aec.Process(nearEnd, farEnd, output);

                        nearEndBuffer.AddRange(nearEnd);
                        processedBuffer.AddRange(output);

                        // Measure ERLE after adaptation
                        if (frame >= 50 && frame < numFrames - 20)
                        {
                            double nearPower = ComputePower(nearEnd);
                            double outPower = ComputePower(output);

                            // Lower threshold for scaled signals
                            if (nearPower > 1e5)
                            {
                                totalNearPower += nearPower;
                                totalOutputPower += outPower;
                                activeFrames++;
                            }
                        }
                    }

                    if (activeFrames > 10)
                    {
                        double avgNearPower = totalNearPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: {farEndGain * 100:F0}%");
                        Console.WriteLine($"  ERLE: {erle:F2} dB (measured over {activeFrames} active frames)");

                        if (erle > 10.0)
                            Console.WriteLine($"  ✓ PASSED (ERLE > 10 dB)");
                        else
                            Console.WriteLine($"  ⚠ Lower than target (>10 dB)");
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠ Not enough active frames ({activeFrames})");
                    }

                    // Save output files
                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string nearEndFile = $"{baseName}_scaled{farEndGain * 100:F0}_nearend.wav";
                    string processedFile = $"{baseName}_scaled{farEndGain * 100:F0}_processed.wav";

                    SaveWavFile(nearEndFile, nearEndBuffer.ToArray(), sampleRate);
                    SaveWavFile(processedFile, processedBuffer.ToArray(), sampleRate);

                    Console.WriteLine($"  Saved: {nearEndFile}");
                    Console.WriteLine($"  Saved: {processedFile}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ FAILED: {ex.Message}");
            }
        }

        private static short[] BytesToShorts(byte[] input)
        {
            short[] processedValues = new short[input.Length / 2];
            for (int c = 0; c < processedValues.Length; c++)
            {
                processedValues[c] = (short)(((int)input[(c * 2)]) << 0);
                processedValues[c] += (short)(((int)input[(c * 2) + 1]) << 8);
            }
            return processedValues;
        }

        private static double ComputePower(short[] signal)
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

        private static void SaveWavFile(string filename, short[] samples, int sampleRate)
        {
            using (var stream = new FileStream(filename, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                // RIFF header
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Length * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // Mono
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);

                // data chunk
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2);

                foreach (short sample in samples)
                {
                    writer.Write(sample);
                }
            }
        }
    }
}
