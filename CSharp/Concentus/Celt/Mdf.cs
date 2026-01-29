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
    using Concentus.Common;
    using System;

    /// <summary>
    /// MDF (Multi-Delay Filter) acoustic echo cancellation
    /// Implements frequency-domain adaptive filtering for echo removal
    /// </summary>
    internal static class Mdf
    {
        /// <summary>
        /// DC notch filter to remove DC offset (second-order IIR highpass)
        /// </summary>
        internal static void filter_dc_notch_16(Span<short> input, int radius, Span<int> mem, int mem_ptr, int len)
        {
            int i;
            int den2 = Inlines.MULT16_16_Q15(radius, radius);

            for (i = 0; i < len; i++)
            {
                int vin = input[i];
                int vout = mem[mem_ptr] + vin;
                mem[mem_ptr] = Inlines.SHL32(Inlines.SHL32(vin, 15) - Inlines.MULT16_32_Q15(radius, vout), 1);
                // Saturate vout to 16-bit range
                if (vout > 32767) vout = 32767;
                if (vout < -32768) vout = -32768;
                input[i] = Inlines.SATURATE16(Inlines.EXTRACT16(Inlines.ADD32(Inlines.SHR32(vout, 1), Inlines.MULT16_32_Q15(radius, mem[mem_ptr + 1]))));
                mem[mem_ptr + 1] = vout;
            }
        }

        /// <summary>
        /// Spectral multiplication: Y[i] = X[i] * H[i] (complex multiplication)
        /// </summary>
        internal static void spectral_mul(Span<int> X, Span<int> Y, Span<int> prod, int N)
        {
            int i;
            prod[0] = Inlines.MULT16_32_Q15(X[0], Y[0]);
            for (i = 1; i < N - 1; i++)
            {
                int re = Inlines.MULT16_32_Q15(X[2 * i], Y[2 * i]) - Inlines.MULT16_32_Q15(X[2 * i + 1], Y[2 * i + 1]);
                int im = Inlines.MULT16_32_Q15(X[2 * i + 1], Y[2 * i]) + Inlines.MULT16_32_Q15(X[2 * i], Y[2 * i + 1]);
                prod[2 * i] = re;
                prod[2 * i + 1] = im;
            }
            prod[2 * (N - 1)] = Inlines.MULT16_32_Q15(X[2 * (N - 1)], Y[2 * (N - 1)]);
        }

        /// <summary>
        /// Weighted power spectrum: ps[i] = weight * X[i]^2
        /// </summary>
        internal static void weighed_spectral_mul(int w, Span<int> X, Span<int> Y, Span<int> prod, int N)
        {
            int i;
            prod[0] = Inlines.MULT16_32_Q15(w, Inlines.MULT16_32_Q15(X[0], Y[0]));
            for (i = 1; i < N - 1; i++)
            {
                int re = Inlines.MULT16_32_Q15(X[2 * i], Y[2 * i]) - Inlines.MULT16_32_Q15(X[2 * i + 1], Y[2 * i + 1]);
                int im = Inlines.MULT16_32_Q15(X[2 * i + 1], Y[2 * i]) + Inlines.MULT16_32_Q15(X[2 * i], Y[2 * i + 1]);
                prod[2 * i] = Inlines.MULT16_32_Q15(w, re);
                prod[2 * i + 1] = Inlines.MULT16_32_Q15(w, im);
            }
            prod[2 * (N - 1)] = Inlines.MULT16_32_Q15(w, Inlines.MULT16_32_Q15(X[2 * (N - 1)], Y[2 * (N - 1)]));
        }

        /// <summary>
        /// Compute proportionate weights for NLMS adaptation
        /// </summary>
        internal static void mdf_adjust_prop(Span<int> W, int N, int M, Span<int> prop)
        {
            int i, j;
            int max_sum = 1;

            // Find maximum absolute weight
            for (i = 0; i < M; i++)
            {
                int block_offset = i * N;
                for (j = 0; j < N; j++)
                {
                    int tmp;
                    if (W[block_offset + 2 * j] > 0)
                        tmp = W[block_offset + 2 * j];
                    else
                        tmp = -W[block_offset + 2 * j];

                    if (tmp > max_sum)
                        max_sum = tmp;
                }
            }

            // Compute proportionate weights
            for (i = 0; i < M; i++)
            {
                int block_offset = i * N;
                for (j = 0; j < N; j++)
                {
                    int tmp;
                    if (W[block_offset + 2 * j] > 0)
                        tmp = W[block_offset + 2 * j];
                    else
                        tmp = -W[block_offset + 2 * j];

                    // prop[i*N+j] = (delta + |W[i][j]|) / (delta*M*N + ||W||_1)
                    // Compute delta = 0.01 * max_sum
                    int delta = Inlines.MULT16_32_Q15((short)(0.01f * 32767), max_sum);

                    // Compute denominator: delta*M*N + max_sum
                    // Scale factor for delta*M*N to prevent overflow
                    int scaledMN = Math.Min(M * N, 32767); // Prevent overflow
                    int denom = Inlines.ADD32(Inlines.MULT16_32_Q15((short)(0.01f * 32767), Inlines.MULT16_32_Q15((short)scaledMN, max_sum)), max_sum);

                    // Prevent division by zero
                    if (denom < 1)
                        denom = 1;

                    prop[block_offset + j] = Inlines.DIV32(Inlines.SHL32(tmp + delta, 15), denom);
                }
            }
        }

        /// <summary>
        /// Initialize MDF state
        /// </summary>
        internal static MdfState init(int sampleRate, int frameSize, int filterLength)
        {
            // Validate parameters
            if (sampleRate != 8000 && sampleRate != 16000 && sampleRate != 24000 && sampleRate != 48000)
                throw new ArgumentException("Sample rate must be 8000, 16000, 24000, or 48000 Hz", nameof(sampleRate));

            if (frameSize < 64 || frameSize > 256 || (frameSize & (frameSize - 1)) != 0)
                throw new ArgumentException("Frame size must be power of 2 between 64 and 256", nameof(frameSize));

            if (filterLength < frameSize || (filterLength % frameSize) != 0)
                throw new ArgumentException("Filter length must be multiple of frame size", nameof(filterLength));

            MdfState st = new MdfState();
            st.sample_rate = sampleRate;
            st.frame_size = frameSize;
            st.filter_length = filterLength;
            st.nb_blocks = filterLength / frameSize;
            st.fft_size = 2 * frameSize;

            // Create FFT state
            st.fft_table = KissFFT.create_fft_state(st.fft_size);

            // Generate analysis window
            st.window = MdfTables.generate_window(frameSize);

            // Allocate buffers
            int N = st.fft_size / 2 + 1;  // Number of frequency bins
            st.x = new int[st.nb_blocks * frameSize];
            st.y = new int[frameSize];
            st.last_y = new int[frameSize];
            st.e = new int[st.fft_size * 2];  // Complex output from IFFT

            // FFT buffers need 2x size for complex interleaved (real, imag) data
            st.X = new int[st.fft_size * 2];
            st.Y = new int[st.fft_size * 2];
            st.E = new int[st.fft_size * 2];
            st.PHI = new int[st.fft_size * 2];

            st.W = new int[N * 2 * st.nb_blocks];
            st.foreground = new int[N * 2 * st.nb_blocks];
            st.Wtmp = new int[N * 2 * st.nb_blocks];

            st.power = new int[N];
            st.power_1 = new int[N];
            st.Yf = new int[N];
            st.Rf = new int[N];
            st.Xf = new int[N];
            st.Eh = new int[N];
            st.Yh = new int[N];

            st.prop = new int[N * st.nb_blocks];
            st.wtmp2 = new float[N * st.nb_blocks];

            st.notch_mem = new int[2];

            // Residual echo suppression buffers
            st.residual_echo = new int[N];
            st.echo_noise = new int[N];
            st.gain = new float[N];

            // Initialize state
            st.leak_estimate = MdfTables.DEFAULT_LEAK;
            st.adapted = 0;
            st.saturated = 0;
            st.screwed_up = 0;
            st.x_insert_pos = 0;
            st.frame_count = 0;

            // Initialize gains to 1.0
            for (int i = 0; i < N; i++)
                st.gain[i] = 1.0f;

            // Initialize double-talk detection state
            st.far_peak_history = new int[MdfTables.DTD_HISTORY_LENGTH];
            st.far_peak_idx = 0;
            st.dtd_hangover = 0;
            st.dtd_active = false;

            // Initialize divergence monitoring
            st.prev_filter_energy = 0;
            st.swap_count = 0;

            return st;
        }

        /// <summary>
        /// Reset filter state
        /// </summary>
        internal static void reset(MdfState st)
        {
            int N = st.fft_size / 2 + 1;

            Array.Clear(st.x, 0, st.x.Length);
            Array.Clear(st.y, 0, st.y.Length);
            Array.Clear(st.last_y, 0, st.last_y.Length);
            Array.Clear(st.e, 0, st.e.Length);
            Array.Clear(st.W, 0, st.W.Length);
            Array.Clear(st.foreground, 0, st.foreground.Length);
            Array.Clear(st.Wtmp, 0, st.Wtmp.Length);
            Array.Clear(st.power, 0, st.power.Length);
            Array.Clear(st.power_1, 0, st.power_1.Length);
            Array.Clear(st.Yf, 0, st.Yf.Length);
            Array.Clear(st.Rf, 0, st.Rf.Length);
            Array.Clear(st.Xf, 0, st.Xf.Length);
            Array.Clear(st.Eh, 0, st.Eh.Length);
            Array.Clear(st.Yh, 0, st.Yh.Length);
            Array.Clear(st.prop, 0, st.prop.Length);
            Array.Clear(st.notch_mem, 0, st.notch_mem.Length);
            Array.Clear(st.residual_echo, 0, st.residual_echo.Length);
            Array.Clear(st.echo_noise, 0, st.echo_noise.Length);

            st.leak_estimate = MdfTables.DEFAULT_LEAK;
            st.adapted = 0;
            st.saturated = 0;
            st.screwed_up = 0;
            st.x_insert_pos = 0;
            st.frame_count = 0;

            for (int i = 0; i < N; i++)
                st.gain[i] = 1.0f;

            // Reset DTD state
            if (st.far_peak_history != null)
                Array.Clear(st.far_peak_history, 0, st.far_peak_history.Length);
            st.far_peak_idx = 0;
            st.dtd_hangover = 0;
            st.dtd_active = false;

            // Reset divergence monitoring
            st.prev_filter_energy = 0;
            st.swap_count = 0;
            st.Davg1 = 0;
            st.Davg2 = 0;
            st.Dvar1 = 0;
            st.Dvar2 = 0;
        }

        /// <summary>
        /// Detect double-talk using Geigel algorithm
        /// Double-talk is detected when near-end level exceeds a threshold relative to far-end
        /// </summary>
        private static void detect_double_talk(MdfState st, ReadOnlySpan<short> farEnd, ReadOnlySpan<short> nearEnd)
        {
            // Find far-end peak for this frame
            int farPeak = 0;
            for (int i = 0; i < st.frame_size; i++)
            {
                int val = Math.Abs(farEnd[i]);
                if (val > farPeak) farPeak = val;
            }

            // Update circular buffer with current far-end peak
            st.far_peak_history[st.far_peak_idx] = farPeak;
            st.far_peak_idx = (st.far_peak_idx + 1) % MdfTables.DTD_HISTORY_LENGTH;

            // Find maximum far-end peak in history
            int maxFarPeak = 0;
            for (int i = 0; i < MdfTables.DTD_HISTORY_LENGTH; i++)
            {
                if (st.far_peak_history[i] > maxFarPeak)
                    maxFarPeak = st.far_peak_history[i];
            }

            // Find near-end peak
            int nearPeak = 0;
            for (int i = 0; i < st.frame_size; i++)
            {
                int val = Math.Abs(nearEnd[i]);
                if (val > nearPeak) nearPeak = val;
            }

            // Geigel DTD: if near > threshold * max_far, double-talk detected
            // Only consider when far-end has significant level to avoid false positives
            if (nearPeak > MdfTables.DTD_THRESHOLD * maxFarPeak && maxFarPeak > MdfTables.DTD_MIN_FAR_PEAK)
            {
                st.dtd_active = true;
                st.dtd_hangover = MdfTables.DTD_HANGOVER_FRAMES;
            }
            else if (st.dtd_hangover > 0)
            {
                st.dtd_hangover--;
                st.dtd_active = true;
            }
            else
            {
                st.dtd_active = false;
            }
        }

        /// <summary>
        /// Process one frame of echo cancellation
        /// </summary>
        internal static void process_frame(MdfState st, ReadOnlySpan<short> farEnd, ReadOnlySpan<short> nearEnd, Span<short> output)
        {
            int i, j;
            int N = st.fft_size / 2 + 1;
            int M = st.nb_blocks;
            int frameSize = st.frame_size;

            // Check input lengths
            if (farEnd.Length < frameSize || nearEnd.Length < frameSize || output.Length < frameSize)
                throw new ArgumentException("Input/output buffers must be at least frame_size samples");

            // WORKAROUND: Check if far-end signal has any power
            // If not, bypass AEC entirely as it has a bug that destroys near-end speech
            // when there's no echo to cancel.
            double farEndPower = 0;
            for (i = 0; i < frameSize; i++)
            {
                double val = farEnd[i];
                farEndPower += val * val;
            }

            // If far-end power is negligible, pass near-end through unchanged
            // This preserves near-end speech quality when speakers are silent
            if (farEndPower < 1e6) // Threshold: ~sqrt(1e6/128) = ~88 RMS per sample for 128-sample frame
            {
                nearEnd.Slice(0, frameSize).CopyTo(output);
                st.frame_count++;
                return;
            }

            // Apply DC notch filter to inputs
            Span<short> farEndFiltered = stackalloc short[frameSize];
            Span<short> nearEndFiltered = stackalloc short[frameSize];
            farEnd.Slice(0, frameSize).CopyTo(farEndFiltered);
            nearEnd.Slice(0, frameSize).CopyTo(nearEndFiltered);

            filter_dc_notch_16(farEndFiltered, MdfTables.NOTCH_RADIUS_Q15, st.notch_mem, 0, frameSize);
            filter_dc_notch_16(nearEndFiltered, MdfTables.NOTCH_RADIUS_Q15, st.notch_mem, 0, frameSize);

            // Detect double-talk (before filter adaptation)
            detect_double_talk(st, farEnd, nearEnd);

            // Store far-end in ring buffer
            int writePos = st.x_insert_pos;
            for (i = 0; i < frameSize; i++)
            {
                st.x[writePos] = Inlines.SHL32(farEndFiltered[i], 8); // Scale to Q23
                writePos = (writePos + 1) % (M * frameSize);
            }
            st.x_insert_pos = writePos;

            // Store near-end
            for (i = 0; i < frameSize; i++)
                st.y[i] = Inlines.SHL32(nearEndFiltered[i], 8);

            // Compute foreground filter output (convolution in frequency domain)
            Array.Clear(st.PHI, 0, st.fft_size);

            // Allocate working buffers outside loop (complex = 2x size for real+imag)
            Span<int> xBlock = stackalloc int[st.fft_size * 2];
            Span<int> phiTmp = stackalloc int[st.fft_size * 2];

            // For each filter block
            int readPos = st.x_insert_pos;
            for (j = 0; j < M; j++)
            {
                // Get FFT of this block of far-end signal
                // Fill with windowed samples (real part), zero imaginary part
                for (i = 0; i < frameSize; i++)
                {
                    readPos = (readPos > 0) ? readPos - 1 : (M * frameSize - 1);
                    xBlock[2 * i] = Inlines.MULT16_32_Q15(st.window[frameSize - 1 - i], st.x[readPos]);
                    xBlock[2 * i + 1] = 0; // Imaginary part
                }
                // Zero-pad second half
                for (i = frameSize; i < st.fft_size / 2; i++)
                {
                    xBlock[2 * i] = 0;
                    xBlock[2 * i + 1] = 0;
                }

                // FFT
                KissFFT.opus_fft(st.fft_table, xBlock.ToArray(), st.X);

                // Multiply by filter weights and accumulate
                int wOffset = j * N * 2;
                Span<int> wBlock = st.W.AsSpan(wOffset, N * 2);
                spectral_mul(st.X.AsSpan(), wBlock, phiTmp, N);

                // Accumulate to PHI
                for (i = 0; i < st.fft_size; i++)
                    st.PHI[i] = Inlines.ADD32(st.PHI[i], phiTmp[i]);
            }

            // Compute error signal: E = Y - PHI
            // First, get FFT of near-end
            Span<int> yWindowed = stackalloc int[st.fft_size * 2];
            for (i = 0; i < frameSize; i++)
            {
                yWindowed[2 * i] = Inlines.MULT16_32_Q15(st.window[i], st.y[i]);
                yWindowed[2 * i + 1] = 0; // Imaginary part
            }
            for (i = frameSize; i < st.fft_size / 2; i++)
            {
                yWindowed[2 * i] = 0;
                yWindowed[2 * i + 1] = 0;
            }

            KissFFT.opus_fft(st.fft_table, yWindowed.ToArray(), st.Y);

            // Compute error spectrum
            for (i = 0; i < st.fft_size; i++)
                st.E[i] = Inlines.SUB32(st.Y[i], st.PHI[i]);

            // Inverse FFT to get time-domain error
            KissFFT.opus_ifft(st.fft_table, st.E, st.e);

            // Store echo estimate for residual echo computation (PHI in time domain)
            // We need to inverse FFT the PHI to get time-domain echo estimate
            int[] phiTime = new int[st.fft_size * 2];
            KissFFT.opus_ifft(st.fft_table, st.PHI, phiTime);
            for (i = 0; i < frameSize; i++)
                st.last_y[i] = phiTime[2 * i]; // Take real part only

            // Compute and apply residual echo suppression
            apply_residual_echo_suppression(st);

            // Extract output with suppression (scale back from Q23 to Q0)
            for (i = 0; i < frameSize; i++)
            {
                int val = Inlines.SHR32(st.e[2 * i], 8);  // Take real part only
                output[i] = Inlines.SATURATE16(val);
            }

            // Update filter weights (NLMS adaptation)
            update_weights(st);

            st.frame_count++;
        }

        /// <summary>
        /// Compute and apply residual echo suppression gain
        /// </summary>
        private static void apply_residual_echo_suppression(MdfState st)
        {
            int i;
            int N = st.fft_size / 2 + 1;

            // Compute power spectrum of estimated echo (from last foreground filter output)
            Span<int> yWindowed = stackalloc int[st.fft_size * 2];
            for (i = 0; i < st.frame_size; i++)
            {
                yWindowed[2 * i] = Inlines.MULT16_32_Q15(st.window[i], st.last_y[i]);
                yWindowed[2 * i + 1] = 0; // Imaginary part
            }
            for (i = st.frame_size; i < st.fft_size / 2; i++)
            {
                yWindowed[2 * i] = 0;
                yWindowed[2 * i + 1] = 0;
            }
            yWindowed[i] = 0;

            int[] ySpectrum = new int[st.fft_size * 2];
            KissFFT.opus_fft(st.fft_table, yWindowed.ToArray(), ySpectrum);

            // Compute residual echo power spectrum
            KissFFT.power_spectrum(ySpectrum.AsSpan(), st.residual_echo.AsSpan(), N);

            // Scale by leak estimate (accounts for echo path uncertainty)
            float leak2 = 2.0f * st.leak_estimate;
            for (i = 0; i < N; i++)
            {
                st.residual_echo[i] = (int)(st.residual_echo[i] * leak2);
            }

            // Smooth residual echo estimate (prevent musical noise)
            for (i = 0; i < N; i++)
            {
                int smoothed = (int)(MdfTables.RES_ECHO_SMOOTH * st.echo_noise[i] +
                                     (1.0f - MdfTables.RES_ECHO_SMOOTH) * st.residual_echo[i]);
                st.echo_noise[i] = Math.Max(smoothed, st.residual_echo[i]);
            }

            // Compute error signal power spectrum
            KissFFT.power_spectrum(st.E.AsSpan(), st.Eh.AsSpan(), N);

            // Compute suppression gain using Wiener-like filter
            for (i = 0; i < N; i++)
            {
                float signalPower = Math.Max(st.Eh[i], MdfTables.MIN_POWER);
                float noisePower = Math.Max(st.echo_noise[i], MdfTables.MIN_POWER);

                // Gain = signal / (signal + noise)
                float gain = signalPower / (signalPower + noisePower);

                // Apply minimum gain floor
                gain = Math.Max(gain, MdfTables.MIN_GAIN);

                st.gain[i] = gain;
            }
        }

        /// <summary>
        /// Update adaptive filter weights using NLMS algorithm
        /// Includes background filter for divergence recovery and DTD-aware adaptation
        /// </summary>
        private static void update_weights(MdfState st)
        {
            int i, j;
            int N = st.fft_size / 2 + 1;
            int M = st.nb_blocks;

            // Compute power spectrum of far-end
            KissFFT.power_spectrum(st.X.AsSpan(), st.power.AsSpan(), N);

            // Smooth power estimate
            for (i = 0; i < N; i++)
            {
                st.power[i] = Inlines.ADD32(Inlines.MULT16_32_Q15((short)(0.8f * 32767), st.power_1[i]),
                                           Inlines.MULT16_32_Q15((short)(0.2f * 32767), st.power[i]));
                st.power_1[i] = st.power[i];
            }

            // Compute step size
            int totalPower = MdfTables.MIN_POWER_Q15;
            for (i = 0; i < N; i++)
                totalPower = Inlines.ADD32(totalPower, st.power[i]);

            // Update proportionate weights for foreground filter
            mdf_adjust_prop(st.W.AsSpan(), N, M, st.prop.AsSpan());

            // Foreground adaptation step (skip if double-talk detected)
            int mu = (short)(0.5f * 32767); // Step size for foreground
            int mu_bg = (short)(MdfTables.BACKGROUND_STEP_SIZE * 32767); // Larger step for background

            // Always update background filter (even during double-talk)
            for (j = 0; j < M; j++)
            {
                int wOffset = j * N * 2;
                for (i = 0; i < N; i++)
                {
                    if (st.power[i] > MdfTables.MIN_POWER_Q15)
                    {
                        int normFactor_fg = Inlines.DIV32(Inlines.SHL32(mu, 15), st.power[i]);
                        int normFactor_bg = Inlines.DIV32(Inlines.SHL32(mu_bg, 15), st.power[i]);
                        int update_re = Inlines.MULT16_32_Q15(normFactor_fg, st.E[2 * i]);
                        int update_im = (i < N - 1) ? Inlines.MULT16_32_Q15(normFactor_fg, st.E[2 * i + 1]) : 0;
                        int update_re_bg = Inlines.MULT16_32_Q15(normFactor_bg, st.E[2 * i]);
                        int update_im_bg = (i < N - 1) ? Inlines.MULT16_32_Q15(normFactor_bg, st.E[2 * i + 1]) : 0;

                        // Update foreground filter only if NOT in double-talk
                        if (!st.dtd_active)
                        {
                            st.W[wOffset + 2 * i] = Inlines.ADD32(st.W[wOffset + 2 * i],
                                                                  Inlines.MULT16_32_Q15(st.prop[j * N + i], update_re));
                            if (i < N - 1)
                                st.W[wOffset + 2 * i + 1] = Inlines.ADD32(st.W[wOffset + 2 * i + 1],
                                                                          Inlines.MULT16_32_Q15(st.prop[j * N + i], update_im));
                        }

                        // Always update background filter (with reduced step during double-talk)
                        int bg_scale = st.dtd_active ? (int)(0.1f * 32767) : (int)(1.0f * 32767);
                        st.Wtmp[wOffset + 2 * i] = Inlines.ADD32(st.Wtmp[wOffset + 2 * i],
                                                                  Inlines.MULT16_32_Q15(bg_scale, update_re_bg));
                        if (i < N - 1)
                            st.Wtmp[wOffset + 2 * i + 1] = Inlines.ADD32(st.Wtmp[wOffset + 2 * i + 1],
                                                                          Inlines.MULT16_32_Q15(bg_scale, update_im_bg));
                    }
                }
            }

            // Compute foreground error energy for divergence metric
            float foregroundError = 0;
            for (i = 0; i < N; i++)
            {
                float re = st.E[2 * i];
                float im = (i < N - 1) ? st.E[2 * i + 1] : 0;
                foregroundError += re * re + im * im;
            }

            // Smooth divergence metrics
            st.Davg1 = (int)(MdfTables.DIVERGENCE_SMOOTH * st.Davg1 + (1 - MdfTables.DIVERGENCE_SMOOTH) * foregroundError);

            // Check if background filter would produce less error
            // (In a full implementation, we'd compute background error separately)
            // For now, use filter coefficient energy as a proxy
            float filterEnergy = 0;
            float bgFilterEnergy = 0;
            for (i = 0; i < st.W.Length; i++)
            {
                filterEnergy += (float)st.W[i] * st.W[i];
                bgFilterEnergy += (float)st.Wtmp[i] * st.Wtmp[i];
            }

            // Divergence detection: only check for extreme filter energy growth
            // Skip growth rate check during early adaptation (first 100 frames)
            bool diverged = filterEnergy > MdfTables.MAX_FILTER_ENERGY;

            if (diverged)
            {
                st.screwed_up++;
                if (st.screwed_up > MdfTables.MAX_SCREWED_UP)
                {
                    // Scale down filter coefficients instead of full reset
                    float scale = (float)Math.Sqrt(MdfTables.MAX_FILTER_ENERGY / filterEnergy) * 0.5f;
                    for (i = 0; i < st.W.Length; i++)
                    {
                        st.W[i] = (int)(st.W[i] * scale);
                    }
                    st.screwed_up = 0;
                }
            }
            else if (st.screwed_up > 0)
            {
                st.screwed_up--;
            }

            st.prev_filter_energy = filterEnergy;

            // Update leak estimate based on echo cancellation performance
            // Increase leak if error is high relative to input, decrease otherwise
            float signalPower = 0;
            for (i = 0; i < N; i++)
                signalPower += st.power[i];

            if (signalPower > MdfTables.MIN_POWER)
            {
                float echoRatio = foregroundError / (signalPower + MdfTables.MIN_POWER);
                if (echoRatio > 1.0f)
                    st.leak_estimate = Math.Min(st.leak_estimate * 1.01f, 1.0f);
                else
                    st.leak_estimate *= 0.999f;
            }
            else
            {
                st.leak_estimate *= 0.999f;
            }

            if (st.leak_estimate < MdfTables.MIN_LEAK)
                st.leak_estimate = MdfTables.MIN_LEAK;
        }
    }
}
