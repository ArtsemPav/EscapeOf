using System;
using UnityEngine;

/// <summary>
/// Generic component for simple electrical devices that should activate/deactivate
/// with the general power state. Register as an <see cref="IPowerConsumer"/> with
/// <see cref="LightingSystem"/> automatically.
///
/// For complex systems (e.g. puzzle controllers) implement
/// <see cref="IPowerConsumer"/> directly instead of using this component.
/// </summary>
public class ElectricDevice : MonoBehaviour, IPowerConsumer
{
    [Header("Toggle Targets")]
    [Tooltip("GameObjects to activate/deactivate with power.")]
    [SerializeField] private GameObject[] _toggleObjects;

    [Tooltip("Behaviours (Lights, AudioSources, etc.) to enable/disable with power.")]
    [SerializeField] private Behaviour[] _toggleBehaviours;

    /// <summary>True when master power is currently on.</summary>
    public bool IsPowered { get; private set; }

    /// <summary>Fired whenever the power state changes for this device.</summary>
    public event Action<bool> OnPowerChanged;

    private void Start()
    {
        LightingSystem.Instance?.RegisterConsumer(this);
    }

    private void OnDestroy()
    {
        LightingSystem.Instance?.UnregisterConsumer(this);
    }

    /// <summary>
    /// Called by LightingSystem when power state changes.
    /// Toggles assigned objects and behaviours, and fires <see cref="OnPowerChanged"/>.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        IsPowered = isPowered;

        foreach (var obj in _toggleObjects)
            if (obj != null) obj.SetActive(isPowered);

        foreach (var behaviour in _toggleBehaviours)
            if (behaviour != null) behaviour.enabled = isPowered;

        OnPowerChanged?.Invoke(isPowered);
    }
}
