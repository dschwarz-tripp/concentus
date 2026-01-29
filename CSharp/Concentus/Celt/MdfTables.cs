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

namespace Concentus.Celt
{
    using System;

    /// <summary>
    /// Tables and constants for MDF acoustic echo cancellation
    /// </summary>
    internal static class MdfTables
    {
        // DC notch filter parameters (second-order highpass at ~50Hz)
        // Transfer function: H(z) = (1 - z^-1) / (1 - r*z^-1) where r ≈ 0.99
        internal const int NOTCH_RADIUS_Q15 = 32440; // 0.99 in Q15 format

        // MDF adaptation parameters
        internal const float MIN_LEAK = 0.005f;     // Minimum leak estimate
        internal const float DEFAULT_LEAK = 0.25f;   // Default/starting leak
        internal const float LEAK_ESTIMATE_TIME = 3.0f; // Time constant for leak adaptation (seconds)

        // Proportionate NLMS parameters
        internal const float MIN_PROP_COEF = 0.01f;  // Minimum proportionate coefficient
        internal const float PROP_SMOOTH = 0.1f;      // Smoothing factor for proportionate weights

        // Residual echo suppression
        internal const float RES_ECHO_SMOOTH = 0.6f;    // Smoothing for residual echo estimate
        internal const float MIN_GAIN = 0.01f;          // Minimum suppression gain (-40 dB)
        internal const float MIN_GAIN_ACTIVE = 0.18f;   // Minimum gain during speech (-15 dB)

        // Power spectrum regularization
        internal const float MIN_POWER = 1e-10f;      // Minimum power to avoid division by zero
        internal const int MIN_POWER_Q15 = 1;         // Minimum power in fixed-point

        // Double-talk detection (Geigel algorithm)
        internal const float DTD_THRESHOLD = 0.5f;      // Near/far ratio threshold
        internal const int DTD_HANGOVER_FRAMES = 25;    // Hold DTD state for 25 frames (~50ms at 16kHz/128 frame)
        internal const int DTD_HISTORY_LENGTH = 32;     // Far-end peak history size
        internal const int DTD_MIN_FAR_PEAK = 100;      // Minimum far-end peak to consider for DTD

        // Background filter parameters
        internal const float BACKGROUND_STEP_SIZE = 0.7f;  // Larger step size for background filter
        internal const float DIVERGENCE_SMOOTH = 0.9f;     // Smoothing factor for divergence metrics
        internal const float SWAP_THRESHOLD = 0.8f;        // Swap if Davg2 < 0.8 * Davg1
        internal const int SWAP_COUNT_THRESHOLD = 5;       // Frames before swapping filters

        // Divergence detection
        internal const int MAX_SCREWED_UP = 10;            // Reset after 10 diverged frames
        internal const float MAX_FILTER_ENERGY = 1e8f;     // Maximum filter coefficient energy

        /// <summary>
        /// Generate half-Hanning window for overlap-add processing
        /// </summary>
        /// <param name="size">Window size (typically frame_size)</param>
        /// <returns>Window coefficients in Q15 format</returns>
        internal static short[] generate_window(int size)
        {
            short[] window = new short[size];
            for (int i = 0; i < size; i++)
            {
                // Hanning window: 0.5 * (1 - cos(2*pi*i/(2*size)))
                double phase = Math.PI * i / size;
                double value = Math.Sin(phase);
                window[i] = (short)(32767.0 * value + 0.5);
            }
            return window;
        }

        /// <summary>
        /// Generate precomputed scaling factors for different FFT sizes
        /// </summary>
        /// <param name="fftSize">FFT size</param>
        /// <returns>Scaling factor in Q15</returns>
        internal static short get_fft_scale(int fftSize)
        {
            // Normalize by sqrt(fftSize) for proper power scaling
            double scale = 1.0 / Math.Sqrt(fftSize);
            return (short)(32767.0 * scale + 0.5);
        }
    }
}
