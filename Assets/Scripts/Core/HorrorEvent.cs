using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════════════════
// HORROR EVENT — ИНСТРУКЦИЯ
// ═══════════════════════════════════════════════════════════════════════════
//
// ЧТО ЭТО:
//   Один компонент = один хоррор-момент в игре.
//   У события есть ТРИГГЕР (когда сработать) и ЭФФЕКТ (что показать/скрыть).
//
// КАК ДОБАВИТЬ ХОРРОР-МОМЕНТ:
//   1. Создай дочерний GameObject под HorrorSystem (например Event_Shadow).
//   2. Добавь компонент HorrorEvent.
//   3. Выбери Trigger Type — что запустит событие.
//   4. Выбери Effect Type — что произойдёт с Target.
//   5. Назначь Target — объект который появится/исчезнет (должен быть НЕактивен).
//   6. При необходимости подключи On Activated / On Deactivated (звук, анимация).
//
// ТИПЫ ТРИГГЕРОВ:
//   OnItemPickup        — игрок подбирает конкретный предмет (Required Item).
//   OnRoomEnter         — игрок входит в комнату с индексом Required Room Index.
//   OnManual            — только ручной вызов: HorrorSystem.Instance.Trigger("id").
//   OnPlayerEnterZone   — игрок входит в trigger-коллайдер на этом GameObject
//                         (нужен BoxCollider с Is Trigger = true).
//                         Если назначен Puzzle To Watch — зона игнорируется
//                         пока эта загадка не решена (prerequisite).
//   OnPuzzleSolved      — привязка к загадке. Укажи Puzzle To Watch (объект с
//                         PuzzleModeController). Событие сработает когда загадка
//                         решена. Если загадка уже решена (из сейва) — сработает
//                         сразу при старте.
//   OnPowerStateChanged — привязка к электричеству. Событие сработает когда
//                         мастер-питание LightingSystem перейдёт в состояние
//                         Required Power State (true = включилось, false = выключилось).
//                         Если питание уже в нужном состоянии при старте — сработает сразу.
//   OnZoneSwitchChanged — привязка к выключателю света. Укажи Required Zone Id
//                         (строковый ID зоны освещения) и Required Zone State
//                         (true = свет включился, false = выключился).
//                         Если зона уже в нужном состоянии при старте — сработает сразу.
//
// ТИПЫ ЭФФЕКТОВ:
//   AppearAndStay                   — Target появляется и остаётся навсегда.
//   AppearThenDisappearOnLookAway   — Target появляется; исчезает после того как
//                                     игрок посмотрел на него и отвернулся.
//   AppearThenDisappearAfterDelay   — Target появляется и исчезает через Disappear Delay секунд.
//   DisappearOnTrigger              — Target стартует видимым; скрывается при срабатывании.
//
// ПРИВЯЗКА К ДРУГИМ СИСТЕМАМ (универсальный способ):
//   Если нужного триггера нет в списке — используй OnManual и подключи через
//   GameEventListener (компонент) который вызовет HorrorEvent.Activate().
//   Или вызови из кода: HorrorSystem.Instance.Trigger("event_id");
//
// SOUND OBJECT:
//   В поле Sound Object можно перетащить GameObject с AudioSource из сцены
//   (например дочерний объект SoundSource под этим событием).
//   При активации события AudioSource.Play() вызовется автоматически.
//   AudioSource должен иметь Play On Awake = false и SpatialBlend = 3D.
//   Если поле пусто — звук не проиграется.
//
// INTERACTABLE OBJECT:
//   В поле Interactable Object можно перетащить GameObject с HorrorInteractable
//   (например телефон, картина, радио). При активации события объект автоматически
//   «включится» (Arm) и игрок сможет с ним взаимодействовать.
//   Когда игрок ответит/взаимодействует — вызовется StopSoundObject() автоматически,
//   звук остановится. Не нужно настраивать On Activated вручную.
//   Если поле пусто — интерактивный объект не активируется.
//
// ПРИМЕР — ТЕЛЕФОН:
//   Sound Object:        SoundSource (phoneRing.aif, loop)   ← звук звонка
//   Interactable Object: phone (HorrorInteractable)          ← телефон станет активным
//   On Activated:        (пусто — больше не нужно настраивать!)
//
// ПРИМЕР — СТУК В ДВЕРЬ:
//   Sound Object:        SoundSource (woodenKnock.aif)       ← звук стука
//   Interactable Object: (пусто — стук не требует ответа)
//   On Activated:        (пусто)
//
// СЕЙВ-СИСТЕМА:
//   HorrorEvent сохраняет HasFired — событие не повторится после загрузки.
//   Ключ сейва: "horror_" + EventId. EventId должен быть уникальным.
//
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>What causes this horror event to fire.</summary>
public enum HorrorTriggerType
{
    OnItemPickup,        // Player picks up a specific ItemData
    OnRoomEnter,         // Player enters a specific room (GameManager.OnRoomChanged index)
    OnManual,            // Fired explicitly via HorrorSystem.Instance.Trigger(eventId)
    OnPlayerEnterZone,   // Player enters the trigger collider on this GameObject.
                         // If Puzzle To Watch is assigned, the zone is ignored until that puzzle is solved.
    OnPuzzleSolved,      // A referenced PuzzleModeController is solved
    OnPowerStateChanged, // LightingSystem master power matches desired state
    OnZoneSwitchChanged  // A specific light zone switch matches desired state
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
/// Place this component on any always-active GameObject (typically a child of HorrorSystem).
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

