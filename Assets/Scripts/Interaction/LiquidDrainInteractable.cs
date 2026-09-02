using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Liquid drain interaction for sinks, baths, and other liquid-filled containers.
///
/// The player looks at the liquid and clicks to drain it, revealing items hidden beneath.
/// Optionally requires an item in the inventory (e.g. a plunger) — presence only, not consumed.
/// When fully drained, disables the collider so the player can interact with items below.
/// Implements ISaveable: persists the drained state across sessions.
///
/// Requires BathLiquidController on the same GameObject for fill-level control.
/// </summary>
[RequireComponent(typeof(BathLiquidController))]
public class LiquidDrainInteractable : MonoBehaviour, IInteractable, ISaveable
{
    // ── Settings ──────────────────────────────────────────────────────────────

    [Header("Drain Settings")]
    [Tooltip("Time in seconds for the liquid to fully drain.")]
    [SerializeField] private float _drainDuration = 5f;

    [Tooltip("Initial fill level (0-1). Set to match the desired starting water level.")]
    [SerializeField] [Range(0f, 1f)] private float _initialFill = 0.8f;

    [Header("Item Requirement (optional)")]
    [Tooltip("If set, the player must have this item to drain the liquid. Not consumed.")]
    [SerializeField] private ItemData _requiredItem;

    [Tooltip("Hint shown when the player lacks the required item.")]
    [SerializeField] private string _missingItemHint = "Нужен вантуз";

    [Header("Audio")]
    [Tooltip("Drain sound played as a one-shot 3D spatial audio. Plays to completion even after the drain animation finishes.")]
    [SerializeField] private AudioClip _drainClip;

    [Tooltip("Volume of the drain sound.")]
    [SerializeField] [Range(0f, 1f)] private float _drainVolume = 0.8f;

    [Tooltip("Minimum distance at which the drain sound is at full volume.")]
    [SerializeField] private float _drainSoundMinDistance = 1f;

    [Tooltip("Maximum distance at which the drain sound is still audible.")]
    [SerializeField] private float _drainSoundMaxDistance = 10f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Слить воду";

    [Header("Save")]
    [Tooltip("Stable unique ID for the save system. Right-click → Generate Save ID to auto-fill.")]
    [SerializeField] private string _saveId;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private BathLiquidController _liquid;
    private Collider _collider;
    private AudioSource _drainSource;
    private bool _isDrained;
    private bool _isDraining;

    /// <summary>True when the liquid has been fully drained and the collider is disabled.</summary>
    public bool IsDrained => _isDrained;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>Serializes the drained state for the save system.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new LiquidDrainSaveData
    {
        isDrained = _isDrained,
    });

    /// <summary>Restores the drained state. Applied before Start() runs.</summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<LiquidDrainSaveData>(json);
        if (!data.isDrained) return;

        _isDrained  = true;
        _isDraining = false;
        if (_liquid != null)
            _liquid.SetRuntimeFill(0f);
        if (_collider != null)
            _collider.enabled = false;
    }

    [Serializable]
    private struct LiquidDrainSaveData
    {
        public bool isDrained;
    }

    [ContextMenu("Generate Save ID")]
    private void GenerateSaveId()
    {
        if (!string.IsNullOrEmpty(_saveId)) return;
        _saveId = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ── Unity lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        _liquid   = GetComponent<BathLiquidController>();
        _collider = GetComponent<Collider>();
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (!_isDrained)
            _liquid.SetRuntimeFill(_initialFill);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Interactable only when the liquid has not been drained or is not currently draining.</summary>
    public bool CanInteract() => !_isDrained && !_isDraining;

    /// <summary>Starts draining the liquid if requirements are met.</summary>
    public void Interact()
    {
        if (_isDrained || _isDraining) return;

        if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem))
            return;

        StartDrain();
    }

    public string GetInteractText() => _interactText;

    public bool IsPickable() => false;

    /// <summary>Single-click interaction — drain starts on LMB press.</summary>
    public bool UseLMBClick => true;

    /// <summary>Returns Locked when the required item is missing, Hand otherwise.</summary>
    public CrosshairMode GetCrosshairMode()
    {
        if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem))
            return CrosshairMode.Locked;
        return CrosshairMode.Hand;
    }

    /// <summary>Returns a hint when the required item is missing.</summary>
    public string GetBlockedHint()
    {
        if (_isDrained) return string.Empty;
        if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem))
            return _missingItemHint;
        return string.Empty;
    }

    // ── Drain logic ────────────────────────────────────────────────────────────

    /// <summary>Starts the drain animation and plays a one-shot 3D drain sound that plays to completion.</summary>
    private void StartDrain()
    {
        _isDraining = true;

        if (_drainClip != null)
        {
            _drainSource = gameObject.AddComponent<AudioSource>();
            _drainSource.clip             = _drainClip;
            _drainSource.volume           = _drainVolume;
            _drainSource.spatialBlend     = 1f;
            _drainSource.minDistance      = _drainSoundMinDistance;
            _drainSource.maxDistance      = _drainSoundMaxDistance;
            _drainSource.loop             = false;
            _drainSource.playOnAwake      = false;
            _drainSource.Play();
            AudioManager.Instance?.RegisterLoopSource(_drainSource, _drainVolume);
        }

        _liquid.AnimateFillTo(0f, _drainDuration);
        StartCoroutine(DrainCoroutine());
    }

    private IEnumerator DrainCoroutine()
    {
        // Wait for the drain animation to complete.
        yield return new WaitForSeconds(_drainDuration);

        _isDraining = false;
        _isDrained  = true;

        if (_collider != null)
            _collider.enabled = false;

        SaveManager.Instance?.Save();

        // Let the drain sound finish playing, then clean up.
        if (_drainSource != null)
        {
            yield return new WaitWhile(() => _drainSource != null && _drainSource.isPlaying);
            if (_drainSource != null)
            {
                AudioManager.Instance?.UnregisterLoopSource(_drainSource);
                Destroy(_drainSource);
                _drainSource = null;
            }
        }
    }
}
