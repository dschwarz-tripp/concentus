# Live Audio Test for AEC

## Overview
The live audio test plays an audio file through your speakers while simultaneously recording from your microphone. The recorded audio is processed through the AEC using the played audio as the far-end reference, simulating a real echo cancellation scenario.

## Requirements

### macOS Only
This test currently only works on macOS due to the use of macOS-specific audio tools.

### Required Software
You need `sox` (Sound eXchange) installed to record audio:

```bash
brew install sox
```

## Setup

1. **Position your microphone** so it can hear your speakers clearly
2. **Adjust volume** to a moderate level (not too loud, to avoid clipping)
3. **Ensure quiet environment** to minimize background noise

## Running the Test

When you run the AecTestConsole, you'll be prompted:

```
--- Live Audio Test ---
This test uses real speakers and microphone.
Run live audio test? (y/n):
```

Type `y` and press Enter.

The test will:
1. Ask you to press Enter to start
2. Play audio through your speakers (~10 seconds)
3. Record from your microphone simultaneously
4. Process the recording through the AEC
5. Calculate ERLE (Echo Return Loss Enhancement)
6. Save three output files:
   - `*_live_mic.wav` - Raw microphone recording
   - `*_live_processed.wav` - AEC-processed output
   - `*_live_farend.wav` - Speaker playback reference

## Understanding Results

### ERLE Values
- **> 10 dB**: Good echo cancellation
- **5-10 dB**: Moderate cancellation
- **< 5 dB**: Low cancellation
- **Negative**: No effective cancellation

### Factors Affecting Performance
- **Speaker-to-mic distance**: Closer = stronger echo = better test
- **Speaker volume**: Moderate levels work best
- **Room acoustics**: Reflective surfaces increase echo
- **Background noise**: Minimize for best results
- **Audio file quality**: Speech content works better than music

## Troubleshooting

### "sox not found" or "rec not found"
Install sox using Homebrew:
```bash
brew install sox
```

### "Not enough active audio detected"
- Increase speaker volume
- Move microphone closer to speakers
- Use an audio file with more speech content

### Poor ERLE results
- Check that far-end audio is actually playing
- Ensure microphone is picking up the speakers
- Verify microphone isn't muted or too quiet
- Try adjusting speaker volume
- Reduce background noise

## Technical Details

- **Sample Rate**: 16 kHz
- **Frame Size**: 128 samples
- **Test Duration**: ~10 seconds (1250 frames max)
- **Adaptation Period**: First 30 frames skipped from ERLE measurement
- **Audio Tools**: Uses `afplay` for playback and `sox` for recording
