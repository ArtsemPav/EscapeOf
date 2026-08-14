using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central singleton that manages all light zones, the master power state,
/// the generator readiness flag, and power consumer registration.
/// 
/// Power model:
///   - Generator must be restored first (SetGeneratorReady) before the electric
///     panel puzzle can be solved.
///   - Solving the electric panel puzzle calls ActivatePower() — this enables
///     general power for the first time.
///   - After initial activation, ElectricPanel can toggle power on/off for scares.
///   - When power is OFF → all lights are disabled regardless of switch states.
///   - When power is ON  → each zone reflects its own switch state (on/off).
/// 
/// LightZone components register themselves on Awake.
/// LightSwitch components call SetZoneSwitch() to toggle individual zones.
/// ElectricPanel calls SetPower() to control the master breaker.
/// IPowerConsumer implementations register via RegisterConsumer() and receive
/// notifications whenever master power changes.
/// </summary>
[DefaultExecutionOrder(-5)]
public class LightingSystem : MonoBehaviour, ISaveable
{
    public static LightingSystem Instance { get; private set; }

    [Header("Fade")]
    [Tooltip("How long lights take to turn on/off in seconds. 0 = instant.")]
    [SerializeField] private float _fadeDuration = 0.3f;

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when master power changes. Parameter: isPowered.</summary>
    public event Action<bool> OnPowerChanged;

    /// <summary>Fired when a zone's switch state changes. Parameters: zoneId, isSwitchedOn.</summary>
    public event Action<string, bool> OnZoneSwitchChanged;

    /// <summary>Fired when the generator readiness state changes. Parameter: isReady.</summary>
    public event Action<bool> OnGeneratorReadyChanged;

    // ── State ─────────────────────────────────────────────────────────────────

    // Power starts OFF — the electric panel puzzle must activate it first.
    private bool _isPowered = false;

    /// <summary>True when the master breaker (щиток) is supplying power.</summary>
    public bool IsPowered => _isPowered;

    // Generator must be restored before the electric panel puzzle can be solved.
    private bool _isGeneratorReady = false;

    /// <summary>True when the generator has been restored and is supplying power to the electric panel.</summary>
    public bool IsGeneratorReady => _isGeneratorReady;

    // Tracks whether power has ever been activated by the electric panel puzzle.
    // Until this is true, ElectricPanel cannot toggle power.
    private bool _isPowerActivated = false;

    /// <summary>True after the electric panel puzzle has activated power at least once.</summary>
    public bool IsPowerActivated => _isPowerActivated;

    // zoneId → list of LightZone components registered from the scene/prefabs
    private readonly Dictionary<string, List<LightZone>> _zones = new();

    // zoneId → switch state (true = switch is ON). Default true when first seen.
    private readonly Dictionary<string, bool> _switchStates = new();

    // Zones suppressed for performance (player not in/near them). Not persisted to saves.
    private readonly HashSet<string> _suppressedZones = new();

    // Active fade coroutines per zone
    private readonly Dictionary<string, Coroutine> _fadeCoroutines = new();

    // Registered power consumers — notified whenever master power changes.
    private readonly List<IPowerConsumer> _consumers = new();

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "LightingSystem";

    [Serializable]
    private class SaveData
    {
        public bool isPowered;
        public bool isGeneratorReady;
        public bool isPowerActivated;
        public List<ZoneRecord> zones = new();
    }

    [Serializable]
    private class ZoneRecord
    {
        public string zoneId;
        public bool switchOn;
    }

    public string GetSaveData()
    {
        var data = new SaveData
        {
            isPowered = _isPowered,
            isGeneratorReady = _isGeneratorReady,
            isPowerActivated = _isPowerActivated
        };
        foreach (var kvp in _switchStates)
            data.zones.Add(new ZoneRecord { zoneId = kvp.Key, switchOn = kvp.Value });
        return JsonUtility.ToJson(data);
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data == null) return;

        _isPowered = data.isPowered;
        _isGeneratorReady = data.isGeneratorReady;
        _isPowerActivated = data.isPowerActivated;

        // Backwards compatibility: old saves lack isGeneratorReady/isPowerActivated.
        // If power was on, assume the full chain was already completed.
        if (_isPowered && !_isPowerActivated)
        {
            _isPowerActivated = true;
            _isGeneratorReady = true;
        }

