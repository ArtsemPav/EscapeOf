using System;
using System.Collections;
using ChemicalPuzzle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Phase 2 of the final door puzzle: the skull.
/// After the skull moves into position (Phase 1 cinematic), the player
/// interacts with the skull to enter a close-up puzzle mode via scullCamera.
///
/// Flow:
/// 1. Player inserts two coins (Coin 1, Coin 2) into the skull's eyes.
///    Order and side do not matter. Free retrieval until both are placed.
/// 2. When both eyes are filled, the jaw opens (ScullOpen animation).
/// 3. After the jaw opens, the player drags SerumColba onto the skull.
/// 4. The colba animates to the colbaRead position (pouring animation via Lerp).
/// 5. FinalDoorPuzzleController.SetSolved() is called — the puzzle is complete.
///
/// Attach to the Scull GameObject. Requires a Collider on Scull for interaction.
/// </summary>
public class ScullPuzzlePhase : MonoBehaviour, IInteractable, IPuzzleDropHandler, IPuzzleExitGuard, ISaveable
{
    [Header("Controller")]
    [Tooltip("Auto-found via GetComponentInParent if empty.")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Camera")]
    [Tooltip("CinemachineCamera for the skull close-up.")]
    [SerializeField] private CinemachineCamera _scullCamera;

    [Header("Eyes (2)")]
    [Tooltip("Coin1 object — left eye visual + collider. Hidden until a coin is placed.")]
    [SerializeField] private GameObject _eye0Object;

    [Tooltip("Coin2 object — right eye visual + collider. Hidden until a coin is placed.")]
    [SerializeField] private GameObject _eye1Object;

    [Tooltip("LayerMask for eye colliders (raycast target for coin drops).")]
    [SerializeField] private LayerMask _eyeLayer = 1024; // PuzzleInteractable

    [Header("Animator")]
    [Tooltip("Animator on the Scull object.")]
    [SerializeField] private Animator _scullAnimator;

    [Tooltip("Name of the jaw-opening animation state.")]
    [SerializeField] private string _scullOpenStateName = "ScullOpen";

    [Header("Accepted Items")]
    [Tooltip("Coin items accepted by the eyes (Coin 1, Coin 2). Either can go in either eye.")]
    [SerializeField] private ItemData[] _coinItems;

    [Tooltip("SerumColba ItemData — poured into the skull after the jaw opens.")]
    [SerializeField] private ItemData _serumColbaItem;

    [Header("Pouring Animation")]
    [Tooltip("Transform of the colba object (parent of mesh + liquid). Animate rotation from upright to flipped.")]
    [SerializeField] private Transform _colbaTransform;

    [Tooltip("Final position/rotation for the colba when pouring is complete (colbaRead in prefab).")]
    [SerializeField] private Transform _colbaReadTransform;

    [Tooltip("LiquidWobble component on the liquid mesh — disabled before pouring to prevent wobble physics from interfering.")]
    [SerializeField] private ChemicalPuzzle.LiquidWobble _liquidWobble;

    [Tooltip("Renderer on the liquid mesh — its material _FillAmount is animated to 0 during pouring.")]
    [SerializeField] private Renderer _liquidRenderer;

    [Tooltip("Duration of the pouring animation (seconds).")]
    [SerializeField, Min(0.5f)] private float _pourDuration = 2f;

    [Tooltip("Duration of the colba flip animation (seconds).")]
    [SerializeField, Min(0.3f)] private float _flipDuration = 1f;

    [Header("Serum Drop")]
    [Tooltip("Collider on the skull for SerumColba drop detection. Enabled after jaw opens.")]
    [SerializeField] private Collider _scullDropCollider;

    [Tooltip("LayerMask for the skull drop collider (same as interaction layer).")]
    [SerializeField] private LayerMask _scullDropLayer = 64; // Interactable Layer (bit 6)

    [Header("Coin Visuals")]
    [Tooltip("Fallback coin prefab for ghost preview.")]
    [SerializeField] private GameObject _coinPrefab;

    [Tooltip("URP rendering layer mask applied to ghost previews so they receive light " +
             "from the puzzle's light groups. Default includes 'CoridorFDoor' (bit 22) " +
             "and 'flashLight' (bit 9).")]
    [SerializeField] private uint _coinRenderingLayerMask = (1u << 22) | (1u << 9);

    [Tooltip("Drop height for coin insert animation.")]
    [SerializeField] private float _dropHeight = 0.05f;

    [Tooltip("Drop duration for coin insert animation.")]
    [SerializeField] private float _dropDuration = 0.3f;

    [Header("Fade")]
    [Tooltip("Duration of the screen fade to/from black during the pour cinematic.")]
    [SerializeField, Min(0.1f)] private float _fadeDuration = 1f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Осмотреть череп";
    [SerializeField] private CrosshairMode _crosshairMode = CrosshairMode.Hand;

    [Header("Sounds")]
    [SerializeField] private AudioClip _coinDropClip;
    [SerializeField] private AudioClip _jawOpenClip;
    [SerializeField] private AudioClip _pourClip;
    [SerializeField, Range(0f, 1f)] private float _coinDropVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float _jawOpenVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _pourVolume = 1f;

    // ── State ──────────────────────────────────────────────────────────────────

    private bool _activated;
    private bool _jawOpen;
    private bool _pouring;
    private bool _serumPoured;

    private readonly ItemData[] _eyeItems = new ItemData[2];
    private Collider _eye0Collider;
    private Collider _eye1Collider;

    private MedallionHole _ghostEye0;
    private MedallionHole _ghostEye1;
    private GameObject _ghostPreview;
    private int _hoveredEye = -1;

    private ScullSaveData? _pendingLoad;

    // ── IInteractable ──────────────────────────────────────────────────────────

    public bool CanInteract() => _activated && !_serumPoured && !_pouring;

    public void Interact()
    {
        if (CanInteract())
            _controller?.EnterPuzzleMode(_scullCamera, this);
    }

    public string GetInteractText() => _serumPoured ? string.Empty : _interactText;
    public bool IsPickable() => false;
    public CrosshairMode GetCrosshairMode() => _crosshairMode;

    // ── IPuzzleDropHandler ──────────────────────────────────────────────────────

    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = null;
        if (item == null || Camera.main == null || _serumPoured) return false;

        if (_jawOpen)
        {
            if (item == _serumColbaItem)
                return HandleSerumDrop(screenPosition);
            return false;
        }

        if (IsCoinItem(item))
            return HandleCoinDrop(item, screenPosition);

        return false;
    }

