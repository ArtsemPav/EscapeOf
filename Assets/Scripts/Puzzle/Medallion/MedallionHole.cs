using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Represents one hole in the medallion box.
/// Accepts any medallion — correct order is validated externally by MedallionBoxUI.
/// Supports retrieval: the placed medallion can be taken back out.
///
/// <para><b>Hover highlight:</b> when a coin is placed, its <c>Renderer</c> is cached and the
/// <c>_EMISSION</c> keyword is enabled on a per-instance material copy. <see cref="Highlight"/>
/// then drives <c>_EmissionColor</c> via <see cref="MaterialPropertyBlock"/> (zero GC).
/// MedallionBoxUI calls <see cref="Highlight"/> every frame based on cursor raycast results.</para>
///
/// <para><b>Ghost preview:</b> while the player drags a medallion icon over an empty hole,
/// <see cref="ShowGhost"/> instantiates a semi-transparent 3D preview of the coin at the
/// hole's final position. <see cref="HideGhost"/> removes it. The preview is replaced by
/// the real drop animation when <see cref="Fill"/> is called.</para>
/// </summary>
public class MedallionHole : MonoBehaviour
{
    // URP/Lit shader property IDs for runtime transparency.
    private static readonly int SurfacePropId       = Shader.PropertyToID("_Surface");
    private static readonly int BlendPropId         = Shader.PropertyToID("_Blend");
    private static readonly int SrcBlendPropId      = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendPropId      = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWritePropId        = Shader.PropertyToID("_ZWrite");
    private static readonly int AlphaClipPropId     = Shader.PropertyToID("_AlphaClip");
    private static readonly int BaseColorPropId     = Shader.PropertyToID("_BaseColor");
    private static readonly int SurfaceTransparentKw = Shader.PropertyToID("_SURFACE_TYPE_TRANSPARENT");

    [Header("Coin Animation")]
    [Tooltip("Optional material override for the spawned coin. Leave null to use prefab default.")]
    [SerializeField] private Material _coinMaterial;

    [Header("Lighting")]
    [Tooltip("URP rendering layer mask applied to the spawned coin so it receives light " +
             "from the puzzle's light groups. Default includes 'CoridorFDoor' (bit 22) " +
             "and 'flashLight' (bit 9).")]
    [SerializeField] private uint _coinRenderingLayerMask = (1u << 22) | (1u << 9);

