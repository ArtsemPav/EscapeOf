using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Fires a horror event when the player, while inside this trigger zone,
/// looks at a target object (e.g. the glass pane of the Room 7 window).
/// Entering the zone alone is not enough — the player must turn their gaze
/// to the glass, and only then the linked event activates.
///
/// Attach to a GameObject with a trigger collider (e.g. Event_Window7Figure).
/// On activation calls  HorrorSystem.Instance.Trigger(_eventId)  once and
/// disables itself — the event never repeats (HasFired protection as well).
/// </summary>
public class WindowGazeTrigger : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Tag of the collider that activates the trigger.")]
    [SerializeField] private string _playerTag = "Player";

    [Tooltip("Horror event ID fired via HorrorSystem.Instance.Trigger.")]
    [SerializeField] private string _eventId = "window7_figure";

    [Header("Look Detection")]
    [Tooltip("Object the player must look at while inside the zone (e.g. the window glass pane).")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Dot product above which the player is considered to be looking at the target. " +
             "0.7 ≈ within 45°, 0.5 ≈ 60°.")]
    [SerializeField, Range(0f, 1f)] private float _lookAtThreshold = 0.7f;

    [Tooltip("Camera used for look checks. Leave empty to auto-assign Camera.main.")]
    [SerializeField] private Camera _playerCamera;

    [Header("Callbacks")]
    [Tooltip("Fired at the trigger moment, before the horror event's Activation Delay. " +
             "Use for immediate reactions.")]
    [SerializeField] private UnityEvent _onFired;

    private bool _playerInside;
    private Camera _cachedCamera;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(_playerTag)) return;
        _playerInside = true;
        TryFire();
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(_playerTag))
            _playerInside = false;
    }

    private void Update()
    {
        if (_playerInside)
            TryFire();
    }

    /// <summary>
    /// Fires the linked horror event when the player inside the zone looks at the target,
    /// then disables itself so the trigger works only once.
    /// Also stays disabled if the event already fired (e.g. restored from a save).
    /// </summary>
    private void TryFire()
    {
        if (HorrorSystem.Instance == null)
        {
            Debug.LogWarning($"[WindowGazeTrigger '{name}'] HorrorSystem not found in scene.", this);
            return;
        }

        if (HorrorSystem.Instance.HasFired(_eventId))
        {
            _playerInside = false;
            enabled = false;
            return;
        }

        if (_lookTarget == null) return;

        var camera = GetCamera();
        if (camera == null) return;

        Vector3 toTarget = (_lookTarget.position - camera.transform.position).normalized;
        float dot = Vector3.Dot(camera.transform.forward, toTarget);
        if (dot < _lookAtThreshold) return;

        HorrorSystem.Instance.Trigger(_eventId);
        _onFired?.Invoke();
        _playerInside = false;
        enabled = false;
    }

    private Camera GetCamera()
    {
        if (_cachedCamera == null)
            _cachedCamera = _playerCamera != null ? _playerCamera : Camera.main;
        return _cachedCamera;
    }
}
