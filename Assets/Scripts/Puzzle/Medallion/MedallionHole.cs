using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Represents one hole in the medallion box.
/// Accepts any medallion — correct order is validated externally by MedallionBoxUI.
/// Supports retrieval: the placed medallion can be taken back out.
///
/// <para><b>Hover highlight:</b> when a coin is placed, its <c>Renderer</c> is cached and the
/// <c>_EMISSION</c> keyword is enabled on a per-instance material copy. <see cref="Highlight"/>
/// then drives <c>_EmissionColor</c> via <see cref="MaterialPropertyBlock"/> (zero GC).
/// MedallionBoxUI calls <see cref="Highlight"/> every frame based on cursor raycast results.</para>
/// </summary>
public class MedallionHole : MonoBehaviour
{
    private static readonly Quaternion CoinRotation = Quaternion.Euler(0f, -90f, 0f);

    [Header("Coin Animation")]
    [Tooltip("Optional material override for the spawned coin. Leave null to use prefab default.")]
    [SerializeField] private Material _coinMaterial;

    [Header("Hover Highlight")]
    [Tooltip("HDR emission colour applied to the coin when the cursor hovers over it.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color _highlightEmission = new Color(0.55f, 0.42f, 0.08f);

    /// <summary>Raised when a medallion is successfully placed into this hole.</summary>
    public event System.Action OnFilled;

    /// <summary>Raised when a medallion is retrieved from this hole.</summary>
    public event System.Action OnRetrieved;

    /// <summary>The medallion currently placed in this hole, or null if empty.</summary>
    public ItemData PlacedItem { get; private set; }

    /// <summary>True when a medallion is sitting in this hole.</summary>
    public bool IsFilled => PlacedItem != null;

    private GameObject _placedCoin;
    private Renderer _placedRenderer;
    private MaterialPropertyBlock _propBlock;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="item"/> into this hole and plays a drop animation.
    /// Uses <paramref name="item"/>.inspectionPrefab if assigned, otherwise falls back to <paramref name="fallbackPrefab"/>.
    /// </summary>
    public void Fill(ItemData item, GameObject fallbackPrefab, float dropHeight, float dropDuration)
    {
        if (IsFilled || item == null) return;
        var prefab = item.inspectionPrefab != null ? item.inspectionPrefab : fallbackPrefab;
        if (prefab == null) return;

        PlacedItem = item;
        StartCoroutine(DropRoutine(prefab, dropHeight, dropDuration));
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

        var coin = Instantiate(prefab, transform.position, Quaternion.identity, transform);
        StripInteractableComponents(coin);
        
        // The coin prefab's localScale is tuned for world placement (as a pickable item).
        // Inside the hole, we need scale (1,1,1) so the mesh fills the hole properly.
        coin.transform.localScale = Vector3.one;
        // Flip 180° on X so the symbol face points up.
        coin.transform.localRotation = CoinRotation;

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
    /// can restore it to the inventory. The coin GameObject plays a rise animation and is
    /// destroyed at the top — mirroring the drop animation in reverse.
    /// </summary>
    public ItemData Retrieve(float riseHeight, float riseDuration)
    {
        if (!IsFilled) return null;

        var item = PlacedItem;
        PlacedItem = null;
        Highlight(false);
        _placedRenderer = null;

        if (_placedCoin != null)
            StartCoroutine(RiseRoutine(_placedCoin, riseHeight, riseDuration));

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
    /// Caches the renderer of a freshly instantiated coin and enables the emission keyword
    /// on its material instance so <see cref="Highlight"/> can drive emission via property block.
    /// </summary>
    private void CacheRenderer(GameObject coin)
    {
        _placedRenderer = coin.GetComponentInChildren<Renderer>();
        if (_placedRenderer == null) return;

        // Create a per-instance material so enabling emission does not affect the shared asset.
        // EnableKeyword is required for URP/Lit to evaluate _EmissionColor at runtime.
        _placedRenderer.material.EnableKeyword("_EMISSION");
    }

    private IEnumerator DropRoutine(GameObject prefab, float dropHeight, float dropDuration)
    {
        Vector3 endPos   = transform.position;
        Vector3 startPos = endPos + Vector3.up * dropHeight;

        var coin = Instantiate(prefab, startPos, Quaternion.identity, transform);
        StripInteractableComponents(coin);

        // The coin prefab's localScale is tuned for world placement (as a pickable item).
        // Inside the hole, we need scale (1,1,1) so the mesh fills the hole properly.
        coin.transform.localScale = Vector3.one;
        // Flip 180° on X so the symbol face points up.
        coin.transform.localRotation = CoinRotation;

        _placedCoin = coin;
        CacheRenderer(coin);

        if (_coinMaterial != null)
        {
            var rend = coin.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.sharedMaterial = _coinMaterial;
        }

        float elapsed = 0f;
        while (elapsed < dropDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dropDuration);
            coin.transform.position = Vector3.Lerp(startPos, endPos, t * t); // ease-in
            yield return null;
        }

        coin.transform.position = endPos;
    }

    private IEnumerator RiseRoutine(GameObject coin, float riseHeight, float riseDuration)
    {
        Vector3 startPos = coin.transform.position;
        Vector3 endPos   = startPos + Vector3.up * riseHeight;

        float elapsed = 0f;
        while (elapsed < riseDuration)
        {
            elapsed += Time.deltaTime;
            float t      = Mathf.Clamp01(elapsed / riseDuration);
            float eased  = t * (2f - t); // ease-out: fast start, slow finish
            coin.transform.position = Vector3.Lerp(startPos, endPos, eased);
            yield return null;
        }

        coin.transform.position = endPos;
        Destroy(coin);
        _placedCoin = null;
    }
}
