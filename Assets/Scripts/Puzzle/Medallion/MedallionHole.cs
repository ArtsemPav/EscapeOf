using System.Collections;
using UnityEngine;

/// <summary>
/// Represents one hole in the medallion box.
/// Accepts any medallion — correct order is validated externally by MedallionBoxUI.
/// Supports retrieval: the placed medallion can be taken back out.
/// </summary>
public class MedallionHole : MonoBehaviour
{
    [Header("Coin Animation")]
    [Tooltip("Optional material override for the spawned coin. Leave null to use prefab default.")]
    [SerializeField] private Material _coinMaterial;

    /// <summary>The medallion currently placed in this hole, or null if empty.</summary>
    public ItemData PlacedItem { get; private set; }

    /// <summary>True when a medallion is sitting in this hole.</summary>
    public bool IsFilled => PlacedItem != null;

    private GameObject _placedCoin;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Places <paramref name="item"/> into this hole and plays a drop animation.
    /// <paramref name="dropHeight"/> and <paramref name="dropDuration"/> come from MedallionBoxUI.
    /// </summary>
    public void Fill(ItemData item, GameObject coinPrefab, float dropHeight, float dropDuration)
    {
        if (IsFilled || coinPrefab == null || item == null) return;
        PlacedItem = item;
        StartCoroutine(DropRoutine(coinPrefab, dropHeight, dropDuration));
    }

    /// <summary>
    /// Places <paramref name="item"/> immediately without animation.
    /// Used when restoring puzzle state on load.
    /// </summary>
    public void FillImmediate(ItemData item, GameObject coinPrefab)
    {
        if (IsFilled || coinPrefab == null || item == null) return;
        PlacedItem = item;

        var coin = Instantiate(coinPrefab, transform.position, transform.rotation, transform);
        _placedCoin = coin;

        if (_coinMaterial != null)
        {
            var rend = coin.GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.sharedMaterial = _coinMaterial;
        }
    }

    /// <summary>
    /// Removes the medallion from this hole, destroys the coin GameObject,
    /// and returns the item so the caller can restore it to the inventory and UI.
    /// </summary>
    public ItemData Retrieve()
    {
        if (!IsFilled) return null;

        var item = PlacedItem;
        PlacedItem = null;

        if (_placedCoin != null)
        {
            Destroy(_placedCoin);
            _placedCoin = null;
        }

        return item;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private IEnumerator DropRoutine(GameObject prefab, float dropHeight, float dropDuration)
    {
        Vector3 endPos = transform.position;
        Vector3 startPos = endPos + Vector3.up * dropHeight;

        var coin = Instantiate(prefab, startPos, transform.rotation, transform);
        _placedCoin = coin;

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
}
