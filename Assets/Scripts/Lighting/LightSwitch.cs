using UnityEngine;

/// <summary>
/// Interactable light switch that controls a single lighting zone.
/// Place on any GameObject with a Collider. The player interacts with it via E key.
/// 
/// When the master power (ElectricPanel) is off, the switch is blocked — it can
/// be flipped visually but will have no effect on lights until power is restored.
/// </summary>
public class LightSwitch : MonoBehaviour, IInteractable
{
    [Tooltip("The zone ID this switch controls. Must match LightZone.ZoneId on the lamp objects.")]
    [SerializeField] private string _zoneId;

    [Tooltip("Text shown when the player looks at the switch while power is on.")]
    [SerializeField] private string _interactHint = "Выключатель";

    [Tooltip("Text shown when the player looks at the switch while power is off.")]
    [SerializeField] private string _noPowerHint = "Нет питания";

    [Header("Visuals")]
    [Tooltip("Optional transform that rotates when the switch toggles (e.g. a lever or button).")]
    [SerializeField] private Transform _switchHandle;

    [Tooltip("Local rotation when switched ON.")]
    [SerializeField] private Vector3 _handleRotationOn = new Vector3(20f, 0f, 0f);

    [Tooltip("Local rotation when switched OFF.")]
    [SerializeField] private Vector3 _handleRotationOff = new Vector3(-20f, 0f, 0f);

    [Header("Audio")]
    [SerializeField] private AudioClip _switchOnClip;
    [SerializeField] private AudioClip _switchOffClip;

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool CanInteract() => true;

    public void Interact()
    {
        if (LightingSystem.Instance == null) return;

        // If power is off, switching is meaningless — show feedback but do nothing.
        if (!LightingSystem.Instance.IsPowered)
        {
            // Optional: play a click-dead sound here.
            return;
        }

        bool newState = LightingSystem.Instance.ToggleZoneSwitch(_zoneId);
        UpdateVisuals(newState);
        PlayAudio(newState);
    }

    public string GetInteractText()
    {
        if (LightingSystem.Instance != null && !LightingSystem.Instance.IsPowered)
            return _noPowerHint;

        bool isOn = LightingSystem.Instance?.GetZoneSwitchState(_zoneId) ?? true;
        return $"{_interactHint} [{(isOn ? "ВКЛ" : "ВЫКЛ")}]";
    }

    public bool IsPickable() => false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Sync handle position to the actual zone state at game start.
        bool isOn = LightingSystem.Instance?.GetZoneSwitchState(_zoneId) ?? true;
        UpdateVisuals(isOn);

        // Subscribe to power changes to update visuals when щиток cuts power.
        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnPowerChanged += OnPowerChanged;
    }

    private void OnDestroy()
    {
        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnPowerChanged -= OnPowerChanged;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    private void OnPowerChanged(bool isPowered)
    {
        // When power is cut, visually show the switch state hasn't changed — it just has no effect.
        // Optionally you could snap the handle to OFF here if desired.
    }

    private void UpdateVisuals(bool isOn)
    {
        if (_switchHandle == null) return;
        _switchHandle.localRotation = Quaternion.Euler(isOn ? _handleRotationOn : _handleRotationOff);
    }

    private void PlayAudio(bool isOn)
    {
        var clip = isOn ? _switchOnClip : _switchOffClip;
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip);
    }
}
