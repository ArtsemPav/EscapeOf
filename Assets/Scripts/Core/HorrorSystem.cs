using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton coordinator for all horror events in the scene.
/// Subscribes to InventorySystem and GameManager and notifies registered
/// HorrorEvent instances when their trigger conditions are met.
///
/// Usage — manual trigger from any script:
///   HorrorSystem.Instance.Trigger("my_event_id");
/// </summary>
public class HorrorSystem : MonoBehaviour
{
    public static HorrorSystem Instance { get; private set; }

    private readonly List<HorrorEvent> _events = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged += OnInventoryChanged;
        else
            Debug.LogWarning("[HorrorSystem] InventorySystem not found — OnItemPickup triggers won't fire.", this);

        if (GameManager.Instance != null)
            GameManager.Instance.OnRoomChanged += OnRoomChanged;
        else
            Debug.LogWarning("[HorrorSystem] GameManager not found — OnRoomEnter triggers won't fire.", this);
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= OnInventoryChanged;

        if (GameManager.Instance != null)
            GameManager.Instance.OnRoomChanged -= OnRoomChanged;
    }

    /// <summary>Called by HorrorEvent on Start to subscribe to the system.</summary>
    public void Register(HorrorEvent evt)
    {
        if (!_events.Contains(evt))
            _events.Add(evt);
    }

    /// <summary>Called by HorrorEvent on Destroy to unsubscribe.</summary>
    public void Unregister(HorrorEvent evt) => _events.Remove(evt);

    /// <summary>Manually fires all horror events with the given ID.</summary>
    public void Trigger(string eventId)
    {
        foreach (var evt in _events)
            if (!evt.HasFired && evt.EventId == eventId)
                evt.Activate();
    }

    private void OnInventoryChanged()
    {
        if (InventorySystem.Instance == null) return;

        foreach (var evt in _events)
        {
            if (evt.HasFired || evt.TriggerType != HorrorTriggerType.OnItemPickup) continue;
            if (evt.RequiredItem != null && InventorySystem.Instance.HasItem(evt.RequiredItem))
                evt.Activate();
        }
    }

    private void OnRoomChanged(int roomIndex)
    {
        foreach (var evt in _events)
        {
            if (evt.HasFired || evt.TriggerType != HorrorTriggerType.OnRoomEnter) continue;
            if (evt.RequiredRoomIndex == roomIndex)
                evt.Activate();
        }
    }
}
