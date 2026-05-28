using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a smooth press-and-return animation on a target Transform
/// by displacing it along a configurable local axis.
/// Attach to any parent that owns the visual button mesh as a child.
/// </summary>
public class ButtonPressAnimation : MonoBehaviour
{
    [Tooltip("Transform that physically moves. If null, this GameObject's transform is used.")]
    [SerializeField] private Transform _pressTarget;

    [Tooltip("Axis of displacement in local space.")]
    [SerializeField] private PressAxis _axis = PressAxis.Y;

    [Tooltip("How far the button travels inward (in local units).")]
    [SerializeField] private float _pressDepth = 0.015f;

    [Tooltip("Duration of the inward press phase in seconds.")]
    [SerializeField] private float _pressDuration = 0.06f;

    [Tooltip("Duration of the return phase in seconds.")]
    [SerializeField] private float _returnDuration = 0.12f;

    private Vector3   _restPosition;
    private Coroutine _pressCoroutine;

    /// <summary>Fired once each time Play() is called — before the animation begins.</summary>
    public event Action OnPressed;

    private void Awake()
    {
        if (_pressTarget == null)
            _pressTarget = transform;

        _restPosition = _pressTarget.localPosition;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Triggers the press-and-return animation. Safe to call while already animating.</summary>
    public void Play()
    {
        OnPressed?.Invoke();

        if (_pressCoroutine != null)
            StopCoroutine(_pressCoroutine);

        _pressCoroutine = StartCoroutine(PressRoutine());
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private IEnumerator PressRoutine()
    {
        Vector3 pressedPosition = _restPosition + AxisDirection() * (-_pressDepth);

        // Press inward
        yield return MoveTo(pressedPosition, _pressDuration);

        // Return to rest
        yield return MoveTo(_restPosition, _returnDuration);

        _pressCoroutine = null;
    }

    private IEnumerator MoveTo(Vector3 target, float duration)
    {
        Vector3 start   = _pressTarget.localPosition;
        float   elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            _pressTarget.localPosition = Vector3.LerpUnclamped(start, target, t);
            yield return null;
        }

        _pressTarget.localPosition = target;
    }

    private Vector3 AxisDirection() => _axis switch
    {
        PressAxis.X => Vector3.right,
        PressAxis.Y => Vector3.up,
        PressAxis.Z => Vector3.forward,
        _           => Vector3.up
    };
}

public enum PressAxis { X, Y, Z }
