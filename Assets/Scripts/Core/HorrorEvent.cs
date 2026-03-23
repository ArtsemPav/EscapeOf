using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>What causes this horror event to fire.</summary>
public enum HorrorTriggerType
{
    OnItemPickup,      // Player picks up a specific ItemData
    OnRoomEnter,       // Player enters a specific room (GameManager.OnRoomChanged index)
    OnManual,          // Fired explicitly via HorrorSystem.Instance.Trigger(eventId)
    OnPlayerEnterZone  // Player enters the trigger collider on this GameObject (requires BoxCollider + isTrigger)
}

/// <summary>What happens to the target when the event fires.</summary>
public enum HorrorEffectType
{
    AppearAndStay,                  // Activate target; it stays until manually hidden
    AppearThenDisappearOnLookAway,  // Activate target; hide it after player sees it and looks away
    AppearThenDisappearAfterDelay,  // Activate target; hide it automatically after a set delay
    DisappearOnTrigger              // Target starts visible; hides when the trigger fires
}

/// <summary>
/// Defines one self-contained horror moment: a trigger condition, a scene effect,
/// and optional UnityEvent callbacks for sounds, animations, etc.
///
/// Place this component on any always-active GameObject.
/// The _target object is what gets shown/hidden — it can be anywhere in the scene.
/// All HorrorEvents register automatically with HorrorSystem on Start.
/// Implements ISaveable: persists HasFired state so events don't replay after load.
/// </summary>
public class HorrorEvent : MonoBehaviour, ISaveable
{
    [Header("Identity")]
    [Tooltip("Unique string ID. Used to fire this event manually:\n  HorrorSystem.Instance.Trigger(\"id\")")]
    [SerializeField] private string _eventId;

    [Header("Trigger")]
    [SerializeField] private HorrorTriggerType _triggerType = HorrorTriggerType.OnItemPickup;

    [Tooltip("Item that must be picked up (OnItemPickup only).")]
    [SerializeField] private ItemData _requiredItem;

    [Tooltip("Room index to enter (OnRoomEnter only). Matches GameManager.CurrentRoomIndex.")]
    [SerializeField] private int _requiredRoomIndex = 1;

    [Header("Zone Trigger")]
    [Tooltip("Tag of the collider that triggers the event (OnPlayerEnterZone only). " +
             "Requires a BoxCollider with Is Trigger enabled on this GameObject.")]
    [SerializeField] private string _playerTag = "Player";

    [Tooltip("Seconds between trigger and effect start.")]
    [SerializeField] private float _activationDelay = 0f;

    [Header("Effect")]
    [SerializeField] private HorrorEffectType _effectType = HorrorEffectType.AppearThenDisappearOnLookAway;

    [Tooltip("The scene object to show/hide as the horror effect.")]
    [SerializeField] private GameObject _target;

    [Tooltip("Seconds before auto-hiding the target (AppearThenDisappearAfterDelay only).")]
    [SerializeField] private float _disappearDelay = 3f;

    [Header("Look Detection")]
    [Tooltip("Camera for look checks. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Dot product above which the player is confirmed to be looking at the target.\n" +
             "0.7 ≈ within 45°  |  0.5 ≈ within 60°")]
    [SerializeField] private float _lookAtThreshold = 0.7f;

    [Tooltip("Dot product below which the player is considered to have looked away.\n" +
             "0 = 90° off-axis. Only active AFTER the player has first confirmed seeing the target.")]
    [SerializeField] private float _lookAwayThreshold = 0f;

    [Header("Callbacks")]
    [Tooltip("Fired when the effect starts (after delay). Wire up audio, animation, etc.")]
    [SerializeField] private UnityEvent _onActivated;

    [Tooltip("Fired when the target is hidden. Wire up audio, animation, etc.")]
    [SerializeField] private UnityEvent _onDeactivated;

    // Public read-only state used by HorrorSystem for trigger matching
    public string EventId => _eventId;
    public HorrorTriggerType TriggerType => _triggerType;
    public ItemData RequiredItem => _requiredItem;
    public int RequiredRoomIndex => _requiredRoomIndex;
    public bool HasFired { get; private set; }

