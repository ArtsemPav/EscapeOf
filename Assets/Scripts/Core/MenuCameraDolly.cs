using System.Collections;
using UnityEngine;

/// <summary>
/// Menu camera "arrival" move: starts from an offset behind the camera's own
/// position (pulled back along its view direction, slightly lowered) and drifts
/// into place while the start fade reveals the scene — the camera feels like it
/// enters the room. Attach to the menu camera; the camera's scene transform is
/// the resting pose all offsets are relative to.
/// The same component also plays the short push-forward on the transition to
/// the loading screen (BeginLoadingPush), after which the menu buttons are
/// blocked by the black fade anyway.
/// </summary>
public class MenuCameraDolly : MonoBehaviour
{
    private const float MaxFrameDeltaSeconds = 0.05f;

    [Tooltip("How far behind the resting pose the camera starts, in meters along its view direction (negative = backwards).")]
    [SerializeField, Min(0f)] private float _arrivalBackDistance = 2.2f;

    [Tooltip("Vertical drop of the camera's start pose below the resting pose, in meters.")]
    [SerializeField, Min(0f)] private float _arrivalDropHeight = 0.35f;

    [Tooltip("Arrival drift duration in seconds; should roughly match the start fade reveal.")]
    [SerializeField, Min(0.1f)] private float _arrivalDurationSeconds = 4f;

    [Tooltip("Push-forward distance along the view direction when the loading transition starts, in meters.")]
    [SerializeField, Min(0f)] private float _loadingPushDistance = 1.2f;

    [Tooltip("Push-forward duration in seconds, tuned to end under the fade to black.")]
    [SerializeField, Min(0.1f)] private float _loadingPushDurationSeconds = 1.2f;

    [Tooltip("Exit retreat duration in seconds: the camera slowly pulls back into its arrival start pose while the app quits.")]
    [SerializeField, Min(0.1f)] private float _exitRetreatDurationSeconds = 4f;

    /// <summary>Duration of the exit retreat beat; MainMenuController keeps its fade in sync with it.</summary>
    public float ExitRetreatDurationSeconds => _exitRetreatDurationSeconds;

    private Vector3 _restingPosition;
    private Quaternion _restingRotation;
    private Coroutine _activeRoutine;

    private void Awake()
    {
        _restingPosition = transform.position;
        _restingRotation = transform.rotation;
    }

    private void Start()
    {
        StartArrival();
    }

    /// <summary>
    /// Places the camera at the offset start pose and drifts it into the
    /// resting pose with an ease-out (fast entry, gentle landing).
    /// </summary>
    public void StartArrival()
    {
        if (_activeRoutine != null) StopCoroutine(_activeRoutine);

        Vector3 back = -transform.forward * _arrivalBackDistance + Vector3.down * _arrivalDropHeight;
        transform.position = _restingPosition + back;
        transform.rotation = _restingRotation;

        _activeRoutine = StartCoroutine(ArrivalRoutine());
    }

    /// <summary>
    /// Pushes the camera forward from wherever it currently is — the "walking
    /// into the loading screen" beat before the fade to black takes over.
    /// </summary>
    public void BeginLoadingPush()
    {
        if (_loadingPushDistance <= 0f || _loadingPushDurationSeconds <= 0f) return;
        if (_activeRoutine != null) StopCoroutine(_activeRoutine);

        Vector3 target = transform.position + transform.forward * _loadingPushDistance;
        _activeRoutine = StartCoroutine(PushRoutine(target, _loadingPushDurationSeconds));
    }

    /// <summary>
    /// Exit retreat: drifts the camera back out of the scene — into the same
    /// offset pose the arrival beat starts from (pulled back along the view
    /// direction, slightly lowered) — as if it reverses its entry. The caller
    /// covers the end with the fade to black and quits after it.
    /// </summary>
    public void BeginExitRetreat()
    {
        if (_exitRetreatDurationSeconds <= 0f) return;
        if (_activeRoutine != null) StopCoroutine(_activeRoutine);

        Vector3 back = -transform.forward * _arrivalBackDistance + Vector3.down * _arrivalDropHeight;
        Vector3 target = _restingPosition + back;
        _activeRoutine = StartCoroutine(RetreatRoutine(target, _exitRetreatDurationSeconds));
    }

    private void OnDisable()
    {
        // A disabled component must not leave the camera stranded off its pose.
        if (_activeRoutine != null)
        {
            StopCoroutine(_activeRoutine);
            _activeRoutine = null;
        }
        transform.position = _restingPosition;
        transform.rotation = _restingRotation;
    }

    private IEnumerator ArrivalRoutine()
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < _arrivalDurationSeconds)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSeconds);
            float linear = Mathf.Clamp01(elapsed / _arrivalDurationSeconds);
            // Ease-out cubic: the entry speed reads as momentum, the landing is soft.
            float eased = 1f - (1f - linear) * (1f - linear) * (1f - linear);
            transform.position = Vector3.LerpUnclamped(start, _restingPosition, eased);
            yield return null;
        }

        transform.position = _restingPosition;
        _activeRoutine = null;
    }

    private IEnumerator PushRoutine(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSeconds);
            float linear = Mathf.Clamp01(elapsed / duration);
            // Ease-in quad: slow lean forward that accelerates into the fade.
            float eased = linear * linear;
            transform.position = Vector3.LerpUnclamped(start, target, eased);
            yield return null;
        }

        transform.position = target;
        _activeRoutine = null;
    }

    private IEnumerator RetreatRoutine(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxFrameDeltaSeconds);
            float linear = Mathf.Clamp01(elapsed / duration);
            // Ease-in cubic: slow departure that keeps accelerating into the fade —
            // the mirror of the arrival's ease-out, "the camera leaves the room".
            float eased = linear * linear * linear;
            transform.position = Vector3.LerpUnclamped(start, target, eased);
            yield return null;
        }

        transform.position = target;
        _activeRoutine = null;
    }
}
