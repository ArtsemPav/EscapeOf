using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton tooltip panel shown when hovering over a filled inventory slot.
/// Positions itself below the hovered slot and clamps inside canvas bounds.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    public static ItemTooltip Instance { get; private set; }

    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private float panelWidth = 260f;
    [SerializeField] private float slotGap = 6f;

    private RectTransform _canvasRect;
    private Camera _uiCamera;

    private void Awake()
    {
        Instance = this;

        Canvas rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        _canvasRect = rootCanvas.GetComponent<RectTransform>();
        _uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;

        // Pivot at top-center so the panel extends downward from the slot's bottom edge
        panel.pivot = new Vector2(0.5f, 1f);
        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);

        panel.gameObject.SetActive(false);
    }

    /// <summary>Shows the tooltip for the given item, anchored below slotRect.</summary>
    public void Show(ItemData item, RectTransform slotRect)
    {
        if (item == null) return;

        itemNameText.text = item.itemName;
        descriptionText.text = item.description;
        panel.gameObject.SetActive(true);

        PositionBelowSlot(slotRect);
    }

    /// <summary>Hides the tooltip.</summary>
    public void Hide()
    {
        panel.gameObject.SetActive(false);
    }

    private void PositionBelowSlot(RectTransform slotRect)
    {
        // [0]=BL [1]=TL [2]=TR [3]=BR
        Vector3[] corners = new Vector3[4];
        slotRect.GetWorldCorners(corners);

        Vector2 bottomCenter = ((Vector2)corners[0] + (Vector2)corners[3]) * 0.5f;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect,
            bottomCenter,
            _uiCamera,
            out Vector2 localPoint
        );

        panel.localPosition = new Vector2(localPoint.x, localPoint.y - slotGap);

        // Force layout so panel.rect.height is accurate before clamping
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
        ClampInsideCanvas();
    }

    private void ClampInsideCanvas()
    {
        Vector2 canvasHalf = _canvasRect.rect.size * 0.5f;
        Vector2 panelSz = panel.rect.size;
        Vector2 pos = panel.localPosition;

        // Horizontal: pivot is center, clamp both sides
        pos.x = Mathf.Clamp(pos.x, -canvasHalf.x + panelSz.x * 0.5f, canvasHalf.x - panelSz.x * 0.5f);
        // Vertical: pivot is top, panel extends downward
        pos.y = Mathf.Clamp(pos.y, -canvasHalf.y + panelSz.y, canvasHalf.y);

        panel.localPosition = pos;
    }
}
