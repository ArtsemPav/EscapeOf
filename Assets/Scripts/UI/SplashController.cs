using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// In-scene splash sequence: crossfades a series of logo images over a black
/// background on top of the main menu, then hides the splash overlay so the
/// menu beneath becomes visible and interactive. Any key/click skips the
/// current (and remaining) logos. Attach to the splash canvas root.
/// OnSplashFinished fires once the splash fully dissolves — subscribers can
/// start their own intro beats (e.g. the menu camera arrival) at that moment.
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

    [Tooltip("How long the black splash screen dissolves into the menu after the last logo, in seconds.")]
    [SerializeField, Min(0.01f)] private float _menuRevealSeconds = 0.8f;

    [Tooltip("How much faster fades run while the player is skipping (e.g. 4 = 4x speed).")]
    [SerializeField, Min(1f)] private float _skipSpeedFactor = 4f;

    [Header("Logos")]
    [Tooltip("Logo CanvasGroups shown in order, one after another.")]
    [SerializeField] private CanvasGroup[] _logos;

    [Tooltip("CanvasGroup on the splash root, faded out at the end to reveal the menu beneath.")]
    [SerializeField] private CanvasGroup _splashGroup;

    [Tooltip("Root object hidden when the sequence ends (the splash canvas).")]
    [SerializeField] private GameObject _splashRoot;

    [Tooltip("Fires once the splash has fully dissolved and the canvas is hidden.")]
    [SerializeField] private UnityEvent _onSplashFinished;

    /// <summary>Raised when the splash sequence ends (after the dissolve, with the canvas hide).</summary>
    public event System.Action SplashFinished;

    private Coroutine _sequence;
    private bool _skipped;
    private bool _splashFinishedRaised;

    private void Start()
    {
        _sequence = StartCoroutine(PlaySequence());
    }

    /// <summary>Skip on any key, click, or gamepad button press.</summary>
    private void Update()
    {
        bool anyInput = (Keyboard.current?.anyKey.wasPressedThisFrame ?? false)
                        || (Mouse.current?.leftButton.wasPressedThisFrame ?? false)
                        || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false);
        if (anyInput) _skipped = true;
    }

    private IEnumerator PlaySequence()
    {
        foreach (var logo in _logos)
        {
            if (logo != null) logo.alpha = 0f;
        }

        foreach (var logo in _logos)
        {
            if (logo == null) continue;

            yield return Fade(logo, 0f, 1f, _fadeInSeconds);

            float wait = 0f;
            while (wait < _logoDisplaySeconds && !_skipped)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            // The arrival/camera beats start HERE — in parallel with the last
            // logo's fade-out (or with an accelerated skip fade), so the menu
            // is already drifting into place while the logo dissolves.
            bool isLast = logo == _logos[_logos.Length - 1];
            if (isLast || _skipped)
            {
                RaiseSplashFinished();
            }

            // Skip must still fade the logo out — just faster (no hard pop).
            yield return Fade(logo, 1f, 0f, _fadeOutSeconds);
            if (_skipped) break;
        }

        // Dissolve the whole splash (black background included) into the menu,
        // then hide the canvas. The player never sees a hard cut.
        if (_splashGroup != null)
        {
            yield return Fade(_splashGroup, 1f, 0f, _menuRevealSeconds);
        }

        if (_splashRoot != null) _splashRoot.SetActive(false);
        RaiseSplashFinished();
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
            // While skipping, fades run faster instead of being cut off.
            t += Time.unscaledDeltaTime * (_skipped ? _skipSpeedFactor : 1f);
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
