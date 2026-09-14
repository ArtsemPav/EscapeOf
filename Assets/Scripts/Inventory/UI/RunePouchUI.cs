using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Controls the rune pouch icon visibility and hover tooltip.
/// The pouch appears in the inventory panel once at least one rune has been collected.
/// Hovering shows collected / remaining rune counts via <see cref="ItemTooltip"/>.
/// </summary>
public class RunePouchUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static RunePouchUI Instance { get; private set; }

    private const string TooltipTitle = "Мешочек с рунами";

    private RectTransform _rectTransform;

    private void Awake()
    {
        Instance = this;
        _rectTransform = GetComponent<RectTransform>();

        if (InventorySystem.Instance == null)
        {
            Debug.LogError("[RunePouchUI] InventorySystem не найден в сцене!", this);
            return;
        }

        InventorySystem.Instance.OnRunesChanged += UpdateVisibility;
        InventorySystem.Instance.OnInventoryChanged += UpdateVisibility;
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.OnRunesChanged -= UpdateVisibility;
            InventorySystem.Instance.OnInventoryChanged -= UpdateVisibility;
        }
    }

    /// <summary>Shows the pouch when at least one rune has been collected, hides otherwise.</summary>
    private void UpdateVisibility()
    {
        if (InventorySystem.Instance == null) return;
        gameObject.SetActive(InventorySystem.Instance.CollectedRuneCount > 0);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (InventorySystem.Instance == null) return;

        int collected = InventorySystem.Instance.CollectedRuneCount;
        int total = InventorySystem.Instance.TotalRuneCount;
        int remaining = InventorySystem.Instance.RemainingRuneCount;

        string description = $"Собрано: {collected} / {total}";
        ItemTooltip.Instance?.Show(TooltipTitle, description, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }
}
