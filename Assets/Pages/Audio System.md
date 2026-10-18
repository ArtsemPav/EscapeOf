## Audio System

The Audio System covers ambient music, SFX, and environmental audio. It is split by responsibility: `AudioManager` owns sound effects and mixer routing, `MusicDirector` owns ALL music, `SystemSettings` owns player-facing volume settings.

## Mixer Topology

The single mixer asset `Assets/sound/SFX.mixer` carries two buses:

```
Master
├── Music      # exposed parameter: MusicVolume
└── SFX        # exposed parameter: SFXVolume
```

- `SystemSettings` writes normalized loudness (0–1) to the exposed parameters as decibels via `ToDecibels` (log10 × 20, `-80 dB` at zero).
- Every dynamically created `AudioSource` must be routed to a bus (`RouteToSfxGroup` / `MusicDirector`'s `_musicGroup`), otherwise it plays unmixed and ignores the sliders.
- **Important:** the exposed parameter must point at the group's `m_Volume` node in the mixer asset. `MusicVolume` was once bound to a group's attenuation MixLevel instead, which made `SetFloat` fail silently with `Exposed name does not exist: MusicVolume` in the console.

## Core Components

### AudioManager.cs

Sound-effect service. Owns no music. Provides:

- `PlaySFX(clip, volume)` / `PlaySFXExclusive(clip, volume)` — 2D one-shots on the shared SFX source (exclusive variant stops overlapping copies first).
- `Play3DOneShot(clip, position, volume, minDistance, maxDistance)` — pooled temporary 3D one-shot, auto-destroyed after the clip.
- `Play3DLoop(...)` / `RegisterLoopSource(...)` / `UnregisterLoopSource(...)` — spatial loops registered in the background tracking system (faded by `MuteBackground`/`UnmuteBackground`).
- `PlayBackgroundLayer(clip, volume, fade)` / `StopBackgroundLayer(fade)` — 2D layers (heartbeat, hum) faded independently.
- `ApplySavedVolumes()` — pushes saved PlayerPrefs volumes to both mixer buses at startup.
- `OnSceneLoaded` — auto-routes every orphan source (no mixer group) to the SFX bus as a safety net.
- `PlayNewMusicSource(clip, loop, trackName)` — factory used exclusively by `MusicDirector`.

### MusicDirector.cs

Single authority for all music (menu, gameplay, zones, events). Key design:

- **Pooled sources:** every track gets a dedicated `AudioSource`; switching tracks fades the old one out and *pauses* it, so it resumes from the same position later.
- **Track assets:** `MusicTrack` ScriptableObjects (clip, volume, loop, fade duration); zones register tracks in Awake and request/clear them via `RequestTrack` / `ClearTrack`. `playOnce` tracks never re-trigger and survive saves.
- **Save integration (ISaveable, id `music_director`):** persists the dominant track, its playback position and fired play-once tracks. The autosave never persists the pause-menu track as dominant.
- **Menu music:** `PlayMenuMusic()` / `PlayGameMusic()` replace the legacy `AudioManager.PlayMenuMusic`. The gameplay track's position is remembered and restored. The menu track is marked `ignoreListenerPause` so it remains audible while `AudioListener.pause` silences everything else.
- **Horror mute:** `MuteForEvent(fade)` / `RestoreAfterEvent(fade)` use a reference-counted owner set (`_muteOwnerCount`) — nested events each own a mute and the music restores only when the last one releases. While muted, all director sources are silenced and new track requests are queued out; `PlayGameMusic` during an active event stays silent until restore.
- **Ducking:** `DuckActiveTrack(multiplier, fade)` — fades the active track down/up (elevator). Restores to the exact pre-duck level; ignored while a horror mute or the menu owns the music; cancelled by a track switch.
- **Minimum switch interval** (`_minSwitchInterval`) rejects flickering requests from door triggers.

### NeonLightFlicker.cs

Audio-visual synchronization: requests a 3D loop from `AudioManager`, analyzes the waveform (RMS) in real time and maps amplitude to light intensity and material emission.

### SystemSettings.cs

Static player settings (PlayerPrefs):

- `SetMusicVolume` / `SetSfxVolume` — clamp 0–1, persist, and apply to the mixer instantly.
- `MusicVolumePref` / `SfxVolumePref` — PlayerPrefs keys; `ToDecibels` — loudness conversion.
- Quality level settings live here too.

## Pause & Muting Model

| State | Mechanism | What stays audible |
|---|---|---|
| Game paused (`GameManager.SetPause`) | `Time.timeScale = 0` **and** `AudioListener.pause = true` | Only the pause-menu track (`ignoreListenerPause`) |
| Horror event active | `MusicDirector.MuteForEvent` (ref-counted, with a per-event timeout failsafe) | Event's own sounds; music and tracked loops faded out |
| Inside elevator | `MusicDirector.DuckActiveTrack` | Music at the duck multiplier |

Notes:

- `Time.timeScale = 0` alone does not stop audio; the world pause is `AudioListener.pause`.
- All fade coroutines use `Time.unscaledDeltaTime`, so fades work during pause and time stops.
- Music does not resume when the pause menu closes while a horror mute is still held — the event's `RestoreAfterEvent` refades it later.

## Integration Guide

### Adding new environmental sounds

1. Looping spatial sound (humming lamp, ventilation): `AudioManager.Instance.Play3DLoop(clip, transform, volume, minDistance, maxDistance)`, then stop it via `UnregisterLoopSource(source)`. Tracked loops follow background mutes automatically.
2. One-shot positional sound: `AudioManager.Instance.Play3DOneShot(clip, position, volume, minDistance, maxDistance)` — used by `ElevatorController`, for example.
3. Non-repeating UI/minigame chime: `AudioManager.Instance.PlaySFXExclusive(clip, volume)`.

Sources created outside these APIs must be routed manually: `AudioManager.Instance.RouteSourceToSfxGroup(source)` (the scene-load sweep covers most strays anyway).

### Adding music

1. Create a `MusicTrack` asset (clip, volume, loop, fade duration).
2. Trigger it from a `MusicZone` (enter/exit) or code: `MusicDirector.RequestTrack(track, playOnce: false)` and `MusicDirector.ClearTrack(track)` to restore the previous track.

### Horror events

`HorrorEvent` integrates automatically when `_ownsBackgroundMusic` is set: it calls `MusicDirector.MuteForEvent(_musicMuteFadeDuration)` on start and `RestoreAfterEvent(_musicRestoreFadeDuration)` on finish (interactable completion or `_musicMuteTimeout` failsafe). Manual UnityEvent mute/restore wiring on event objects is no longer needed.
