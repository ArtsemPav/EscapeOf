using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Random = UnityEngine.Random;

/// <summary>
/// Мини-игра "Skill Check" в стиле Dead by Daylight.
/// Стрелка вращается по кругу, игрок нажимает ЛКМ в момент,
/// когда стрелка проходит над сектором попадания.
/// Белая зона даёт больше прогресса, серая — меньше.
/// Прогресс-бар заполняется при попаданиях.
/// При превышении лимита промахов или полном обороте без клика — штраф.
/// </summary>
public class ScrewdriverMinigamePanel : MonoBehaviour
{
    private const float FullCircle = 360f;

    [Header("Arrow Settings")]
    [Tooltip("Скорость вращения стрелки в градусах в секунду.")]
    [SerializeField, Min(1f)] private float _arrowSpeed = 200f;

    [Tooltip("Минимальное угловое расстояние (в градусах) между стартом стрелки и ближайшим краём сектора.")]
    [SerializeField, Range(0f, 180f)] private float _minStartGap = 90f;

    [Header("Sector Settings")]
    [Tooltip("Полный размер сектора попадания в градусах (серая + белая зоны).")]
    [SerializeField, Range(10f, 360f)] private float _sectorSize = 55f;

    [Tooltip("Размер белой (идеальной) зоны в градусах, по центру сектора.")]
    [SerializeField, Range(1f, 180f)] private float _whiteZoneSize = 16f;

    [Header("Progress Settings")]
    [Tooltip("Сколько очков нужно набрать для завершения мини-игры.")]
    [SerializeField, Min(1f)] private float _progressGoal = 100f;

    [Tooltip("Сколько очков даёт попадание в белую зону.")]
    [SerializeField, Min(0f)] private float _whiteZoneProgress = 18f;

    [Tooltip("Сколько очков даёт попадание в серую зону.")]
    [SerializeField, Min(0f)] private float _grayZoneProgress = 9f;

    [Header("Penalty Settings")]
    [Tooltip("Максимальное количество допустимых промахов. 0 = без лимита. При превышении вызывается OnFailed.")]
    [SerializeField, Min(0)] private int _maxMisses = 3;

    [Tooltip("Откат прогресса при промахе в очках. 0 = без отката.")]
    [SerializeField, Min(0f)] private float _missPenalty = 0f;

    [Header("UI References")]
    [SerializeField] private RectTransform _circleContainer;
    [SerializeField] private Image _ringBackground;
    [SerializeField] private Image _graySector;
    [SerializeField] private Image _whiteSector;
    [SerializeField] private RectTransform _arrow;
    [SerializeField] private Image _progressBarFill;
    [SerializeField] private TextMeshProUGUI _missCounterText;

    [Header("Circle Position")]
    [Tooltip("Если включено — CircleContainer появляется в случайном месте экрана при каждом новом чеке.")]
    [SerializeField] private bool _randomizeCirclePosition = true;

    [Tooltip("Отступ от краёв экрана (в пикселях канваса) для случайной позиции CircleContainer.")]
    [SerializeField] private Vector2 _circleScreenMargin = new Vector2(150f, 150f);

    [Tooltip("Длительность скрытия CircleContainer между чеками (в секундах).")]
    [SerializeField, Min(0f)] private float _circleHideDuration = 0.15f;

    [Header("Colors")]
    [SerializeField] private Color _ringColor = new(0.16f, 0.16f, 0.16f, 1f);
    [SerializeField] private Color _grayZoneColor = new(0.42f, 0.42f, 0.42f, 1f);
    [SerializeField] private Color _whiteZoneColor = new(0.91f, 0.91f, 0.91f, 1f);
    [SerializeField] private Color _arrowColor = new(1f, 0.25f, 0.25f, 1f);
    [SerializeField] private Color _progressFillColor = new(0.78f, 0.25f, 0.25f, 1f);

    /// <summary>Срабатывает при полном заполнении прогресс-бара (успех).</summary>
    public event Action OnCompleted;

    /// <summary>Срабатывает при попадании в сектор (белая или серая зона).</summary>
    public event Action OnHit;

    /// <summary>Срабатывает при каждом промахе (вне сектора или полный оборот).</summary>
    public event Action OnMiss;

    /// <summary>Срабатывает при превышении лимита промахов (провал).</summary>
    public event Action OnFailed;

    public float Progress => _progress;
    public float ProgressGoal => _progressGoal;
    public int Misses => _misses;
    public int MaxMisses => _maxMisses;
    public bool IsRunning => _isRunning;

    private bool _isRunning;
    private bool _isTransitioning;
    private float _arrowAngle;
    private float _sectorStart;
    private int _rotationDirection;
    private float _totalRotation;
    private float _progress;
    private int _misses;

