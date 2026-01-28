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
    using System;

    /// <summary>
    /// MDF (Multi-Delay Filter) acoustic echo canceller with residual echo suppression
    /// Implements frequency-domain adaptive filtering for mono echo removal
    /// </summary>
    public class SpeexEchoCanceller : IAcousticEchoCanceller
    {
        private MdfState _state;
        private bool _disposed = false;

        /// <summary>
        /// Create a new echo canceller
        /// </summary>
        /// <param name="sampleRate">Sample rate in Hz (8000, 16000, 24000, or 48000)</param>
        /// <param name="frameSize">Frame size in samples - must be power of 2 between 64 and 256</param>
        /// <param name="filterLength">Filter length in samples (echo tail) - must be multiple of frameSize</param>
        public SpeexEchoCanceller(int sampleRate, int frameSize, int filterLength)
        {
            _state = Mdf.Init(sampleRate, frameSize, filterLength);
        }

        /// <summary>
        /// Gets the frame size in samples
        /// </summary>
        public int FrameSize => _state.frame_size;

        /// <summary>
        /// Gets the filter length in samples
        /// </summary>
        public int FilterLength => _state.filter_length;

        /// <summary>
        /// Gets the sample rate in Hz
        /// </summary>
        public int SampleRate => _state.sample_rate;

        /// <summary>
        /// Process one frame of audio to cancel acoustic echo
        /// </summary>
        /// <param name="nearEnd">Near-end signal (microphone input) - must be at least FrameSize samples</param>
        /// <param name="farEnd">Far-end signal (speaker output) - must be at least FrameSize samples</param>
        /// <param name="output">Output signal with echo cancelled - must be at least FrameSize samples</param>
        public void Process(ReadOnlySpan<short> nearEnd, ReadOnlySpan<short> farEnd, Span<short> output)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpeexEchoCanceller));

            Mdf.ProcessFrame(_state, farEnd, nearEnd, output);
        }

        /// <summary>
        /// Reset the echo canceller (clear all state and filter coefficients)
        /// </summary>
        public void Reset()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SpeexEchoCanceller));

            Mdf.Reset(_state);
        }

        /// <summary>
        /// Dispose of unmanaged resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose pattern implementation
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Managed resources cleanup (state is managed)
                    _state = null;
                }
                _disposed = true;
            }
        }
    }
}
