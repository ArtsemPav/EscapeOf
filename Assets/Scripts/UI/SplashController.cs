using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// In-scene splash sequence: crossfades a series of logo images over a black
/// background on top of the main menu, then hides the splash overlay so the
/// menu beneath becomes visible and interactive. The sequence cannot be
/// skipped — it always plays to the end. Attach to the splash canvas root.
/// OnSplashFinished fires when the background starts dissolving into the
/// menu — subscribers can start their own intro beats (e.g. the menu camera
/// arrival) in sync with the reveal.
/// </summary>
public class SplashController : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Fade-in duration of each logo, in seconds.")]
    [SerializeField, Min(0.01f)] private float _fadeInSeconds = 0.8f;

    [Tooltip("Fade-out duration of each logo, in seconds.")]
    [SerializeField, Min(0.01f)] private float _fadeOutSeconds = 0.8f;

    [Tooltip("How long each fully-visible logo holds before fading out, in seconds.")]
    [SerializeField, Min(0f)] private float _logoDisplaySeconds = 1.6f;

    [Tooltip("Extra display seconds added to the FIRST logo in the array (it appears from full black, so it reads as shorter).")]
    [SerializeField, Min(0f)] private float _firstLogoExtraSeconds = 1f;

    [Tooltip("How long the black splash screen dissolves into the menu after the last logo, in seconds.")]
    [SerializeField, Min(0.01f)] private float _menuRevealSeconds = 0.8f;

    [Tooltip("Black pause after each logo's fade-out, in seconds (breathing room between logos).")]
    [SerializeField, Min(0f)] private float _pauseAfterFadeOutSeconds = 0.5f;

    [Header("Logos")]
    [Tooltip("Logo CanvasGroups shown in order, one after another.")]
    [SerializeField] private CanvasGroup[] _logos;

    [Tooltip("CanvasGroup on the splash root, faded out at the end to reveal the menu beneath.")]
    [SerializeField] private CanvasGroup _splashGroup;

    [Tooltip("Root object hidden when the sequence ends (the splash canvas).")]
    [SerializeField] private GameObject _splashRoot;

    [Tooltip("Fires when the background starts dissolving into the menu (camera arrival hooks here).")]
    [SerializeField] private UnityEvent _onSplashFinished;

    [Tooltip("EventSystem disabled while the splash plays (blocks keyboard/gamepad menu input). Re-enabled when the menu is revealed.")]
    [SerializeField] private EventSystem _eventSystem;

    /// <summary>Raised when the splash background starts dissolving into the menu.</summary>
    public event System.Action SplashFinished;

    private Coroutine _sequence;
    private bool _splashFinishedRaised;

    // Static: survives scene reloads within one app run (resets on app start).
    private static bool _splashPlayedThisSession;

#if UNITY_EDITOR
    // Domain reload can be disabled in Editor — reset the flag on every Play.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionFlag() => _splashPlayedThisSession = false;
#endif

    private void Start()
    {
        if (_splashPlayedThisSession)
        {
            // Return from gameplay (pause menu / credits): skip the logo sequence,
            // but still fire the splash-finished beats so the camera arrival plays —
            // the menu opens with the same dolly move as on first launch.
            if (_splashGroup != null) _splashGroup.alpha = 0f;
            if (_splashRoot != null) _splashRoot.SetActive(false);
            RaiseSplashFinished();
            return;
        }

        // Menu keyboard/gamepad input stays dead until the menu is revealed;
        // mouse clicks are already blocked by the splash background image.
        if (_eventSystem != null) _eventSystem.enabled = false;

        _sequence = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        foreach (var logo in _logos)
        {
            if (logo != null) logo.alpha = 0f;
        }

        for (int i = 0; i < _logos.Length; i++)
        {
            var logo = _logos[i];
            if (logo == null) continue;

            yield return Fade(logo, 0f, 1f, _fadeInSeconds);

            // The first logo fades in from full black and reads as shorter,
            // so it gets extra hold time to even out the perceived rhythm.
            float displaySeconds = _logoDisplaySeconds + (i == 0 ? _firstLogoExtraSeconds : 0f);

            yield return new WaitForSecondsRealtime(displaySeconds);

            yield return Fade(logo, 1f, 0f, _fadeOutSeconds);

            // Black pause between logos — a standalone beat, not part of fades.
            yield return new WaitForSecondsRealtime(_pauseAfterFadeOutSeconds);
        }

        // The camera arrival starts HERE — simultaneously with the background
        // dissolve, so the menu is already drifting into place as it appears.
        RaiseSplashFinished();

        // Dissolve the whole splash (black background included) into the menu,
        // then hide the canvas. The player never sees a hard cut.
        if (_splashGroup != null)
        {
            yield return Fade(_splashGroup, 1f, 0f, _menuRevealSeconds);
        }

        if (_splashRoot != null) _splashRoot.SetActive(false);

        // The menu is revealed: restore keyboard/gamepad navigation and clicks.
        if (_eventSystem != null) _eventSystem.enabled = true;

        RaiseSplashFinished();
        _splashPlayedThisSession = true;
    }

    /// <summary>Fires the splash-finished beats exactly once per sequence run.</summary>
    private void RaiseSplashFinished()
    {
        if (_splashFinishedRaised) return;
        _splashFinishedRaised = true;

        _onSplashFinished?.Invoke();
        SplashFinished?.Invoke();
    }

    private IEnumerator Fade(CanvasGroup group, float from, float to, float durationSeconds)
    {
        float t = 0f;
        while (t < durationSeconds)
        {
            t += Time.unscaledDeltaTime;
            group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / durationSeconds));
            yield return null;
        }
        group.alpha = to;
    }

    /// <summary>Stops the sequence if the object is disabled mid-run.</summary>
    private void OnDisable()
    {
        if (_sequence != null)
        {
            StopCoroutine(_sequence);
            _sequence = null;
        }
    }
}