    private void Awake()
    {
        AutoResolveReferences();
        InitializeVisuals();
    }

    /// <summary>Автоматически находит дочерние UI-элементы по имени, если ссылки не заданы в инспекторе.</summary>
    private void AutoResolveReferences()
    {
        if (_circleContainer == null)
            _circleContainer = transform.Find("CircleContainer") as RectTransform;
        if (_ringBackground == null && _circleContainer != null)
            _ringBackground = _circleContainer.Find("RingBackground")?.GetComponent<Image>();
        if (_graySector == null && _circleContainer != null)
            _graySector = _circleContainer.Find("GraySector")?.GetComponent<Image>();
        if (_whiteSector == null && _circleContainer != null)
            _whiteSector = _circleContainer.Find("WhiteSector")?.GetComponent<Image>();
        if (_arrow == null && _circleContainer != null)
            _arrow = _circleContainer.Find("Arrow") as RectTransform;
        if (_progressBarFill == null)
            _progressBarFill = transform.Find("ProgressBarBg/ProgressFill")?.GetComponent<Image>();
        if (_missCounterText == null)
            _missCounterText = transform.Find("MissCounter")?.GetComponent<TextMeshProUGUI>();
    }

    /// <summary>Настраивает Image-компоненты для заполнения по секторам.</summary>
    private void InitializeVisuals()
    {
        ConfigureRadialFilled(_ringBackground, _ringColor, 1f);
        ConfigureRadialFilled(_graySector, _grayZoneColor, 0f);
        ConfigureRadialFilled(_whiteSector, _whiteZoneColor, 0f);

        if (_arrow != null)
        {
            Image arrowImage = _arrow.GetComponent<Image>();
            if (arrowImage != null)
                arrowImage.color = _arrowColor;
        }

        if (_progressBarFill != null)
        {
            var rt = _progressBarFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            _progressBarFill.type = Image.Type.Simple;
            _progressBarFill.color = _progressFillColor;
        }
    }

    private void ConfigureRadialFilled(Image img, Color color, float fillAmount)
    {
        if (img == null) return;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = 2; // Top (Image.Origin360.Top)
        img.fillClockwise = true;
        img.fillAmount = fillAmount;
        img.color = color;
    }

    /// <summary>Запускает мини-игру и сбрасывает прогресс.</summary>
    public void StartMinigame()
    {
        _progress = 0f;
        _misses = 0;
        _isRunning = true;
        StartNewCheck();
        UpdateProgressVisual();
        UpdateMissVisual();
    }

    /// <summary>Останавливает мини-игру и скрывает сектор.</summary>
    public void StopMinigame()
    {
        _isRunning = false;
        _isTransitioning = false;
        StopAllCoroutines();
        HideSectorVisuals();
    }

    private void StartNewCheck()
    {
        if (_randomizeCirclePosition && _circleContainer != null)
            StartCoroutine(TransitionToNewPosition());
        else
            SetupCheck();
    }

    /// <summary>Скрывает CircleContainer, переносит в случайное место и показывает снова.</summary>
    private IEnumerator TransitionToNewPosition()
    {
        _isTransitioning = true;

        if (_circleContainer != null)
            _circleContainer.gameObject.SetActive(false);

        if (_circleHideDuration > 0f)
            yield return new WaitForSecondsRealtime(_circleHideDuration);

        RandomizeCirclePosition();
        SetupCheck();

        if (_circleContainer != null)
            _circleContainer.gameObject.SetActive(true);

        _isTransitioning = false;
    }

    /// <summary>Генерирует новый сектор, направление и стартовую позицию стрелки.</summary>
    private void SetupCheck()
    {
        _sectorStart = Random.Range(0f, FullCircle);
        _rotationDirection = Random.Range(0, 2) == 0 ? 1 : -1;
        _totalRotation = 0f;

        // Place arrow at a safe distance from the sector
        float safeRange = FullCircle - _sectorSize - 2f * _minStartGap;
        float sectorEnd = (_sectorStart + _sectorSize) % FullCircle;

        if (safeRange <= 0f)
        {
            _arrowAngle = (sectorEnd + _minStartGap) % FullCircle;
        }
        else
        {
            float offset = Random.Range(0f, safeRange);
            _arrowAngle = (sectorEnd + _minStartGap + offset) % FullCircle;
        }

        UpdateSectorVisuals();
        UpdateArrowVisual();
    }

