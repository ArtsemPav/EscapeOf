using UnityEngine;

/// <summary>
/// Wall switch inside the painting room. Toggles a LightingSystem zone by ID.
/// Room lights just need a Lamp component with the matching ZoneId —
/// no direct Light references, so lights can live in any prefab or scene object.
/// State is persisted automatically by LightingSystem — no separate ISaveable needed.
/// Default zone state is ON (LightingSystem defaults to true for unknown zones).
/// </summary>
public class PaintingRoomLightSwitch : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private string _interactTextWhenOn  = "Выключить свет";
    [SerializeField] private string _interactTextWhenOff = "Включить свет";
    [SerializeField] private string _lockedInteractText  = "Заблокировано";

    [Header("Lighting Zone")]
    [Tooltip("Must match the ZoneId on all Lamp components in the painting room, " +
             "and the _roomLightZoneId on LoopPuzzleController.")]
    [SerializeField] private string _zoneId = "painting_room";

    private bool _isLocked;

    /// <summary>True when the zone is currently switched off.</summary>
    public bool IsLightOff
    {
        get
        {
            if (LightingSystem.Instance == null) return false;
            return !LightingSystem.Instance.GetZoneSwitchState(_zoneId);
        }
    }

    /// <summary>Locks or unlocks the switch. When locked, Interact() is ignored.</summary>
    public void SetLocked(bool locked) => _isLocked = locked;

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact()
    {
        if (_isLocked) return;

        if (LightingSystem.Instance == null)
        {
            Debug.LogWarning("[PaintingRoomLightSwitch] LightingSystem not found in scene.", this);
            return;
        }
        LightingSystem.Instance.ToggleZoneSwitch(_zoneId);
    }

    public bool IsPickable()        => false;
    public bool UseLMBClick         => true;
    public string GetInteractText()
    {
        if (_isLocked)
        {
            // Keep the Inspector fallback when this localization key is missing.
            return Localization.TryGet(LocalizationKeys.InteractBlocked, out string localizedBlocked)
                ? localizedBlocked
                : _lockedInteractText;
        }

        string key = IsLightOff ? LocalizationKeys.InteractLightSwitchOn : LocalizationKeys.InteractLightOff;
        string fallback = IsLightOff ? _interactTextWhenOff : _interactTextWhenOn;

        // Keep the Inspector fallback when this localization key is missing.
        return Localization.TryGet(key, out string localized) ? localized : fallback;
    }
}
