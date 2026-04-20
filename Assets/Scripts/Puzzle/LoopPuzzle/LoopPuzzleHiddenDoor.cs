using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Hidden door that slides open when the Loop Puzzle is solved.
/// Slide direction, distance, and duration are configurable in the Inspector.
/// Door starts at its current local position (closed). On Open(), it slides by
/// SlideDirection * SlideDistance in local space with a SmoothStep curve.
/// Persists its open state via ISaveable — snaps to open position on load.
/// </summary>
public class LoopPuzzleHiddenDoor : MonoBehaviour, ISaveable
{
    [Header("Save")]
    [SerializeField] private string _saveId = "loop_puzzle_hidden_door";

    [Header("Door Visual")]
    [Tooltip("The Transform that slides (assign Door_Visual child). " +
             "If left empty, this GameObject's transform is used.")]
    [SerializeField] private Transform _doorVisual;

    [Header("Slide Settings")]
    [Tooltip("Local-space direction the door slides when opening. " +
             "Example: (1,0,0) = right, (0,1,0) = up, (0,0,-1) = forward.")]
    [SerializeField] private Vector3 _slideDirection = Vector3.right;
    [SerializeField] private float _slideDistance = 1.5f;
    [SerializeField] private float _slideDuration  = 1.2f;

    [Header("Audio")]
    [SerializeField] private AudioClip _openClip;
    [SerializeField] [Range(0f, 1f)] private float _openVolume = 1f;

    private bool      _isOpen;
    private Vector3   _closedLocalPos;
    private Coroutine _slideCoroutine;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { isOpen = _isOpen });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data.isOpen) SnapOpen();
    }

    [Serializable]
    private struct SaveData { public bool isOpen; }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        Transform t = _doorVisual != null ? _doorVisual : transform;
        _closedLocalPos = t.localPosition;
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy() => SaveManager.Instance?.Unregister(this);

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Slides the door open with animation and optional sound. Idempotent.</summary>
    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        if (_openClip != null)
            AudioManager.Instance?.PlaySFX(_openClip, _openVolume);

        Vector3 targetLocalPos = _closedLocalPos + _slideDirection.normalized * _slideDistance;

        if (_slideCoroutine != null) StopCoroutine(_slideCoroutine);
        _slideCoroutine = StartCoroutine(SlideTo(targetLocalPos));

        SaveManager.Instance?.Save();
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void SnapOpen()
    {
        _isOpen = true;
        Transform t = _doorVisual != null ? _doorVisual : transform;
        t.localPosition = _closedLocalPos + _slideDirection.normalized * _slideDistance;
    }

    private IEnumerator SlideTo(Vector3 targetLocalPos)
    {
        Transform t   = _doorVisual != null ? _doorVisual : transform;
        Vector3 start = t.localPosition;
        float elapsed = 0f;

        while (elapsed < _slideDuration)
        {
            elapsed += Time.deltaTime;
            float step = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _slideDuration));
            t.localPosition = Vector3.Lerp(start, targetLocalPos, step);
            yield return null;
        }

        t.localPosition = targetLocalPos;
        _slideCoroutine = null;
    }
}