    // ── IPuzzleExitGuard ─────────────────────────────────────────────────────────

    public bool CanExitPuzzle() => !_pouring;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => "final_door_scull_phase";

    public string GetSaveData()
    {
        var ids = new string[2];
        for (int i = 0; i < 2; i++)
            ids[i] = _eyeItems[i] != null ? _eyeItems[i].ItemId : string.Empty;

        return JsonUtility.ToJson(new ScullSaveData
        {
            activated = _activated,
            jawOpen = _jawOpen,
            serumPoured = _serumPoured,
            placedEyeItemIds = ids
        });
    }

    public void LoadSaveData(string json)
    {
        _pendingLoad = JsonUtility.FromJson<ScullSaveData>(json);
    }

    [Serializable]
    private struct ScullSaveData
    {
        public bool activated;
        public bool jawOpen;
        public bool serumPoured;
        public string[] placedEyeItemIds;
    }

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>Called by FinalDoorPuzzleInteraction after Phase 1 cinematic completes.</summary>
    public void Activate()
    {
        if (_activated) return;
        _activated = true;
        HideEyeVisual(0);
        HideEyeVisual(1);
    }

    /// <summary>True when the skull phase is fully solved.</summary>
    public bool IsSolved => _serumPoured;

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponentInParent<FinalDoorPuzzleController>();

        if (_eye0Object != null) _eye0Collider = _eye0Object.GetComponent<Collider>();
        if (_eye1Object != null) _eye1Collider = _eye1Object.GetComponent<Collider>();

        // Coin1/Coin2 GameObjects stay inactive until a coin is placed.
        // Ghost previews are parented to coinLeft/CoinRight (the eye objects),
        // not to Coin1/Coin2, so they don't need Coin1/Coin2 to be active.
        HideEyeVisual(0);
        HideEyeVisual(1);

