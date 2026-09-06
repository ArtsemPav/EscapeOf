using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the interactive state of a single room.
/// Lock/Unlock controls whether the player can interact with objects inside.
/// Holds the room's light zones and an optional local post-processing Volume.
/// For performance culling it reports its light zones to RoomVisibilityManager and
/// toggles its own geometry via SetGeometryActive(); light suppression itself is applied
/// centrally by LightingSystem because a ZoneId can be shared across rooms.
/// </summary>
public class RoomController : MonoBehaviour
{
    /// <summary>
    /// Pairs a RoomTrigger with the rooms that stay rendered while the player is inside
    /// it. Lets a room own several triggers (e.g. along a long corridor), each lighting a
    /// different group of rooms.
    /// </summary>
    [System.Serializable]
    public class TriggerZone
    {
        [Tooltip("Trigger volume that activates this group of rooms.")]
        public RoomTrigger Trigger;

        [Tooltip("Rooms that stay rendered while the player is inside the trigger above.")]
        public RoomController[] VisibleRooms;
    }

    [Tooltip("Local post-processing Volume for this room. Leave empty if the room uses no post-processing.")]
    [SerializeField] private Volume _localVolume;

    [Header("Visibility")]
    [Tooltip("Per-trigger visibility zones. Assign a trigger and the rooms it should keep " +
             "rendered. Add more entries to control several triggers from this room.")]
    [SerializeField] private TriggerZone[] _triggerZones;

    public bool IsUnlocked { get; private set; }

    /// <summary>The local post-processing Volume assigned to this room. May be null.</summary>
    public Volume LocalVolume => _localVolume;

    /// <summary>Distinct light zone ids used by lamps in this room.</summary>
    public IReadOnlyList<string> ZoneIds => _zoneIds;

    private Collider[] _interactableColliders;
    private string[] _zoneIds;
    private Renderer[] _managedRenderers;

    private void Awake()
    {
        CollectInteractableColliders();
        CollectZoneIds();
        CollectRenderers();
        ConfigureTriggers();
    }

    /// <summary>
    /// Injects each configured trigger with the room list it should activate. Zones that
    /// reference the same trigger are merged, so a duplicated entry never silently
    /// overwrites another.
    /// </summary>
    private void ConfigureTriggers()
    {
        if (_triggerZones == null) return;

        var merged = new Dictionary<RoomTrigger, List<RoomController>>();

        foreach (var zone in _triggerZones)
        {
            if (zone?.Trigger == null) continue;

            if (!merged.TryGetValue(zone.Trigger, out var rooms))
            {
                rooms = new List<RoomController>();
                merged.Add(zone.Trigger, rooms);
            }

            if (zone.VisibleRooms == null) continue;
            foreach (var room in zone.VisibleRooms)
                if (room != null && !rooms.Contains(room)) rooms.Add(room);
        }

        foreach (var pair in merged)
            pair.Key.Configure(this, pair.Value.ToArray());
    }

    /// <summary>Collects colliders only from objects that implement IInteractable.</summary>
    private void CollectInteractableColliders()
    {
        var behaviours = GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
        var colliderList = new List<Collider>();

        foreach (var behaviour in behaviours)
        {
            if (behaviour is IInteractable && behaviour.TryGetComponent(out Collider col))
                colliderList.Add(col);
        }

        _interactableColliders = colliderList.ToArray();
    }

    /// <summary>Collects the distinct light zone ids used by lamps in this room.</summary>
    private void CollectZoneIds()
    {
        var zones = GetComponentsInChildren<LightZone>(includeInactive: true);
        var idSet = new HashSet<string>();

        foreach (var zone in zones)
        {
            if (!string.IsNullOrEmpty(zone.ZoneId))
                idSet.Add(zone.ZoneId);
        }

        _zoneIds = new string[idSet.Count];
        idSet.CopyTo(_zoneIds);
    }

    /// <summary>
    /// Collects renderers that are enabled at startup. Authored-disabled renderers are
    /// left untouched so culling never force-enables something meant to stay hidden.
    /// </summary>
    private void CollectRenderers()
    {
        var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        var managed = new List<Renderer>();

        foreach (var renderer in renderers)
        {
            if (renderer != null && renderer.enabled)
                managed.Add(renderer);
        }

        _managedRenderers = managed.ToArray();
    }

    /// <summary>
    /// Performance gate for this room's geometry. Toggles only the renderers that were
    /// enabled at startup, preserving gameplay state (colliders, logic, audio).
    /// Light zones are handled centrally by RoomVisibilityManager + LightingSystem,
    /// because a single ZoneId can be shared across several rooms.
    /// </summary>
    public void SetGeometryActive(bool active)
    {
        if (_managedRenderers == null) return;

        foreach (var renderer in _managedRenderers)
        {
            if (renderer != null)
                renderer.enabled = active;
        }
    }

    /// <summary>
    /// Controls whether the room's renderers participate in occlusion culling.
    /// Set to false when the room is behind a removed wall (puzzle gate) so baked
    /// occlusion data does not cull geometry that should be visible through the doorway.
    /// </summary>
    public void SetOcclusionCulling(bool enabled)
    {
        if (_managedRenderers == null) return;

        foreach (var renderer in _managedRenderers)
        {
            if (renderer != null)
                renderer.allowOcclusionWhenDynamic = enabled;
        }
    }

    /// <summary>Enables interaction with all IInteractable objects in this room.</summary>
    public void Unlock()
    {
        IsUnlocked = true;
        SetCollidersEnabled(true);
    }

    /// <summary>Disables interaction with all IInteractable objects in this room.</summary>
    public void Lock()
    {
        IsUnlocked = false;
        SetCollidersEnabled(false);
    }

    private void SetCollidersEnabled(bool enabled)
    {
        if (_interactableColliders == null) return;
        foreach (var col in _interactableColliders)
        {
            if (col != null)
                col.enabled = enabled;
        }
    }
}
