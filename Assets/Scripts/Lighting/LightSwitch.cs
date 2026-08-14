using UnityEngine;

/// <summary>
/// Interactable light switch that controls a single lighting zone.
/// Place on any GameObject with a Collider. The player interacts with it via E key.
/// 
/// The switch always flips physically (animation + click sound) regardless of
/// master power state. When power is off, the switch state is still tracked —
/// lights will turn on when power is restored if the switch is in the ON position.
/// </summary>
public class LightSwitch : MonoBehaviour, IInteractable
{
    [Tooltip("The zone ID this switch controls. Must match LightZone.ZoneId on the lamp objects.")]
    [SerializeField] private string _zoneId;

    [Tooltip("Default switch state for a new game (no save). When power is first restored, " +
             "the zone will be ON or OFF depending on this value. Ignored when save data exists.")]
    [SerializeField] private bool _defaultSwitchState = true;

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

    /// <summary>
    /// Toggles the switch. The physical flip, click sound, and state update
    /// always happen — even without master power. LightingSystem.ApplyToZone
    /// checks IsPowered internally, so lights only respond when power is on.
    /// </summary>
    public void Interact()
    {
        if (LightingSystem.Instance == null) return;

        bool newState = LightingSystem.Instance.ToggleZoneSwitch(_zoneId);
        UpdateVisuals(newState);
        PlayAudio(newState);
    }

    public string GetInteractText()
    {
        if (LightingSystem.Instance != null && !LightingSystem.Instance.IsPowered)
            return _noPowerHint;

        bool isOn = LightingSystem.Instance?.GetZoneSwitchState(_zoneId) ?? _defaultSwitchState;
        return $"{_interactHint} [{(isOn ? "ВКЛ" : "ВЫКЛ")}]";
    }

    public bool IsPickable() => false;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Push the configured default into LightingSystem.
        // Only applies for new games — if save data was loaded, the zone already
        // has an explicit state and InitializeZoneSwitch will skip.
        LightingSystem.Instance?.InitializeZoneSwitch(_zoneId, _defaultSwitchState);

        // Sync handle position to the actual zone state at game start.
        bool isOn = LightingSystem.Instance?.GetZoneSwitchState(_zoneId) ?? _defaultSwitchState;
        UpdateVisuals(isOn);
    }

    // ── Internals ─────────────────────────────────────────────────────────────

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