        SaveManager.Instance?.Register(this);
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnEntered += HandleEntered;
            _controller.OnExited += HandleExited;
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
        {
            _controller.OnEntered -= HandleEntered;
            _controller.OnExited -= HandleExited;
        }
        ClearGhost();
    }

    private void Start()
    {
        ApplyPendingLoad();
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (!_activated || _controller == null || !_controller.IsActive) return;
        if (Mouse.current == null || _serumPoured || _pouring) return;

        var mousePos = Mouse.current.position.ReadValue();
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (_jawOpen)
        {
            // After jaw opens — ghost preview for SerumColba over the skull.
            if (PuzzleInventoryBar.IsDragging && !overUI)
                UpdateSerumGhostPreview(mousePos);
            else
                ClearGhost();
            return;
        }

        // Before jaw opens — coin ghost preview + coin retrieval.
        if (PuzzleInventoryBar.IsDragging && !overUI)
            UpdateGhostPreview(mousePos);
        else
            ClearGhost();

        // Click on a filled eye → retrieve.
        if (Mouse.current.leftButton.wasPressedThisFrame && !overUI && !PuzzleInventoryBar.IsDragging)
            TryRetrieveFromEye(mousePos);
    }

    // ── Controller Events ────────────────────────────────────────────────────────

    private void HandleEntered()
    {
        if (!_activated) return;

        // Eye colliders are enabled for coin drops; the Scull's own collider
        // stays always enabled for player interaction and SerumColba drops.
        if (!_jawOpen)
            EnableEyeColliders();
    }

    private void HandleExited()
    {
        DisableEyeColliders();
        ClearGhost();
    }

    // ── Coin Drop ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Animates a coin dropping from above into the eye socket, then reveals
    /// the real Coin1/Coin2 visual at the final position.
    /// </summary>
    private IEnumerator CoinInsertRoutine(int eyeIndex, ItemData item)
    {
        var eyeObj = eyeIndex == 0 ? _eye0Object : _eye1Object;
        if (eyeObj == null) yield break;

        // Find the real coin child for target position/rotation/scale.
        Transform coinRef = null;
        for (int i = 0; i < eyeObj.transform.childCount; i++)
        {
            coinRef = eyeObj.transform.GetChild(i);
            break;
        }
        if (coinRef == null) yield break;

        Vector3 finalPos = coinRef.position;
        Quaternion finalRot = coinRef.rotation;
        Vector3 finalScale = coinRef.localScale;

        // Spawn a temporary animated coin above the eye.
        var prefab = item.inspectionPrefab != null ? item.inspectionPrefab : _coinPrefab;
        if (prefab == null)
        {
            ShowEyeVisual(eyeIndex);
            if (BothEyesFilled()) StartCoroutine(OpenJawRoutine());
            yield break;
        }

        Vector3 startPos = finalPos + eyeObj.transform.up * _dropHeight;
        var animCoin = Instantiate(prefab, startPos, finalRot, eyeObj.transform);
        animCoin.transform.localScale = finalScale;

        foreach (var rend in animCoin.GetComponentsInChildren<Renderer>(true))
            rend.renderingLayerMask = _coinRenderingLayerMask;

        // Lerp the animated coin down into the eye.
        float elapsed = 0f;
        while (elapsed < _dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _dropDuration);
            animCoin.transform.position = Vector3.Lerp(startPos, finalPos, t);
            yield return null;
        }

        animCoin.transform.position = finalPos;

        // Replace animated coin with the real Coin1/Coin2 visual.
        Destroy(animCoin);
        ShowEyeVisual(eyeIndex);

        if (BothEyesFilled())
            StartCoroutine(OpenJawRoutine());
    }

    private bool HandleCoinDrop(ItemData item, Vector2 screenPosition)
    {
        ClearGhost();

        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, 50f, _eyeLayer, QueryTriggerInteraction.Collide))
            return false;

        int eyeIndex = GetEyeIndex(hit.collider);
        if (eyeIndex < 0 || _eyeItems[eyeIndex] != null) return false;

        _eyeItems[eyeIndex] = item;
        PlaySFX(_coinDropClip, _coinDropVolume);

        StartCoroutine(CoinInsertRoutine(eyeIndex, item));

        return true;
    }

    // ── Serum Drop ───────────────────────────────────────────────────────────────

    private bool HandleSerumDrop(Vector2 screenPosition)
    {
        var ray = Camera.main.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, 50f, _scullDropLayer, QueryTriggerInteraction.Collide))
            return false;

        if (_scullDropCollider != null && hit.collider != _scullDropCollider) return false;

        StartCoroutine(PourRoutine());
        return true;
    }

    // ── Jaw Opening ──────────────────────────────────────────────────────────────

    private IEnumerator OpenJawRoutine()
    {
        // Wait a moment for the last coin to settle visually.
        yield return new WaitForSeconds(0.3f);

        PlaySFX(_jawOpenClip, _jawOpenVolume);

        if (_scullAnimator != null)
            _scullAnimator.Play(_scullOpenStateName);

        yield return WaitForAnimationFinish(_scullAnimator, _scullOpenStateName);

        _jawOpen = true;

        // Eye colliders are no longer needed — SerumColba drops use the Scull's own collider.
        DisableEyeColliders();

        SaveManager.Instance?.Save();
    }

    // ── Pouring Animation ─────────────────────────────────────────────────────────

    private IEnumerator PourRoutine()
    {
        _pouring = true;

        PlaySFX(_pourClip, _pourVolume);

        if (_colbaReadTransform != null)
        {
            // Activate the colba hierarchy.
            EnsureActiveUpTo(_colbaReadTransform, transform);
            _colbaReadTransform.gameObject.SetActive(true);

            // Apply rendering layer mask so the colba and liquid receive light.
            foreach (var rend in _colbaReadTransform.GetComponentsInChildren<Renderer>(true))
                rend.renderingLayerMask = _coinRenderingLayerMask;

            // Keep LiquidWobble ENABLED during the flip — it pushes _PivotWS
            // to the shader every frame, which is needed for correct liquid
            // level calculation as the flask rotates. We only animate
            // fillFraction to 0; the wobble physics is harmless during the pour.
            // LiquidWobble is disabled AFTER the animation completes.

            // Animate colbaRead itself so mesh + liquid move together.
            Vector3 finalPos = _colbaReadTransform.position;
            Quaternion finalRot = _colbaReadTransform.rotation;
            Vector3 startPos = _colbaReadTransform.position;
            Quaternion startRot = _colbaReadTransform.rotation;

            float liquidStartFill = _liquidWobble != null ? _liquidWobble.fillFraction : 0f;
            float elapsed = 0f;
            float totalDuration = Mathf.Max(_pourDuration, _flipDuration);

            while (elapsed < totalDuration)
            {
                elapsed += Time.deltaTime;

                // Flip animation (ease-in-out) on colbaRead.
                if (elapsed < _flipDuration)
                {
                    float flipT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _flipDuration));
                    _colbaReadTransform.position = Vector3.Lerp(startPos, finalPos, flipT);
                    _colbaReadTransform.rotation = Quaternion.Slerp(startRot, finalRot, flipT);
                }
                else
                {
                    _colbaReadTransform.position = finalPos;
                    _colbaReadTransform.rotation = finalRot;
                }

                // Drain liquid via fillFraction — LiquidWobble pushes it to the shader.
                if (_liquidWobble != null)
                {
                    float drainT = Mathf.Clamp01(elapsed / _pourDuration);
                    _liquidWobble.fillFraction = Mathf.Lerp(liquidStartFill, 0f, drainT);
                }

                yield return null;
            }

            // Ensure final state.
            _colbaReadTransform.position = finalPos;
            _colbaReadTransform.rotation = finalRot;

            if (_liquidWobble != null)
            {
                _liquidWobble.fillFraction = 0f;
                _liquidWobble.enabled = false;
            }
        }

        // Hold the shot briefly so the player sees the empty colba, then hide it.
        yield return new WaitForSeconds(0.5f);

        if (_colbaReadTransform != null)
            _colbaReadTransform.gameObject.SetActive(false);

        // Fade to black before returning camera to the player.
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        _serumPoured = true;
        _pouring = false;

        // Exit puzzle mode and mark the whole puzzle as solved.
        _controller?.ExitPuzzleModeInstant();
        _controller?.SetSolved();

        // Wait one frame so the brain processes the camera return.
        yield return null;

        // Fade in — player regains control.
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        SaveManager.Instance?.Save();
    }

    // ── Ghost Preview ─────────────────────────────────────────────────────────────

    private void UpdateGhostPreview(Vector2 mousePos)
    {
        if (Camera.main == null) return;

        var draggedItem = PuzzleInventoryBar.DraggedItem;
        if (draggedItem == null || !IsCoinItem(draggedItem))
        {
            ClearGhost();
            return;
        }

        var ray = Camera.main.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out var hit, 50f, _eyeLayer, QueryTriggerInteraction.Collide))
        {
            ClearGhost();
            return;
        }

        int eyeIndex = GetEyeIndex(hit.collider);
        if (eyeIndex < 0 || _eyeItems[eyeIndex] != null)
        {
            ClearGhost();
            return;
        }

        if (eyeIndex == _hoveredEye && _ghostPreview != null) return;

        ClearGhost();

        var prefab = draggedItem.inspectionPrefab != null ? draggedItem.inspectionPrefab : _coinPrefab;
        if (prefab == null) return;

        var eyeObj = eyeIndex == 0 ? _eye0Object : _eye1Object;
        if (eyeObj == null) return;

        // Use the actual coin child's world transform (Coin1/Coin2) as reference
        // — the eye parent (coinLeft/CoinRight) has a different rotation.
        Transform coinRef = null;
        for (int i = 0; i < eyeObj.transform.childCount; i++)
        {
            coinRef = eyeObj.transform.GetChild(i);
            break;
        }

        Vector3 ghostPos = coinRef != null ? coinRef.position : eyeObj.transform.position;
        Vector3 ghostLocalScale = coinRef != null ? coinRef.localScale : Vector3.one;

        _ghostPreview = Instantiate(prefab, ghostPos, Quaternion.identity, eyeObj.transform);
        // Match the local rotation of the real coin (Coin1/Coin2) relative to its parent.
        if (coinRef != null)
            _ghostPreview.transform.localRotation = coinRef.localRotation;
        _ghostPreview.transform.localScale = ghostLocalScale;
        foreach (var rend in _ghostPreview.GetComponentsInChildren<Renderer>())
        {
            // Apply rendering layer mask so the ghost receives light from the puzzle's light groups.
            rend.renderingLayerMask = _coinRenderingLayerMask;

            rend.sharedMaterial = new Material(rend.sharedMaterial);
            var mat = rend.material;
            mat.SetFloat(Shader.PropertyToID("_Surface"), 1f);
            mat.SetFloat(Shader.PropertyToID("_Blend"), 0f);
            mat.SetFloat(Shader.PropertyToID("_SrcBlend"), (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat(Shader.PropertyToID("_DstBlend"), (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(Shader.PropertyToID("_ZWrite"), 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (mat.HasProperty(Shader.PropertyToID("_BaseColor")))
            {
                var c = mat.GetColor(Shader.PropertyToID("_BaseColor"));
                c.a = 0.4f;
                mat.SetColor(Shader.PropertyToID("_BaseColor"), c);
            }
        }

        _hoveredEye = eyeIndex;
    }

    /// <summary>
    /// Ghost preview for SerumColba — shown at the colbaRead position when the
    /// player drags SerumColba over the skull after the jaw has opened.
    /// </summary>
    private void UpdateSerumGhostPreview(Vector2 mousePos)
    {
        if (Camera.main == null) return;

        var draggedItem = PuzzleInventoryBar.DraggedItem;
        if (draggedItem == null || draggedItem != _serumColbaItem)
        {
            ClearGhost();
            return;
        }

        // Check if the cursor is over the skull drop collider.
        var ray = Camera.main.ScreenPointToRay(mousePos);
        if (!Physics.Raycast(ray, out var hit, 50f, _scullDropLayer, QueryTriggerInteraction.Collide))
        {
            ClearGhost();
            return;
        }

        if (_scullDropCollider != null && hit.collider != _scullDropCollider)
        {
            ClearGhost();
            return;
        }

        // Already showing ghost — keep it.
        if (_ghostPreview != null) return;

        var prefab = draggedItem.inspectionPrefab != null ? draggedItem.inspectionPrefab : null;
        if (prefab == null || _colbaReadTransform == null) return;

        // Parent to Scull (active) — colbaRead is inactive so parenting to it
        // would make the ghost invisible.
        _ghostPreview = Instantiate(prefab, _colbaReadTransform.position, _colbaReadTransform.rotation, transform);
        _ghostPreview.transform.localScale = _colbaReadTransform.localScale;

        foreach (var rend in _ghostPreview.GetComponentsInChildren<Renderer>(true))
        {
            rend.renderingLayerMask = _coinRenderingLayerMask;
            rend.sharedMaterial = new Material(rend.sharedMaterial);
            var mat = rend.material;
            mat.SetFloat(Shader.PropertyToID("_Surface"), 1f);
            mat.SetFloat(Shader.PropertyToID("_Blend"), 0f);
            mat.SetFloat(Shader.PropertyToID("_SrcBlend"), (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat(Shader.PropertyToID("_DstBlend"), (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(Shader.PropertyToID("_ZWrite"), 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (mat.HasProperty(Shader.PropertyToID("_BaseColor")))
            {
                var c = mat.GetColor(Shader.PropertyToID("_BaseColor"));
                c.a = 0.4f;
                mat.SetColor(Shader.PropertyToID("_BaseColor"), c);
            }
        }

        _hoveredEye = -1;
    }

    private void ClearGhost()
    {
        if (_ghostPreview != null)
            Destroy(_ghostPreview);
        _ghostPreview = null;
        _hoveredEye = -1;
    }

    // ── Retrieval ──────────────────────────────────────────────────────────────────

    private void TryRetrieveFromEye(Vector2 screenPos)
    {
        if (Camera.main == null) return;

        var ray = Camera.main.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out var hit, 50f, _eyeLayer, QueryTriggerInteraction.Collide))
            return;

        int eyeIndex = GetEyeIndex(hit.collider);
        if (eyeIndex < 0 || _eyeItems[eyeIndex] == null) return;

        var inv = InventorySystem.Instance;
        if (inv == null) return;

        inv.ReleaseAllReservations();
        inv.Compact();

        if (inv.IsFull) return;

        var item = _eyeItems[eyeIndex];
        _eyeItems[eyeIndex] = null;
        HideEyeVisual(eyeIndex);

        if (!inv.AddItem(item))
        {
            // Restore if inventory was full between guard and AddItem.
            _eyeItems[eyeIndex] = item;
            ShowEyeVisual(eyeIndex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private bool IsCoinItem(ItemData item)
    {
        if (_coinItems == null) return false;
        foreach (var coin in _coinItems)
            if (coin == item) return true;
        return false;
    }

    private int GetEyeIndex(Collider hitCollider)
    {
        if (_eye0Collider != null && hitCollider == _eye0Collider) return 0;
        if (_eye1Collider != null && hitCollider == _eye1Collider) return 1;
        return -1;
    }

    private bool BothEyesFilled() => _eyeItems[0] != null && _eyeItems[1] != null;

    /// <summary>Activates the eye's coin GameObject and all its children.
    /// Called only when a coin is actually placed into the eye.</summary>
    private void EnsureEyeActive(int index)
    {
        var obj = index == 0 ? _eye0Object : _eye1Object;
        if (obj == null) return;
        for (int i = 0; i < obj.transform.childCount; i++)
            obj.transform.GetChild(i).gameObject.SetActive(true);
    }

    /// <summary>Activates the given Transform's GameObject and all its ancestors
    /// up to (but not including) the specified root.</summary>
    private static void EnsureActiveUpTo(Transform target, Transform root)
    {
        Transform t = target;
        while (t != null && t != root)
        {
            t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    private void ShowEyeVisual(int index)
    {
        var obj = index == 0 ? _eye0Object : _eye1Object;
        if (obj != null)
        {
            EnsureEyeActive(index);
            foreach (var rend in obj.GetComponentsInChildren<Renderer>(true))
            {
                rend.renderingLayerMask = _coinRenderingLayerMask;
                rend.enabled = true;
            }
        }
    }

    private void HideEyeVisual(int index)
    {
        var obj = index == 0 ? _eye0Object : _eye1Object;
        if (obj != null)
        {
            foreach (var rend in obj.GetComponentsInChildren<Renderer>(true))
                rend.enabled = false;
        }
    }

    private void EnableEyeColliders()
    {
        if (_eye0Collider != null) _eye0Collider.enabled = true;
        if (_eye1Collider != null) _eye1Collider.enabled = true;
    }

    private void DisableEyeColliders()
    {
        if (_eye0Collider != null) _eye0Collider.enabled = false;
        if (_eye1Collider != null) _eye1Collider.enabled = false;
    }

    private void EnableScullDropCollider()
    {
        if (_scullDropCollider != null) _scullDropCollider.enabled = true;
    }

    private void DisableScullDropCollider()
    {
        if (_scullDropCollider != null) _scullDropCollider.enabled = false;
    }

    private static IEnumerator WaitForAnimationFinish(Animator animator, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
        {
            yield return new WaitForSeconds(1f);
            yield break;
        }

        yield return null;

        while (!animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
            yield return null;

        while (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;
    }

    private static void PlaySFX(AudioClip clip, float volume)
    {
        if (clip != null)
            AudioManager.Instance?.PlaySFX(clip, volume);
    }

    // ── Save / Restore ────────────────────────────────────────────────────────────

    private void ApplyPendingLoad()
    {
        if (_pendingLoad == null) return;

        var data = _pendingLoad.Value;
        _pendingLoad = null;

        if (data.serumPoured)
        {
            _activated = true;
            _jawOpen = true;
            _serumPoured = true;

            // Restore both eyes as filled.
            RestoreEyeCoins(data.placedEyeItemIds);
            ShowEyeVisual(0);
            ShowEyeVisual(1);

            // Restore colba — snap colbaRead to final flipped pose from prefab.
            if (_colbaReadTransform != null)
            {
                EnsureActiveUpTo(_colbaReadTransform, transform);
                _colbaReadTransform.gameObject.SetActive(true);
            }

            // Colba was poured out and hidden — keep it hidden.
            if (_liquidWobble != null)
            {
                _liquidWobble.fillFraction = 0f;
                _liquidWobble.enabled = false;
            }

            // Restore animator to end of ScullOpen.
            if (_scullAnimator != null)
                _scullAnimator.Play(_scullOpenStateName, 0, 1f);

            DisableEyeColliders();
            return;
        }

        if (data.jawOpen)
        {
            _activated = true;
            _jawOpen = true;

            RestoreEyeCoins(data.placedEyeItemIds);
            ShowEyeVisual(0);
            ShowEyeVisual(1);

            if (_scullAnimator != null)
                _scullAnimator.Play(_scullOpenStateName, 0, 1f);

            DisableEyeColliders();
            return;
        }

        if (data.activated)
        {
            _activated = true;
            RestoreEyeCoins(data.placedEyeItemIds);
            return;
        }
    }

    private void RestoreEyeCoins(string[] ids)
    {
        if (ids == null) return;

        for (int i = 0; i < 2 && i < ids.Length; i++)
        {
            var id = ids[i];
            if (string.IsNullOrEmpty(id)) continue;

            var item = FindItemById(id);
            if (item != null)
            {
                _eyeItems[i] = item;
                ShowEyeVisual(i);
            }
        }
    }

    private ItemData FindItemById(string id)
    {
        // Check coin items first.
        if (_coinItems != null)
            foreach (var item in _coinItems)
                if (item != null && item.ItemId == id) return item;

        if (_serumColbaItem != null && _serumColbaItem.ItemId == id)
            return _serumColbaItem;

        // Fall back to inventory.
        if (InventorySystem.Instance != null)
            return InventorySystem.Instance.FindItemByIdPublic(id);

        return null;
    }
}