    [Header("Puzzle Trigger")]
    [Tooltip("Puzzle to watch (OnPuzzleSolved only). Drag a GameObject with PuzzleModeController here.\n" +
             "The event fires when this puzzle is solved.")]
    [SerializeField] private PuzzleModeController _puzzleToWatch;

    [Header("Power Trigger")]
    [Tooltip("Desired power state (OnPowerStateChanged only).\n" +
             "true  = fire when master power turns ON (e.g. electricity restored).\n" +
             "false = fire when master power turns OFF (e.g. blackout scare).")]
    [SerializeField] private bool _requiredPowerState = true;

    [Header("Zone Switch Trigger")]
    [Tooltip("Zone ID to watch (OnZoneSwitchChanged only). Must match a LightingSystem zone ID.")]
    [SerializeField] private string _requiredZoneId = "";

    [Tooltip("Desired zone switch state (OnZoneSwitchChanged only).\n" +
             "true  = fire when this zone's light turns ON.\n" +
             "false = fire when this zone's light turns OFF.")]
    [SerializeField] private bool _requiredZoneState = true;

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

    [Header("Sound")]
    [Tooltip("GameObject с AudioSource который проиграется при активации события.\n" +
             "Перетащи объект из сцены (например дочерний SoundSource).\n" +
             "AudioSource должен иметь Play On Awake = false и SpatialBlend = 3D.\n" +
             "Оставь пустым если звук не нужен.")]
    [SerializeField] private GameObject _soundObject;

    [Header("Interactable")]
    [Tooltip("Объект с HorrorInteractable который станет активным при срабатывании события.\n" +
             "Перетащи объект (например телефон). При активации он автоматически «включится»\n" +
             "и игрок сможет с ним взаимодействовать. Когда игрок ответит — звук остановится.\n" +
             "Оставь пустым если интерактивный объект не нужен.")]
    [SerializeField] private GameObject _interactableObject;

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

    // For OnPlayerEnterZone with Puzzle To Watch: false until the prerequisite puzzle is solved
    private bool _prerequisiteMet = true;

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

