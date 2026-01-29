/* Live audio test using macOS CoreAudio */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Concentus;

namespace AecTestConsole
{
    /// <summary>
    /// Tests AEC with live audio: plays to speakers and records from microphone
    /// </summary>
    internal static class TestLiveAudio
    {
        private const int SAMPLE_RATE = 16000;
        private const int FRAME_SIZE = 128;
        private const int CHANNELS = 1;

        public static void Run()
        {
            Console.WriteLine("\n=== Live Audio Test (macOS CoreAudio) ===");
            Console.WriteLine("This test will play audio through speakers and record from microphone.");
            Console.WriteLine("Point your microphone at the speaker for echo cancellation testing.\n");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.WriteLine("⚠ This test only works on macOS");
                return;
            }

            TestLiveEchoCancellation("../../AudioData/16Khz Mono.raw");
        }

        private static void TestLiveEchoCancellation(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"File not found: {filePath}");
                return;
            }

            Console.WriteLine($"Testing with: {Path.GetFileName(filePath)}");
            Console.WriteLine("Press Enter to start playback and recording (duration: ~10 seconds)...");
            Console.ReadLine();

            try
            {
                // Load audio file
                byte[] fileBytes = File.ReadAllBytes(filePath);
                short[] audioSamples = BytesToShorts(fileBytes);

                // Limit to reasonable duration
                int maxFrames = Math.Min(audioSamples.Length / FRAME_SIZE, 1250); // ~10 seconds at 16kHz
                int totalSamples = maxFrames * FRAME_SIZE;

                Console.WriteLine($"Starting playback and recording for {maxFrames} frames...");

                var recordedSamples = new List<short>();
                var processedSamples = new List<short>();
                var farEndSamples = new List<short>();

                double totalNearPower = 0;
                double totalOutputPower = 0;
                int activeFrames = 0;

                using (var aec = AecFactory.CreateMdf(SAMPLE_RATE, FRAME_SIZE, 2432))
                {
                    var stopwatch = Stopwatch.StartNew();

                    // Use afplay for playback and sox for recording (if available)
                    // For simplicity, we'll use a subprocess-based approach
                    var result = RunLiveAudioTest(audioSamples, totalSamples, aec,
                        recordedSamples, processedSamples, farEndSamples,
                        ref totalNearPower, ref totalOutputPower, ref activeFrames);

                    stopwatch.Stop();

                    if (!result)
                    {
                        Console.WriteLine("⚠ Live audio test failed - audio subsystem error");
                        return;
                    }

                    Console.WriteLine($"\nProcessing completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

                    // Calculate ERLE
                    if (activeFrames > 10)
                    {
                        double avgNearPower = totalNearPower / activeFrames;
                        double avgOutputPower = totalOutputPower / activeFrames;
                        double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                        Console.WriteLine($"\nResults:");
                        Console.WriteLine($"  Active Frames: {activeFrames}");
                        Console.WriteLine($"  ERLE: {erle:F2} dB");

                        if (erle > 10.0)
                            Console.WriteLine($"  ✓ Echo cancellation working (ERLE > 10 dB)");
                        else if (erle > 5.0)
                            Console.WriteLine($"  ⚠ Moderate cancellation (ERLE 5-10 dB)");
                        else if (erle > 0)
                            Console.WriteLine($"  ⚠ Low cancellation (ERLE < 5 dB)");
                        else
                            Console.WriteLine($"  ⚠ No cancellation detected (ERLE negative)");
                    }
                    else
                    {
                        Console.WriteLine("⚠ Not enough active audio detected");
                    }

                    // Save output files
                    string baseName = Path.GetFileNameWithoutExtension(filePath);
                    string micFile = $"{baseName}_live_mic.wav";
                    string processedFile = $"{baseName}_live_processed.wav";
                    string farEndFile = $"{baseName}_live_farend.wav";

                    SaveWavFile(micFile, recordedSamples.ToArray(), SAMPLE_RATE);
                    SaveWavFile(processedFile, processedSamples.ToArray(), SAMPLE_RATE);
                    SaveWavFile(farEndFile, farEndSamples.ToArray(), SAMPLE_RATE);

                    Console.WriteLine($"\nSaved files:");
                    Console.WriteLine($"  {micFile} - Raw microphone input");
                    Console.WriteLine($"  {processedFile} - AEC processed output");
                    Console.WriteLine($"  {farEndFile} - Speaker playback reference");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ FAILED: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private static bool RunLiveAudioTest(short[] audioFile, int totalSamples,
            IAcousticEchoCanceller aec,
            List<short> recordedSamples, List<short> processedSamples, List<short> farEndSamples,
            ref double totalNearPower, ref double totalOutputPower, ref int activeFrames)
        {
            try
            {
                // Create temporary WAV file for playback
                string tempPlayFile = Path.GetTempFileName() + ".wav";
                SaveWavFile(tempPlayFile, audioFile[..totalSamples], SAMPLE_RATE);

                // Start playback using afplay (background)
                var playProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "afplay",
                        Arguments = $"\"{tempPlayFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                // Start recording using sox (if available) or rec
                string tempRecFile = Path.GetTempFileName() + ".wav";
                double durationSeconds = (double)totalSamples / SAMPLE_RATE;

                var recProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "sox",
                        Arguments = $"-d -r {SAMPLE_RATE} -c 1 -b 16 \"{tempRecFile}\" trim 0 {durationSeconds:F2}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    }
                };

                Console.WriteLine("Starting audio I/O...");
                Console.WriteLine($"Recording duration: {durationSeconds:F2} seconds");

                // Start recording first
                bool recStarted = false;
                try
                {
                    recProcess.Start();
                    recStarted = true;
                    Console.WriteLine("Recording started with sox...");

                    // Start reading stderr to capture any error messages
                    var errorReader = recProcess.StandardError;
                    var outputReader = recProcess.StandardOutput;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ sox failed to start: {ex.Message}");
                    Console.WriteLine("Trying rec...");
                    recProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "rec",
                            Arguments = $"-r {SAMPLE_RATE} -c 1 -b 16 \"{tempRecFile}\" trim 0 {durationSeconds:F2}",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true
                        }
                    };

                    try
                    {
                        recProcess.Start();
                        recStarted = true;
                        Console.WriteLine("Recording started with rec...");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"✗ Recording failed: {ex2.Message}");
                        Console.WriteLine("Install sox with: brew install sox");
                        Console.WriteLine("\nAlternatively, check your system audio settings:");
                        Console.WriteLine("  System Preferences > Security & Privacy > Privacy > Microphone");
                        Console.WriteLine("  Make sure Terminal or your terminal app has microphone access.");
                        return false;
                    }
                }

                if (!recStarted)
                {
                    Console.WriteLine("✗ Failed to start recording");
                    return false;
                }

                // Give recording a moment to initialize
                Thread.Sleep(500);

                // Start playback
                Console.WriteLine("Playing audio...");
                playProcess.Start();

                // Wait for both to complete
                Console.WriteLine("Waiting for playback to complete...");
                playProcess.WaitForExit();

                Console.WriteLine("Waiting for recording to complete...");

                // Give recording a bit more time if needed
                if (!recProcess.WaitForExit(TimeSpan.FromSeconds(durationSeconds + 5)))
                {
                    Console.WriteLine("⚠ Recording process timeout, killing...");
                    try
                    {
                        recProcess.Kill();
                    }
                    catch { }
                }

                // Read any error output
                if (recProcess.HasExited)
                {
                    try
                    {
                        string errors = recProcess.StandardError.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(errors))
                        {
                            Console.WriteLine($"Recording stderr: {errors}");
                        }
                    }
                    catch { }
                }

                Console.WriteLine("Playback and recording completed.");

                // Check if recording file was created and has content
                if (!File.Exists(tempRecFile))
                {
                    Console.WriteLine("✗ Recording file not created");
                    Console.WriteLine("This usually means:");
                    Console.WriteLine("  1. sox/rec doesn't have microphone permission");
                    Console.WriteLine("  2. No default microphone is configured");
                    Console.WriteLine("  3. The recording command failed");
                    return false;
                }

                var fileInfo = new FileInfo(tempRecFile);
                Console.WriteLine($"Recording file size: {fileInfo.Length} bytes");

                if (fileInfo.Length < 1000)
                {
                    Console.WriteLine("⚠ Recording file is too small, likely empty");
                    return false;
                }

                var recordedData = ReadWavFile(tempRecFile, out int recSampleRate);
                if (recordedData == null || recordedData.Length == 0)
                {
                    Console.WriteLine("✗ No audio recorded");
                    return false;
                }

                Console.WriteLine($"Recorded {recordedData.Length} samples ({recordedData.Length / (double)SAMPLE_RATE:F2} seconds)");
                Console.WriteLine("Processing through AEC...");

                // Process through AEC frame by frame
                int numFrames = Math.Min(recordedData.Length / FRAME_SIZE, totalSamples / FRAME_SIZE);

                for (int frame = 0; frame < numFrames; frame++)
                {
                    int offset = frame * FRAME_SIZE;

                    // Extract frames
                    short[] nearEnd = new short[FRAME_SIZE];
                    short[] farEnd = new short[FRAME_SIZE];
                    short[] output = new short[FRAME_SIZE];

                    // Microphone input
                    Array.Copy(recordedData, offset, nearEnd, 0, Math.Min(FRAME_SIZE, recordedData.Length - offset));

                    // Far-end reference from file
                    if (offset + FRAME_SIZE <= audioFile.Length)
                        Array.Copy(audioFile, offset, farEnd, 0, FRAME_SIZE);

                    // Process
                    aec.Process(nearEnd, farEnd, output);

                    recordedSamples.AddRange(nearEnd);
                    farEndSamples.AddRange(farEnd);
                    processedSamples.AddRange(output);

                    // Measure ERLE after adaptation period
                    if (frame >= 30)
                    {
                        double nearPower = ComputePower(nearEnd);
                        double outputPower = ComputePower(output);

                        if (nearPower > 1e6) // Active speech threshold
                        {
                            totalNearPower += nearPower;
                            totalOutputPower += outputPower;
                            activeFrames++;
                        }
                    }
                }

                // Clean up temp files
                try
                {
                    File.Delete(tempPlayFile);
                    File.Delete(tempRecFile);
                }
                catch { }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in audio I/O: {ex.Message}");
                return false;
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

        private static short[] ReadWavFile(string filename, out int sampleRate)
        {
            sampleRate = 0;
            try
            {
                using (var stream = new FileStream(filename, FileMode.Open))
                using (var reader = new BinaryReader(stream))
                {
                    // Read RIFF header
                    string riff = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (riff != "RIFF")
                        return null;

                    reader.ReadInt32(); // File size
                    string wave = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (wave != "WAVE")
                        return null;

                    // Read fmt chunk
                    string fmt = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (fmt != "fmt ")
                        return null;

                    int fmtSize = reader.ReadInt32();
                    short audioFormat = reader.ReadInt16();
                    short numChannels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32(); // Byte rate
                    reader.ReadInt16(); // Block align
                    short bitsPerSample = reader.ReadInt16();

                    // Skip any extra fmt bytes
                    if (fmtSize > 16)
                        reader.ReadBytes(fmtSize - 16);

                    // Find data chunk
                    while (stream.Position < stream.Length)
                    {
                        string chunkId = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
                        int chunkSize = reader.ReadInt32();

                        if (chunkId == "data")
                        {
                            // Read samples
                            int numSamples = chunkSize / 2;
                            short[] samples = new short[numSamples];

                            for (int i = 0; i < numSamples; i++)
                            {
                                samples[i] = reader.ReadInt16();
                            }

                            // Convert stereo to mono if needed
                            if (numChannels == 2)
                            {
                                short[] mono = new short[numSamples / 2];
                                for (int i = 0; i < mono.Length; i++)
                                {
                                    mono[i] = (short)((samples[i * 2] + samples[i * 2 + 1]) / 2);
                                }
                                return mono;
                            }

                            return samples;
                        }
                        else
                        {
                            // Skip unknown chunk
                            stream.Seek(chunkSize, SeekOrigin.Current);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading WAV file: {ex.Message}");
            }

            return null;
        }
    }
}
