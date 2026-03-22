## Overview

The Audio System in Escape is designed to handle ambient music, sound effects (SFX), and environmental audio with a focus on immersive transitions and synchronization with visual effects.

## Core Components

### AudioManager.cs
The central hub for all audio operations. It manages:
- **Background Music (BGM):** Seamless transitions between Menu and Gameplay music.
- **3D Sound Loops:** Dynamic creation of 3D audio sources for environmental sounds.
- **Global Volume Control:** (Ready for implementation via Audio Mixer).

### NeonLightFlicker.cs
A specialized script for audio-visual synchronization. It:
- Requests a 3D loop from `AudioManager`.
- Analyzes the audio waveform (RMS) in real-time.
- Maps audio amplitude to light intensity and material emission.

## Music Management

The system uses a dual-source approach to manage background music transitions:

1.  **Gameplay Music:** Plays continuously in the background. When paused, its volume fades to 0 but the playback continues, allowing it to resume from the exact same point when returning to the game.
2.  **Menu Music:** Always restarts from the beginning when the menu is opened to provide a consistent "entry" experience.

### Transition Logic
Transitions are handled via `FadeBetweenSources` using `Time.unscaledDeltaTime`, ensuring that audio fades work correctly even when `Time.timeScale` is set to 0 (Pause).

## Integration Guide

### Adding New Environmental Sounds
To add a looping 3D sound (like a humming lamp or ventilation):
1. Use `AudioManager.Instance.Play3DLoop(clip, transform, volume, minDistance, maxDistance)`.
2. This creates a managed child GameObject with an `AudioSource` configured for 3D spatialization.

### Handling Pause
The `AudioManager` and `NeonLightFlicker` scripts are designed to respect the game's pause state:
- **BGM:** Handled via volume fades triggered by `GameManager.SetPause(bool)`. `AudioManager.PlayMenuMusic()` and `AudioManager.PlayGameMusic()` are called directly from `GameManager`.
- **Environmental Sounds:** `NeonLightFlicker` explicitly calls `_audioSource.Pause()` when `Time.timeScale == 0` to prevent environmental "noise" during menu navigation.
