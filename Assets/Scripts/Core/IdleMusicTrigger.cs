using UnityEngine;

namespace Escape.Core {
    /// <summary>
    /// Anti-stuck music cue: watches gameplay progress and asks MusicDirector to
    /// play a hint track when the player goes too long without solving a puzzle
    /// or opening a door.
    ///
    /// Rules:
    /// - The cue only takes over while the DEFAULT game track plays (a zone's
    ///   own music always wins — the cue never overrides it).
    /// - The first progress event (solved puzzle, opened door) fades the cue
    ///   back to the default track.
    /// - While the player is inside a puzzle mode the cue fades out too —
    ///   solving counts as progress, and the puzzle's own atmosphere takes over.
    ///
    /// Progress sources:
    /// - ProgressLogger.PuzzleSolved (all puzzles report through it);
    /// - DoorInteraction.DoorOpened — any door the player got open;
    /// - PuzzleModeController.PuzzleModeStarted / PuzzleModeEnded.
    ///
    /// The idle timer runs on unscaled time (survives pause). Attach to the
    /// GameManager GameObject in the Game scene (DontDestroyOnLoad like its host).
    /// </summary>
    public class IdleMusicTrigger : MonoBehaviour {
        [Tooltip("Track played when the player is idle too long (the hint music).")]
        [SerializeField] private MusicTrack _idleTrack;

        [Tooltip("Seconds without a solved puzzle or an opened door before the idle track fades in.")]
        [SerializeField] private float _idleThreshold = 120f;

        private float _lastProgressTime;
        private bool _idleTrackRequested;
        private bool _puzzleModeActive;

        private void OnEnable() {
            ProgressLogger.PuzzleSolved += OnPuzzleSolved;
            DoorInteraction.DoorOpened += OnDoorOpened;
            PuzzleModeController.PuzzleModeStarted += OnPuzzleModeStarted;
            PuzzleModeController.PuzzleModeEnded += OnPuzzleModeEnded;
        }

        private void OnDisable() {
            ProgressLogger.PuzzleSolved -= OnPuzzleSolved;
            DoorInteraction.DoorOpened -= OnDoorOpened;
            PuzzleModeController.PuzzleModeStarted -= OnPuzzleModeStarted;
            PuzzleModeController.PuzzleModeEnded -= OnPuzzleModeEnded;
        }

        private void Start() {
            _lastProgressTime = Time.unscaledTime;
        }

        private void Update() {
            if (_puzzleModeActive) return; // solving counts as progress — the timer holds
            if (_idleTrack == null || _idleTrackRequested) return;
            if (GameManager.Instance != null && GameManager.Instance.IsPaused) return; // pause menu owns the music

            if (Time.unscaledTime - _lastProgressTime < _idleThreshold) return;

            // A zone's own track is playing — the cue never overrides it. The
            // timer keeps running and retries every frame until the default
            // track is back (zone tracks end into the default via fallback).
            if (MusicDirector.Instance == null || !MusicDirector.Instance.IsDefaultTrackActive) return;

            _idleTrackRequested = true;
            MusicDirector.RequestIdleCueTrack(_idleTrack);
            Debug.Log($"[IdleMusic] {Time.unscaledTime - _lastProgressTime:0}s without progress -> idle track '{_idleTrack.name}'");
        }

        /// <summary>A solved puzzle resets the idle timer and restores the default track.</summary>
        private void OnPuzzleSolved() {
            RegisterProgress();
        }

        /// <summary>An opened door resets the idle timer and restores the default track.</summary>
        private void OnDoorOpened() {
            RegisterProgress();
        }

        /// <summary>Entering a puzzle fades the cue out and freezes the timer —
        /// the puzzle's own tension music (if any) plays while solving.</summary>
        private void OnPuzzleModeStarted() {
            _puzzleModeActive = true;
            RestoreDefaultTrack();
        }

        /// <summary>Leaving the puzzle restarts the idle countdown from zero.</summary>
        private void OnPuzzleModeEnded() {
            _puzzleModeActive = false;
            _lastProgressTime = Time.unscaledTime;
        }

        private void RegisterProgress() {
            _lastProgressTime = Time.unscaledTime;
            RestoreDefaultTrack();
        }

        /// <summary>Returns the director to the default game track when the idle cue is active; no-op otherwise.</summary>
        private void RestoreDefaultTrack() {
            if (!_idleTrackRequested) return;

            _idleTrackRequested = false;
            MusicDirector.ClearTrack(_idleTrack);
            Debug.Log("[IdleMusic] progress registered -> restoring default track");
        }
    }
}