        SubscribeToTriggerSource();
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        HorrorSystem.Instance?.Unregister(this);
        UnsubscribeFromTriggerSource();
    }

    // ── Trigger source subscription ───────────────────────────────────────────

    /// <summary>
    /// Subscribes to the event source matching the current trigger type.
    /// Also checks if the condition is already met (e.g. puzzle solved from a previous save)
    /// and fires immediately if so.
    /// </summary>
    private void SubscribeToTriggerSource()
    {
        if (HasFired) return;

        switch (_triggerType)
        {
            case HorrorTriggerType.OnPlayerEnterZone:
                // If a prerequisite puzzle is assigned, arm the zone only after it's solved.
                if (_puzzleToWatch != null)
                {
                    _prerequisiteMet = _puzzleToWatch.IsSolved;
                    if (!_prerequisiteMet)
                        _puzzleToWatch.OnSolved += OnPrerequisiteSolvedHandler;
                }
                break;

            case HorrorTriggerType.OnPuzzleSolved:
                if (_puzzleToWatch == null)
                {
                    Debug.LogWarning($"[HorrorEvent '{_eventId}'] Puzzle To Watch is not assigned for OnPuzzleSolved trigger.", this);
                    return;
                }
                _puzzleToWatch.OnSolved += OnPuzzleSolvedHandler;
                // If already solved (restored from save), fire now.
                if (_puzzleToWatch.IsSolved)
                    Activate();
                break;

            case HorrorTriggerType.OnPowerStateChanged:
                if (LightingSystem.Instance == null)
                {
                    Debug.LogWarning($"[HorrorEvent '{_eventId}'] LightingSystem not found for OnPowerStateChanged trigger.", this);
                    return;
                }
                LightingSystem.Instance.OnPowerChanged += OnPowerChangedHandler;
                // If power is already in the desired state, fire now.
                if (LightingSystem.Instance.IsPowered == _requiredPowerState)
                    Activate();
                break;

            case HorrorTriggerType.OnZoneSwitchChanged:
                if (LightingSystem.Instance == null)
                {
                    Debug.LogWarning($"[HorrorEvent '{_eventId}'] LightingSystem not found for OnZoneSwitchChanged trigger.", this);
                    return;
                }
                if (string.IsNullOrEmpty(_requiredZoneId))
                {
                    Debug.LogWarning($"[HorrorEvent '{_eventId}'] Required Zone Id is empty for OnZoneSwitchChanged trigger.", this);
                    return;
                }
                LightingSystem.Instance.OnZoneSwitchChanged += OnZoneSwitchChangedHandler;
                // If zone is already in the desired state, fire now.
                if (LightingSystem.Instance.GetZoneSwitchState(_requiredZoneId) == _requiredZoneState)
                    Activate();
                break;
        }
    }

    /// <summary>Unsubscribes from the event source matching the current trigger type.</summary>
    private void UnsubscribeFromTriggerSource()
    {
        if (_puzzleToWatch != null)
        {
            _puzzleToWatch.OnSolved -= OnPuzzleSolvedHandler;
            _puzzleToWatch.OnSolved -= OnPrerequisiteSolvedHandler;
        }

        if (LightingSystem.Instance != null)
        {
            LightingSystem.Instance.OnPowerChanged -= OnPowerChangedHandler;
            LightingSystem.Instance.OnZoneSwitchChanged -= OnZoneSwitchChangedHandler;
        }
    }

    // ── Trigger handlers ──────────────────────────────────────────────────────

    /// <summary>Called when the prerequisite puzzle is solved (OnPlayerEnterZone + Puzzle To Watch).</summary>
    private void OnPrerequisiteSolvedHandler()
    {
        _prerequisiteMet = true;
    }

    private void OnPuzzleSolvedHandler()
    {
        if (!HasFired) Activate();
    }

    private void OnPowerChangedHandler(bool isPowered)
    {
        if (!HasFired && isPowered == _requiredPowerState)
            Activate();
    }

    private void OnZoneSwitchChangedHandler(string zoneId, bool isOn)
    {
        if (!HasFired && zoneId == _requiredZoneId && isOn == _requiredZoneState)
            Activate();
    }

    // ── Zone trigger (physics) ────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (_triggerType != HorrorTriggerType.OnPlayerEnterZone) return;
        if (!_prerequisiteMet) return;
        if (!other.CompareTag(_playerTag)) return;
        Activate();
    }

    // ── Look detection ────────────────────────────────────────────────────────

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

    // ── Activation / Deactivation ─────────────────────────────────────────────

    // ═══════════════════════════════════════════════════════════════════════════
    // ЦЕПОЧКА СОБЫТИЙ — ЧТО ПРОИСХОДИТ ПРИ СРАБАТЫВАНИИ
    // ═══════════════════════════════════════════════════════════════════════════
    //
    // Когда триггер срабатывает (замок открыт / игрок вошёл в зону / и т.д.),
    // вызывается Activate() → запускается ActivateRoutine().
    //
    // ActivateRoutine() — это пошаговая цепочка. Каждый шаг — отдельное действие.
    // Можно менять порядок, удалять шаги или добавлять новые.
    //
    // Шаг 1: Ждём задержку (Activation Delay)
    // Шаг 2: Показываем Target (если назначен)
    // Шаг 3: Проигрываем звук (Sound Object)
    // Шаг 4: Активируем интерактивный объект (Interactable Object — телефон, картина)
    // Шаг 5: Вызываем On Activated (дополнительные действия через UnityEvent)
    // Шаг 6: Применяем эффект (показать навсегда / исчезнуть по взгляду / исчезнуть по таймеру)
    //
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Called by HorrorSystem when the trigger condition is met, or call directly for manual control.</summary>
    public void Activate()
    {
        if (HasFired) return;
        HasFired = true;
        StartCoroutine(ActivateRoutine());
    }

    /// <summary>
    /// Пошаговая цепочка активации события.
    /// Меняй порядок шагов здесь — это повлияет на ход события.
    /// </summary>
    private IEnumerator ActivateRoutine()
    {
        // ── Шаг 1: Ждём задержку ──────────────────────────────────────────────
        // Сколько секунд подождать перед началом (поле Activation Delay в Inspector)
        if (_activationDelay > 0f)
            yield return new WaitForSeconds(_activationDelay);

        // ── Шаг 2: Показываем Target ──────────────────────────────────────────
        // Включаем объект (например манекен). Если Target не назначен — пропускается.
        if (_target != null)
            _target.SetActive(true);

        // ── Шаг 3: Проигрываем звук ───────────────────────────────────────────
        // Запускаем AudioSource на объекте из поля Sound Object.
        // Обычно это дочерний объект SoundSource с клипом (звонок, стук, шёпот).
        if (_soundObject != null && _soundObject.TryGetComponent(out AudioSource soundAudio))
            soundAudio.Play();

        // ── Шаг 4: Активируем интерактивный объект ────────────────────────────
        // «Включаем» объект из поля Interactable Object (телефон, картина, радио).
        // После этого игрок сможет с ним взаимодействовать (нажать E).
        // Когда игрок ответит — HorrorInteractable сам остановит звук через
        // поле Stop Sound On Trigger.
        if (_interactableObject != null && _interactableObject.TryGetComponent(out HorrorInteractable interactable))
            interactable.Arm();

        // ── Шаг 5: Дополнительные действия (On Activated) ─────────────────────
        // UnityEvent — для сложных цепочек: анимация, активация других объектов,
        // вызов методов на других компонентах. Обычно пустой — шаги 2-4够了.
        _onActivated?.Invoke();

        // ── Шаг 6: Применяем эффект ───────────────────────────────────────────
        // Что происходит с Target после показа:
        //
        //   AppearAndStay                   — остаётся навсегда
        //   AppearThenDisappearOnLookAway   — исчезнет когда игрок посмотрит и отвернётся
        //   AppearThenDisappearAfterDelay   — исчезнет через Disappear Delay секунд
        //   DisappearOnTrigger              — был виден → теперь скрывается
        //
        switch (_effectType)
        {
            case HorrorEffectType.AppearAndStay:
                // Ничего не делаем — Target остаётся видимым
                break;

            case HorrorEffectType.AppearThenDisappearOnLookAway:
                // Включаем отслеживание взгляда в Update()
                _targetVisible = true;
                _hasSeenTarget = false;
                break;

            case HorrorEffectType.AppearThenDisappearAfterDelay:
                // Ждём Disappear Delay секунд, затем скрываем
                yield return new WaitForSeconds(_disappearDelay);
                Deactivate();
                break;

            case HorrorEffectType.DisappearOnTrigger:
                // Target был видим → скрываем (с дополнительной задержкой если нужно)
                if (_activationDelay > 0f)
                    yield return new WaitForSeconds(_activationDelay);
                Deactivate();
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ДЕАКТИВАЦИЯ — скрытие Target и остановка
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Скрывает Target и вызывает On Deactivated.
    /// Вызывается автоматически (по взгляду / по таймеру) или вручную.
    /// </summary>
    public void Deactivate()
    {
        // Скрываем Target
        if (_target != null)
            _target.SetActive(false);

        _targetVisible = false;

        // Останавливаем звук если был
        if (_soundObject != null && _soundObject.TryGetComponent(out AudioSource audio))
            audio.Stop();

        // Дополнительные действия при скрытии
        _onDeactivated?.Invoke();
    }

    /// <summary>
    /// Останавливает звук на Sound Object.
    /// Вызывается автоматически из HorrorInteractable когда игрок отвечает
    /// (поле Stop Sound On Trigger на телефоне/картине).
    /// </summary>
    public void StopSoundObject()
    {
        if (_soundObject != null && _soundObject.TryGetComponent(out AudioSource audio))
            audio.Stop();
    }

    /// <summary>Helper for UnityEvents in prefabs to play sound without scene dependencies.</summary>
    public void PlayGlobalSFX(AudioClip clip)
    {
        if (AudioManager.Instance != null && clip != null)
            AudioManager.Instance.PlaySFX(clip);
    }
}
