using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Interactable button that cycles the spectral lens on a target PaintingSpotlight.
/// Each press advances one of four steps (0→1→2→3→0), rotating a target Transform
/// by the configured angle per step around the configured axis.
/// Saves/loads its current step index via ISaveable.
/// </summary>
public class SpotlightLensButton : MonoBehaviour, IInteractable, ISaveable
{
    private const int StepCount = 4;

    [Header("Save")]
    [SerializeField] private string _saveId = "lens_button_unique_id";

    [Header("Interaction")]
    [SerializeField] private string _interactText       = "Сменить линзу";
    [SerializeField] private string _lockedInteractText = "Заблокировано";

    [Header("Target Spotlight")]
    [Tooltip("The spotlight (L1, L2 or L4) whose lens this button controls.")]
    [SerializeField] private PaintingSpotlight _targetSpotlight;

    [Header("Lens Cycle")]
    [Tooltip("Lens applied at step 0, 1, 2, 3 respectively. Must have exactly 4 entries.")]
    [SerializeField] private LensColor[] _lensOptions = { LensColor.None, LensColor.Red, LensColor.Blue, LensColor.Yellow };

    [Header("Rotation")]
    [Tooltip("Transform that physically rotates. If null, this GameObject's transform is used.")]
    [SerializeField] private Transform _rotationTarget;
    [Tooltip("Axis of rotation in local space.")]
    [SerializeField] private RotationAxis _axis = RotationAxis.Y;
    [Tooltip("Angle applied per step in degrees.")]
    [SerializeField] private float _stepAngle = 15f;
    [Tooltip("Duration of the rotation animation in seconds.")]
    [SerializeField] private float _rotateDuration = 0.2f;

    private int       _currentStep;
    private bool      _isLocked;
    private Coroutine _rotateCoroutine;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { stepIndex = _currentStep });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _currentStep = Mathf.Clamp(data.stepIndex, 0, StepCount - 1);
        SnapRotation();
        ApplyCurrentLens();
    }

    [Serializable]
    private struct SaveData { public int stepIndex; }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_rotationTarget == null)
            _rotationTarget = transform;

        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        SnapRotation();
        ApplyCurrentLens();
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Locks or unlocks the button. When locked, Interact() is ignored.</summary>
    public void SetLocked(bool locked) => _isLocked = locked;

    /// <summary>Advances to the next step (0→1→2→3→0), rotates the button, and updates the spotlight lens.</summary>
    public void Interact()
    {
        if (_isLocked) return;

        _currentStep = (_currentStep + 1) % StepCount;
        ApplyCurrentLens();
        AnimateRotation();
    }

    public bool   IsPickable()        => false;
    public bool   UseLMBClick         => true;
    public string GetInteractText()   => _isLocked ? _lockedInteractText : _interactText;

    // ── Rotation ───────────────────────────────────────────────────────────────

    private void AnimateRotation()
    {
        if (_rotateCoroutine != null)
            StopCoroutine(_rotateCoroutine);
        _rotateCoroutine = StartCoroutine(RotateTo(TargetRotation()));
    }

    private void SnapRotation()
    {
        Vector3 euler = _rotationTarget.localEulerAngles;
        SetAxisValue(ref euler, _currentStep * _stepAngle);
        _rotationTarget.localEulerAngles = euler;
    }

    private IEnumerator RotateTo(Quaternion target)
    {
        Quaternion start   = _rotationTarget.localRotation;
        float      elapsed = 0f;

        while (elapsed < _rotateDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _rotateDuration));
            _rotationTarget.localRotation = Quaternion.Lerp(start, target, t);
            yield return null;
        }

        _rotationTarget.localRotation = target;
        _rotateCoroutine = null;
    }

    private Quaternion TargetRotation()
    {
        Vector3 euler = Vector3.zero;
        SetAxisValue(ref euler, _currentStep * _stepAngle);
        return Quaternion.Euler(euler);
    }

    private void SetAxisValue(ref Vector3 euler, float value)
    {
        switch (_axis)
        {
            case RotationAxis.X: euler.x = value; break;
            case RotationAxis.Y: euler.y = value; break;
            case RotationAxis.Z: euler.z = value; break;
        }
    }

    // ── Lens ───────────────────────────────────────────────────────────────────

    private void ApplyCurrentLens()
    {
        if (_lensOptions == null || _lensOptions.Length == 0) return;
        var lens = _lensOptions[Mathf.Clamp(_currentStep, 0, _lensOptions.Length - 1)];
        _targetSpotlight?.SetLens(lens);
    }
}

/// <summary>Local-space axis around which the button rotates.</summary>
public enum RotationAxis { X, Y, Z }
