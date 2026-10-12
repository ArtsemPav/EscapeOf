using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Moves two horizontal separator lines so they frame the currently hovered
/// (active) main-menu button. Lines glide smoothly using unscaled time and
/// stay on the last hovered item when the pointer leaves.
/// Attach to the VerticalLayout that holds the menu buttons.
/// </summary>
public class MenuSelectionLines : MonoBehaviour
{
    [Tooltip("Line rendered above the active item.")]
    [SerializeField] private RectTransform _topLine;

    [Tooltip("Line rendered below the active item.")]
    [SerializeField] private RectTransform _bottomLine;

    [Tooltip("Gap between a line and the top/bottom edge of the active button.")]
    [SerializeField] private float _padding = 8f;

    [Tooltip("How fast the lines glide to the active item.")]
    [SerializeField] private float _animationSpeed = 12f;

    private readonly List<RectTransform> _buttons = new List<RectTransform>();
    private RectTransform _activeButton;
    private bool _ready;

    private void Start()
    {
        // MenuHoverEffect lets the VerticalLayoutGroup arrange the buttons for
        // one frame and then disables the group — wait for the same moment so
        // the buttons have their final positions before the lines take over.
        StartCoroutine(Initialize());
    }

    private IEnumerator Initialize()
    {
        yield return new WaitForEndOfFrame();

        foreach (Transform child in transform)
        {
            if (child == (Transform)_topLine || child == (Transform)_bottomLine) continue;
            if (child.TryGetComponent<Button>(out _))
                _buttons.Add((RectTransform)child);
        }

        for (int i = 0; i < _buttons.Count; i++)
        {
            RectTransform button = _buttons[i];
            EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
            if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => Select(button));
            trigger.triggers.Add(enter);
        }

        if (_buttons.Count > 0)
            Select(_buttons[0]);

        _ready = true;
    }

    /// <summary>Moves both lines so they frame the given button.</summary>
    public void Select(RectTransform button) => _activeButton = button;

    private void Update()
    {
        if (!_ready || _activeButton == null || _topLine == null || _bottomLine == null) return;

        float step = Time.unscaledDeltaTime * _animationSpeed;
        float halfHeight = _activeButton.rect.height * 0.5f;
        float topTarget = _activeButton.anchoredPosition.y + halfHeight + _padding;
        float bottomTarget = _activeButton.anchoredPosition.y - halfHeight - _padding;

        _topLine.anchoredPosition = new Vector2(_topLine.anchoredPosition.x, Mathf.Lerp(_topLine.anchoredPosition.y, topTarget, step));
        _bottomLine.anchoredPosition = new Vector2(_bottomLine.anchoredPosition.x, Mathf.Lerp(_bottomLine.anchoredPosition.y, bottomTarget, step));
    }
}