    /// <summary>Случайно позиционирует CircleContainer в пределах экрана с отступом.</summary>
    private void RandomizeCirclePosition()
    {
        if (_circleContainer == null) return;

        var parentRect = _circleContainer.parent as RectTransform;
        if (parentRect == null) return;

        Rect bounds = parentRect.rect;
        float halfWidth = bounds.width * 0.5f;
        float halfHeight = bounds.height * 0.5f;

        float elemHalfW = _circleContainer.rect.width * 0.5f;
        float elemHalfH = _circleContainer.rect.height * 0.5f;

        float minX = -halfWidth + elemHalfW + _circleScreenMargin.x;
        float maxX = halfWidth - elemHalfW - _circleScreenMargin.x;
        float minY = -halfHeight + elemHalfH + _circleScreenMargin.y;
        float maxY = halfHeight - elemHalfH - _circleScreenMargin.y;

        float x = maxX > minX ? Random.Range(minX, maxX) : 0f;
        float y = maxY > minY ? Random.Range(minY, maxY) : 0f;

        _circleContainer.anchoredPosition = new Vector2(x, y);
    }

    private void Update()
    {
        if (!_isRunning || _isTransitioning) return;

        float delta = _arrowSpeed * _rotationDirection * Time.unscaledDeltaTime;
        _totalRotation += Mathf.Abs(delta);
        _arrowAngle = ((_arrowAngle + delta) % FullCircle + FullCircle) % FullCircle;

        UpdateArrowVisual();

        // Full rotation without click = miss
        if (_totalRotation >= FullCircle)
        {
            RegisterMiss();
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            EvaluateHit();
        }
    }

    private void EvaluateHit()
    {
        float whiteStart = (_sectorStart + (_sectorSize - _whiteZoneSize) * 0.5f) % FullCircle;
        float whiteEnd = (whiteStart + _whiteZoneSize) % FullCircle;
        float sectorEnd = (_sectorStart + _sectorSize) % FullCircle;

        if (AngleInRange(_arrowAngle, whiteStart, whiteEnd))
        {
            RegisterHit(_whiteZoneProgress);
        }
        else if (AngleInRange(_arrowAngle, _sectorStart, sectorEnd))
        {
            RegisterHit(_grayZoneProgress);
        }
        else
        {
            RegisterMiss();
        }
    }

    private void RegisterHit(float progressGain)
    {
        _progress = Mathf.Min(_progressGoal, _progress + progressGain);
        UpdateProgressVisual();
        OnHit?.Invoke();

        if (_progress >= _progressGoal)
        {
            _isRunning = false;
            HideSectorVisuals();
            OnCompleted?.Invoke();
        }
        else
        {
            StartNewCheck();
        }
    }

    private void RegisterMiss()
    {
        _misses++;

        if (_missPenalty > 0f)
        {
            _progress = Mathf.Max(0f, _progress - _missPenalty);
            UpdateProgressVisual();
        }

        UpdateMissVisual();
        OnMiss?.Invoke();

        if (_maxMisses > 0 && _misses > _maxMisses)
        {
            _isRunning = false;
            HideSectorVisuals();
            OnFailed?.Invoke();
        }
        else
        {
            StartNewCheck();
        }
    }

    private static bool AngleInRange(float angle, float start, float end)
    {
        float a = ((angle % FullCircle) + FullCircle) % FullCircle;
        float s = ((start % FullCircle) + FullCircle) % FullCircle;
        float e = ((end % FullCircle) + FullCircle) % FullCircle;

        if (s <= e) return a >= s && a <= e;
        return a >= s || a <= e;
    }

    // ── Visual Updates ──

    private void UpdateArrowVisual()
    {
        if (_arrow != null)
            _arrow.localEulerAngles = new Vector3(0f, 0f, -_arrowAngle);
    }

    private void UpdateSectorVisuals()
    {
        if (_graySector != null)
        {
            _graySector.fillAmount = _sectorSize / FullCircle;
            _graySector.rectTransform.localEulerAngles = new Vector3(0f, 0f, -_sectorStart);
        }

        if (_whiteSector != null)
        {
            float whiteStart = (_sectorStart + (_sectorSize - _whiteZoneSize) * 0.5f) % FullCircle;
            _whiteSector.fillAmount = _whiteZoneSize / FullCircle;
            _whiteSector.rectTransform.localEulerAngles = new Vector3(0f, 0f, -whiteStart);
        }
    }

    private void HideSectorVisuals()
    {
        if (_graySector != null) _graySector.fillAmount = 0f;
        if (_whiteSector != null) _whiteSector.fillAmount = 0f;
    }

    private void UpdateProgressVisual()
    {
        if (_progressBarFill != null)
        {
            float ratio = Mathf.Clamp01(_progress / _progressGoal);
            _progressBarFill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        }
    }

    private void UpdateMissVisual()
    {
        if (_missCounterText == null) return;

        if (_maxMisses > 0)
            _missCounterText.text = $"Промахи: {_misses}/{_maxMisses}";
        else
            _missCounterText.text = $"Промахи: {_misses}";
    }
}
