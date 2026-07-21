using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// 2D-мини-игра "Взлом замка концентрическими кольцами".
/// Внешнее кольцо (gearMain) статично и содержит стрелку-ориентир.
/// N вращающихся колец со встроенными засечками. Игрок нажимает Space или ЛКМ,
/// чтобы остановить активное кольцо когда его засечка совпадает со стрелкой.
/// Промах — откат на 1 кольцо назад. Все заблокированы — победа.
/// Декоративные шестерёнки по бокам вращаются для атмосферы.
/// </summary>
public class LockPickMinigame : MonoBehaviour
{
    // ── Config ─────────────────────────────────────────────────────────────────

    [Serializable]
    public struct RingConfig
    {
        [Tooltip("Скорость вращения в градусах в секунду.")]
        public float speed;

        [Tooltip("Направление: true — по часовой, false — против часовой.")]
        public bool clockwise;

        [Tooltip("Спрайт кольца с засечкой в верхней части.")]
        public Sprite sprite;

        [Tooltip("Размер кольца относительно контейнера (0–2).")]
        [Range(0.05f, 2f)] public float relativeSize;

        [Tooltip("Цветовой тинт кольца после успешной блокировки. White = нет тинта.")]
        public Color lockedColor;
    }

    [Serializable]
    public struct DecorGearConfig
    {
        [Tooltip("Спрайт декоративной шестерёнки.")]
        public Sprite sprite;

        [Tooltip("Скорость вращения в градусах в секунду.")]
        public float speed;

        [Tooltip("Направление: true — по часовой, false — против часовой.")]
        public bool clockwise;

        [Tooltip("Позиция относительно центра контейнера (пиксели).")]
        public Vector2 anchoredPosition;

        [Tooltip("Размер относительно контейнера (0–2).")]
        [Range(0.05f, 2f)] public float relativeSize;
    }

    [Header("Static Sprites")]
    [Tooltip("Внешнее статичное кольцо со стрелкой-ориентиром.")]
    [SerializeField] private Sprite _gearMainSprite;

    [Tooltip("Центральный статичный элемент.")]
    [SerializeField] private Sprite _centerSprite;

    [Tooltip("Размер внешнего кольца относительно контейнера.")]
    [SerializeField, Range(0.05f, 2f)] private float _gearMainSize = 0.95f;

    [Tooltip("Размер центрального элемента относительно контейнера.")]
    [SerializeField, Range(0.05f, 1f)] private float _centerSize = 0.18f;

    [Header("Ring Configuration")]
    [SerializeField] private RingConfig[] _ringConfigs = new RingConfig[3]
    {
        new RingConfig { speed = 80f,  clockwise = true,  relativeSize = 0.72f, lockedColor = new Color(0.5f, 1f, 0.5f, 1f) },
        new RingConfig { speed = 110f, clockwise = false, relativeSize = 0.52f, lockedColor = new Color(0.5f, 1f, 0.5f, 1f) },
        new RingConfig { speed = 140f, clockwise = true,  relativeSize = 0.35f, lockedColor = new Color(0.5f, 1f, 0.5f, 1f) },
    };

    [Header("Decorative Gears")]
    [SerializeField] private DecorGearConfig[] _decorGears = new DecorGearConfig[3]
    {
        new DecorGearConfig { speed = 40f, clockwise = true,  anchoredPosition = new Vector2(-200f, 140f), relativeSize = 0.18f },
        new DecorGearConfig { speed = 55f, clockwise = false, anchoredPosition = new Vector2(200f, 140f),  relativeSize = 0.18f },
        new DecorGearConfig { speed = 70f, clockwise = true,  anchoredPosition = new Vector2(0f, -180f),   relativeSize = 0.15f },
    };

    [Header("Settings")]
    [Tooltip("Допустимая погрешность попадания в градусах.")]
    [SerializeField] private float _tolerance = 8f;

    [Tooltip("Размер всей области колец в пикселях.")]
    [SerializeField] private float _containerSize = 400f;

    [Header("Colors")]
    [Tooltip("Тинт активного кольца. White = нет подсветки.")]
    [SerializeField] private Color _activeColor = new Color(1f, 0.92f, 0.65f, 1f);

