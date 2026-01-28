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
    using System;

    /// <summary>
    /// Interface for acoustic echo cancellation
    /// </summary>
    public interface IAcousticEchoCanceller : IDisposable
    {
        /// <summary>
        /// Process one frame of audio to cancel acoustic echo
        /// </summary>
        /// <param name="nearEnd">Near-end signal (microphone input) containing speech + echo</param>
        /// <param name="farEnd">Far-end signal (speaker output) - the echo source</param>
        /// <param name="output">Output signal with echo cancelled</param>
        void Process(ReadOnlySpan<short> nearEnd, ReadOnlySpan<short> farEnd, Span<short> output);

        /// <summary>
        /// Reset the echo canceller state (clear filter coefficients and history)
        /// </summary>
        void Reset();

        /// <summary>
        /// Gets the frame size in samples (number of samples per Process() call)
        /// </summary>
        int FrameSize { get; }

        /// <summary>
        /// Gets the filter length in samples (echo tail length)
        /// </summary>
        int FilterLength { get; }

        /// <summary>
        /// Gets the sample rate in Hz
        /// </summary>
        int SampleRate { get; }
    }
}
