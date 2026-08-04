using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Timing-мини-игра для загадки генератора.
/// Ползунок ходит по полоске туда-обратно, ЛКМ фиксирует его позицию.
/// Попадание в зелёную зону проигрывает звук успеха, промах — звук ошибки.
/// Для завершения нужно попасть в зелёную зону заданное число раз подряд.
/// </summary>
public class GeneratorTimingMinigame : MonoBehaviour
{
    private const int RequiredSuccessesDefault = 3;

    private const float GreenZoneMargin = 0.08f;

    [Header("Green Zone Shrink")]
    [Tooltip("Множитель уменьшения ширины зелёной зоны после каждого попадания (0.8 = −20% за клик).")]
    [SerializeField, Range(0.1f, 1f)] private float _shrinkFactor = 0.8f;

    [Tooltip("Минимальная ширина зелёной зоны в пикселях, меньше которой она не уменьшается.")]
    [SerializeField, Min(1f)] private float _minGreenZoneWidth = 20f;

    [Header("Green Zone Error Color")]
    [Tooltip("Цвет зоны при ошибке (красный).")]
    [SerializeField] private Color _greenZoneErrorColor = new Color(0.85f, 0.2f, 0.2f, 1f);

    [Tooltip("Промежуточный цвет при возврате (оранжевый).")]
    [SerializeField] private Color _greenZoneWarningColor = new Color(1f, 0.55f, 0.1f, 1f);

    [Tooltip("Нормальный цвет зоны (зелёный).")]
    [SerializeField] private Color _greenZoneNormalColor = new Color(0.2f, 0.8f, 0.25f, 1f);

    [Tooltip("Длительность полного перехода красный → оранжевый → зелёный, секунды.")]
    [SerializeField, Min(0.05f)] private float _greenZoneTransitionDuration = 0.6f;

    [Header("UI (дети полоски _track)")]
    [Tooltip("Фон полоски. Pivot должен быть (0.5, 0.5).")]
    [SerializeField] private RectTransform _track;

    [Tooltip("Зелёная зона, спозиционированная через anchoredPosition относительно центра _track.")]
    [SerializeField] private RectTransform _greenZone;

    [Tooltip("Движущийся ползунок.")]
    [SerializeField] private RectTransform _handle;

    [Header("Settings")]
    [Tooltip("Количество полных проходов ползунка в секунду.")]
    [SerializeField, Min(0.1f)] private float _speed = 0.8f;

    [Tooltip("Сколько раз подряд нужно попасть в зелёную зону для завершения.")]
    [SerializeField, Min(1)] private int _requiredSuccesses = RequiredSuccessesDefault;

    [Header("Audio")]
    [Tooltip("Звук 1 — попадание в зелёную зону.")]
    [SerializeField] private AudioClip _successClip;

    [Tooltip("Звук 2 — промах (красная зона).")]
    [SerializeField] private AudioClip _failClip;

    [SerializeField, Range(0f, 1f)] private float _successVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _failVolume = 1f;

    [Header("Feedback — Progress Lamps")]
    [Tooltip("Лампочки прогресса. Загораются по одной за каждое попадание подряд, гаснут при промахе.")]
    [SerializeField] private Image[] _progressLamps;
    [SerializeField] private Color _lampOnColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [SerializeField] private Color _lampOffColor = new Color(0.25f, 0.25f, 0.25f, 1f);

    [Header("Feedback — Handle Flash")]
    [Tooltip("Image ползунка для подсветки попадания/промаха.")]
    [SerializeField] private Image _handleImage;
    [SerializeField] private Color _handleDefaultColor = Color.white;
    [SerializeField] private Color _handleHitColor = new Color(0.2f, 0.8f, 0.25f, 1f);
    [SerializeField] private Color _handleMissColor = new Color(0.85f, 0.2f, 0.2f, 1f);
    [SerializeField, Min(0f)] private float _flashDuration = 0.2f;

    /// <summary>Срабатывает при наборе нужного числа попаданий подряд.</summary>
    public event Action OnCompleted;