    [Header("Visuals (assign in scene — auto-created if null)")]
    [Tooltip("Контейнер для всех элементов. Если не назначен — создаётся автоматически.")]
    [SerializeField] private RectTransform _ringContainer;

    [Tooltip("RectTransform внешнего статичного кольца (gearMain). Если null — создаётся автоматически.")]
    [SerializeField] private RectTransform _gearMainTransform;

    [Tooltip("Image внешнего статичного кольца. Если null — берётся с _gearMainTransform.")]
    [SerializeField] private Image _gearMainImage;

    [Tooltip("RectTransform центрального элемента. Если null — создаётся автоматически.")]
    [SerializeField] private RectTransform _centerTransform;

    [Tooltip("Image центрального элемента. Если null — берётся с _centerTransform.")]
    [SerializeField] private Image _centerImage;

    [Tooltip("RectTransform каждого вращающегося кольца. Если null — создаются автоматически.")]
    [SerializeField] private RectTransform[] _ringTransforms;

    [Tooltip("Image каждого вращающегося кольца. Если null — берутся с _ringTransforms.")]
    [SerializeField] private Image[] _ringImages;

    [Tooltip("RectTransform декоративных шестерёнок. Если null — создаются автоматически.")]
    [SerializeField] private RectTransform[] _decorGearTransforms;

    [Tooltip("Image декоративных шестерёнок. Если null — берутся с _decorGearTransforms.")]
    [SerializeField] private Image[] _decorGearImages;

    [Header("Audio")]
    [SerializeField] private AudioClip _successClip;
    [SerializeField] private AudioClip _failClip;
    [SerializeField] private AudioClip _completeClip;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    [Header("Miss Feedback")]
    [Tooltip("Длительность визуальной реакции на промах в секундах.")]
    [SerializeField] private float _missFlashDuration = 0.5f;

    [Tooltip("Во сколько раз увеличивается кольцо при промахе.")]
    [SerializeField] private float _missScaleMultiplier = 1.15f;

    [Tooltip("Цвет кольца при промахе.")]
    [SerializeField] private Color _missColor = new Color(1f, 0.15f, 0.15f, 0.9f);

    [Header("Appearance / Completion Animation")]
    [Tooltip("Длительность масштабирования одного кольца при появлении/исчезновении.")]
    [SerializeField] private float _ringAnimDuration = 0.35f;

    [Tooltip("Задержка между появлением соседних колец (веерный эффект).")]
    [SerializeField] private float _ringStaggerDelay = 0.12f;

    [Tooltip("Кривая плавности для анимации колец.")]
    [SerializeField] private AnimationCurve _ringAnimCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    // ── Events ──────────────────────────────────────────────────────────────────

    /// <summary>Срабатывает когда все кольца заблокированы и веерная анимация завершена.</summary>
    public event Action OnCompleted;

    /// <summary>Срабатывает при успешной блокировке кольца. Параметр — индекс кольца.</summary>
    public event Action<int> OnRingLocked;

    /// <summary>Срабатывает при промахе. Параметр — индекс кольца, на котором промахнулись.</summary>
    public event Action<int> OnRingMissed;

    // ── State ───────────────────────────────────────────────────────────────────

    private bool _isRunning;
    private bool _isAnimating;
    private int _activeIndex;
    private float[] _angles;
    private bool[] _locked;
    private float[] _decorGearAngles;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Запускает мини-игру: сбрасывает прогресс, случайные углы, веерное появление колец.</summary>
    public void StartMinigame()
    {
        EnsureVisuals();

        int count = _ringConfigs.Length;
        if (_angles == null || _angles.Length != count)
        {
            _angles = new float[count];
            _locked = new bool[count];
        }

        for (int i = 0; i < count; i++)
        {
            _locked[i] = false;
            _angles[i] = Random.Range(20f, 340f);
            ApplyRingVisual(i);
        }

        _activeIndex = 0;
        _isRunning = true;
        StopAllCoroutines();
        StartCoroutine(AnimateAppearance());
    }

