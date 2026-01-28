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

namespace Concentus.EchoCancellation
{
    using Concentus.Celt.Structs;
    using System;

    /// <summary>
    /// Internal state for MDF (Multi-Delay Filter) acoustic echo cancellation
    /// </summary>
    internal class MdfState
    {
        // Configuration
        internal int sample_rate;       // Sample rate (8000, 16000, 24000, 48000 Hz)
        internal int frame_size;        // Frame size in samples (64-256)
        internal int filter_length;     // Filter length in samples (tail length)
        internal int nb_blocks;         // Number of filter blocks (filter_length / frame_size)

        // FFT state
        internal FFTState fft_table;    // FFT state for 2*frame_size
        internal int fft_size;          // FFT size (2*frame_size)
        internal short[] window;        // Analysis window (frame_size samples, Q15)

        // Buffers - Input/output ring buffers
        internal int[] x;               // Far-end (speaker) history: nb_blocks * frame_size samples
        internal int[] y;               // Near-end (microphone) history: frame_size samples
        internal int[] last_y;          // Last near-end frame (for residual echo)
        internal int[] e;               // Error signal (output) buffer: frame_size samples

        // Frequency domain buffers (size = fft_size + 2 for complex)
        internal int[] X;               // Far-end spectrum (2*fft_size for real+imag)
        internal int[] Y;               // Near-end spectrum
        internal int[] E;               // Error spectrum
        internal int[] PHI;             // Foreground filter output spectrum

        // Adaptive filter coefficients (frequency domain)
        internal int[] W;               // Foreground filter weights: (fft_size/2 + 1) * nb_blocks
        internal int[] foreground;      // Foreground filter work buffer

        // Background filter for adaptation (shadow filter)
        internal int[] Wtmp;            // Background filter weights (same size as W)
        internal int Davg1;             // Foreground divergence metric
        internal int Davg2;             // Background divergence metric
        internal int Dvar1;             // Foreground variance
        internal int Dvar2;             // Background variance

        // Power estimates (size = fft_size/2 + 1)
        internal int[] power;           // Input power spectrum
        internal int[] power_1;         // Previous input power (for smoothing)
        internal int[] Yf;              // Far-end power spectrum
        internal int[] Rf;              // Residual power spectrum
        internal int[] Xf;              // Near-end power spectrum
        internal int[] Eh;              // Echo power estimate
        internal int[] Yh;              // Output power estimate

        // Adaptation state
        internal int[] prop;            // Proportionate weights for NLMS
        internal float[] wtmp2;         // Temporary weight storage (for background filter)
        internal int sum_adapt;         // Sum for proportionate adaptation
        internal float leak_estimate;   // Echo path leak estimate (0-1)
        internal int adapted;           // Boolean: has filter adapted?
        internal int saturated;         // Boolean: is signal saturated?
        internal int screwed_up;        // Counter for divergence detection

        // DC notch filter state (IIR highpass)
        internal int[] notch_mem;       // Notch filter memory: 2 samples per channel

        // Residual echo suppression state
        internal int[] residual_echo;   // Residual echo power spectrum
        internal int[] echo_noise;      // Smoothed residual echo estimate
        internal float[] gain;          // Suppression gain per frequency bin

        // Ring buffer pointers
        internal int x_insert_pos;      // Write position in x buffer

        // Statistics
        internal long frame_count;      // Number of frames processed
    }
}