    [Header("Hover Highlight")]
    [Tooltip("HDR emission colour applied to the coin when the cursor hovers over it.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color _highlightEmission = new Color(0.55f, 0.42f, 0.08f);

    [Header("Ghost Preview")]
    [Tooltip("Material used for the 3D ghost preview when dragging a medallion over this hole. " +
             "If null, the coin material is made semi-transparent at runtime (requires URP/Lit).")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Alpha (transparency) of the ghost preview when no explicit _ghostMaterial is assigned.")]
    [SerializeField, Range(0.1f, 0.9f)] private float _ghostAlpha = 0.45f;

    [Tooltip("Duration of the ghost fade-in/out animation (seconds).")]
    [SerializeField, Min(0.05f)] private float _ghostFadeDuration = 0.2f;

    [Header("Coin Placement")]
    [Tooltip("If true, the coin is positioned at the world-space center of the hole's collider. " +
             "If false, uses the hole's transform.position (legacy behaviour).")]
    [SerializeField] private bool _useColliderCenter = true;

    [Tooltip("Additional position offset applied to the coin in local space (relative to the hole). " +
             "Use for fine-tuning when the collider center doesn't match the visual slot.")]
    [SerializeField] private Vector3 _coinPositionOffset = Vector3.zero;

    [Tooltip("Rotation offset applied to the coin in local space (Euler degrees). " +
             "The final rotation is: holeTransform.rotation * Quaternion.Euler(this offset). " +
             "Default (0, -90, 180) flips the coin face-up and keeps the symbol right-side up.")]
    [SerializeField] private Vector3 _coinRotationOffset = new Vector3(0f, -90f, 180f);

    [Header("Insert Animation")]
    [Tooltip("How far above the hole the coin starts its insert animation (metres).")]
    [SerializeField, Min(0.01f)] private float _insertHeight = 0.3f;

    [Tooltip("Duration of the insert animation (seconds).")]
    [SerializeField, Min(0.05f)] private float _insertDuration = 0.4f;

    [Header("Retrieve Animation")]
    [Tooltip("How far above the hole the coin rises during retrieval before fading out (metres).")]
    [SerializeField, Min(0.01f)] private float _retrieveHeight = 0.25f;

    [Tooltip("Duration of the retrieve animation (seconds).")]
    [SerializeField, Min(0.05f)] private float _retrieveDuration = 0.4f;

    /// <summary>Raised when a medallion is successfully placed into this hole.</summary>
    public event System.Action OnFilled;

    /// <summary>Raised when a medallion is retrieved from this hole.</summary>
    public event System.Action OnRetrieved;

    /// <summary>The medallion currently placed in this hole, or null if empty.</summary>
    public ItemData PlacedItem { get; private set; }

    /// <summary>True when a medallion is sitting in this hole.</summary>
    public bool IsFilled => PlacedItem != null;

    private Collider _holeCollider;
    private bool _colliderInitialized;

    // ── Collider Management ───────────────────────────────────────────────────

    /// <summary>Enables or disables this hole's collider so it doesn't block
    /// the puzzle's main interaction collider while the player is outside.</summary>
    public void SetColliderEnabled(bool enabled)
    {
        if (!_colliderInitialized)
        {
            _holeCollider = GetComponent<Collider>();
            _colliderInitialized = true;
        }
        if (_holeCollider != null)
            _holeCollider.enabled = enabled;
    }

    // ── Private Fields ────────────────────────────────────────────────────────

    private GameObject _placedCoin;
    private GameObject _ghostCoin;
    private Renderer _ghostRenderer;
    private Coroutine _ghostFadeRoutine;
    private Renderer _placedRenderer;
    private MaterialPropertyBlock _propBlock;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="item"/> into this hole and plays an insert animation.
    /// Uses <paramref name="item"/>.inspectionPrefab if assigned, otherwise falls back to <paramref name="fallbackPrefab"/>.
    /// Clears any active ghost preview before placing.
    /// </summary>
    public void Fill(ItemData item, GameObject fallbackPrefab, float dropHeight, float dropDuration)
    {
        if (IsFilled || item == null) return;
        HideGhost();
        var prefab = item.inspectionPrefab != null ? item.inspectionPrefab : fallbackPrefab;
        if (prefab == null) return;

        PlacedItem = item;
        StartCoroutine(InsertRoutine(prefab, dropHeight, dropDuration));
        OnFilled?.Invoke();
    }

    /// <summary>
    /// Places <paramref name="item"/> immediately without animation.
    /// Uses <paramref name="item"/>.inspectionPrefab if assigned, otherwise falls back to <paramref name="fallbackPrefab"/>.
    /// Used when restoring puzzle state on load.
    /// </summary>
    public void FillImmediate(ItemData item, GameObject fallbackPrefab)
    {
        if (IsFilled || item == null) return;
        var prefab = item.inspectionPrefab != null ? item.inspectionPrefab : fallbackPrefab;
        if (prefab == null) return;

        PlacedItem = item;

        var coin = Instantiate(prefab, GetCoinWorldPosition(), GetCoinWorldRotation(), transform);
        StripInteractableComponents(coin);

        coin.transform.localScale = Vector3.one;

        _placedCoin = coin;
        CacheRenderer(coin);

        if (_coinMaterial != null)
        {
            var rend = coin.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.sharedMaterial = _coinMaterial;
        }
    }

    /// <summary>
    /// Removes the medallion from this hole and returns the item immediately so the caller
    /// can restore it to the inventory. The coin GameObject plays a retrieve animation:
    /// rises from the hole with a spin and fades out at the top.
    /// </summary>
    public ItemData Retrieve(float riseHeight, float riseDuration)
    {
        if (!IsFilled) return null;

        var item = PlacedItem;
        PlacedItem = null;
        Highlight(false);
        _placedRenderer = null;

        if (_placedCoin != null)
            StartCoroutine(RetrieveRoutine(_placedCoin, riseHeight, riseDuration));

        OnRetrieved?.Invoke();
        return item;
    }

    /// <summary>
    /// Applies or removes a hover highlight on the placed coin using emission.
    /// Has no effect when the hole is empty.
    /// </summary>
    public void Highlight(bool on)
    {
        if (_placedRenderer == null) return;

        _propBlock ??= new MaterialPropertyBlock();
        _placedRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor("_EmissionColor", on ? _highlightEmission : Color.black);
        _placedRenderer.SetPropertyBlock(_propBlock);
    }

    // ── Ghost Preview ──────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a semi-transparent 3D preview of <paramref name="item"/> at this hole's
    /// final position. Called every frame by the UI while the player drags a medallion
    /// icon over an empty hole. If a ghost is already showing on this hole, does nothing.
    /// </summary>
    public void ShowGhost(ItemData item, GameObject fallbackPrefab)
    {
        if (IsFilled || item == null) return;
        if (_ghostCoin != null) return;

        var prefab = item.inspectionPrefab != null ? item.inspectionPrefab : fallbackPrefab;
        if (prefab == null) return;

        _ghostCoin = Instantiate(prefab, GetCoinWorldPosition(), GetCoinWorldRotation(), transform);
        StripInteractableComponents(_ghostCoin);
        _ghostCoin.transform.localScale = Vector3.one;

        ApplyRenderingLayers(_ghostCoin);
        ApplyGhostAppearance(_ghostCoin);
        _ghostRenderer = _ghostCoin.GetComponentInChildren<Renderer>();

        if (_ghostFadeRoutine != null) StopCoroutine(_ghostFadeRoutine);
        _ghostFadeRoutine = StartCoroutine(GhostFadeRoutine(0f, _ghostAlpha, _ghostFadeDuration));
    }

    /// <summary>Immediately removes and destroys the ghost preview.</summary>
    public void HideGhost()
    {
        if (_ghostFadeRoutine != null)
        {
            StopCoroutine(_ghostFadeRoutine);
            _ghostFadeRoutine = null;
        }
        if (_ghostCoin != null) Destroy(_ghostCoin);
        _ghostCoin = null;
        _ghostRenderer = null;
    }

    private IEnumerator GhostFadeRoutine(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetGhostAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetGhostAlpha(to);
        _ghostFadeRoutine = null;
    }

    private void SetGhostAlpha(float alpha)
    {
        if (_ghostCoin == null) return;
        foreach (var rend in _ghostCoin.GetComponentsInChildren<Renderer>())
        {
            var mat = rend.material;
            if (mat.HasProperty(BaseColorPropId))
            {
                var c = mat.GetColor(BaseColorPropId);
                c.a = alpha;
                mat.SetColor(BaseColorPropId, c);
            }
        }
    }

    /// <summary>
    /// Makes the ghost coin semi-transparent. If <see cref="_ghostMaterial"/> is assigned,
    /// uses it directly. Otherwise switches the URP/Lit material to Transparent surface
    /// mode at runtime and reduces <c>_BaseColor</c> alpha to <see cref="_ghostAlpha"/>.
    /// </summary>
    private void ApplyGhostAppearance(GameObject coin)
    {
        var renderers = coin.GetComponentsInChildren<Renderer>();

        if (_ghostMaterial != null)
        {
            foreach (var rend in renderers)
                rend.sharedMaterial = _ghostMaterial;
            return;
        }

        // Runtime transparency for URP/Lit materials.
        foreach (var rend in renderers)
        {
            // material getter creates a per-instance copy we can modify safely.
            var mat = rend.material;

            mat.SetFloat(SurfacePropId, 1f);
            mat.SetFloat(BlendPropId, 0f);
            mat.SetFloat(SrcBlendPropId, (float)BlendMode.SrcAlpha);
            mat.SetFloat(DstBlendPropId, (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat(ZWritePropId, 0f);
            mat.SetFloat(AlphaClipPropId, 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (mat.HasProperty(BaseColorPropId))
            {
                var color = mat.GetColor(BaseColorPropId);
                color.a = 0f;
                mat.SetColor(BaseColorPropId, color);
            }

            rend.material = mat;
        }
    }

    // ── Coin Placement Helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the world-space position where the coin should be placed.
    /// Uses the collider's world center (if _useColliderCenter is true and a collider exists),
    /// otherwise falls back to the hole's transform.position. Adds _coinPositionOffset.
    /// </summary>
    private Vector3 GetCoinWorldPosition()
    {
        Vector3 basePos = transform.position;

        if (_useColliderCenter)
        {
            var col = GetComponent<Collider>();
            if (col != null)
            {
                // bounds.center returns (0,0,0) when the collider is disabled,
                // so use the local center property via TransformPoint instead.
                if (col is SphereCollider sphere)
                    basePos = transform.TransformPoint(sphere.center);
                else if (col is BoxCollider box)
                    basePos = transform.TransformPoint(box.center);
                else if (col is CapsuleCollider cap)
                    basePos = transform.TransformPoint(cap.center);
                else if (col.enabled)
                    basePos = col.bounds.center;
                // last resort: transform.position (already assigned)
            }
        }

        return basePos + transform.TransformDirection(_coinPositionOffset);
    }

    /// <summary>
    /// Returns the world-space rotation for the coin.
    /// Combines the hole's own rotation with the configurable _coinRotationOffset.
    /// This ensures the coin's symbol face points "up" relative to the hole's surface,
    /// not just world-up.
    /// </summary>
    private Quaternion GetCoinWorldRotation()
    {
        return transform.rotation * Quaternion.Euler(_coinRotationOffset);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips all interactable/pickable components and colliders from the instantiated coin
    /// so FPSController cannot detect it as an interactive world object.
    /// Uses DestroyImmediate for ISaveable components to prevent them from overwriting
    /// the original world object's save state in SaveManager.
    /// </summary>
    private static void StripInteractableComponents(GameObject coin)
    {
        // Remove ISaveable components immediately to prevent SaveManager registry overwrites.
        var saveables = coin.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(mb => mb is ISaveable)
            .ToArray();

        foreach (var mb in saveables)
            DestroyImmediate(mb);

        // Remove remaining IInteractable components (deferred Destroy is fine here).
        var interactables = coin.GetComponentsInChildren<MonoBehaviour>(true)
            .Where(mb => mb is IInteractable)
            .ToArray();

        foreach (var mb in interactables)
            Destroy(mb);

        // Remove colliders so the coin cannot be hit by FPSController raycasts.
        var colliders = coin.GetComponentsInChildren<Collider>(true);
        foreach (var col in colliders)
            Destroy(col);

        // Set layer to Default for the root and all children.
        SetLayerRecursively(coin, 0);
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
    }

    /// <summary>
    /// Applies the rendering-layer mask to every Renderer on the spawned coin so it
    /// receives light from the same URP light groups as the surrounding geometry.
    /// Auto-detects the mask from a nearby Renderer in the parent hierarchy (the box
    /// mesh) so coins always match the room's lighting — falls back to
    /// <see cref="_coinRenderingLayerMask"/> when no reference renderer is found.
    /// </summary>
    private void ApplyRenderingLayers(GameObject coin)
    {
        uint mask = ResolveRenderingLayerMask();
        foreach (var rend in coin.GetComponentsInChildren<Renderer>(true))
            rend.renderingLayerMask = mask;
    }

    /// <summary>
    /// Returns the rendering-layer mask to use for spawned coins. Walks up the
    /// hierarchy to find the enclosing <see cref="RoomController"/>, then ORs the
    /// rendering-layer masks of every <see cref="Light"/> in that room together
    /// with the serialized <see cref="_coinRenderingLayerMask"/> (which carries
    /// the flashlight bit). This ensures coins are lit by the same light groups
    /// as the room, regardless of which rendering layers the room uses.
    /// </summary>
    private uint ResolveRenderingLayerMask()
    {
        // Start with the inspector mask so the flashlight bit is always included.
        uint mask = _coinRenderingLayerMask;

        // Walk up to find the room that contains this hole.
        var roomController = GetComponentInParent<RoomController>();
        if (roomController != null)
        {
            var lights = roomController.GetComponentsInChildren<Light>(true);
            foreach (var light in lights)
            {
                if (light == null) continue;
                mask |= (uint)light.renderingLayerMask;
            }
        }

        // Always include Default so the coin is visible in default lighting.
        mask |= 1u;

        return mask;
    }

    /// <summary>
    /// Caches the renderer of a freshly instantiated coin and enables the emission keyword
    /// on its material instance so <see cref="Highlight"/> can drive emission via property block.
    /// </summary>
    private void CacheRenderer(GameObject coin)
    {
        ApplyRenderingLayers(coin);

        _placedRenderer = coin.GetComponentInChildren<Renderer>();
        if (_placedRenderer == null) return;

        // Create a per-instance material so enabling emission does not affect the shared asset.
        // EnableKeyword is required for URP/Lit to evaluate _EmissionColor at runtime.
        _placedRenderer.material.EnableKeyword("_EMISSION");

        // Immediately suppress emission so the coin doesn't glow with the
        // material's baked _EmissionColor until Highlight(true) is called.
        _propBlock ??= new MaterialPropertyBlock();
        _placedRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor("_EmissionColor", Color.black);
        _placedRenderer.SetPropertyBlock(_propBlock);
    }

    /// <summary>
    /// Insert animation: coin flies in from above the hole and settles
    /// into the final position with an ease-in curve. No rotation.
    /// </summary>
    private IEnumerator InsertRoutine(GameObject prefab, float fallbackHeight, float fallbackDuration)
    {
        Vector3 endPos   = GetCoinWorldPosition();
        float height     = _insertHeight > 0.01f ? _insertHeight : fallbackHeight;
        float duration   = _insertDuration > 0.05f ? _insertDuration : fallbackDuration;
        Vector3 startPos = endPos + transform.up * height;

        Quaternion finalRot = GetCoinWorldRotation();

        var coin = Instantiate(prefab, startPos, finalRot, transform);
        StripInteractableComponents(coin);
        coin.transform.localScale = Vector3.one;

        _placedCoin = coin;
        CacheRenderer(coin);

        if (_coinMaterial != null)
        {
            var rend = coin.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.sharedMaterial = _coinMaterial;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t; // ease-in: slow start, fast finish
            coin.transform.position = Vector3.Lerp(startPos, endPos, eased);
            yield return null;
        }

        coin.transform.position = endPos;
    }

    /// <summary>
    /// Retrieve animation: coin rises from the hole and fades out at the top,
    /// then is destroyed. Ease-out for position, fade-out in the last 40%.
    /// </summary>
    private IEnumerator RetrieveRoutine(GameObject coin, float fallbackHeight, float fallbackDuration)
    {
        Vector3 startPos = coin.transform.position;
        float height     = _retrieveHeight > 0.01f ? _retrieveHeight : fallbackHeight;
        float duration   = _retrieveDuration > 0.05f ? _retrieveDuration : fallbackDuration;
        Vector3 endPos   = startPos + transform.up * height;

        // Cache renderers for fade-out.
        var renderers = coin.GetComponentsInChildren<Renderer>();
        var originalAlphas = new float[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            var mat = renderers[i].material;
            if (mat.HasProperty(BaseColorPropId))
            {
                originalAlphas[i] = new[] { mat.GetColor(BaseColorPropId).a };
                mat.SetFloat(SurfacePropId, 1f);
                mat.SetFloat(BlendPropId, 0f);
                mat.SetFloat(SrcBlendPropId, (float)BlendMode.SrcAlpha);
                mat.SetFloat(DstBlendPropId, (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat(ZWritePropId, 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            else
            {
                originalAlphas[i] = null;
            }
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * (2f - t); // ease-out

            coin.transform.position = Vector3.Lerp(startPos, endPos, eased);

            // Fade out in the last 40% of the animation.
            if (t > 0.6f)
            {
                float fadeT = Mathf.Clamp01((t - 0.6f) / 0.4f);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (originalAlphas[i] == null) continue;
                    var mat = renderers[i].material;
                    var c = mat.GetColor(BaseColorPropId);
                    c.a = Mathf.Lerp(originalAlphas[i][0], 0f, fadeT);
                    mat.SetColor(BaseColorPropId, c);
                }
            }

            yield return null;
        }

        coin.transform.position = endPos;
        Destroy(coin);
        _placedCoin = null;
    }
}