    private bool _targetVisible;
    private bool _hasSeenTarget;

    // True when loaded state requires showing the target after Start() hides it
    private bool _pendingActivation;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    /// <summary>Uses _eventId as the stable save key. Must be unique across all HorrorEvents.</summary>
    public string SaveId => string.IsNullOrEmpty(_eventId) ? string.Empty : "horror_" + _eventId;

    /// <summary>Serializes whether this event has already fired.</summary>
    public string GetSaveData() => JsonUtility.ToJson(new HorrorEventSaveData { hasFired = HasFired });

    /// <summary>
    /// Restores HasFired state. For AppearAndStay effects, marks the target to be shown in Start().
    /// </summary>
    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<HorrorEventSaveData>(json);
        if (!data.hasFired) return;
        HasFired = true;
        _pendingActivation = _effectType == HorrorEffectType.AppearAndStay;
    }

    [Serializable]
    private struct HorrorEventSaveData
    {
        public bool hasFired;
    }

    private void Awake()
    {
        // Register before SaveManager.Start() distributes loaded data
        if (!string.IsNullOrEmpty(_eventId))
            SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // For AppearAndStay events that already fired: keep target visible.
        // For all other cases: hide target at start (or leave DisappearOnTrigger visible).
        if (_target != null)
        {
            if (_effectType == HorrorEffectType.DisappearOnTrigger)
                _target.SetActive(!HasFired); // already disappeared if fired
            else
                _target.SetActive(_pendingActivation); // true only for AppearAndStay
        }
        _pendingActivation = false;

        if (_playerCamera == null)
            _playerCamera = Camera.main;

        if (HorrorSystem.Instance != null)
            HorrorSystem.Instance.Register(this);
        else
            Debug.LogWarning($"[HorrorEvent '{_eventId}'] HorrorSystem not found. Make sure HorrorSystem is in the scene.", this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        HorrorSystem.Instance?.Unregister(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggerType != HorrorTriggerType.OnPlayerEnterZone) return;
        if (!other.CompareTag(_playerTag)) return;
        Activate();
    }

    private void Update()
    {
        if (!_targetVisible || _target == null || _playerCamera == null) return;
        if (_effectType != HorrorEffectType.AppearThenDisappearOnLookAway) return;

        Vector3 toTarget = (_target.transform.position - _playerCamera.transform.position).normalized;
        float dot = Vector3.Dot(_playerCamera.transform.forward, toTarget);

        if (!_hasSeenTarget)
        {
            // Phase 1: wait until player is clearly looking at the target
            if (dot >= _lookAtThreshold)
                _hasSeenTarget = true;
        }
        else
        {
            // Phase 2: player has confirmed seeing it — hide the moment they look away
            if (dot < _lookAwayThreshold)
                Deactivate();
        }
    }

    /// <summary>Called by HorrorSystem when the trigger condition is met, or call directly for manual control.</summary>
    public void Activate()
    {
        if (HasFired) return;
        HasFired = true;
        StartCoroutine(ActivateRoutine());
    }

    private IEnumerator ActivateRoutine()
    {
        if (_activationDelay > 0f)
            yield return new WaitForSeconds(_activationDelay);

        if (_target != null)
            _target.SetActive(true);

        _onActivated?.Invoke();

        switch (_effectType)
        {
            case HorrorEffectType.AppearAndStay:
                break;

            case HorrorEffectType.AppearThenDisappearOnLookAway:
                _targetVisible = true;
                _hasSeenTarget = false;
                break;

            case HorrorEffectType.AppearThenDisappearAfterDelay:
                yield return new WaitForSeconds(_disappearDelay);
                Deactivate();
                break;

            case HorrorEffectType.DisappearOnTrigger:
                // Target was already visible — just hide it (with optional delay).
                if (_activationDelay > 0f)
                    yield return new WaitForSeconds(_activationDelay);
                Deactivate();
                break;
        }
    }

    /// <summary>Hides the target and fires OnDeactivated. Safe to call from outside.</summary>
    public void Deactivate()
    {
        if (_target != null)
            _target.SetActive(false);

        _targetVisible = false;
        _onDeactivated?.Invoke();
    }
}