        _switchStates.Clear();
        foreach (var record in data.zones)
            _switchStates[record.zoneId] = record.switchOn;

        // Apply loaded state to all already-registered lights.
        RefreshAllZones();

        // Re-notify consumers — they registered during Awake() when _isPowered
        // was still the default (false). Save data may have changed it to true.
        NotifyConsumers(_isPowered);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SaveManager.Instance?.Unregister(this);
    }

    // ── Registration (called by LightZone) ────────────────────────────────────

    /// <summary>Called automatically by LightZone.Awake().</summary>
    public void RegisterZone(LightZone zone)
    {
        if (string.IsNullOrEmpty(zone.ZoneId))
        {
            Debug.LogWarning($"[LightingSystem] LightZone on '{zone.gameObject.name}' has no ZoneId — skipping.", zone);
            return;
        }

        if (!_zones.ContainsKey(zone.ZoneId))
            _zones[zone.ZoneId] = new List<LightZone>();
        _zones[zone.ZoneId].Add(zone);

        // Default switch state is NOT set here — LightSwitch.Start() calls
        // InitializeZoneSwitch() with its configured _defaultSwitchState.
        // Zones without a LightSwitch default to true via GetZoneSwitchState fallback.

        // Apply current state immediately so the light starts correct.
        zone.SetActive(IsZoneActive(zone.ZoneId));
    }

    /// <summary>Called automatically by LightZone.OnDestroy().</summary>
    public void UnregisterZone(LightZone zone)
    {
        if (string.IsNullOrEmpty(zone.ZoneId)) return;
        if (_zones.TryGetValue(zone.ZoneId, out var list))
            list.Remove(zone);
    }

    // ── Master Power API ──────────────────────────────────────────────────────

    /// <summary>
    /// Turns the master breaker on or off.
    /// When off, all lights go dark regardless of their switch states.
    /// When on, each zone restores to its switch state.
    /// Also notifies all registered IPowerConsumer instances.
    /// </summary>
    public void SetPower(bool on)
    {
        if (_isPowered == on) return;
        _isPowered = on;
        OnPowerChanged?.Invoke(_isPowered);
        NotifyConsumers(_isPowered);
        RefreshAllZones();
        SaveManager.Instance?.Save();
    }

    /// <summary>Toggles master power.</summary>
    public void TogglePower() => SetPower(!_isPowered);

    /// <summary>
    /// Marks the generator as restored. Called by GeneratorPuzzleController
    /// when the generator mini-game is completed. This unlocks the electric
    /// panel puzzle — the fuse can be inserted and the lever can be pulled.
    /// </summary>
    public void SetGeneratorReady(bool ready)
    {
        if (_isGeneratorReady == ready) return;
        _isGeneratorReady = ready;
        OnGeneratorReadyChanged?.Invoke(ready);
        SaveManager.Instance?.Save();
    }

    /// <summary>
    /// Called by ElectricPuzzleController when the wire puzzle is solved.
    /// Activates general power for the first time. After this, SetPower /
    /// TogglePower can be used by ElectricPanel for scripted scares.
    /// </summary>
    public void ActivatePower()
    {
        _isPowerActivated = true;
        SetPower(true);
    }

    // ── Power Consumer Registration ───────────────────────────────────────────

    /// <summary>
    /// Registers an IPowerConsumer to receive power state notifications.
    /// The current power state is delivered immediately upon registration.
    /// </summary>
    public void RegisterConsumer(IPowerConsumer consumer)
    {
        if (consumer == null) return;
        if (!_consumers.Contains(consumer))
        {
            _consumers.Add(consumer);
            consumer.OnPowerStateChanged(_isPowered);
        }
    }

    /// <summary>Unregisters an IPowerConsumer. No further notifications will be sent.</summary>
    public void UnregisterConsumer(IPowerConsumer consumer)
    {
        _consumers.Remove(consumer);
    }

    /// <summary>Notifies all registered consumers of the current power state.</summary>
    private void NotifyConsumers(bool isPowered)
    {
        foreach (var consumer in _consumers)
            consumer?.OnPowerStateChanged(isPowered);
    }

    // ── Zone Switch API ───────────────────────────────────────────────────────

    /// <summary>
    /// Sets the initial switch state for a zone. Only applies when the zone has
    /// no prior state (new game with no save data). If save data was loaded or
    /// the zone was already initialized, this call is ignored.
    /// Called by LightSwitch.Start() to push its configured _defaultSwitchState.
    /// </summary>
    public void InitializeZoneSwitch(string zoneId, bool defaultOn)
    {
        if (string.IsNullOrEmpty(zoneId)) return;
        if (_switchStates.ContainsKey(zoneId)) return;

        _switchStates[zoneId] = defaultOn;
        ApplyToZone(zoneId, IsZoneActive(zoneId));
    }

    /// <summary>
    /// Sets the switch state for a zone. The zone lights up only if power is also on.
    /// </summary>
    public void SetZoneSwitch(string zoneId, bool on)
    {
        _switchStates[zoneId] = on;
        OnZoneSwitchChanged?.Invoke(zoneId, on);

        ApplyToZone(zoneId, IsZoneActive(zoneId));
        SaveManager.Instance?.Save();
    }

    /// <summary>Toggles the switch for a zone. Returns the new switch state.</summary>
    public bool ToggleZoneSwitch(string zoneId)
    {
        bool current = GetZoneSwitchState(zoneId);
        SetZoneSwitch(zoneId, !current);
        return !current;
    }

    /// <summary>Returns the switch state for a zone (true = switched on). Defaults to true.</summary>
    public bool GetZoneSwitchState(string zoneId)
    {
        return _switchStates.TryGetValue(zoneId, out bool state) ? state : true;
    }

    /// <summary>Returns true if the zone is both powered and switched on (gameplay state, ignores performance suppression).</summary>
    public bool IsZoneLit(string zoneId) => _isPowered && GetZoneSwitchState(zoneId);

    // ── Performance Suppression API ───────────────────────────────────────────

    /// <summary>
    /// Performance layer: suppresses or restores a zone's rendering independently of gameplay state.
    /// A suppressed zone stays dark even when powered and switched on. Not persisted to saves.
    /// </summary>
    public void SetZoneRenderSuppressed(string zoneId, bool suppressed)
    {
        if (string.IsNullOrEmpty(zoneId)) return;

        bool changed = suppressed ? _suppressedZones.Add(zoneId) : _suppressedZones.Remove(zoneId);
        if (!changed) return;

        ApplyToZone(zoneId, IsZoneActive(zoneId));
    }

    /// <summary>True if the zone is currently suppressed for performance.</summary>
    public bool IsZoneRenderSuppressed(string zoneId) => _suppressedZones.Contains(zoneId);

    /// <summary>Final on/off state combining gameplay (power, switch) and performance suppression.</summary>
    private bool IsZoneActive(string zoneId) =>
        _isPowered && GetZoneSwitchState(zoneId) && !_suppressedZones.Contains(zoneId);

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RefreshAllZones()
    {
        foreach (var zoneId in _zones.Keys)
            ApplyToZone(zoneId, IsZoneActive(zoneId));
    }

    private void ApplyToZone(string zoneId, bool on)
    {
        if (!_zones.TryGetValue(zoneId, out var lights) || lights.Count == 0) return;

        if (_fadeCoroutines.TryGetValue(zoneId, out var existing) && existing != null)
            StopCoroutine(existing);

        if (_fadeDuration > 0f)
            _fadeCoroutines[zoneId] = StartCoroutine(FadeZone(lights, on));
        else
            SetZoneLights(lights, on ? 1f : 0f, on);
    }

    private IEnumerator FadeZone(List<LightZone> lights, bool on)
    {
        // Before fading out — stop flicker so intensity is at baseline (OriginalIntensity).
        if (!on)
            foreach (var z in lights) z?.StopFlicker();

        // Enable lights before fading in so intensity lerp is visible.
        if (on)
            foreach (var z in lights)
                if (z != null) { z.Light.enabled = true; z.SetIntensityMultiplier(0f); }

        float elapsed = 0f;
        while (elapsed < _fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _fadeDuration);
            float multiplier = on ? t : (1f - t);
            foreach (var zone in lights)
                zone?.SetIntensityMultiplier(multiplier);
            yield return null;
        }

        // After fade completes — hand control back to LightZone (enables flicker if any).
        foreach (var zone in lights)
            zone?.SetActive(on);
    }

    private static void SetZoneLights(List<LightZone> lights, float multiplier, bool enabled)
    {
        foreach (var zone in lights)
        {
            if (zone == null) continue;
            zone.SetIntensityMultiplier(multiplier);
            zone.SetActive(enabled);
        }
    }
}
