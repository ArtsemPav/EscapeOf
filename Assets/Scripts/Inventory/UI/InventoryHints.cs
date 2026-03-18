using TMPro;
using UnityEngine;

/// <summary>
/// Populates the inventory hints bar with control descriptions.
/// Assign this component to the HintsBar GameObject inside InventoryPanel.
/// Hints are displayed in rows of HintsPerRow items. All text is configurable
/// via the Inspector so it can be localised without code changes.
/// </summary>
public class InventoryHints : MonoBehaviour
{
    [System.Serializable]
    private struct HintEntry
    {
        [Tooltip("Key label, e.g. 'ЛКМ + Тащить'")]
        public string key;
        [Tooltip("Action description, e.g. 'Переместить предмет'")]
        public string action;
    }

    [Header("Hint Text")]
    [SerializeField] private TextMeshProUGUI hintsLabel;

    [Header("Hints")]
    [SerializeField] private HintEntry[] hints = new HintEntry[]
    {
        new HintEntry { key = "ЛКМ + тащить",       action = "Переместить предмет" },
        new HintEntry { key = "ПКМ",                 action = "3D-осмотр предмета"  },
        new HintEntry { key = "Перетащить на слот",  action = "Объединить / крафт"  },
        new HintEntry { key = "Tab / I",             action = "Закрыть инвентарь"   },
    };

    [Header("Formatting")]
    [Tooltip("How many hints to place on each row before inserting a line break.")]
    [SerializeField] private int hintsPerRow = 2;
    [Tooltip("Separator between key label and action description.")]
    [SerializeField] private string separator = "  —  ";
    [Tooltip("Spacing between hints on the same row.")]
    [SerializeField] private string columnGap = "          ";

    private void Start()
    {
        BuildHintsText();
    }

    /// <summary>Rebuilds the hints text from the hints array. Call if you change hints at runtime.</summary>
    public void BuildHintsText()
    {
        if (hintsLabel == null) return;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hints.Length; i++)
        {
            sb.Append($"<b>{hints[i].key}</b>{separator}{hints[i].action}");

            bool isLastOnRow = (i + 1) % hintsPerRow == 0;
            bool isLast      = i == hints.Length - 1;

            if (!isLast)
                sb.Append(isLastOnRow ? "\n" : columnGap);
        }

        hintsLabel.text = sb.ToString();
    }
}
