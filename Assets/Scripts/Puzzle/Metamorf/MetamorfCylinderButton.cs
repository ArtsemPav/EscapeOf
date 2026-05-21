using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placed on each of Cylinder 1–4. Implements IInteractable.
/// On interaction: detects nearby decorative cylinders, reparents them, 
/// and rotates ITSELF by 90 degrees cumulative (carrying children with it).
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class MetamorfCylinderButton : MonoBehaviour, IInteractable
{
    [Header("Detection")]
    [Tooltip("Layer mask for the decorative cylinders (Cylinder.001–.012). Set to Default.")]
    [SerializeField] private LayerMask _detectableLayerMask;

    [Header("Animation")]
    [SerializeField] private float _rotationDuration = 0.4f;
    [SerializeField] private float _stepAngle = 90f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired after each rotation step completes.</summary>
    public event Action OnStateChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Toggle state that changes with every rotation step.</summary>
    public bool IsCollapsed => _isCollapsed;

    private bool _isCollapsed;
    private bool _isAnimating;
    private SphereCollider _sphereCollider;

    private void Awake()
    {
        _sphereCollider = GetComponent<SphereCollider>();
    }

    private void OnDisable()
    {
        _isAnimating = false;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public void Interact()
    {
        if (_isAnimating) return;
        StartCoroutine(RotateStepRoutine());
    }

    public bool CanInteract() => !_isAnimating;

    public string GetInteractText() => "Повернуть";

    public bool IsPickable() => false;

    public bool UseLMBClick => true;

    public CrosshairMode GetCrosshairMode() => CrosshairMode.Point;

    public string GetBlockedHint() => string.Empty;

    // ── Animation Routine ────────────────────────────────────────────────────

    private IEnumerator RotateStepRoutine()
    {
        _isAnimating = true;

        // 1. Capture decor cylinders
        CaptureDecors();

        // 2. Animate cumulative rotation of THIS cylinder
        Quaternion startRotation = transform.localRotation;
        Quaternion endRotation = startRotation * Quaternion.Euler(0, _stepAngle, 0);

        float elapsed = 0f;
        while (elapsed < _rotationDuration)
        {
            elapsed += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(startRotation, endRotation, elapsed / _rotationDuration);
            yield return null;
        }

        transform.localRotation = endRotation;

        // 3. Release decor cylinders
        ReleaseDecors();

        // Toggle state for the puzzle controller
        _isCollapsed = !_isCollapsed;

        _isAnimating = false;
        OnStateChanged?.Invoke();
    }

    private void CaptureDecors()
    {
        Vector3 worldCenter = transform.TransformPoint(_sphereCollider.center);
        Vector3 lossyScale = transform.lossyScale;
        float worldRadius = _sphereCollider.radius * Mathf.Max(lossyScale.x, lossyScale.y, lossyScale.z);

        Collider[] hits = Physics.OverlapSphere(worldCenter, worldRadius, _detectableLayerMask);

        foreach (Collider hit in hits)
        {
            if (hit.TryGetComponent<MetamorfDecorCylinder>(out var decor))
            {
                decor.transform.SetParent(this.transform, worldPositionStays: true);
            }
        }
    }

    private void ReleaseDecors()
    {
        var children = GetComponentsInChildren<MetamorfDecorCylinder>();
        foreach (var decor in children)
        {
            decor.transform.SetParent(decor.OriginalParent, worldPositionStays: true);
        }
    }
}
