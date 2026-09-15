using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Controls the rune pouch icon visibility and hover tooltip.
/// The pouch appears in the inventory panel once at least one rune has been collected.
/// Hovering shows collected / remaining rune counts via <see cref="ItemTooltip"/>.
/// When all runes are collected, the icon swaps to the master emblem and the tooltip reads "Мастер рун".
/// </summary>
public class RunePouchUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public static RunePouchUI Instance { get; private set; }

    private const string PouchTooltipTitle = "Мешочек с рунами";
    private const string MasterTooltipTitle = "Мастер рун";

    [Header("Icons")]
    [Tooltip("Sprite shown while runes are still being collected.")]
    [SerializeField] private Sprite pouchIcon;
    [Tooltip("Sprite shown when all runes have been collected.")]
    [SerializeField] private Sprite masterIcon;

    private RectTransform _rectTransform;
    private Image _image;
    private bool _allRunesCollected;

    private void Awake()
    {
        Instance = this;
        _rectTransform = GetComponent<RectTransform>();
        _image = GetComponent<Image>();

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

    /// <summary>Shows the pouch when at least one rune has been collected, swaps icon when all are collected.</summary>
    private void UpdateVisibility()
    {
        if (InventorySystem.Instance == null) return;

        int collected = InventorySystem.Instance.CollectedRuneCount;
        int total = InventorySystem.Instance.TotalRuneCount;

        if (collected <= 0)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        _allRunesCollected = total > 0 && collected >= total;

        if (_image != null)
            _image.sprite = _allRunesCollected && masterIcon != null ? masterIcon : pouchIcon;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (InventorySystem.Instance == null) return;

        string title;
        string description;

        if (_allRunesCollected)
        {
            title = MasterTooltipTitle;
            description = InventorySystem.Instance.CollectedRuneCount.ToString();
        }
        else
        {
            title = PouchTooltipTitle;
            int collected = InventorySystem.Instance.CollectedRuneCount;
            int total = InventorySystem.Instance.TotalRuneCount;
            description = $"Собрано: {collected} / {total}";
        }

        ItemTooltip.Instance?.Show(title, description, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemTooltip.Instance?.Hide();
    }
}
