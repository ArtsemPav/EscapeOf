using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central singleton that manages all light zones and the master power state.
/// 
/// Power model:
///   - When power is OFF → all lights are disabled regardless of switch states.
///   - When power is ON  → each zone reflects its own switch state (on/off).
/// 
/// LightZone components register themselves on Awake.
/// LightSwitch components call SetZoneSwitch() to toggle individual zones.
/// ElectricPanel calls SetPower() to control the master breaker.
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

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _isPowered = true;

    /// <summary>True when the master breaker (щиток) is supplying power.</summary>
    public bool IsPowered => _isPowered;

    // zoneId → list of LightZone components registered from the scene/prefabs
    private readonly Dictionary<string, List<LightZone>> _zones = new();

    // zoneId → switch state (true = switch is ON). Default true when first seen.
    private readonly Dictionary<string, bool> _switchStates = new();

    // Active fade coroutines per zone
    private readonly Dictionary<string, Coroutine> _fadeCoroutines = new();

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "LightingSystem";

    [Serializable]
    private class SaveData
    {
        public bool isPowered;
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
        var data = new SaveData { isPowered = _isPowered };
        foreach (var kvp in _switchStates)
            data.zones.Add(new ZoneRecord { zoneId = kvp.Key, switchOn = kvp.Value });
        return JsonUtility.ToJson(data);
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data == null) return;

        _isPowered = data.isPowered;

        _switchStates.Clear();
        foreach (var record in data.zones)
            _switchStates[record.zoneId] = record.switchOn;

        // Apply loaded state to all already-registered lights.
        RefreshAllZones();
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

        // Ensure a default switch state exists for newly discovered zones.
        if (!_switchStates.ContainsKey(zone.ZoneId))
            _switchStates[zone.ZoneId] = true;

        // Apply current state immediately so the light starts correct.
        bool shouldBeOn = _isPowered && _switchStates[zone.ZoneId];
        zone.SetActive(shouldBeOn);
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
    /// </summary>
    public void SetPower(bool on)
    {
        if (_isPowered == on) return;
        _isPowered = on;
        OnPowerChanged?.Invoke(_isPowered);
        RefreshAllZones();
        SaveManager.Instance?.Save();
    }

    /// <summary>Toggles master power.</summary>
    public void TogglePower() => SetPower(!_isPowered);

    // ── Zone Switch API ───────────────────────────────────────────────────────

    /// <summary>
    /// Sets the switch state for a zone. The zone lights up only if power is also on.
    /// </summary>
    public void SetZoneSwitch(string zoneId, bool on)
    {
        _switchStates[zoneId] = on;
        OnZoneSwitchChanged?.Invoke(zoneId, on);

        bool shouldBeOn = _isPowered && on;
        ApplyToZone(zoneId, shouldBeOn);
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

    /// <summary>Returns true if the zone is both powered and switched on.</summary>
    public bool IsZoneLit(string zoneId) => _isPowered && GetZoneSwitchState(zoneId);

    // ── Internals ─────────────────────────────────────────────────────────────

    private void RefreshAllZones()
    {
        foreach (var zoneId in _zones.Keys)
        {
            bool shouldBeOn = _isPowered && GetZoneSwitchState(zoneId);
            ApplyToZone(zoneId, shouldBeOn);
        }
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