    /// <summary>Текущее число попаданий подряд (0..требуемое). Полезно для UI-индикации.</summary>
    public int SuccessStreak => _successStreak;

    /// <summary>Требуемое число попаданий подряд.</summary>
    public int RequiredSuccesses => _requiredSuccesses;

    private bool _isRunning;
    private float _elapsed;
    private float _trackWidth;
    private float _greenMin; // нормализованная левая граница зелёной зоны [0..1]
    private float _greenMax; // нормализованная правая граница зелёной зоны [0..1]
    private int _successStreak;
    private Coroutine _flashRoutine;
    private Coroutine _greenZoneColorRoutine;
    private Image _greenZoneImage;
    private float _initialGreenZoneWidth;

    /// <summary>Запускает мини-игру и сбрасывает прогресс.</summary>
    public void StartMinigame()
    {
        if (_greenZone != null)
        {
            if (_initialGreenZoneWidth <= 0f)
                _initialGreenZoneWidth = _greenZone.rect.width;
            else
                _greenZone.sizeDelta = new Vector2(_initialGreenZoneWidth, _greenZone.sizeDelta.y);

            if (_greenZoneImage == null)
                _greenZoneImage = _greenZone.GetComponent<Image>();
            if (_greenZoneImage != null)
                _greenZoneImage.color = _greenZoneNormalColor;
        }

        CacheGreenZone();
        _successStreak = 0;
        _elapsed = 0f;
        _isRunning = true;
        UpdateLamps();
        ResetHandleColor();
    }

    /// <summary>Останавливает мини-игру без вызова завершения.</summary>
    public void StopMinigame()
    {
        _isRunning = false;
        if (_flashRoutine != null)
        {
            StopCoroutine(_flashRoutine);
            _flashRoutine = null;
        }
        if (_greenZoneColorRoutine != null)
        {
            StopCoroutine(_greenZoneColorRoutine);
            _greenZoneColorRoutine = null;
        }
        ResetGreenZoneColor();
        ResetHandleColor();
    }

    private void CacheGreenZone()
    {
        if (_track == null || _greenZone == null) return;

        _trackWidth = _track.rect.width;
        if (_trackWidth <= 0f) return;

        float half = _trackWidth * 0.5f;
        float center = _greenZone.anchoredPosition.x; // относительно центра полоски
        float greenHalf = _greenZone.rect.width * 0.5f;
        _greenMin = Mathf.Clamp01((center - greenHalf + half) / _trackWidth);
        _greenMax = Mathf.Clamp01((center + greenHalf + half) / _trackWidth);
    }

    /// <summary>
    /// Уменьшает ширину зелёной зоны на _shrinkFactor,
    /// но не меньше _minGreenZoneWidth пикселей.
    /// </summary>
    private void ShrinkGreenZone()
    {
        if (_greenZone == null) return;

        float newWidth = Mathf.Max(_greenZone.rect.width * _shrinkFactor, _minGreenZoneWidth);
        _greenZone.sizeDelta = new Vector2(newWidth, _greenZone.sizeDelta.y);
    }

    /// <summary>Возвращает зелёной зоне исходную ширину, сохранённую при старте мини-игры.</summary>
    private void RestoreGreenZoneWidth()
    {
        if (_greenZone == null || _initialGreenZoneWidth <= 0f) return;

        _greenZone.sizeDelta = new Vector2(_initialGreenZoneWidth, _greenZone.sizeDelta.y);
    }

