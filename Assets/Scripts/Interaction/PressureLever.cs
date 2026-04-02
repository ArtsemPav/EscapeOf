using UnityEngine;

/// <summary>
/// A single toggle lever in the pressure puzzle.
/// Each lever contributes one of two fixed values to the total pressure:
/// _offValue when the lever is DOWN, _onValue when the lever is UP.
///
/// This component lives on the stick GameObject (the pivot).
/// The child Crutches mesh carries the MeshCollider — FPSController detects it
/// and walks up via GetComponentInParent to find this component.
///
/// Rotation is a delta offset from the pivot's original placement euler.
/// OFF = 0° delta (stays exactly where placed in the editor).
/// ON  = _angleOnDelta applied on top of the original rotation.
/// </summary>
public class PressureLever : MonoBehaviour, IInteractable
{
    // ── Settings ──────────────────────────────────────────────────────────────

    // _offValue / _onValue are assigned at runtime by PressurePuzzle.GenerateAndAssignLeverValues().
    // They are NOT serialized — never set them manually in the Inspector.
    private float _offValue = -10f;
    private float _onValue  =  10f;

    [Header("Visual Rotation")]
    [Tooltip("Z-axis rotation delta applied to the stick when switched ON. " +
             "OFF stays at the original editor placement rotation.")]
    [SerializeField] private float _angleOnDelta = -180f;
    [Tooltip("Rotation lerp speed toward the target angle.")]
    [SerializeField] private float _rotationSpeed = 8f;

    [Header("Audio")]
    [SerializeField] private AudioClip _switchClip;
    [SerializeField] [Range(0f, 1f)] private float _switchVolume = 0.8f;

    [Header("Interaction")]
    [SerializeField] private string _textWhenOff = "Включить рычаг";
    [SerializeField] private string _textWhenOn  = "Выключить рычаг";

    // ── Runtime state ─────────────────────────────────────────────────────────

    /// <summary>Whether the lever is currently switched ON.</summary>
    public bool IsOn { get; private set; }

    /// <summary>Pressure value this lever currently contributes to the total.</summary>
    public float CurrentValue => IsOn ? _onValue : _offValue;

    /// <summary>Pressure value when lever is in OFF state. Used by PressurePuzzle for range calculation.</summary>
    public float OffValue => _offValue;

    /// <summary>Pressure value when lever is in ON state. Used by PressurePuzzle for range calculation.</summary>
    public float OnValue => _onValue;

    private float _currentDelta;
    private float _targetDelta;
    private Vector3 _baseEuler;
    private AudioSource _audioSource;
    private PressurePuzzle _puzzle;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake  = false;
        _audioSource.spatialBlend = 1f;
        _audioSource.loop         = false;

        _puzzle = GetComponentInParent<PressurePuzzle>();

        // Capture placement rotation in Awake so PressurePuzzle.Start() can safely
        // call SetStateQuiet() before our own Start() runs.
        _baseEuler    = transform.localEulerAngles;
        _currentDelta = 0f;
        _targetDelta  = 0f;
    }

    private void Start()
    {
        // Nothing left here — kept for potential future use.
    }

    private void Update()
    {
        if (Mathf.Approximately(_currentDelta, _targetDelta)) return;

        _currentDelta = Mathf.Lerp(_currentDelta, _targetDelta, _rotationSpeed * Time.deltaTime);

        if (Mathf.Abs(_currentDelta - _targetDelta) < 0.05f)
            _currentDelta = _targetDelta;

        ApplyRotation(_currentDelta);
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    /// <summary>Toggles the lever and notifies the puzzle controller.</summary>
    public void Interact()
    {
        if (_puzzle != null && _puzzle.IsSolved) return;

        IsOn         = !IsOn;
        _targetDelta = IsOn ? _angleOnDelta : 0f;

        if (_switchClip != null)
            _audioSource.PlayOneShot(_switchClip, _switchVolume);

        _puzzle?.OnLeverChanged();
    }

    /// <summary>Returns hint text reflecting the current lever state.</summary>
    public string GetInteractText() => IsOn ? _textWhenOn : _textWhenOff;

    public bool IsPickable() => false;
    // LMB is already bound to the Interact action in PlayerInputActions.
    // UseLMBClick = false prevents HandleDragInteraction from firing a second Interact() call.
    public bool UseLMBClick => false;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PressurePuzzle.GenerateAndAssignLeverValues() before Start().
    /// Sets the pressure contribution this lever makes in each state.
    /// </summary>
    public void AssignValues(float offValue, float onValue)
    {
        _offValue = offValue;
        _onValue  = onValue;
    }

    private void ApplyRotation(float delta)
    {
        Vector3 euler = _baseEuler;
        euler.z += delta;
        transform.localEulerAngles = euler;
    }

    /// <summary>
    /// Sets the lever state immediately without animation and without notifying the puzzle.
    /// Used by PressurePuzzle during initialization to randomize starting positions.
    /// </summary>
    public void SetStateQuiet(bool on)
    {
        IsOn          = on;
        _currentDelta = on ? _angleOnDelta : 0f;
        _targetDelta  = _currentDelta;
        ApplyRotation(_currentDelta);
    }
}
