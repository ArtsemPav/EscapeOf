using System;
using System.Collections;
using UnityEngine;

/// <summary>Pairs an unknown flask ItemData with the identified version the analyzer returns.</summary>
[Serializable]
public struct IdentificationEntry
{
    public ItemData unknown;
    public ItemData identified;
}

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
    [SerializeField] [Min(0.001f)] private float _flaskPlacementScale = 1f;

    [Tooltip("Height above the slot from which the flask begins its drop animation.")]
    [SerializeField] private float _dropHeight = 0.05f;

    [Tooltip("Duration of the drop animation in seconds.")]
    [SerializeField] private float _dropDuration = 0.4f;

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

    [Tooltip("ItemId of the flask that triggers OnSuccess (puzzle win).\n" +
             "RECOMMENDED: Set this via ChemicalSynthesisController → Synthesis Recipe — Step 4: Analyzer.\n" +
             "The value there overrides this field at runtime if it is non-empty.\n" +
             "This field acts as a fallback when the main controller leaves its field blank.")]
    [SerializeField] private string _winItemId = DefaultWinId;

    [Header("Audio")]
    [Tooltip("Played once when a flask is placed into the analyzer slot.")]
    [SerializeField] private AudioClip _flaskDropClip;

    [Tooltip("Played once when the start button is pressed.")]
    [SerializeField] private AudioClip _startClip;

    [Tooltip("Looping 3D sound played during the scanning phase.")]
    [SerializeField] private AudioClip _scanLoopClip;

    [SerializeField] [Range(0f, 1f)] private float _scanLoopVolume = 0.7f;
    [SerializeField] private float _scanLoopMinDistance = 0.5f;
    [SerializeField] private float _scanLoopMaxDistance = 5f;

    [Tooltip("Played once when analysis returns a successful (win) result.")]
    [SerializeField] private AudioClip _successClip;

    [Tooltip("Played once when analysis returns a failed result.")]
    [SerializeField] private AudioClip _failClip;

    [SerializeField] [Range(0f, 1f)] private float _sfxVolume = 1f;

    [Header("Ghost Preview")]
    [Tooltip("Optional material applied to the hover ghost.")]
    [SerializeField] private Material _ghostMaterial;

    [Header("Hover Highlight")]
    [Tooltip("Renderer that lights up when a valid item is dragged over the analyzer. " +
             "Auto-resolved to the first child MeshRenderer if left empty.")]
    [SerializeField] private Renderer _hoverHighlightRenderer;

    [Tooltip("Emission colour added on top of the material while a valid item hovers. " +
             "Keep values below 1 for a subtle glow.")]
    [SerializeField] private Color _highlightColor = new Color(0f, 0.5f, 0.3f);

    [Tooltip("Amplitude of the bob animation for the hover ghost (world-space meters).")]
    [SerializeField] private float _hoverBobAmplitude = 0.015f;

    [Tooltip("Speed of the bob animation cycle.")]
    [SerializeField] private float _hoverBobSpeed = 2.5f;

    // ── Win condition — set at runtime by ChemicalSynthesisController.ApplySynthesisRecipe() ──
    // Configure in ChemicalSynthesisController → Synthesis Steps (Device: Analyzer).

    // ── Shared context (injected by ChemicalSynthesisController) ──────────────

    private IChemicalPuzzleContext _context;

    /// <summary>Injects the shared puzzle context. Called by ChemicalSynthesisController in Awake.</summary>
    public void Initialize(IChemicalPuzzleContext context) => _context = context;

    /// <summary>
    /// Overrides the win-item identifier at runtime from the central Synthesis Steps plan.
    /// Called by ChemicalSynthesisController.ApplySynthesisRecipe() in Awake.
    /// </summary>
    public void SetWinItemId(string winItemId)
    {
        if (!string.IsNullOrWhiteSpace(winItemId))
            _winItemId = winItemId;
    }

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Action OnSuccess;

    /// <summary>Fired after a failed analysis (wrong flask).</summary>
    public event Action OnFail;

    /// <summary>Fired after analysis completes with the analyzed flask so the caller can return it.</summary>
    public event Action<ItemData> OnFlaskReturned;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private ItemData   _loadedFlask;
    private GameObject _flaskObject;
    private GameObject _hoverGhost;
    private Coroutine  _bobCoroutine;
    private bool       _isBusy;
    private float      _armRestY;
    private AudioSource _scanLoopSource;

    // ── Highlight state ────────────────────────────────────────────────────────

    private bool _isHighlighted;
    private Material _originalSharedMaterial;
    private Material _highlightInstance;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

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
        HideHighlight();
        StopScanLoop();
        if (_highlightInstance != null) Destroy(_highlightInstance);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>True while the analysis cycle is running (arm moving or scanning).</summary>
    public bool IsBusy => _isBusy;

    /// <summary>True when a flask is currently placed in the analyzer slot (before or during analysis).</summary>
    public bool HasFlask => _loadedFlask != null;

    /// <summary>The Colba_Analize collider used as the drop-zone by the orchestrator.</summary>
    public Collider DropZoneCollider => _centerCollider;

    /// <summary>Enables a constant emission highlight on the analyzer mesh.</summary>
    public void ShowHighlight()
    {
        if (_hoverHighlightRenderer == null)
            _hoverHighlightRenderer = GetComponentInChildren<MeshRenderer>();

        if (_hoverHighlightRenderer == null || _isHighlighted) return;
        _isHighlighted = true;

        if (_highlightInstance == null)
        {
            _originalSharedMaterial = _hoverHighlightRenderer.sharedMaterial;
            _highlightInstance = new Material(_originalSharedMaterial);
            _highlightInstance.EnableKeyword("_EMISSION");
        }

        _highlightInstance.SetColor(EmissionColorId, _highlightColor);
        _hoverHighlightRenderer.material = _highlightInstance;
    }

    /// <summary>Removes the emission highlight and restores the original shared material.</summary>
    public void HideHighlight()
    {
        if (!_isHighlighted) return;
        _isHighlighted = false;

        if (_hoverHighlightRenderer != null && _originalSharedMaterial != null)
            _hoverHighlightRenderer.sharedMaterial = _originalSharedMaterial;
    }

    /// <summary>Returns true when <paramref name="item"/> is accepted by the puzzle's global whitelist.</summary>
    public bool CanDrop(ItemData item) => _context?.IsAccepted(item) ?? false;

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

        _bobCoroutine = StartCoroutine(BobGhostRoutine(_hoverGhost.transform.position));
    }

    /// <summary>Destroys the hover ghost and stops bob animation.</summary>
    public void HideHoverPreview()
    {
        if (_bobCoroutine != null) { StopCoroutine(_bobCoroutine); _bobCoroutine = null; }
        if (_hoverGhost   != null) { Destroy(_hoverGhost); _hoverGhost = null; }
    }

    /// <summary>Stores the flask data and drops its visual into the analyzer slot.</summary>
    public void LoadFlask(ItemData flask)
    {
        HideHoverPreview();
        _loadedFlask = flask;
        PlaySFX(_flaskDropClip);

        if (flask?.inspectionPrefab != null && _centerCollider != null)
            StartCoroutine(DropFlaskRoutine(flask.inspectionPrefab));
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

    private IEnumerator DropFlaskRoutine(GameObject prefab)
    {
        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }

        Transform parent = _centerCollider.transform;
        Vector3   center = _centerCollider.bounds.center;

        _flaskObject = Instantiate(prefab, center, parent.rotation, parent);
        _flaskObject.transform.localScale = ComputeLocalScaleForWorldScale(parent, _flaskPlacementScale);

        foreach (var col in _flaskObject.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        Vector3 centerOffset = ComputeBoundsCenter(_flaskObject) - center;
        Vector3 endWorld     = center - centerOffset;
        Vector3 startWorld   = endWorld + Vector3.up * _dropHeight;

        _flaskObject.transform.position = startWorld;

        float elapsed = 0f;
        while (elapsed < _dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _dropDuration);
            _flaskObject.transform.position = Vector3.Lerp(startWorld, endWorld, t * t); // ease-in
            yield return null;
        }

        _flaskObject.transform.position = endWorld;
    }

    private IEnumerator BobGhostRoutine(Vector3 basePos)
    {
        float t = 0f;
        while (_hoverGhost != null)
        {
            t += Time.deltaTime * _hoverBobSpeed;
            _hoverGhost.transform.position = basePos + Vector3.up * (_hoverBobAmplitude * (1f + Mathf.Sin(t)));
            yield return null;
        }
    }

    private void OnButtonPressed()
    {
        if (_loadedFlask == null || _isBusy) return;
        PlaySFX(_startClip);
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

        _scanLoopSource = AudioManager.Instance != null
            ? AudioManager.Instance.Play3DLoop(_scanLoopClip, transform, _scanLoopVolume, _scanLoopMinDistance, _scanLoopMaxDistance)
            : null;

        float elapsed = 0f;
        while (elapsed < _scanDuration)
        {
            elapsed += Time.deltaTime;
            int percent = Mathf.RoundToInt(Mathf.Clamp01(elapsed / _scanDuration) * 100f);
            _screen?.SetScanPercent(percent);
            yield return null;
        }
        _screen?.SetScanPercent(100);

        StopScanLoop();

        // 3. Show result with compound name and description.
        // Resolve identified version — reveals the real name on screen even for unknown flasks.
        ItemData identified  = Identify(_loadedFlask);
        bool   isSuccess    = identified.ItemId == _winItemId;
        string compoundName = identified.itemName;
        string description  = string.IsNullOrEmpty(identified.description)
                              ? compoundName
                              : identified.description;

        _screen?.ShowResult(compoundName, description, isSuccess);
        PlaySFX(isSuccess ? _successClip : _failClip);

        yield return new WaitForSeconds(_resultDisplayDuration);

        // 4. Arm ascends.
        yield return MoveArmTo(_armRestY, _diveMoveDuration);

        // 5. Destroy flask visual and reset screen.
        if (_flaskObject != null) { Destroy(_flaskObject); _flaskObject = null; }
        _screen?.ShowIdle();

        // 6. Return the identified version so the player sees the real name in inventory.
        ItemData returnedFlask = identified;
        _loadedFlask = null;
        _isBusy      = false;

        // Fire success/fail first so subscribers (e.g. ChemicalSynthesisController) can set
        // their cleanup flags before OnFlaskReturned delivers the flask to inventory.
        if (isSuccess)
            OnSuccess?.Invoke();
        else
            OnFail?.Invoke();

        OnFlaskReturned?.Invoke(returnedFlask);
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

    /// <summary>
    /// Returns the identified counterpart for <paramref name="item"/> via the shared
    /// puzzle context's equivalence map. Returns <paramref name="item"/> itself if
    /// no mapping exists.
    /// </summary>
    private ItemData Identify(ItemData item) => _context?.Normalize(item) ?? item;

    private void StopScanLoop()
    {
        if (_scanLoopSource == null) return;
        Destroy(_scanLoopSource.gameObject);
        _scanLoopSource = null;
    }

    /// <summary>Plays a one-shot SFX through AudioManager if a clip is assigned.</summary>
    private void PlaySFX(AudioClip clip) { if (clip != null) AudioManager.Instance?.PlaySFX(clip, _sfxVolume); }

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
