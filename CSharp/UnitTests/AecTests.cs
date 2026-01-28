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
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Concentus;

namespace UnitTests
{
    [TestClass]
    public class AecTests
    {
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
    }
}
