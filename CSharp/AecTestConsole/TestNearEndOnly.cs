using System;
using System.IO;
using Concentus;

namespace AecTestConsole
{
    /// <summary>
    /// Test to verify AEC preserves near-end speech when there's no far-end signal
    /// </summary>
    public static class TestNearEndOnly
    {
        public static void Run()
        {
            Console.WriteLine("\n=== Testing Near-End Speech Preservation (No Echo) ===");
            Console.WriteLine("This simulates speaking into the mic with no speaker output.");
            Console.WriteLine("Expected: Near-end speech should pass through mostly unchanged.\n");

            TestWithRealAudio("../../AudioData/16Khz Mono.raw", 16000);
        }

        static void TestWithRealAudio(string filePath, int sampleRate)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }

            Console.WriteLine($"Testing with: {Path.GetFileName(filePath)}");

            byte[] fileBytes = File.ReadAllBytes(filePath);
            short[] audioSamples = BytesToShorts(fileBytes);

            int frameSize = 128;
            using (var aec = AecFactory.CreateMdf(sampleRate, frameSize, 1024))
            {
                int numFrames = Math.Min(audioSamples.Length / frameSize, 200);

                short[] nearEnd = new short[frameSize];
                short[] farEnd = new short[frameSize]; // Always zero - no speaker output
                short[] output = new short[frameSize];

                var nearEndBuffer = new System.Collections.Generic.List<short>();
                var outputBuffer = new System.Collections.Generic.List<short>();

                double totalInputPower = 0;
                double totalOutputPower = 0;
                int activeFrames = 0;

                for (int frame = 0; frame < numFrames; frame++)
                {
                    int offset = frame * frameSize;
                    Array.Copy(audioSamples, offset, nearEnd, 0, frameSize);

                    // Far-end is silent (no speaker output)
                    Array.Clear(farEnd, 0, frameSize);

                    aec.Process(nearEnd, farEnd, output);

                    nearEndBuffer.AddRange(nearEnd);
                    outputBuffer.AddRange(output);

                    // Measure signal preservation (skip adaptation period)
                    if (frame >= 20)
                    {
                        double inputPower = ComputePower(nearEnd);
                        double outputPower = ComputePower(output);

                        if (inputPower > 1e6) // Active speech frame
                        {
                            totalInputPower += inputPower;
                            totalOutputPower += outputPower;
                            activeFrames++;
                        }
                    }
                }

                // Calculate signal preservation ratio
                if (activeFrames > 0)
                {
                    double avgInputPower = totalInputPower / activeFrames;
                    double avgOutputPower = totalOutputPower / activeFrames;
                    double preservationRatio = avgOutputPower / avgInputPower;
                    double preservationDb = 10.0 * Math.Log10(preservationRatio);

                    Console.WriteLine($"Active Frames: {activeFrames}");
                    Console.WriteLine($"Input Power: {avgInputPower:E2}");
                    Console.WriteLine($"Output Power: {avgOutputPower:E2}");
                    Console.WriteLine($"Signal Preservation: {preservationDb:F2} dB (ratio: {preservationRatio:F3})");

                    if (preservationDb > -3.0) // Should preserve at least 70% of power
                    {
                        Console.WriteLine($"✓ PASSED - Near-end speech preserved (> -3 dB)");
                    }
                    else if (preservationDb > -10.0)
                    {
                        Console.WriteLine($"⚠ WARNING - Some signal loss ({preservationDb:F2} dB)");
                    }
                    else
                    {
                        Console.WriteLine($"✗ FAILED - Near-end speech heavily attenuated ({preservationDb:F2} dB)");
                        Console.WriteLine("  This indicates the AEC is incorrectly cancelling near-end speech!");
                    }
                }

                // Save output files
                string baseName = Path.GetFileNameWithoutExtension(filePath);
                string nearEndFile = $"{baseName}_nearend_only.wav";
                string processedFile = $"{baseName}_nearend_only_processed.wav";

                SaveWavFile(nearEndFile, nearEndBuffer.ToArray(), sampleRate);
                SaveWavFile(processedFile, outputBuffer.ToArray(), sampleRate);

                Console.WriteLine($"\nSaved: {nearEndFile}");
                Console.WriteLine($"Saved: {processedFile}");
                Console.WriteLine("Listen to these files to compare input vs output.");
            }
        }

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

        static void SaveWavFile(string filename, short[] samples, int sampleRate)
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
                writer.Write((short)1);
                writer.Write((short)1);
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
