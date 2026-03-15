// This component has been replaced by HorrorEvent + HorrorSystem.
// Remove this component from the scene and use HorrorEvent instead.
#pragma warning disable CS0618
using System.Collections;
using UnityEngine;

/// <summary>
/// Activates a hidden horror character when the player picks up a specific item.
/// The character disappears only AFTER the player has clearly looked at it and then turned away.
///
/// State machine:
///   Inactive → Visible (on pickup) → Confirmed seen (dot > lookAtThreshold) → Gone (dot < lookAwayThreshold)
///
/// Keep this component on an always-active GameObject — NOT on the horror target itself,
/// because a disabled GameObject won't run Start() or receive events.
/// </summary>
public class HorrorTrigger : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The horror character to reveal. Starts hidden; disappears after player sees it and looks away.")]
    [SerializeField] private GameObject _horrorTarget;

    [Header("Trigger")]
    [Tooltip("The item the player must pick up to trigger the event.")]
    [SerializeField] private ItemData _triggerItem;
    [Tooltip("Seconds to wait after pickup before the character appears. 0 = immediate.")]
    [SerializeField] private float _activationDelay = 0f;

    [Header("Look Detection")]
    [Tooltip("Camera used for visibility checks. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;
    [Tooltip("Dot product threshold above which the player is confirmed to be clearly looking at the target.\n" +
             "0.7 ≈ within 45°, 0.5 ≈ within 60°.")]
    [SerializeField] private float _lookAtThreshold = 0.7f;
    [Tooltip("Dot product threshold below which the player is considered to have looked away.\n" +
             "0 = 90° off-axis. Only checked AFTER the player has first confirmed seeing the target.")]
    [SerializeField] private float _lookAwayThreshold = 0f;

    private bool _triggered;
    private bool _dummiVisible;
    private bool _hasSeenDummi; // true once player clearly looked at the target

    private void Start()
    {
        if (_horrorTarget != null)
            _horrorTarget.SetActive(false);

        if (_playerCamera == null)
            _playerCamera = Camera.main;

        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;
        else
            Debug.LogWarning("[HorrorTrigger] InventorySystem not found in scene.", this);
    }

    private void Update()
    {
        if (!_dummiVisible || _horrorTarget == null || _playerCamera == null)
            return;

        Vector3 toTarget = (_horrorTarget.transform.position - _playerCamera.transform.position).normalized;
        float dot = Vector3.Dot(_playerCamera.transform.forward, toTarget);

        if (!_hasSeenDummi)
        {
            // Phase 1: wait until the player is clearly looking at the target
            if (dot >= _lookAtThreshold)
                _hasSeenDummi = true;
        }
        else
        {
            // Phase 2: player has seen it — disappear the moment they look away
            if (dot < _lookAwayThreshold)
            {
                _horrorTarget.SetActive(false);
                _dummiVisible = false;
            }
        }
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;
    }

    private void OnInventoryChanged()
    {
        if (_triggered || _triggerItem == null || InventorySystem.Instance == null)
            return;

        if (InventorySystem.Instance.HasItem(_triggerItem))
        {
            _triggered = true;
            StartCoroutine(ActivateRoutine());
        }
    }

    /// <summary>Waits for the configured delay, then activates the horror target.</summary>
    private IEnumerator ActivateRoutine()
    {
        if (_activationDelay > 0f)
            yield return new WaitForSeconds(_activationDelay);

        if (_horrorTarget != null)
        {
            _horrorTarget.SetActive(true);
            _dummiVisible = true;
            _hasSeenDummi = false;
        }
    }
}
