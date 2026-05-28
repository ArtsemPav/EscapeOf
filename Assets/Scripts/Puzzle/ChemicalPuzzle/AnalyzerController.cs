using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Controls the analyzer device.
/// Player drops a flask on analiseStoyka → LoadFlask spawns the visual at Colba_Analize.
/// Pressing button1 starts the analysis cycle:
///   arm descends → scan (1→100%) → result screen → arm ascends → flask returned.
/// Only the win flask (SerumColba) fires OnSuccess; all others fire OnFail but are still returned.
/// </summary>
public class AnalyzerController : MonoBehaviour
{
    private const string DefaultWinId = "SerumColba";

    [Header("References")]
    [Tooltip("button1 — starts the analysis cycle.")]
    [SerializeField] private ButtonPressAnimation _startButton;

    [Tooltip("AnalyzerScreenController on analizator.")]
    [SerializeField] private AnalyzerScreenController _screen;

    [Header("Flask Placement")]
    [Tooltip("Colba_Analize CapsuleCollider — bounds.center used as placement position.")]
    [SerializeField] private Collider _centerCollider;

    [Tooltip("Desired uniform world-space scale for the flask visual while in the analyzer.")]
    [SerializeField] [Range(0.01f, 5f)] private float _flaskPlacementScale = 1f;

    [Header("Arm Animation")]
    [Tooltip("How far the analizator arm descends in WORLD-SPACE meters. Converted to local units at runtime.")]
    [SerializeField] private float _diveDepth = 0.05f;

    [Tooltip("Duration of the arm descent and ascent in seconds.")]
    [SerializeField] private float _diveMoveDuration = 0.5f;

    [Header("Settings")]
    [Tooltip("Total duration of the scanning phase in seconds.")]
    [SerializeField] private float _scanDuration = 5f;

    [Tooltip("How long the result is shown before the arm retracts.")]
    [SerializeField] private float _resultDisplayDuration = 3f;

    [Tooltip("ItemId of the flask that triggers OnSuccess.")]
    [SerializeField] private string _winItemId = DefaultWinId;

    [Header("Accepted Items")]
    [Tooltip("All flasks the analyzer accepts. Wrong items get an analysis result but are returned.")]
    [SerializeField] private ItemData[] _acceptedItems;

    [Header("Ghost Preview")]
    [Tooltip("Optional material applied to the hover ghost.")]
    [SerializeField] private Material _ghostMaterial;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Fired after a successful analysis (win flask detected).</summary>
    public event Action OnSuccess;

    /// <summary>Fired after a failed analysis (wrong flask).</summary>
    public event Action OnFail;

    /// <summary>Fired after analysis completes with the analyzed flask so the caller can return it.</summary>
    public event Action<ItemData> OnFlaskReturned;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private ItemData   _loadedFlask;
    private GameObject _flaskObject;
    private GameObject _hoverGhost;
    private bool       _isBusy;
    private float      _armRestY;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _armRestY = transform.localPosition.y;

