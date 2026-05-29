using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton tooltip panel shown when hovering over a filled inventory slot.
/// Positions itself above the hovered slot when there is room, otherwise below.
/// Clamps inside canvas bounds in both cases.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    public static ItemTooltip Instance { get; private set; }

    [SerializeField] private RectTransform panel;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private float panelWidth = 260f;
    [SerializeField] private float slotGap = 6f;

    /// <summary>
    /// Minimum canvas-space Y for the tooltip's bottom edge.
    /// Keeps the tooltip above the puzzle inventory bar (≈ -290 puts it ~250 px from screen bottom).
    /// </summary>
    [SerializeField] private float _minimumCanvasY = -200f;

    private RectTransform _canvasRect;
    private Camera _uiCamera;

    private void Awake()
    {
        Instance = this;

        Canvas rootCanvas = GetComponentInParent<Canvas>().rootCanvas;
        _canvasRect = rootCanvas.GetComponent<RectTransform>();
        _uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;

        panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, panelWidth);
        panel.gameObject.SetActive(false);
    }

    /// <summary>Shows the tooltip for the given item, anchored near slotRect.</summary>
    public void Show(ItemData item, RectTransform slotRect)
    {
        if (item == null) return;

        itemNameText.text    = item.itemName;
        descriptionText.text = item.description;
        panel.gameObject.SetActive(true);

        PositionNearSlot(slotRect);
    }

    /// <summary>Hides the tooltip.</summary>
    public void Hide()
    {
        panel.gameObject.SetActive(false);
    }

    private void PositionNearSlot(RectTransform slotRect)
    {
        // Rebuild layout so ContentSizeFitter updates panel dimensions before positioning.
        LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

        Vector3[] corners = new Vector3[4];
        slotRect.GetWorldCorners(corners);
        // corners: [0]=BL  [1]=TL  [2]=TR  [3]=BR

        Vector2 topScreen = ((Vector2)corners[1] + (Vector2)corners[2]) * 0.5f;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvasRect, topScreen, _uiCamera, out Vector2 localTop);

        // Always place above the slot. Pivot at bottom so the panel grows upward.
        // _minimumCanvasY enforces a floor so the tooltip never sinks into the inventory bar.
        float bottomY = Mathf.Max(localTop.y + slotGap, _minimumCanvasY);

        panel.pivot = new Vector2(0.5f, 0f);
        panel.localPosition = new Vector2(localTop.x, bottomY);

        ClampInsideCanvas();
    }

    private void ClampInsideCanvas()
    {
        Vector2 canvasHalf = _canvasRect.rect.size * 0.5f;
        Vector2 panelSz    = panel.rect.size;
        Vector2 pos        = panel.localPosition;

        // Horizontal: pivot is always center-x.
        pos.x = Mathf.Clamp(pos.x,
            -canvasHalf.x + panelSz.x * 0.5f,
             canvasHalf.x - panelSz.x * 0.5f);

        // Vertical: direction depends on pivot.
        if (Mathf.Approximately(panel.pivot.y, 0f))
        {
            // Pivot at bottom → panel extends upward → top edge = pos.y + panelHeight.
            pos.y = Mathf.Clamp(pos.y, -canvasHalf.y, canvasHalf.y - panelSz.y);
        }
        else
        {
            // Pivot at top → panel extends downward → bottom edge = pos.y - panelHeight.
            pos.y = Mathf.Clamp(pos.y, -canvasHalf.y + panelSz.y, canvasHalf.y);
        }

        panel.localPosition = pos;
    }
}
