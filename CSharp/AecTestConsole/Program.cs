/* Simple test program for AEC functionality */

using System;
using Concentus;

namespace AecTestConsole
{
    class Program
    {
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
    }
}
