/* Copyright (c) 2003-2008 Jean-Marc Valin
   Copyright (c) 2007-2008 CSIRO
   Copyright (c) 2007-2011 Xiph.Org Foundation
   Ported to C# by Logan Stromberg for Concentus

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions
   are met:

   - Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.

   - Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.

   - Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER
   OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
   EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
   PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
   LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
   NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
   SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Concentus;

namespace UnitTests
{
    [TestClass]
    public class AecTests
    {
        private static IDictionary<string, short[]> testAudioFiles = new Dictionary<string, short[]>();

        private static string GetAudioFileName(int sampleRateKhz, bool stereo)
        {
            return string.Format("{0}Khz {1}.raw", sampleRateKhz, stereo ? "Stereo" : "Mono");
        }

        private static void LoadAudioFile(int sampleRateKhz, bool stereo)
        {
            string fileName = GetAudioFileName(sampleRateKhz, stereo);
            if (!File.Exists(fileName))
            {
                return; // Skip if file doesn't exist
            }
            byte[] fileBytes = File.ReadAllBytes(fileName);
            short[] samples = BytesToShorts(fileBytes);
            testAudioFiles.Add(fileName, samples);
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

        [ClassInitialize]
        public static void TestInitialize(TestContext context)
        {
            // Try to load audio files (may not exist in all test environments)
            LoadAudioFile(8, false);
            LoadAudioFile(16, false);
            LoadAudioFile(24, false);
            LoadAudioFile(48, false);
        }
        /// <summary>
        /// Test basic echo cancellation with synthetic echo
        /// </summary>
        [TestMethod]
        public void TestBasicEchoCancellation()
        {
            int sampleRate = 16000;
            int frameSize = 128;
            int filterLength = 2400; // 150ms at 16kHz

            using (var aec = AecFactory.CreateMdf(sampleRate, frameSize, filterLength))
            {
                Assert.AreEqual(sampleRate, aec.SampleRate);
                Assert.AreEqual(frameSize, aec.FrameSize);
                Assert.AreEqual(filterLength, aec.FilterLength);

                // Generate test signal: 1 kHz sine wave
                short[] farEnd = GenerateSineWave(1000.0, sampleRate, frameSize);

                // Simulate echo: delayed and attenuated far-end
                int echoDelay = 800; // 50ms at 16kHz
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                // First few frames: just far-end playing (no speech)
                for (int frame = 0; frame < 20; frame++)
                {
                    // Near-end is silent initially
                    Array.Clear(nearEnd, 0, nearEnd.Length);
                    aec.Process(nearEnd, farEnd, output);
                }

                // Now add echo to near-end
                for (int i = 0; i < frameSize; i++)
                {
                    // Echo is 50% of far-end signal
                    nearEnd[i] = (short)(farEnd[i] / 2);
                }

                // Process several frames to let filter adapt
                for (int frame = 0; frame < 50; frame++)
                {
                    aec.Process(nearEnd, farEnd, output);
                }

                // Check that echo is reduced
                double nearEndPower = ComputePower(nearEnd);
                double outputPower = ComputePower(output);
                double echoReduction = 10.0 * Math.Log10(nearEndPower / Math.Max(outputPower, 1e-10));

                // After adaptation, should get at least 10 dB of echo reduction
                Assert.IsTrue(echoReduction > 10.0,
                    $"Expected at least 10 dB echo reduction, got {echoReduction:F2} dB");
            }
        }

        /// <summary>
        /// Test that DC offset is removed by notch filter
        /// </summary>
        [TestMethod]
        public void TestDcOffsetRemoval()
        {
            int sampleRate = 16000;
            int frameSize = 128;
            int filterLength = 1600;

            using (var aec = AecFactory.CreateMdf(sampleRate, frameSize, filterLength))
            {
                // Generate signal with DC offset
                short[] farEnd = new short[frameSize];
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                // Add DC offset of 1000
                for (int i = 0; i < frameSize; i++)
                {
                    farEnd[i] = 1000;
                    nearEnd[i] = 1000;
                }

                // Process a few frames
                for (int frame = 0; frame < 10; frame++)
                {
                    aec.Process(nearEnd, farEnd, output);
                }

                // Check that output DC is near zero (within tolerance)
                double meanOutput = 0;
                for (int i = 0; i < frameSize; i++)
                {
                    meanOutput += output[i];
                }
                meanOutput /= frameSize;

                Assert.IsTrue(Math.Abs(meanOutput) < 100,
                    $"Expected DC near zero, got {meanOutput:F2}");
            }
        }

        /// <summary>
        /// Test reset functionality
        /// </summary>
        [TestMethod]
        public void TestReset()
        {
            int sampleRate = 16000;
            int frameSize = 128;
            int filterLength = 1600;

            using (var aec = AecFactory.CreateMdf(sampleRate, frameSize, filterLength))
            {
                short[] farEnd = GenerateSineWave(1000.0, sampleRate, frameSize);
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                // Process several frames to adapt
                for (int frame = 0; frame < 30; frame++)
                {
                    for (int i = 0; i < frameSize; i++)
                        nearEnd[i] = (short)(farEnd[i] / 2); // Echo
                    aec.Process(nearEnd, farEnd, output);
                }

                // Reset should clear state
                aec.Reset();

                // After reset, filter should be back to initial state
                // Process one frame and check output is similar to input (no adaptation yet)
                aec.Process(nearEnd, farEnd, output);

                double nearPower = ComputePower(nearEnd);
                double outPower = ComputePower(output);
                double ratio = outPower / nearPower;

                // Right after reset, output should be similar to input (ratio near 1.0)
                Assert.IsTrue(ratio > 0.5 && ratio < 2.0,
                    $"Expected output similar to input after reset, got ratio {ratio:F2}");
            }
        }

        /// <summary>
        /// Test factory method with default settings
        /// </summary>
        [TestMethod]
        public void TestFactoryDefaults()
        {
            // Test each supported sample rate
            int[] sampleRates = { 8000, 16000, 24000, 48000 };

            foreach (int sampleRate in sampleRates)
            {
                using (var aec = AecFactory.CreateMdf(sampleRate))
                {
                    Assert.AreEqual(sampleRate, aec.SampleRate);
                    Assert.AreEqual(128, aec.FrameSize); // Default frame size
                    Assert.IsTrue(aec.FilterLength > 0);

                    // Verify filter length makes sense for the sample rate
                    double tailMs = (double)aec.FilterLength / sampleRate * 1000.0;
                    Assert.IsTrue(tailMs >= 100 && tailMs <= 250,
                        $"Expected tail 100-250ms, got {tailMs:F1}ms for {sampleRate} Hz");
                }
            }
        }

        /// <summary>
        /// Test invalid parameters throw exceptions
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestInvalidSampleRate()
        {
            using (var aec = AecFactory.CreateMdf(11025, 128, 1280))
            {
                // Should throw
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestInvalidFrameSize()
        {
            using (var aec = AecFactory.CreateMdf(16000, 100, 1600)) // 100 is not power of 2
            {
                // Should throw
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestInvalidFilterLength()
        {
            using (var aec = AecFactory.CreateMdf(16000, 128, 1000)) // 1000 not multiple of 128
            {
                // Should throw
            }
        }

        /// <summary>
        /// Test double-talk scenario (speech + echo)
        /// </summary>
        [TestMethod]
        public void TestDoubleTalk()
        {
            int sampleRate = 16000;
            int frameSize = 128;
            int filterLength = 2400;

            using (var aec = AecFactory.CreateMdf(sampleRate, frameSize, filterLength))
            {
                // Far-end: 1 kHz sine
                short[] farEnd = GenerateSineWave(1000.0, sampleRate, frameSize);

                // Near-end: 500 Hz sine (speech) + echo
                short[] speech = GenerateSineWave(500.0, sampleRate, frameSize);
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                // Let filter adapt first with just echo
                for (int frame = 0; frame < 30; frame++)
                {
                    for (int i = 0; i < frameSize; i++)
                        nearEnd[i] = (short)(farEnd[i] / 2);
                    aec.Process(nearEnd, farEnd, output);
                }

                // Now add speech
                for (int i = 0; i < frameSize; i++)
                    nearEnd[i] = (short)(speech[i] + farEnd[i] / 2);

                // Process double-talk
                for (int frame = 0; frame < 10; frame++)
                {
                    aec.Process(nearEnd, farEnd, output);
                }

                // Output should preserve speech (500 Hz) while removing echo (1 kHz)
                // Check that output power is not too small (speech preserved)
                double speechPower = ComputePower(speech);
                double outputPower = ComputePower(output);
                double ratio = outputPower / speechPower;

                // Speech should be preserved (ratio not too far from 1.0)
                // Allow 10 dB variation (+/- factor of ~3)
                Assert.IsTrue(ratio > 0.1 && ratio < 10.0,
                    $"Expected speech preservation, got power ratio {ratio:F2}");
            }
        }

        // Helper methods

        private short[] GenerateSineWave(double frequency, int sampleRate, int length)
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

        private double ComputePower(short[] signal)
        {
            double power = 0;
            for (int i = 0; i < signal.Length; i++)
            {
                power += (double)signal[i] * signal[i];
            }
            return power / signal.Length;
        }

        /// <summary>
        /// Test echo cancellation with real audio file (8 kHz)
        /// </summary>
        [TestMethod]
        public void TestEchoCancellationWithRealAudio_8kHz()
        {
            string fileName = GetAudioFileName(8, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            TestEchoCancellationWithRealAudio(8000, testAudioFiles[fileName]);
        }

        /// <summary>
        /// Test echo cancellation with real audio file (16 kHz)
        /// </summary>
        [TestMethod]
        public void TestEchoCancellationWithRealAudio_16kHz()
        {
            string fileName = GetAudioFileName(16, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            TestEchoCancellationWithRealAudio(16000, testAudioFiles[fileName]);
        }

        /// <summary>
        /// Test echo cancellation with real audio file (24 kHz)
        /// </summary>
        [TestMethod]
        public void TestEchoCancellationWithRealAudio_24kHz()
        {
            string fileName = GetAudioFileName(24, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            TestEchoCancellationWithRealAudio(24000, testAudioFiles[fileName]);
        }

        /// <summary>
        /// Test echo cancellation with real audio file (48 kHz)
        /// </summary>
        [TestMethod]
        public void TestEchoCancellationWithRealAudio_48kHz()
        {
            string fileName = GetAudioFileName(48, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            TestEchoCancellationWithRealAudio(48000, testAudioFiles[fileName]);
        }

        /// <summary>
        /// Common test logic for real audio files
        /// </summary>
        private void TestEchoCancellationWithRealAudio(int sampleRate, short[] audioSamples)
        {
            int frameSize = 128;

            using (var aec = AecFactory.CreateMdf(sampleRate))
            {
                Assert.AreEqual(frameSize, aec.FrameSize);

                int numFrames = Math.Min(audioSamples.Length / frameSize, 500); // Test up to 500 frames

                short[] farEnd = new short[frameSize];
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                double totalNearPower = 0;
                double totalOutputPower = 0;
                int powerFrames = 0;

                // Process all frames
                for (int frame = 0; frame < numFrames; frame++)
                {
                    int offset = frame * frameSize;

                    // Use audio as far-end signal
                    Array.Copy(audioSamples, offset, farEnd, 0, frameSize);

                    // Simulate echo: 40% attenuation with 30ms delay
                    int delayFrames = (30 * sampleRate) / (1000 * frameSize); // ~30ms
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

                    // Process echo cancellation
                    aec.Process(nearEnd, farEnd, output);

                    // Skip first 100 frames (adaptation period)
                    if (frame >= 100 && frame < numFrames - 50)
                    {
                        double nearPower = ComputePower(nearEnd);
                        double outPower = ComputePower(output);

                        // Only count frames with significant signal
                        if (nearPower > 1e6) // Threshold for "active" frames
                        {
                            totalNearPower += nearPower;
                            totalOutputPower += outPower;
                            powerFrames++;
                        }
                    }
                }

                // Calculate average ERLE (Echo Return Loss Enhancement)
                if (powerFrames > 0)
                {
                    double avgNearPower = totalNearPower / powerFrames;
                    double avgOutputPower = totalOutputPower / powerFrames;
                    double erle = 10.0 * Math.Log10(avgNearPower / Math.Max(avgOutputPower, 1e-10));

                    // Should achieve at least 15 dB ERLE with real audio
                    Assert.IsTrue(erle > 15.0,
                        $"Expected ERLE > 15 dB at {sampleRate} Hz, got {erle:F2} dB (measured over {powerFrames} frames)");

                    Console.WriteLine($"AEC Performance at {sampleRate} Hz: ERLE = {erle:F2} dB (over {powerFrames} active frames)");
                }
                else
                {
                    Assert.Inconclusive("Not enough active audio frames to measure ERLE");
                }
            }
        }

        /// <summary>
        /// Test echo cancellation with double-talk using real audio
        /// </summary>
        [TestMethod]
        public void TestDoubleTalkWithRealAudio_16kHz()
        {
            string fileName = GetAudioFileName(16, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            short[] audioSamples = testAudioFiles[fileName];
            int sampleRate = 16000;
            int frameSize = 128;

            using (var aec = AecFactory.CreateMdf(sampleRate))
            {
                int numFrames = Math.Min(audioSamples.Length / frameSize, 400);

                short[] farEnd = new short[frameSize];
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                // Generate near-end speech at different frequency content
                short[] nearSpeech = new short[frameSize];

                double totalSpeechPower = 0;
                double totalOutputPower = 0;
                int activeSpeechFrames = 0;

                // First, adapt the filter with just echo (no near-end speech)
                for (int frame = 0; frame < 100; frame++)
                {
                    int offset = frame * frameSize;
                    Array.Copy(audioSamples, offset, farEnd, 0, frameSize);

                    // Near-end = echo only (40% of far-end with delay)
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

                    aec.Process(nearEnd, farEnd, output);
                }

                // Now test double-talk: near-end speech + echo
                for (int frame = 100; frame < numFrames; frame++)
                {
                    int offset = frame * frameSize;
                    Array.Copy(audioSamples, offset, farEnd, 0, frameSize);

                    // Near-end speech from a different part of audio
                    int speechOffset = ((frame * 7) % (numFrames - 10)) * frameSize; // Different timing
                    for (int i = 0; i < frameSize; i++)
                    {
                        nearSpeech[i] = audioSamples[speechOffset + i];
                    }

                    // Near-end = speech + echo
                    int delayFrames = 2;
                    for (int i = 0; i < frameSize; i++)
                    {
                        int echoSample = 0;
                        if (frame >= delayFrames)
                        {
                            int echoIdx = (frame - delayFrames) * frameSize + i;
                            echoSample = (int)(audioSamples[echoIdx] * 0.4);
                        }
                        nearEnd[i] = (short)(nearSpeech[i] + echoSample);
                    }

                    aec.Process(nearEnd, farEnd, output);

                    // Measure how well speech is preserved
                    double speechPower = ComputePower(nearSpeech);
                    double outPower = ComputePower(output);

                    if (speechPower > 1e6) // Active speech
                    {
                        totalSpeechPower += speechPower;
                        totalOutputPower += outPower;
                        activeSpeechFrames++;
                    }
                }

                // Check that near-end speech is preserved
                if (activeSpeechFrames > 0)
                {
                    double avgSpeechPower = totalSpeechPower / activeSpeechFrames;
                    double avgOutputPower = totalOutputPower / activeSpeechFrames;
                    double preservationRatio = avgOutputPower / avgSpeechPower;
                    double preservationDb = 10.0 * Math.Log10(preservationRatio);

                    // Speech should not be attenuated more than 6 dB
                    Assert.IsTrue(preservationDb > -6.0,
                        $"Expected speech preservation > -6 dB, got {preservationDb:F2} dB");

                    Console.WriteLine($"Double-talk speech preservation: {preservationDb:F2} dB (over {activeSpeechFrames} frames)");
                }
                else
                {
                    Assert.Inconclusive("Not enough active speech frames for double-talk test");
                }
            }
        }

        /// <summary>
        /// Test convergence speed with real audio
        /// </summary>
        [TestMethod]
        public void TestConvergenceSpeed_16kHz()
        {
            string fileName = GetAudioFileName(16, false);
            if (!testAudioFiles.ContainsKey(fileName))
            {
                Assert.Inconclusive("Audio file not found: " + fileName);
                return;
            }

            short[] audioSamples = testAudioFiles[fileName];
            int sampleRate = 16000;
            int frameSize = 128;

            using (var aec = AecFactory.CreateMdf(sampleRate))
            {
                int numFrames = Math.Min(audioSamples.Length / frameSize, 300);

                short[] farEnd = new short[frameSize];
                short[] nearEnd = new short[frameSize];
                short[] output = new short[frameSize];

                double[] erleOverTime = new double[numFrames];

                // Process frames and measure ERLE over time
                for (int frame = 0; frame < numFrames; frame++)
                {
                    int offset = frame * frameSize;
                    Array.Copy(audioSamples, offset, farEnd, 0, frameSize);

                    // Simulate echo
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

                    aec.Process(nearEnd, farEnd, output);

                    // Measure ERLE for this frame
                    double nearPower = ComputePower(nearEnd);
                    double outPower = ComputePower(output);

                    if (nearPower > 1e6)
                    {
                        erleOverTime[frame] = 10.0 * Math.Log10(nearPower / Math.Max(outPower, 1e-10));
                    }
                    else
                    {
                        erleOverTime[frame] = 0;
                    }
                }

                // Check convergence: ERLE at frame 50 should be significantly lower than at frame 200
                double erleAt50 = 0;
                double erleAt200 = 0;
                int countAt50 = 0;
                int countAt200 = 0;

                for (int i = 45; i < 55 && i < numFrames; i++)
                {
                    if (erleOverTime[i] > 0)
                    {
                        erleAt50 += erleOverTime[i];
                        countAt50++;
                    }
                }

                for (int i = 195; i < 205 && i < numFrames; i++)
                {
                    if (erleOverTime[i] > 0)
                    {
                        erleAt200 += erleOverTime[i];
                        countAt200++;
                    }
                }

                if (countAt50 > 0 && countAt200 > 0)
                {
                    erleAt50 /= countAt50;
                    erleAt200 /= countAt200;

                    double improvement = erleAt200 - erleAt50;

                    // Should see at least 5 dB improvement from frame 50 to 200
                    Assert.IsTrue(improvement > 5.0,
                        $"Expected convergence improvement > 5 dB, got {improvement:F2} dB (ERLE: {erleAt50:F2} → {erleAt200:F2} dB)");

                    Console.WriteLine($"Convergence: ERLE improved from {erleAt50:F2} dB (frame ~50) to {erleAt200:F2} dB (frame ~200)");
                }
                else
                {
                    Assert.Inconclusive("Not enough data to measure convergence");
                }
            }
        }
    }
}
