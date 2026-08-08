using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

// ═══════════════════════════════════════════════════════════════════════════
// HORROR INTERACTABLE — ИНСТРУКЦИЯ
// ═══════════════════════════════════════════════════════════════════════════
//
// ЧТО ЭТО:
//   Универсальный интерактивный объект — вспомогательный элемент хоррор-системы.
//   HorrorEvent «активирует» (Arm) объект → игрок может с ним взаимодействовать →
//   объект «срабатывает» (Triggered): проигрывает звук и запускает UnityEvent.
//
// ПРИМЕРЫ ИСПОЛЬЗОВАНИЯ:
//   • Телефон — Arm запускает звонок (loop), игрок нажимает E → звук трубки, сюжет.
//   • Картина — Arm делает картину «осмотримой», игрок нажимает E → звук, событие.
//   • Радио — Arm включает помехи (loop), игрок нажимает E → голос из радио.
//   • Дверь — Arm позволяет открыть дверь, игрок нажимает E → скрип + событие.
//
// НАСТРОЙКА:
//   1. Добавь компонент HorrorInteractable на нужный объект (префаб или в сцене).
//   2. На том же объекте должен быть AudioSource (добавится автоматически).
//   3. Заполни поля аудио (см. ниже) — все опциональны.
//   4. Настрой OnTriggered — что произойдёт после взаимодействия игрока.
//   5. Укажи Save Id Suffix — уникальный для каждого объекта.
//
// ПОДКЛЮЧЕНИЕ К HORROR SYSTEM:
//   1. Создай HorrorEvent под HorrorSystem (например Event_PhoneCall).
//   2. Trigger Type — любой (OnPuzzleSolved, OnPowerStateChanged, OnManual, и т.д.)
//   3. Effect Type = AppearAndStay, Target = пусто (объект уже в сцене).
//   4. В On Activated нажми «+» → перетащи объект → HorrorInteractable → Arm().
//
// АУДИО:
//   Armed Clip        — зацикленный звук пока объект активен (звонок, помехи).
//                       Может быть пустым — тогда объект просто ждёт без звука.
//   Trigger Clip      — однократный звук при взаимодействии (поднятие трубки, скрип).
//   Post-Trigger Clip — звук после задержки (дыхание в трубку, голос). Опционально.
//
// ТАЙМЕР ЗВОНКА:
//   Ring Duration     — сколько секунд объект «звенит» перед перезапуском цикла.
//                       0 = бесконечно (без таймера).
//   Ring Pause         — пауза между циклами звонка (секунды). 0 = без паузы.
//   Если Ring Duration > 0: по истечении таймера звонок останавливается,
//   ждёт Ring Pause секунд, и начинает звонить заново. Цикл повторяется пока
//   игрок не ответит. После загрузки сейва таймер стартует заново с полного значения.
//
// СЕЙВ-СИСТЕМА:
//   Сохраняет состояния isArmed и wasTriggered:
//     • Если игрок уже сработал → объект неактивен после загрузки.
//     • Если объект был активен но игрок не сработал → активация восстанавливается,
//       телефон продолжает звонить.
//   Ключ сейва: "hprop_" + SaveIdSuffix.
//
// ONE SHOT:
//   Если true (по умолчанию) — после срабатывания объект нельзя активировать снова.
//   Если false — объект можно активировать повторно (например мигающая картина).
//
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Universal interactive prop driven by HorrorEvent.
/// Arm() → player interacts → Triggered (audio + UnityEvent).
/// Implements IInteractable and ISaveable.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class HorrorInteractable : MonoBehaviour, IInteractable, ISaveable
{
    [Header("Audio")]
    [Tooltip("Looping clip played while armed (e.g. phone ringing, radio static).\n" +
             "Leave empty if no ambient sound is needed while armed.")]
    [SerializeField] private AudioClip _armedClip;

    [Tooltip("One-shot clip played when the player interacts (e.g. phone pickup, painting touch).")]
    [SerializeField] private AudioClip _triggerClip;

    [Tooltip("Optional clip played after a delay following interaction (e.g. breathing, voice).")]
    [SerializeField] private AudioClip _postTriggerClip;

    [Tooltip("Delay before the post-trigger clip plays (seconds).")]
    [SerializeField] private float _postTriggerDelay = 0.5f;

    [Header("Ring Timer")]
    [Tooltip("How long the object stays armed before restarting the cycle (seconds).\n" +
             "0 = infinite (no timer, rings until the player interacts).")]
    [SerializeField] private float _ringDuration = 0f;

    [Tooltip("Pause between ring cycles (seconds). 0 = restart immediately.")]
    [SerializeField] private float _ringPause = 0f;

    [Header("Interaction")]
    [Tooltip("Hint text shown when the player looks at this object while armed.")]
    [SerializeField] private string _interactHint = "Взаимодействовать";

    [Tooltip("Crosshair mode when looking at this object while armed.")]
    [SerializeField] private CrosshairMode _crosshair = CrosshairMode.Hand;

    [Header("Save")]
    [Tooltip("Unique suffix for the save key. Must be unique per HorrorInteractable in the scene.")]
    [SerializeField] private string _saveIdSuffix = "unique_id";

    [Tooltip("If true, this can only be triggered once per save.\n" +
             "If false, it can be re-armed after triggering.")]
    [SerializeField] private bool _oneShot = true;

    [Header("Events")]
    [Tooltip("Fired when the object becomes armed (HorrorEvent calls Arm()).")]
    [SerializeField] private UnityEvent _onArmed;

    [Tooltip("Fired when the player interacts with this object while armed.")]
    [SerializeField] private UnityEvent _onTriggered;

    [Header("Auto Stop Sound")]
    [Tooltip("HorrorEvent, звук которого нужно остановить при ответе игрока.\n" +
             "Перетащи объект с HorrorEvent (например Event_PhoneCall).\n" +
             "Когда игрок ответит — вызовется StopSoundObject() автоматически.\n" +
             "Оставь пустым если не нужно останавливать звук.")]
    [SerializeField] private HorrorEvent _stopSoundOnTrigger;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _isArmed;
    private bool _wasTriggered;
    private AudioSource _audio;
    private float _ringTimer;

    // True when loaded save requires re-arming in Start()
    private bool _pendingArm;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the object is armed and waiting for player interaction.</summary>
    public bool IsArmed => _isArmed;

    /// <summary>True if the player has already triggered this object.</summary>
    public bool WasTriggered => _wasTriggered;

    /// <summary>
    /// Arms the object — makes it interactable and starts the armed audio loop.
    /// Called from HorrorEvent.OnActivated or from code.
    /// Does nothing if the object was already triggered (One Shot mode).
    /// </summary>
    public void Arm()
    {
        if (_wasTriggered && _oneShot) return;

        _isArmed = true;
        _ringTimer = _ringDuration;
        _onArmed?.Invoke();

        if (_armedClip != null && _audio != null)
        {
            _audio.clip = _armedClip;
            _audio.loop = true;
            _audio.Play();
        }

        SaveManager.Instance?.Save();
    }

    /// <summary>Disarms the object without triggering it. Stops armed audio.</summary>
    public void Disarm()
    {
        _isArmed = false;

        if (_audio != null && _audio.isPlaying)
            _audio.Stop();
    }

    // ── Ring timer ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!_isArmed || _ringDuration <= 0f) return;

        _ringTimer -= Time.deltaTime;

        if (_ringTimer <= 0f)
            StartCoroutine(RingCycleRestart());
    }

    /// <summary>
    /// Stops the armed audio, waits for _ringPause seconds, then re-arms.
    /// The object stays interactable throughout — only the audio cycles.
    /// </summary>
    private IEnumerator RingCycleRestart()
    {
        // Stop audio but keep armed state so the player can still interact
        if (_audio != null && _audio.isPlaying)
            _audio.Stop();

        if (_ringPause > 0f)
            yield return new WaitForSeconds(_ringPause);

        // Re-start the ring cycle (only if still armed and not yet triggered)
        if (_isArmed && !_wasTriggered)
        {
            _ringTimer = _ringDuration;

            if (_armedClip != null && _audio != null)
            {
                _audio.clip = _armedClip;
                _audio.loop = true;
                _audio.Play();
            }
        }
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => _isArmed;

    public void Interact()
    {
        if (!_isArmed) return;

        _isArmed = false;
        _wasTriggered = true;
        SaveManager.Instance?.Save();

        // Stop armed audio
        if (_audio != null && _audio.isPlaying)
            _audio.Stop();

        // Stop the HorrorEvent's Sound Object (e.g. stop phone ringing)
        if (_stopSoundOnTrigger != null)
            _stopSoundOnTrigger.StopSoundObject();

        // Play trigger clip
        if (_triggerClip != null && _audio != null)
        {
            _audio.loop = false;
            _audio.PlayOneShot(_triggerClip);
        }

        // Play post-trigger clip after delay
        if (_postTriggerClip != null)
            StartCoroutine(PlayPostTriggerDelayed());

        _onTriggered?.Invoke();
    }

    public string GetInteractText() => _interactHint;

    public bool IsPickable() => false;

    public bool UseLMBClick => true;

    public CrosshairMode GetCrosshairMode() => _crosshair;

    public string GetBlockedHint() => string.Empty;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    /// <summary>Save key: "hprop_" + SaveIdSuffix. Must be unique.</summary>
    public string SaveId => "hprop_" + _saveIdSuffix;

    public string GetSaveData() =>
        JsonUtility.ToJson(new HorrorInteractableSaveData
        {
            isArmed = _isArmed,
            wasTriggered = _wasTriggered
        });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<HorrorInteractableSaveData>(json);
        _wasTriggered = data.wasTriggered;
        _pendingArm = data.isArmed && !_wasTriggered;
    }

    [Serializable]
    private struct HorrorInteractableSaveData
    {
        public bool isArmed;
        public bool wasTriggered;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private IEnumerator PlayPostTriggerDelayed()
    {
        yield return new WaitForSeconds(_postTriggerDelay);

        if (_postTriggerClip != null)
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.PlaySFX(_postTriggerClip);
            else if (_audio != null)
                _audio.PlayOneShot(_postTriggerClip);
        }
    }

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.playOnAwake = false;
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        // Restore armed state from save (e.g. phone was ringing when player saved)
        if (_pendingArm)
        {
            _pendingArm = false;
            Arm();
        }
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }
}
