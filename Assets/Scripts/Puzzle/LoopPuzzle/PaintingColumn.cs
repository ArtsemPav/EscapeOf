using System;
using System.Collections;
using UnityEngine;

public enum PaintingHeight { Low = 0, Mid = 1, High = 2 }

/// <summary>
/// A painting that can occupy one of three vertical heights (Low, Mid, High).
/// Manages smooth movement between heights and persists its state via ISaveable.
/// Symbol visibility is evaluated externally by LoopPuzzleController.
/// </summary>
public class PaintingColumn : MonoBehaviour, ISaveable
{
    [Header("Save")]
    [SerializeField] private string _saveId;

    [Header("Heights (Local Y)")]
    [SerializeField] private float _lowY = 0f;
    [SerializeField] private float _midY = 1f;
    [SerializeField] private float _highY = 2f;
    [Tooltip("Duration of the smooth slide animation in seconds.")]
    [SerializeField] private float _moveDuration = 0.4f;

    private PaintingHeight _currentHeight = PaintingHeight.Low;
    private Coroutine _moveCoroutine;

    // ── Static moving state (shared across all columns) ───────────────────────

    private static int s_movingCount = 0;

    /// <summary>True when at least one PaintingColumn animation is in progress.</summary>
    public static bool IsAnyMoving => s_movingCount > 0;

    /// <summary>Fired when the global moving state changes. True = started, False = all done.</summary>
    public static event Action<bool> OnAnyMovingChanged;

    // ── Instance state ─────────────────────────────────────────────────────────

    /// <summary>True while this column's slide animation is running.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Fired when this column finishes its slide animation.</summary>
    public event Action OnMoveFinished;

    /// <summary>Current height state of this painting.</summary>
    public PaintingHeight CurrentHeight => _currentHeight;

    /// <summary>Raised when the height changes (after advance).</summary>
    public event Action OnHeightChanged;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { height = (int)_currentHeight });

    public void LoadSaveData(string json)
    {
        _wasLoaded = true;
        var data = JsonUtility.FromJson<SaveData>(json);
        _currentHeight = (PaintingHeight)Mathf.Clamp(data.height, 0, 2);
        _initialHeight = _currentHeight;
        SnapToCurrentHeight();
    }

    [Serializable]
    private struct SaveData { public int height; }

    private bool _wasLoaded;

    /// <summary>Height assigned at the start of a fresh session. Used to restore on puzzle reset.</summary>
    private PaintingHeight _initialHeight;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
        SnapToCurrentHeight();
    }

    private void OnDestroy() => SaveManager.Instance?.Unregister(this);

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances height by one step (Low → Mid → High → Low) with smooth animation.
    /// </summary>
    public void AdvanceHeight()
    {
        _currentHeight = (PaintingHeight)(((int)_currentHeight + 1) % 3);

        bool wasMoving = _moveCoroutine != null;
        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);

        if (!wasMoving)
        {
            IsMoving = true;
            s_movingCount++;
            if (s_movingCount == 1) OnAnyMovingChanged?.Invoke(true);
        }

        _moveCoroutine = StartCoroutine(SlideTo(GetTargetY(_currentHeight)));
        OnHeightChanged?.Invoke();
    }

    /// <summary>
    /// Picks a random starting height that is never equal to <paramref name="excludedHeight"/>.
    /// Caches the result as the initial height for future resets.
    /// No-op if this column was loaded from a save — the saved position takes priority.
    /// Must be called before any AdvanceHeight() calls.
    /// </summary>
    public void RandomizeStartingHeight(PaintingHeight excludedHeight)
    {
        if (_wasLoaded) return;

        int offset = UnityEngine.Random.Range(1, 3);
        _currentHeight = (PaintingHeight)(((int)excludedHeight + offset) % 3);
        _initialHeight = _currentHeight;
        SnapToCurrentHeight();
    }

    /// <summary>
    /// Slides the column back to the height it had at the start of this session.
    /// If the column was loaded from a save, returns to the loaded position.
    /// </summary>
    public void ResetToInitialState()
    {
        _currentHeight = _initialHeight;

        bool wasMoving = _moveCoroutine != null;
        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);

        if (!wasMoving)
        {
            IsMoving = true;
            s_movingCount++;
            if (s_movingCount == 1) OnAnyMovingChanged?.Invoke(true);
        }

        _moveCoroutine = StartCoroutine(SlideTo(GetTargetY(_currentHeight)));
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void SnapToCurrentHeight()
    {
        Vector3 pos = transform.localPosition;
        pos.y = GetTargetY(_currentHeight);
        transform.localPosition = pos;
    }

    private float GetTargetY(PaintingHeight height) => height switch
    {
        PaintingHeight.Low  => _lowY,
        PaintingHeight.Mid  => _midY,
        PaintingHeight.High => _highY,
        _                   => _lowY
    };

    private IEnumerator SlideTo(float targetY)
    {
        float startY   = transform.localPosition.y;
        float elapsed  = 0f;

        while (elapsed < _moveDuration)
        {
            elapsed += Time.deltaTime;
            float t   = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _moveDuration));
            Vector3 pos = transform.localPosition;
            pos.y = Mathf.Lerp(startY, targetY, t);
            transform.localPosition = pos;
            yield return null;
        }

        Vector3 final = transform.localPosition;
        final.y = targetY;
        transform.localPosition = final;

        _moveCoroutine = null;
        IsMoving       = false;
        OnMoveFinished?.Invoke();

        s_movingCount = Mathf.Max(0, s_movingCount - 1);
        if (s_movingCount == 0) OnAnyMovingChanged?.Invoke(false);
    }
}