    /// <summary>
    /// Смещает зелёную зону в случайную позицию вдоль полоски
    /// и пересчитывает кэшированные границы.
    /// </summary>
    private void RelocateGreenZone()
    {
        if (_track == null || _greenZone == null || _trackWidth <= 0f) return;

        float half = _trackWidth * 0.5f;
        float greenHalf = _greenZone.rect.width * 0.5f;
        float margin = _trackWidth * GreenZoneMargin;

        float minCenter = -half + greenHalf + margin;
        float maxCenter = half - greenHalf - margin;

        if (maxCenter <= minCenter)
        {
            _greenZone.anchoredPosition = new Vector2(0f, _greenZone.anchoredPosition.y);
        }
        else
        {
            float newCenter = UnityEngine.Random.Range(minCenter, maxCenter);
            _greenZone.anchoredPosition = new Vector2(newCenter, _greenZone.anchoredPosition.y);
        }

        CacheGreenZone();
    }

    private void Update()
    {
        if (!_isRunning) return;

        // Нормализованная позиция ползунка 0..1 туда-обратно.
        _elapsed += Time.unscaledDeltaTime * _speed;
        float t = Mathf.PingPong(_elapsed, 1f);

        if (_handle != null)
        {
            float x = (t - 0.5f) * _trackWidth;
            _handle.anchoredPosition = new Vector2(x, _handle.anchoredPosition.y);
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            Evaluate(t);
    }

    private void Evaluate(float t)
    {
        bool hitGreen = t >= _greenMin && t <= _greenMax;
        if (hitGreen)
        {
            AudioManager.Instance?.PlaySFX(_successClip, _successVolume);
            _successStreak++;
            Debug.Log($"[GeneratorTimingMinigame] Попадание в зелёную зону. Серия: {_successStreak}/{_requiredSuccesses}");
            UpdateLamps();
            FlashHandle(_handleHitColor);
            if (_successStreak >= _requiredSuccesses)
            {
                _isRunning = false;
                OnCompleted?.Invoke();
            }
            else
            {
                ShrinkGreenZone();
                RelocateGreenZone();
            }
        }
        else
        {
            AudioManager.Instance?.PlaySFX(_failClip, _failVolume);
            _successStreak = 0; // требуется серия попаданий подряд
            RestoreGreenZoneWidth();
            RelocateGreenZone();
            UpdateLamps();
            FlashHandle(_handleMissColor);
            StartGreenZoneErrorTransition();
        }
    }

    private void UpdateLamps()
    {
        if (_progressLamps == null) return;
        for (int i = 0; i < _progressLamps.Length; i++)
        {
            if (_progressLamps[i] != null)
                _progressLamps[i].color = i < _successStreak ? _lampOnColor : _lampOffColor;
        }
    }

    private void FlashHandle(Color color)
    {
        if (_handleImage == null) return;
        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(FlashHandleRoutine(color));
    }

    private IEnumerator FlashHandleRoutine(Color color)
    {
        _handleImage.color = color;
        yield return new WaitForSecondsRealtime(_flashDuration);
        ResetHandleColor();
        _flashRoutine = null;
    }

    private void ResetHandleColor()
    {
        if (_handleImage != null)
            _handleImage.color = _handleDefaultColor;
    }

    /// <summary>Запускает переход цвета зоны: красный → оранжевый → зелёный.</summary>
    private void StartGreenZoneErrorTransition()
    {
        if (_greenZoneImage == null) return;
        if (_greenZoneColorRoutine != null) StopCoroutine(_greenZoneColorRoutine);
        _greenZoneColorRoutine = StartCoroutine(GreenZoneErrorTransitionRoutine());
    }

    private IEnumerator GreenZoneErrorTransitionRoutine()
    {
        float halfDuration = _greenZoneTransitionDuration * 0.5f;

        // красный → оранжевый
        float elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            _greenZoneImage.color = Color.Lerp(_greenZoneErrorColor, _greenZoneWarningColor, t);
            yield return null;
        }

        // оранжевый → зелёный
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            _greenZoneImage.color = Color.Lerp(_greenZoneWarningColor, _greenZoneNormalColor, t);
            yield return null;
        }

        _greenZoneImage.color = _greenZoneNormalColor;
        _greenZoneColorRoutine = null;
    }

    private void ResetGreenZoneColor()
    {
        if (_greenZoneImage != null)
            _greenZoneImage.color = _greenZoneNormalColor;
    }
}
