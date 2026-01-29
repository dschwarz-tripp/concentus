/* Test scenarios with scaled (reduced gain) far-end signal */

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            // Test 4: Sudden echo onset/offset to test adaptation
            TestSuddenEchoOnset("../../AudioData/16Khz Mono.raw", 16000);
            TestSuddenEchoOnset("../../AudioData/48Khz Mono.raw", 48000);
        }

        private static void TestScaledSynthetic()
        {
            Console.WriteLine("Test: Scaled Far-End - 50% Gain (Synthetic)");
            var totalStopwatch = Stopwatch.StartNew();
            long totalProcessingTicks = 0;
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

                        var frameStopwatch = Stopwatch.StartNew();
                        aec.Process(nearEnd, farEnd, output);
                        frameStopwatch.Stop();
                        totalProcessingTicks += frameStopwatch.ElapsedTicks;

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

                    totalStopwatch.Stop();
                    double avgProcessingTimeMs = (totalProcessingTicks * 1000.0) / (Stopwatch.Frequency * numFrames);
                    double audioLengthMs = (numFrames * frameSize * 1000.0) / 16000.0;
                    double realTimeFactor = audioLengthMs / totalStopwatch.Elapsed.TotalMilliseconds;

                    if (activeFrames > 0)
                    {
                        double avgEchoPower = totalEchoPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgEchoPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: 50% (scaled)");
                        Console.WriteLine($"  Echo Level: 25% of max amplitude");
                        Console.WriteLine($"  ERLE: {erle:F2} dB");

                        Console.WriteLine($"  Processing Time: {avgProcessingTimeMs:F3} ms/frame");
                        Console.WriteLine($"  Real-Time Factor: {realTimeFactor:F2}x");

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
            var totalStopwatch = Stopwatch.StartNew();
            long totalProcessingTicks = 0;
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
                            farEnd[i] = (short)(sample * 0.5); // 25% amplitude

                            // Echo at 50% of far-end (so 12.5% of max amplitude)
                            nearEnd[i] = (short)sample;
                        }

                        var frameStopwatch = Stopwatch.StartNew();
                        aec.Process(nearEnd, farEnd, output);
                        frameStopwatch.Stop();
                        totalProcessingTicks += frameStopwatch.ElapsedTicks;

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

                    totalStopwatch.Stop();
                    double avgProcessingTimeMs = (totalProcessingTicks * 1000.0) / (Stopwatch.Frequency * numFrames);
                    double audioLengthMs = (numFrames * frameSize * 1000.0) / 16000.0;
                    double realTimeFactor = audioLengthMs / totalStopwatch.Elapsed.TotalMilliseconds;

                    if (activeFrames > 0)
                    {
                        double avgEchoPower = totalEchoPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgEchoPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: 25% (very scaled)");
                        Console.WriteLine($"  Echo Level: 12.5% of max amplitude");
                        Console.WriteLine($"  ERLE: {erle:F2} dB");

                        Console.WriteLine($"  Processing Time: {avgProcessingTimeMs:F3} ms/frame");
                        Console.WriteLine($"  Real-Time Factor: {realTimeFactor:F2}x");

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
            var totalStopwatch = Stopwatch.StartNew();
            long totalProcessingTicks = 0;
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

                        var frameStopwatch = Stopwatch.StartNew();
                        aec.Process(nearEnd, farEnd, output);
                        frameStopwatch.Stop();
                        totalProcessingTicks += frameStopwatch.ElapsedTicks;

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

                    totalStopwatch.Stop();
                    double avgProcessingTimeMs = (totalProcessingTicks * 1000.0) / (Stopwatch.Frequency * numFrames);
                    double audioLengthMs = (numFrames * frameSize * 1000.0) / sampleRate;
                    double realTimeFactor = audioLengthMs / totalStopwatch.Elapsed.TotalMilliseconds;

                    if (activeFrames > 10)
                    {
                        double avgNearPower = totalNearPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  Far-End Gain: {farEndGain * 100:F0}%");
                        Console.WriteLine($"  ERLE: {erle:F2} dB (measured over {activeFrames} active frames)");

                        Console.WriteLine($"  Processing Time: {avgProcessingTimeMs:F3} ms/frame ({totalStopwatch.Elapsed.TotalMilliseconds:F1} ms total)");
                        Console.WriteLine($"  Real-Time Factor: {realTimeFactor:F2}x (audio length: {audioLengthMs:F1} ms)");

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
        private static void TestSuddenEchoOnset(string filePath, int sampleRate)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"\n{Path.GetFileName(filePath)}: File not found (skipping sudden echo test)");
                return;
            }

            Console.WriteLine($"\nTest: {Path.GetFileName(filePath)} - Sudden Echo Onset/Offset");
            var totalStopwatch = Stopwatch.StartNew();
            long totalProcessingTicks = 0;
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                short[] audioSamples = BytesToShorts(fileBytes);

                int frameSize = 128;
                using (var aec = AecFactory.CreateMdf(sampleRate))
                {
                    int numFrames = Math.Min(audioSamples.Length / frameSize, 400);

                    if (numFrames < 100)
                    {
                        Console.WriteLine($"  ⚠ Audio file too short");
                        return;
                    }

                    short[] farEnd = new short[frameSize];
                    short[] nearEnd = new short[frameSize];
                    short[] output = new short[frameSize];

                    // Split test into three phases
                    int phase1End = numFrames / 3;      // First third: silence
                    int phase2End = 2 * numFrames / 3;  // Second third: echo present
                    // Final third: silence again

                    double totalNearPower = 0;
                    double totalOutputPower = 0;
                    int activeFrames = 0;

                    var nearEndBuffer = new List<short>();
                    var farEndBuffer = new List<short>();
                    var processedBuffer = new List<short>();

                    int delayFrames = 2;

                    for (int frame = 0; frame < numFrames; frame++)
                    {
                        int offset = frame * frameSize;

                        // Phase 1: No far-end (silence) - tests baseline
                        if (frame < phase1End)
                        {
                            Array.Clear(farEnd, 0, frameSize);
                            Array.Clear(nearEnd, 0, frameSize);
                        }
                        // Phase 2: Echo present - tests adaptation to sudden echo
                        else if (frame < phase2End)
                        {
                            // Far-end from audio file
                            for (int i = 0; i < frameSize; i++)
                            {
                                if (offset + i < audioSamples.Length)
                                    farEnd[i] = audioSamples[offset + i];
                                else
                                    farEnd[i] = 0;
                            }

                            // Near-end has delayed echo (40% of far-end)
                            if (frame >= phase1End + delayFrames)
                            {
                                int echoOffset = (frame - delayFrames) * frameSize;
                                for (int i = 0; i < frameSize; i++)
                                {
                                    if (echoOffset + i < audioSamples.Length)
                                        nearEnd[i] = (short)(audioSamples[echoOffset + i] * 0.4);
                                    else
                                        nearEnd[i] = 0;
                                }
                            }
                            else
                            {
                                Array.Clear(nearEnd, 0, frameSize);
                            }
                        }
                        // Phase 3: Return to silence - tests adaptation to echo removal
                        else
                        {
                            Array.Clear(farEnd, 0, frameSize);
                            Array.Clear(nearEnd, 0, frameSize);
                        }

                        var frameStopwatch = Stopwatch.StartNew();
                        aec.Process(nearEnd, farEnd, output);
                        frameStopwatch.Stop();
                        totalProcessingTicks += frameStopwatch.ElapsedTicks;

                        nearEndBuffer.AddRange(nearEnd);
                        farEndBuffer.AddRange(farEnd);
                        processedBuffer.AddRange(output);

                        // Measure ERLE during phase 2 after adaptation period
                        if (frame >= phase1End + 30 && frame < phase2End - 10)
                        {
                            double nearPower = ComputePower(nearEnd);
                            double outPower = ComputePower(output);

                            if (nearPower > 1e6)
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

                    Console.WriteLine($"  Test Structure:");
                    Console.WriteLine($"    Frames 0-{phase1End}: Silence (no echo)");
                    Console.WriteLine($"    Frames {phase1End}-{phase2End}: Echo present (40% delayed far-end)");
                    Console.WriteLine($"    Frames {phase2End}-{numFrames}: Silence again");

                    if (activeFrames > 10)
                    {
                        double avgNearPower = totalNearPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"  ERLE (during echo phase): {erle:F2} dB (measured over {activeFrames} active frames)");

                        if (erle > 15.0)
                            Console.WriteLine($"  ✓ PASSED (ERLE > 15 dB, good adaptation)");
                        else if (erle > 10.0)
                            Console.WriteLine($"  ⚠ Moderate performance (ERLE 10-15 dB)");
                        else
                            Console.WriteLine($"  ⚠ Lower than target (>15 dB)");
                    }
                    else
                    {
                        Console.WriteLine($"  ⚠ Not enough active frames to measure ERLE");
                    }

                    Console.WriteLine($"  Processing Time: {avgProcessingTimeMs:F3} ms/frame ({totalStopwatch.Elapsed.TotalMilliseconds:F1} ms total)");
                    Console.WriteLine($"  Real-Time Factor: {realTimeFactor:F2}x (audio length: {audioLengthMs:F1} ms)");

                    // Save output files
                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string nearEndFile = $"{baseName}_sudden_nearend.wav";
                    string farEndFile = $"{baseName}_sudden_farend.wav";
                    string processedFile = $"{baseName}_sudden_processed.wav";

                    SaveWavFile(nearEndFile, nearEndBuffer.ToArray(), sampleRate);
                    SaveWavFile(farEndFile, farEndBuffer.ToArray(), sampleRate);
                    SaveWavFile(processedFile, processedBuffer.ToArray(), sampleRate);

                    Console.WriteLine($"  Saved: {nearEndFile}, {farEndFile}, {processedFile}");
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