    /// <summary>Останавливает мини-игру без вызова OnCompleted.</summary>
    public void StopMinigame()
    {
        _isRunning = false;
        _isAnimating = false;
        StopAllCoroutines();
    }

    /// <summary>True пока мини-игра активна.</summary>
    public bool IsRunning => _isRunning;

    // ── Unity Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        EnsureVisuals();
    }

    private void Update()
    {
        RotateDecorGears();

        if (!_isRunning || _isAnimating) return;

        RotateRings();

        bool input = false;
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            input = true;
        if (!input && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            input = true;

        if (input)
            TryLock();
    }

    // ── Core Logic ──────────────────────────────────────────────────────────────

    private void RotateRings()
    {
        for (int i = 0; i < _ringConfigs.Length; i++)
        {
            if (_locked[i]) continue;

            float delta = _ringConfigs[i].speed * Time.unscaledDeltaTime;
            if (!_ringConfigs[i].clockwise)
                delta = -delta;

            _angles[i] = (_angles[i] + delta % 360f + 360f) % 360f;

            if (_ringTransforms != null && i < _ringTransforms.Length && _ringTransforms[i] != null)
                _ringTransforms[i].localRotation = Quaternion.Euler(0f, 0f, _angles[i]);
        }
    }

    private void RotateDecorGears()
    {
        if (_decorGearTransforms == null || _decorGears == null) return;

        for (int i = 0; i < _decorGearTransforms.Length; i++)
        {
            if (_decorGearTransforms[i] == null) continue;

            float delta = _decorGears[i].speed * Time.unscaledDeltaTime;
            if (!_decorGears[i].clockwise)
                delta = -delta;

            _decorGearAngles[i] = (_decorGearAngles[i] + delta % 360f + 360f) % 360f;
            _decorGearTransforms[i].localRotation = Quaternion.Euler(0f, 0f, _decorGearAngles[i]);
        }
    }

    private void TryLock()
    {
        int idx = _activeIndex;
        float angle = _angles[idx];
        float diff = Mathf.Min(angle, 360f - angle);

        if (diff <= _tolerance)
        {
            // Успех — блокируем кольцо
            _locked[idx] = true;
            _angles[idx] = 0f;

            if (_ringTransforms != null && idx < _ringTransforms.Length && _ringTransforms[idx] != null)
                _ringTransforms[idx].localRotation = Quaternion.identity;

            ApplyRingVisual(idx);
            AudioManager.Instance?.PlaySFX(_successClip, _volume);
            OnRingLocked?.Invoke(idx);

            _activeIndex++;

            if (_activeIndex >= _ringConfigs.Length)
            {
                // Все кольца заблокированы — запуск веерного сжатия
                _isRunning = false;
                _isAnimating = true;
                AudioManager.Instance?.PlaySFX(_completeClip, _volume);
                StartCoroutine(AnimateCompletion());
                return;
            }

            UpdateActiveHighlight();
        }
        else
        {
            // Промах
            AudioManager.Instance?.PlaySFX(_failClip, _volume);
            OnRingMissed?.Invoke(idx);
            StartCoroutine(FlashMissCoroutine(idx));

            if (idx > 0)
            {
                // Откат: разблокируем предыдущее кольцо, случайный угол
                _activeIndex = idx - 1;
                _locked[_activeIndex] = false;
                _angles[_activeIndex] = Random.Range(20f, 340f);
                ApplyRingVisual(_activeIndex);
                UpdateActiveHighlight();
            }
        }
    }

    // ── Visuals ─────────────────────────────────────────────────────────────────

    private void ApplyRingVisual(int idx)
    {
        if (_ringImages != null && idx < _ringImages.Length && _ringImages[idx] != null)
        {
            _ringImages[idx].color = _locked[idx]
                ? _ringConfigs[idx].lockedColor
                : Color.white;
        }
    }

    private void UpdateActiveHighlight()
    {
        if (_ringImages == null) return;

        for (int i = 0; i < _ringImages.Length; i++)
        {
            if (_ringImages[i] == null) continue;

            if (_locked[i])
                _ringImages[i].color = _ringConfigs[i].lockedColor;
            else
                _ringImages[i].color = i == _activeIndex ? _activeColor : Color.white;
        }
    }

    // ── Fan Animations ──────────────────────────────────────────────────────────

    /// <summary>
    /// Веерное появление: статичные элементы и декоративные шестерёнки появляются вместе,
    /// затем кольца последовательно вырастают из scale 0 → 1 (от внешнего к внутреннему).
    /// </summary>
    private IEnumerator AnimateAppearance()
    {
        _isAnimating = true;

        int count = _ringConfigs.Length;

        // Все элементы в scale 0
        SetElementScale(_gearMainTransform, Vector3.zero);
        SetElementScale(_centerTransform, Vector3.zero);
        for (int i = 0; i < count; i++)
            SetElementScale(_ringTransforms, i, Vector3.zero);
        for (int i = 0; i < _decorGearTransforms?.Length; i++)
            SetElementScale(_decorGearTransforms, i, Vector3.zero);

        // Статичные элементы и декоративные шестерёнки — появляются вместе
        if (_gearMainTransform != null)
            StartCoroutine(ScaleTransformCoroutine(_gearMainTransform, Vector3.zero, Vector3.one, _ringAnimDuration));
        if (_centerTransform != null)
            StartCoroutine(ScaleTransformCoroutine(_centerTransform, Vector3.zero, Vector3.one, _ringAnimDuration));
        if (_decorGearTransforms != null)
        {
            for (int i = 0; i < _decorGearTransforms.Length; i++)
            {
                if (_decorGearTransforms[i] != null)
                    StartCoroutine(ScaleTransformCoroutine(_decorGearTransforms[i], Vector3.zero, Vector3.one, _ringAnimDuration));
            }
        }

        // Веерный рост колец: от внешнего (index 0) к внутреннему
        for (int i = 0; i < count; i++)
        {
            StartCoroutine(ScaleRingCoroutine(i, Vector3.zero, Vector3.one, _ringAnimDuration));
            yield return new WaitForSecondsRealtime(_ringStaggerDelay);
        }

        // Ждём завершения последней корутины
        yield return new WaitForSecondsRealtime(_ringAnimDuration);

        _isAnimating = false;
        UpdateActiveHighlight();
    }

    /// <summary>
    /// Веерное сжатие: кольца последовательно сжимаются scale 1 → 0 (от внешнего к внутреннему),
    /// затем статичные элементы и декоративные шестерёнки исчезают вместе.
    /// По завершении вызывает OnCompleted.
    /// </summary>
    private IEnumerator AnimateCompletion()
    {
        _isAnimating = true;

        int count = _ringConfigs.Length;

        // Веерное сжатие колец: от внешнего (index 0) к внутреннему
        for (int i = 0; i < count; i++)
        {
            StartCoroutine(ScaleRingCoroutine(i, Vector3.one, Vector3.zero, _ringAnimDuration));
            yield return new WaitForSecondsRealtime(_ringStaggerDelay);
        }

        // Ждём завершения последнего кольца
        yield return new WaitForSecondsRealtime(_ringAnimDuration);

        // Статичные элементы и декоративные шестерёнки — исчезают вместе
        if (_centerTransform != null)
            StartCoroutine(ScaleTransformCoroutine(_centerTransform, Vector3.one, Vector3.zero, _ringAnimDuration));
        if (_gearMainTransform != null)
            StartCoroutine(ScaleTransformCoroutine(_gearMainTransform, Vector3.one, Vector3.zero, _ringAnimDuration));
        if (_decorGearTransforms != null)
        {
            for (int i = 0; i < _decorGearTransforms.Length; i++)
            {
                if (_decorGearTransforms[i] != null)
                    StartCoroutine(ScaleTransformCoroutine(_decorGearTransforms[i], Vector3.one, Vector3.zero, _ringAnimDuration));
            }
        }

        yield return new WaitForSecondsRealtime(_ringAnimDuration);

        _isAnimating = false;
        OnCompleted?.Invoke();
    }

    /// <summary>Анимирует масштаб одного кольца по кривой.</summary>
    private IEnumerator ScaleRingCoroutine(int idx, Vector3 from, Vector3 to, float duration)
    {
        if (_ringTransforms == null || idx >= _ringTransforms.Length || _ringTransforms[idx] == null)
            yield break;

        RectTransform rt = _ringTransforms[idx];
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.localScale = Vector3.LerpUnclamped(from, to, _ringAnimCurve.Evaluate(t));
            yield return null;
        }

        rt.localScale = to;
    }

    /// <summary>Анимирует масштаб произвольного RectTransform по кривой.</summary>
    private IEnumerator ScaleTransformCoroutine(RectTransform rt, Vector3 from, Vector3 to, float duration)
    {
        if (rt == null) yield break;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.localScale = Vector3.LerpUnclamped(from, to, _ringAnimCurve.Evaluate(t));
            yield return null;
        }

        rt.localScale = to;
    }

    // ── Miss Feedback ───────────────────────────────────────────────────────────

    /// <summary>
    /// Визуальная реакция кольца на промах: увеличивается и краснеет,
    /// затем плавно возвращается к исходному размеру и цвету.
    /// </summary>
    private IEnumerator FlashMissCoroutine(int idx)
    {
        if (_ringTransforms == null || idx >= _ringTransforms.Length || _ringTransforms[idx] == null)
            yield break;

        RectTransform ringTransform = _ringTransforms[idx];
        Image ringImage = (_ringImages != null && idx < _ringImages.Length) ? _ringImages[idx] : null;

        Color baseColor = ringImage != null ? ringImage.color : Color.white;
        Vector3 baseScale = ringTransform.localScale;
        Vector3 peakScale = baseScale * _missScaleMultiplier;

        const float PeakFraction = 0.3f;
        float elapsed = 0f;

        while (elapsed < _missFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _missFlashDuration);

            if (t < PeakFraction)
            {
                float p = t / PeakFraction;
                if (ringImage != null)
                    ringImage.color = Color.Lerp(baseColor, _missColor, p);
                ringTransform.localScale = Vector3.Lerp(baseScale, peakScale, p);
            }
            else
            {
                float p = (t - PeakFraction) / (1f - PeakFraction);
                if (ringImage != null)
                    ringImage.color = Color.Lerp(_missColor, baseColor, p);
                ringTransform.localScale = Vector3.Lerp(peakScale, baseScale, p);
            }

            yield return null;
        }

        ringTransform.localScale = baseScale;
        UpdateActiveHighlight();
    }

    // ── Setup UI ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Подключает существующие UI-объекты из инспектора или создаёт недостающие.
    /// Все элементы можно предварительно разместить в сцене и настроить вручную.
    /// </summary>
    private void EnsureVisuals()
    {
        int count = _ringConfigs.Length;
        var rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null) return;

        // Контейнер
        if (_ringContainer == null)
        {
            _ringContainer = CreateChild("RingContainer", rectTransform);
            _ringContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _ringContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _ringContainer.anchoredPosition = Vector2.zero;
            _ringContainer.sizeDelta = new Vector2(_containerSize, _containerSize);
        }

        // Вращающиеся кольца
        if (_ringTransforms == null || _ringTransforms.Length != count)
            _ringTransforms = new RectTransform[count];
        if (_ringImages == null || _ringImages.Length != count)
            _ringImages = new Image[count];

        for (int i = 0; i < count; i++)
        {
            if (_ringTransforms[i] == null)
            {
                if (_ringConfigs[i].sprite == null)
                {
                    Debug.LogWarning($"[LockPickMinigame] Ring {i} has no sprite and no RectTransform assigned.", this);
                    continue;
                }

                float ringSize = _containerSize * _ringConfigs[i].relativeSize;
                var ringRect = CreateChild($"Ring_{i}", _ringContainer);
                ringRect.anchoredPosition = Vector2.zero;
                ringRect.sizeDelta = new Vector2(ringSize, ringSize);
                _ringTransforms[i] = ringRect;

                var ringImg = ringRect.gameObject.AddComponent<Image>();
                ringImg.sprite = _ringConfigs[i].sprite;
                ringImg.color = Color.white;
                ringImg.raycastTarget = false;
                ringImg.preserveAspect = true;
                _ringImages[i] = ringImg;
            }
            else if (_ringImages[i] == null)
            {
                _ringImages[i] = _ringTransforms[i].GetComponent<Image>();
            }
        }

        // GearMain
        if (_gearMainTransform == null && _gearMainSprite != null)
        {
            float mainSize = _containerSize * _gearMainSize;
            _gearMainTransform = CreateChild("GearMain", _ringContainer);
            _gearMainTransform.anchoredPosition = Vector2.zero;
            _gearMainTransform.sizeDelta = new Vector2(mainSize, mainSize);

            _gearMainImage = _gearMainTransform.gameObject.AddComponent<Image>();
            _gearMainImage.sprite = _gearMainSprite;
            _gearMainImage.color = Color.white;
            _gearMainImage.raycastTarget = false;
            _gearMainImage.preserveAspect = true;
        }
        else if (_gearMainImage == null && _gearMainTransform != null)
        {
            _gearMainImage = _gearMainTransform.GetComponent<Image>();
        }

        // Center
        if (_centerTransform == null && _centerSprite != null)
        {
            float centerSize = _containerSize * _centerSize;
            _centerTransform = CreateChild("Center", _ringContainer);
            _centerTransform.anchoredPosition = Vector2.zero;
            _centerTransform.sizeDelta = new Vector2(centerSize, centerSize);

            _centerImage = _centerTransform.gameObject.AddComponent<Image>();
            _centerImage.sprite = _centerSprite;
            _centerImage.color = Color.white;
            _centerImage.raycastTarget = false;
            _centerImage.preserveAspect = true;
        }
        else if (_centerImage == null && _centerTransform != null)
        {
            _centerImage = _centerTransform.GetComponent<Image>();
        }

        // Декоративные шестерёнки
        if (_decorGears != null && _decorGears.Length > 0)
        {
            if (_decorGearTransforms == null || _decorGearTransforms.Length != _decorGears.Length)
                _decorGearTransforms = new RectTransform[_decorGears.Length];
            if (_decorGearImages == null || _decorGearImages.Length != _decorGears.Length)
                _decorGearImages = new Image[_decorGears.Length];
            if (_decorGearAngles == null || _decorGearAngles.Length != _decorGears.Length)
                _decorGearAngles = new float[_decorGears.Length];

            for (int i = 0; i < _decorGears.Length; i++)
            {
                if (_decorGearTransforms[i] == null)
                {
                    if (_decorGears[i].sprite == null) continue;

                    float decorSize = _containerSize * _decorGears[i].relativeSize;
                    var decorRect = CreateChild($"DecorGear_{i}", _ringContainer);
                    decorRect.anchoredPosition = _decorGears[i].anchoredPosition;
                    decorRect.sizeDelta = new Vector2(decorSize, decorSize);

                    var decorImg = decorRect.gameObject.AddComponent<Image>();
                    decorImg.sprite = _decorGears[i].sprite;
                    decorImg.color = Color.white;
                    decorImg.raycastTarget = false;
                    decorImg.preserveAspect = true;

                    _decorGearTransforms[i] = decorRect;
                    _decorGearImages[i] = decorImg;
                }
                else if (_decorGearImages[i] == null)
                {
                    _decorGearImages[i] = _decorGearTransforms[i].GetComponent<Image>();
                }

                if (_decorGearAngles[i] == 0f && _decorGearTransforms[i] != null)
                {
                    _decorGearAngles[i] = Random.Range(0f, 360f);
                    _decorGearTransforms[i].localRotation = Quaternion.Euler(0f, 0f, _decorGearAngles[i]);
                }
            }
        }
    }

    private static RectTransform CreateChild(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    // ── Scale Helpers ───────────────────────────────────────────────────────────

    private static void SetElementScale(RectTransform rt, Vector3 scale)
    {
        if (rt != null) rt.localScale = scale;
    }

    private static void SetElementScale(RectTransform[] rts, int idx, Vector3 scale)
    {
        if (rts != null && idx < rts.Length && rts[idx] != null)
            rts[idx].localScale = scale;
    }
}