        if (_startButton != null)
            _startButton.OnPressed += OnButtonPressed;
    }

    private void OnDestroy()
    {
        if (_startButton != null)
            _startButton.OnPressed -= OnButtonPressed;

        HideHoverPreview();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the analysis cycle is running (arm moving or scanning).</summary>
    public bool IsBusy => _isBusy;

    /// <summary>The Colba_Analize collider used as the drop-zone by the orchestrator.</summary>
    public Collider DropZoneCollider => _centerCollider;

    /// <summary>Returns true when <paramref name="item"/> is in the accepted-items list.</summary>
    public bool CanDrop(ItemData item)
    {
        if (item == null || _acceptedItems == null || _acceptedItems.Length == 0) return false;
        return Array.IndexOf(_acceptedItems, item) >= 0;
    }

    /// <summary>Backward-compatible alias for CanDrop.</summary>
    public bool Accepts(ItemData item) => CanDrop(item);

    /// <summary>
    /// Shows a ghost preview at the Colba_Analize position.
    /// Safe to call every frame — rebuilds only when item changes.
    /// </summary>
    public void ShowHoverPreview(ItemData item)
    {
        if (item == null || _centerCollider == null) { HideHoverPreview(); return; }
        if (_hoverGhost != null) return;
        if (item.inspectionPrefab == null) return;

        Transform parent = _centerCollider.transform;
        Vector3   center = _centerCollider.bounds.center;

        _hoverGhost = Instantiate(item.inspectionPrefab, center, parent.rotation, parent);
        _hoverGhost.transform.localScale = ComputeLocalScaleForWorldScale(parent, _flaskPlacementScale);

        Vector3 offset = ComputeBoundsCenter(_hoverGhost) - center;
        _hoverGhost.transform.position -= offset;

        if (_ghostMaterial != null)
        {
            foreach (var rend in _hoverGhost.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMaterial;
                rend.materials = mats;
            }
        }

        foreach (var col in _hoverGhost.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }

    /// <summary>Destroys the hover ghost.</summary>
    public void HideHoverPreview()
    {
        if (_hoverGhost != null) { Destroy(_hoverGhost); _hoverGhost = null; }
    }

    /// <summary>Stores the flask data and spawns its visual at Colba_Analize.</summary>
    public void LoadFlask(ItemData flask)
    {
        HideHoverPreview();
        _loadedFlask = flask;

        if (flask?.inspectionPrefab != null && _centerCollider != null)
            SpawnFlaskVisual(flask.inspectionPrefab);
    }

    /// <summary>
    /// Removes the placed flask without running the analysis cycle.
    /// Only works when the analyzer is idle. Returns the flask data (caller adds to inventory).
    /// </summary>
    public ItemData TryRetrieveFlask()
    {
        if (_isBusy || _loadedFlask == null) return null;

        ItemData flask = _loadedFlask;
        _loadedFlask   = null;

        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }
        _screen?.ShowIdle();

        return flask;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private void SpawnFlaskVisual(GameObject prefab)
    {
        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }

        Transform parent = _centerCollider.transform;
        Vector3   center = _centerCollider.bounds.center;

        _flaskObject = Instantiate(prefab, center, parent.rotation, parent);
        _flaskObject.transform.localScale = ComputeLocalScaleForWorldScale(parent, _flaskPlacementScale);

        Vector3 offset = ComputeBoundsCenter(_flaskObject) - center;
        _flaskObject.transform.position -= offset;

        foreach (var col in _flaskObject.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }

    private void OnButtonPressed()
    {
        if (_loadedFlask == null || _isBusy) return;
        StartCoroutine(AnalyzeCoroutine());
    }

    private IEnumerator AnalyzeCoroutine()
    {
        _isBusy = true;

        // Convert world-space diveDepth to local units (parent may have scale != 1).
        float parentScaleY     = transform.parent != null ? Mathf.Abs(transform.parent.lossyScale.y) : 1f;
        float localDiveDepth   = parentScaleY > 0.0001f ? _diveDepth / parentScaleY : _diveDepth;

        // 1. Arm descends.
        yield return MoveArmTo(_armRestY - localDiveDepth, _diveMoveDuration);

        // 2. Scanning — count from 0 to 100%.
        _screen?.ShowScanning();
        float elapsed = 0f;
        while (elapsed < _scanDuration)
        {
            elapsed += Time.deltaTime;
            int percent = Mathf.RoundToInt(Mathf.Clamp01(elapsed / _scanDuration) * 100f);
            _screen?.SetScanPercent(percent);
            yield return null;
        }
        _screen?.SetScanPercent(100);

        // 3. Show result with compound name and description.
        bool   isSuccess    = _loadedFlask.ItemId == _winItemId;
        string compoundName = _loadedFlask.itemName;
        string description  = string.IsNullOrEmpty(_loadedFlask.description)
                              ? compoundName
                              : _loadedFlask.description;

        _screen?.ShowResult(compoundName, description, isSuccess);
        yield return new WaitForSeconds(_resultDisplayDuration);

        // 4. Arm ascends.
        yield return MoveArmTo(_armRestY, _diveMoveDuration);

        // 5. Destroy flask visual and reset screen.
        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }
        _screen?.ShowIdle();

        // 6. Return flask and fire events.
        ItemData returnedFlask = _loadedFlask;
        _loadedFlask = null;
        _isBusy      = false;

        OnFlaskReturned?.Invoke(returnedFlask);

        if (isSuccess)
            OnSuccess?.Invoke();
        else
            OnFail?.Invoke();
    }

    private IEnumerator MoveArmTo(float targetLocalY, float duration)
    {
        float startY  = transform.localPosition.y;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t   = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            Vector3 p = transform.localPosition;
            p.y = Mathf.Lerp(startY, targetLocalY, t);
            transform.localPosition = p;
            yield return null;
        }

        Vector3 final = transform.localPosition;
        final.y = targetLocalY;
        transform.localPosition = final;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Vector3 ComputeLocalScaleForWorldScale(Transform parent, float worldScale)
    {
        Vector3 ps = parent.lossyScale;
        return new Vector3(
            Mathf.Approximately(ps.x, 0f) ? worldScale : worldScale / ps.x,
            Mathf.Approximately(ps.y, 0f) ? worldScale : worldScale / ps.y,
            Mathf.Approximately(ps.z, 0f) ? worldScale : worldScale / ps.z);
    }

    private static Vector3 ComputeBoundsCenter(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers.Length == 0) return obj.transform.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds.center;
    }
}
