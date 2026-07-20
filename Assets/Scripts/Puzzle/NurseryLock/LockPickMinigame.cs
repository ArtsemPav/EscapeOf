using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// 2D-мини-игра "Взлом замка концентрическими кольцами".
/// 3 кольца вращаются с разной скоростью. Игрок нажимает Space или ЛКМ,
/// чтобы остановить активное кольцо когда его засечка совпадает с ориентиром вверху.
/// Промах — откат на 1 кольцо назад. Все 3 заблокированы — победа.
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

        [Tooltip("Цвет кольца.")]
        public Color ringColor;

        [Tooltip("Цвет засечки (нотча).")]
        public Color notchColor;

        [Tooltip("Цвет кольца после успешной блокировки.")]
        public Color lockedColor;
    }

    [Header("Ring Configuration")]
    [SerializeField] private RingConfig[] _ringConfigs = new RingConfig[3]
    {
        new RingConfig { speed = 80f,  clockwise = true,  ringColor = new Color(0.3f, 0.6f, 1f,  0.8f), notchColor = new Color(1f, 0.85f, 0.2f, 1f), lockedColor = new Color(0.2f, 0.8f, 0.25f, 0.8f) },
        new RingConfig { speed = 110f, clockwise = false, ringColor = new Color(0.8f, 0.4f, 0.9f, 0.8f), notchColor = new Color(1f, 0.85f, 0.2f, 1f), lockedColor = new Color(0.2f, 0.8f, 0.25f, 0.8f) },
        new RingConfig { speed = 140f, clockwise = true,  ringColor = new Color(1f, 0.5f, 0.3f,  0.8f), notchColor = new Color(1f, 0.85f, 0.2f, 1f), lockedColor = new Color(0.2f, 0.8f, 0.25f, 0.8f) },
    };

    [Header("Settings")]
    [Tooltip("Допустимая погрешность попадания в градусах.")]
    [SerializeField] private float _tolerance = 8f;

    [Tooltip("Размер всей области колец в пикселях.")]
    [SerializeField] private float _containerSize = 400f;

    [Header("Visuals (auto-created if null)")]
    [Tooltip("Контейнер для колец. Если не назначен — создаётся автоматически.")]
    [SerializeField] private RectTransform _ringContainer;

    [Tooltip("RectTransform каждого кольца (вращаются). Если не назначены — создаются автоматически.")]
    [SerializeField] private RectTransform[] _ringTransforms;

    [Tooltip("Image засечки каждого кольца. Если не назначены — создаются автоматически.")]
    [SerializeField] private Image[] _notchImages;

    [Tooltip("Image стрелки-ориентира наверху. Если не назначен — создаётся автоматически.")]
    [SerializeField] private Image _pointerImage;

    [Tooltip("Image фона каждого кольца. Если не назначены — создаются автоматически.")]
    [SerializeField] private Image[] _ringImages;

    [Header("Audio")]
    [SerializeField] private AudioClip _successClip;
    [SerializeField] private AudioClip _failClip;
    [SerializeField] private AudioClip _completeClip;
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    // ── Events ──────────────────────────────────────────────────────────────────

    /// <summary>Срабатывает когда все кольца заблокированы.</summary>
    public event Action OnCompleted;

    /// <summary>Срабатывает при успешной блокировке кольца. Параметр — индекс кольца.</summary>
    public event Action<int> OnRingLocked;

    /// <summary>Срабатывает при промахе. Параметр — индекс кольца, на котором промахнулись.</summary>
    public event Action<int> OnRingMissed;

    // ── State ───────────────────────────────────────────────────────────────────

    private bool _isRunning;
    private int _activeIndex;
    private float[] _angles;
    private bool[] _locked;

    // ── Constants ───────────────────────────────────────────────────────────────

    private const float NotchSize = 24f;
    private const float PointerWidth = 12f;
    private const float PointerHeight = 36f;
    private const float RingThickness = 0.12f;

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Запускает мини-игру: сбрасывает прогресс, случайные углы, начинает вращение.</summary>
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
        UpdateActiveHighlight();
    }

    /// <summary>Останавливает мини-игру без вызова OnCompleted.</summary>
    public void StopMinigame()
    {
        _isRunning = false;
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
        if (!_isRunning) return;

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
                // Все кольца заблокированы — победа
                _isRunning = false;
                AudioManager.Instance?.PlaySFX(_completeClip, _volume);
                OnCompleted?.Invoke();
                return;
            }

            UpdateActiveHighlight();
        }
        else
        {
            // Промах
            AudioManager.Instance?.PlaySFX(_failClip, _volume);
            OnRingMissed?.Invoke(idx);

            if (idx > 0)
            {
                // Откат: разблокируем предыдущее кольцо, случайный угол
                _activeIndex = idx - 1;
                _locked[_activeIndex] = false;
                _angles[_activeIndex] = Random.Range(20f, 340f);
                ApplyRingVisual(_activeIndex);
                UpdateActiveHighlight();
            }
            // Если ошиблись на первом кольце — ничего не происходит
        }
    }

    // ── Visuals ─────────────────────────────────────────────────────────────────

    private void ApplyRingVisual(int idx)
    {
        if (_ringImages != null && idx < _ringImages.Length && _ringImages[idx] != null)
        {
            _ringImages[idx].color = _locked[idx]
                ? _ringConfigs[idx].lockedColor
                : _ringConfigs[idx].ringColor;
        }
    }

    private void UpdateActiveHighlight()
    {
        // Подсвечиваем активное кольцо — делаем notch ярче
        for (int i = 0; i < _notchImages.Length; i++)
        {
            if (_notchImages[i] == null) continue;
            _notchImages[i].color = i == _activeIndex
                ? new Color(1f, 0.3f, 0.3f, 1f)
                : _ringConfigs[i].notchColor;
        }
    }

    // ── Auto-create UI ──────────────────────────────────────────────────────────

    private void EnsureVisuals()
    {
        if (_ringContainer != null && _ringTransforms != null && _ringTransforms.Length > 0)
            return; // уже создано

        int count = _ringConfigs.Length;
        var rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null) return;

        // Контейнер колец — по центру панели
        if (_ringContainer == null)
        {
            _ringContainer = CreateChild("RingContainer", rectTransform);
            SetStretch(_ringContainer);
            _ringContainer.anchoredPosition = Vector2.zero;
            _ringContainer.sizeDelta = new Vector2(_containerSize, _containerSize);
        }

        _ringTransforms = new RectTransform[count];
        _ringImages = new Image[count];
        _notchImages = new Image[count];

        // Размеры колец: от большого к маленькому
        float outerSize = _containerSize * 0.9f;
        float sizeStep = outerSize / (count + 1);

        for (int i = 0; i < count; i++)
        {
            float ringSize = outerSize - i * sizeStep;
            float innerRadius = 1f - RingThickness * 2f;
            float outerRadius = 1f;

            // Кольцо
            var ringRect = CreateChild($"Ring_{i}", _ringContainer);
            ringRect.anchoredPosition = Vector2.zero;
            ringRect.sizeDelta = new Vector2(ringSize, ringSize);
            _ringTransforms[i] = ringRect;

            var ringImg = ringRect.gameObject.AddComponent<Image>();
            ringImg.sprite = CreateRingSprite(innerRadius, outerRadius, Color.white);
            ringImg.color = _ringConfigs[i].ringColor;
            ringImg.raycastTarget = false;
            _ringImages[i] = ringImg;

            // Засечка — ребёнок кольца, наверху
            float notchY = ringSize * 0.5f - NotchSize * 0.5f;
            var notchRect = CreateChild($"Notch_{i}", ringRect);
            notchRect.anchoredPosition = new Vector2(0f, notchY);
            notchRect.sizeDelta = new Vector2(NotchSize, NotchSize);

            var notchImg = notchRect.gameObject.AddComponent<Image>();
            notchImg.sprite = CreateDotSprite(Color.white);
            notchImg.color = _ringConfigs[i].notchColor;
            notchImg.raycastTarget = false;
            _notchImages[i] = notchImg;
        }

        // Стрелка-ориентир наверху
        if (_pointerImage == null)
        {
            float pointerY = _containerSize * 0.5f + PointerHeight * 0.5f;
            var pointerRect = CreateChild("Pointer", _ringContainer);
            pointerRect.anchoredPosition = new Vector2(0f, pointerY);
            pointerRect.sizeDelta = new Vector2(PointerWidth, PointerHeight);

            _pointerImage = pointerRect.gameObject.AddComponent<Image>();
            _pointerImage.sprite = CreateBarSprite(Color.white);
            _pointerImage.color = new Color(1f, 0.85f, 0.2f, 1f);
            _pointerImage.raycastTarget = false;
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

    private static void SetStretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    // ── Sprite Generation ───────────────────────────────────────────────────────

    private static Sprite CreateRingSprite(float innerRadius, float outerRadius, Color color)
    {
        const int Size = 128;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float center = Size * 0.5f;

        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy) / center;
                tex.SetPixel(x, y, (dist >= innerRadius && dist <= outerRadius) ? color : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, Size, Size), Vector2.one * 0.5f, Size);
    }

    private static Sprite CreateDotSprite(Color color)
    {
        const int Size = 32;
        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float center = Size * 0.5f;
        float radius = Size * 0.35f;

        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                float dx = x - center + 0.5f;
                float dy = y - center + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                tex.SetPixel(x, y, dist <= radius ? color : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, Size, Size), Vector2.one * 0.5f, Size);
    }

    private static Sprite CreateBarSprite(Color color)
    {
        const int Width = 8;
        const int Height = 40;
        var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                tex.SetPixel(x, y, color);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), Width);
    }
}
