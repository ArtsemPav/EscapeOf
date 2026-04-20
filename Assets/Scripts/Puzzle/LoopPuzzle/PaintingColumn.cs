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
        var data = JsonUtility.FromJson<SaveData>(json);
        _currentHeight = (PaintingHeight)Mathf.Clamp(data.height, 0, 2);
        SnapToCurrentHeight();
    }

    [Serializable]
    private struct SaveData { public int height; }

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

        if (_moveCoroutine != null) StopCoroutine(_moveCoroutine);
        _moveCoroutine = StartCoroutine(SlideTo(GetTargetY(_currentHeight)));

        OnHeightChanged?.Invoke();
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
        PaintingHeight.Low => _lowY,
        PaintingHeight.Mid => _midY,
        PaintingHeight.High => _highY,
        _ => _lowY
    };

    private IEnumerator SlideTo(float targetY)
    {
        float startY = transform.localPosition.y;
        float elapsed = 0f;

        while (elapsed < _moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _moveDuration));
            Vector3 pos = transform.localPosition;
            pos.y = Mathf.Lerp(startY, targetY, t);
            transform.localPosition = pos;
            yield return null;
        }

        Vector3 final = transform.localPosition;
        final.y = targetY;
        transform.localPosition = final;
        _moveCoroutine = null;
    }
}
