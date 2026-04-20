using UnityEngine;

/// <summary>
/// Wall switch inside the painting room. Toggles a LightingSystem zone by ID.
/// Room lights just need a LightZone component with the matching ZoneId —
/// no direct Light references, so lights can live in any prefab or scene object.
/// State is persisted automatically by LightingSystem — no separate ISaveable needed.
/// Default zone state is ON (LightingSystem defaults to true for unknown zones).
/// </summary>
public class PaintingRoomLightSwitch : MonoBehaviour, IInteractable
{
    [Header("Interaction")]
    [SerializeField] private string _interactTextWhenOn  = "Выключить свет";
    [SerializeField] private string _interactTextWhenOff = "Включить свет";

    [Header("Lighting Zone")]
    [Tooltip("Must match the ZoneId on all LightZone components in the painting room, " +
             "and the _roomLightZoneId on LoopPuzzleController.")]
    [SerializeField] private string _zoneId = "painting_room";

    /// <summary>True when the zone is currently switched off.</summary>
    public bool IsLightOff
    {
        get
        {
            if (LightingSystem.Instance == null) return false;
            return !LightingSystem.Instance.GetZoneSwitchState(_zoneId);
        }
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public void Interact()
    {
        if (LightingSystem.Instance == null)
        {
            Debug.LogWarning("[PaintingRoomLightSwitch] LightingSystem not found in scene.", this);
            return;
        }
        LightingSystem.Instance.ToggleZoneSwitch(_zoneId);
    }

    public bool IsPickable()        => false;
    public bool UseLMBClick         => true;
    public string GetInteractText() => IsLightOff ? _interactTextWhenOff : _interactTextWhenOn;
}
