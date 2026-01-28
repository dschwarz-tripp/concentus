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

namespace Concentus
{
    using Concentus.EchoCancellation;
    using System;

    /// <summary>
    /// Factory for creating acoustic echo cancellers
    /// </summary>
    public static class AecFactory
    {
        /// <summary>
        /// Create a new MDF (Multi-Delay Filter) echo canceller with recommended settings
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz - must be 8000, 16000, 24000, or 48000</param>
        /// <param name="frameSize">Frame size in samples - must be power of 2 between 64 and 256. 
        /// Typical values: 64-128 for low latency, 256 for better performance</param>
        /// <param name="filterLength">Filter length in samples (echo tail length) - must be multiple of frameSize.
        /// <para>Recommended values:</para>
        /// <para>- 8 kHz: 800 samples (100 ms)</para>
        /// <para>- 16 kHz: 2400 samples (150 ms)</para>
        /// <para>- 24 kHz: 3600 samples (150 ms)</para>
        /// <para>- 48 kHz: 9600 samples (200 ms)</para>
        /// <para>Longer filter length handles longer echo delays but requires more CPU and memory.</para>
        /// </param>
        /// <returns>A new echo canceller instance</returns>
        /// <exception cref="ArgumentException">If parameters are invalid</exception>
        /// <remarks>
        /// The echo canceller uses fixed-point math internally for efficiency and works on mono audio only.
        /// It includes:
        /// - DC notch filtering to remove DC offset
        /// - Frequency-domain adaptive filtering (MDF algorithm)
        /// - Proportionate NLMS weight update
        /// - Inline residual echo suppression
        /// 
        /// Usage pattern:
        /// 1. Create canceller with appropriate sample rate and filter length
        /// 2. Call Process() for each frame, providing far-end (speaker) and near-end (mic) signals
        /// 3. The output will be the near-end signal with echo removed
        /// 4. Call Reset() if needed to clear filter state (e.g., on far-end speaker change)
        /// 5. Dispose when done
        /// </remarks>
        public static IAcousticEchoCanceller CreateMdf(int sampleRate, int frameSize, int filterLength)
        {
            // Validation is done in the constructor
            return new SpeexEchoCanceller(sampleRate, frameSize, filterLength);
        }

        /// <summary>
        /// Create a new MDF echo canceller with recommended default settings for the given sample rate
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz - must be 8000, 16000, 24000, or 48000</param>
        /// <returns>A new echo canceller with default frame size (128) and recommended filter length</returns>
        public static IAcousticEchoCanceller CreateMdf(int sampleRate)
        {
            const int defaultFrameSize = 128;

            // Select recommended filter length based on sample rate
            // Must be multiple of frame size (128)
            int filterLength;
            if (sampleRate == 8000)
                filterLength = 896;       // ~112 ms (7 blocks)
            else if (sampleRate == 16000)
                filterLength = 2432;      // ~152 ms (19 blocks)
            else if (sampleRate == 24000)
                filterLength = 3584;      // ~149 ms (28 blocks)
            else if (sampleRate == 48000)
                filterLength = 9600;      // 200 ms (75 blocks)
            else
                throw new ArgumentException("Sample rate must be 8000, 16000, 24000, or 48000 Hz", nameof(sampleRate));

            return new SpeexEchoCanceller(sampleRate, defaultFrameSize, filterLength);
        }
    }
}
