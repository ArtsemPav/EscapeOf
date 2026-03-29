using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Smooth FOV zoom triggered by holding RMB.
/// Attach to the GameObject that has a CinemachineCamera component (PlayerCamera).
/// Modifies CinemachineCamera.Lens.FieldOfView so the Cinemachine Brain
/// propagates the change to the rendering camera automatically.
/// Default FOV is read from the CinemachineCamera Lens at startup.
/// During zoom, sensitivity is reduced proportionally to the FOV change for a natural feel.
/// Zoom is automatically suppressed while any UI panel is open.
/// </summary>
public class CameraZoom : MonoBehaviour
{
    public static CameraZoom Instance { get; private set; }

    [Header("Zoom")]
    [Tooltip("Field of view while zoom is active. Default FOV is read from CinemachineCamera.Lens at startup.")]
    [SerializeField] private float zoomedFOV = 40f;

    [Tooltip("Speed of the FOV transition (higher = snappier).")]
    [SerializeField] private float zoomSpeed = 10f;

    private CinemachineCamera _cinemachineCamera;
    private float _defaultFOV;
    private float _currentFOV;
    private bool _isZooming;

    /// <summary>
    /// Mouse sensitivity multiplier to apply during zoom.
    /// Equals zoomedFOV / defaultFOV so angular speed stays constant regardless of zoom level.
    /// Returns 1 when not zooming.
    /// </summary>
    public float SensitivityMultiplier => _isZooming ? (zoomedFOV / _defaultFOV) : 1f;

    private void Awake()
    {
        Instance = this;
        _cinemachineCamera = GetComponent<CinemachineCamera>();

        if (_cinemachineCamera == null)
        {
            Debug.LogError("[CameraZoom] No CinemachineCamera found on this GameObject.", this);
            enabled = false;
            return;
        }

        _defaultFOV = _cinemachineCamera.Lens.FieldOfView;
        _currentFOV = _defaultFOV;
    }

    private void Update()
    {
        bool panelOpen = UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen;
        bool rmbHeld   = Mouse.current != null && Mouse.current.rightButton.isPressed;

        _isZooming = rmbHeld && !panelOpen;

        float targetFOV = _isZooming ? zoomedFOV : _defaultFOV;
        _currentFOV = Mathf.Lerp(_currentFOV, targetFOV, Time.deltaTime * zoomSpeed);

        // LensSettings is a struct — must copy, modify, assign back
        LensSettings lens   = _cinemachineCamera.Lens;
        lens.FieldOfView    = _currentFOV;
        _cinemachineCamera.Lens = lens;
    }
}
