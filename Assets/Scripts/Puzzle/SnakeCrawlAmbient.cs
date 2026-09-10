using System.Collections;
using UnityEngine;

/// <summary>
/// Atmospheric 3D snake-crawl sound that circles around the player while
/// they are solving a puzzle. Starts after a configurable delay, stops when
/// the puzzle is exited or solved.
///
/// Attach to the same GameObject as PuzzleModeController (e.g. chinesBox).
/// Assign the snake AudioClip in the Inspector.
/// </summary>
public class SnakeCrawlAmbient : MonoBehaviour
{
    [Header("Sound")]
    [SerializeField] private AudioClip _snakeClip;

    [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;

    [Tooltip("Seconds after entering puzzle mode before the snake sound starts.")]
    [SerializeField] private float _startDelay = 5f;

    [Tooltip("Fade-in duration when the snake sound begins (seconds).")]
    [SerializeField] private float _fadeInDuration = 2f;

    [Tooltip("Fade-out duration when stopping (seconds).")]
    [SerializeField] private float _fadeOutDuration = 1.5f;

    [Header("Movement")]
    [Tooltip("Radius of the circular path around the listener.")]
    [SerializeField] private float _radius = 2.5f;

    [Tooltip("Vertical offset relative to the listener (negative = below ear level, on the floor).")]
    [SerializeField] private float _heightOffset = -1.2f;

    [Tooltip("How long one full revolution takes (seconds).")]
    [SerializeField] private float _revolutionDuration = 14f;

    [Tooltip("Extra perlin-noise wobble applied to radius and height for organic motion.")]
    [SerializeField, Range(0f, 1f)] private float _wobbleAmount = 0.3f;

    [Header("3D Audio")]
    [SerializeField] private float _minDistance = 0.4f;
    [SerializeField] private float _maxDistance = 10f;

    // ── Constants ─────────────────────────────────────────────────────────────

    private const float FullCircle = Mathf.PI * 2f;
    private const float NoiseScale = 0.3f;

    // ── State ─────────────────────────────────────────────────────────────────

    private PuzzleModeController _controller;
    private GameObject _snakeObject;
    private AudioSource _snakeSource;
    private Coroutine _startRoutine;
    private Coroutine _crawlRoutine;
    private Coroutine _fadeRoutine;
    private bool _stopping;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _controller = GetComponent<PuzzleModeController>();
        if (_controller == null)
            Debug.LogError(
                $"[{nameof(SnakeCrawlAmbient)}] PuzzleModeController not found on {gameObject.name}.", this);
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnEntered += HandleEntered;
            _controller.OnExited  += HandleExited;
            _controller.OnSolved  += HandleSolved;
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited  -= HandleExited;
            _controller.OnSolved  -= HandleSolved;
        }
        StopSnakeImmediate();
    }

    // ── Puzzle Event Handlers ─────────────────────────────────────────────────

    private void HandleEntered()
    {
        _stopping = false;
        _startRoutine = StartCoroutine(StartAfterDelayRoutine(_startDelay));
    }

    private void HandleExited()
    {
        StopSnake();
    }

    private void HandleSolved()
    {
        StopSnake();
    }

    // ── Start / Stop ──────────────────────────────────────────────────────────

    /// <summary>Waits the configured delay, then creates the 3D source and starts crawling.</summary>
    private IEnumerator StartAfterDelayRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (_stopping || _snakeSource != null) yield break;

        CreateSnakeSource();
        _crawlRoutine = StartCoroutine(CrawlRoutine());
        _fadeRoutine = StartCoroutine(FadeRoutine(0f, _volume, _fadeInDuration));
    }

    /// <summary>Fades out and destroys the snake sound. Call when puzzle is exited or solved.</summary>
    private void StopSnake()
    {
        _stopping = true;

        if (_startRoutine != null)
        {
            StopCoroutine(_startRoutine);
            _startRoutine = null;
        }

        // If the sound never started (still in delay), nothing to fade.
        if (_snakeSource == null) return;

        if (_crawlRoutine != null)
        {
            StopCoroutine(_crawlRoutine);
            _crawlRoutine = null;
        }

        if (_fadeRoutine != null)
        {
            StopCoroutine(_fadeRoutine);
        }

        _fadeRoutine = StartCoroutine(FadeOutAndDestroyRoutine(_fadeOutDuration));
    }

    /// <summary>Immediately destroys everything without fade — used in OnDisable.</summary>
    private void StopSnakeImmediate()
    {
        _stopping = true;

        if (_startRoutine != null) StopCoroutine(_startRoutine);
        if (_crawlRoutine != null) StopCoroutine(_crawlRoutine);
        if (_fadeRoutine   != null) StopCoroutine(_fadeRoutine);

        _startRoutine = null;
        _crawlRoutine = null;
        _fadeRoutine   = null;

        if (_snakeObject != null)
            Destroy(_snakeObject);

        _snakeSource = null;
        _snakeObject = null;
    }

    // ── Source Creation ───────────────────────────────────────────────────────

    private void CreateSnakeSource()
    {
        _snakeObject = new GameObject("SnakeCrawl_Audio");
        _snakeObject.transform.SetParent(transform);

        _snakeSource = _snakeObject.AddComponent<AudioSource>();
        _snakeSource.clip          = _snakeClip;
        _snakeSource.volume        = 0f;
        _snakeSource.spatialBlend  = 1f;
        _snakeSource.minDistance   = _minDistance;
        _snakeSource.maxDistance   = _maxDistance;
        _snakeSource.loop          = true;
        _snakeSource.playOnAwake   = false;
        _snakeSource.rolloffMode   = AudioRolloffMode.Linear;
        _snakeSource.Play();
    }

    // ── Movement ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the AudioSource in a circular path around the AudioListener
    /// with perlin-noise wobble for organic snake-crawl feel.
    /// </summary>
    private IEnumerator CrawlRoutine()
    {
        float elapsed = 0f;

        while (_snakeSource != null && _snakeSource.isPlaying && !_stopping)
        {
            var listener = GetListenerPosition();
            if (listener.HasValue)
            {
                float t = elapsed * NoiseScale;
                float radiusWobble  = (Mathf.PerlinNoise(t, 0f)          - 0.5f) * _wobbleAmount * _radius;
                float heightWobble  = (Mathf.PerlinNoise(0f, t + 100f)    - 0.5f) * _wobbleAmount * Mathf.Abs(_heightOffset);

                float angle = (elapsed / _revolutionDuration) * FullCircle;
                var offset = new Vector3(
                    Mathf.Cos(angle) * (_radius + radiusWobble),
                    _heightOffset + heightWobble,
                    Mathf.Sin(angle) * (_radius + radiusWobble)
                );

                _snakeObject.transform.position = listener.Value + offset;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static Vector3? GetListenerPosition()
    {
        var listener = FindFirstObjectByType<AudioListener>();
        if (listener != null)
            return listener.transform.position;

        var cam = Camera.main;
        return cam != null ? cam.transform.position : null;
    }

    // ── Fade ──────────────────────────────────────────────────────────────────

    private IEnumerator FadeRoutine(float from, float to, float duration)
    {
        if (_snakeSource == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration && _snakeSource != null)
        {
            elapsed += Time.deltaTime;
            _snakeSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (_snakeSource != null)
            _snakeSource.volume = to;

        _fadeRoutine = null;
    }

    private IEnumerator FadeOutAndDestroyRoutine(float duration)
    {
        if (_snakeSource != null)
        {
            float startVol = _snakeSource.volume;
            float elapsed  = 0f;

            while (elapsed < duration && _snakeSource != null)
            {
                elapsed += Time.deltaTime;
                _snakeSource.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
        }

        if (_snakeObject != null)
            Destroy(_snakeObject);

        _snakeSource = null;
        _snakeObject = null;
        _fadeRoutine = null;
    }
}
