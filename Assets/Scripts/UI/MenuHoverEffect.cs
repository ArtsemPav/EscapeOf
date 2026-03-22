using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Attaches to the VerticalLayout parent of menu buttons.
/// On hover, buttons above the hovered one slide up and buttons below slide down.
/// Decorative sibling elements track the hovered button's Y position.
/// Uses unscaled time so the animation works when Time.timeScale = 0.
/// </summary>
public class MenuHoverEffect : MonoBehaviour
{
    [Tooltip("Distance in pixels other buttons slide away from the hovered one.")]
    [SerializeField] private float _slideDistance = 55f;

    [Tooltip("Lerp speed for all animations.")]
    [SerializeField] private float _animationSpeed = 12f;

    [Header("Frame — Top Edge")]
    [Tooltip("Decorative elements that form the TOP border of the frame. They shift up by the same amount as the topmost button.")]
    [SerializeField] private RectTransform[] _topFrameElements;

    [Header("Frame — Bottom Edge")]
    [Tooltip("Decorative elements that form the BOTTOM border of the frame. They shift down by the same amount as the bottommost button.")]
    [SerializeField] private RectTransform[] _bottomFrameElements;

    [Header("Floating Decoratives")]
    [Tooltip("Decorative elements that travel with the nearest button in Y. They won't overlap since they mirror button movement.")]
    [SerializeField] private RectTransform[] _floatingElements;

    private RectTransform[] _buttons;
    private Vector2[] _originalPositions;
    private Vector2[] _targetPositions;

    private Vector2[] _topOriginalPositions;
    private Vector2[] _topTargetPositions;
    private Vector2[] _bottomOriginalPositions;
    private Vector2[] _bottomTargetPositions;

    private Vector2[] _floatingOriginalPositions;
    private Vector2[] _floatingTargetPositions;
    private int[] _floatingNearestButtonIndex;

    private VerticalLayoutGroup _layoutGroup;

    private void Start()
    {
        _layoutGroup = GetComponent<VerticalLayoutGroup>();
        StartCoroutine(InitializeAfterLayout());
    }

    /// <summary>
    /// Waits one frame for VerticalLayoutGroup to finish placing elements,
    /// captures their positions, then disables the layout so we can drive positions manually.
    /// </summary>
    private IEnumerator InitializeAfterLayout()
    {
        yield return new WaitForEndOfFrame();

        List<RectTransform> found = new List<RectTransform>();
        foreach (Transform child in transform)
        {
            if (child.GetComponent<Button>() != null)
                found.Add(child as RectTransform);
        }

        _buttons = found.ToArray();
        _originalPositions = new Vector2[_buttons.Length];
        _targetPositions = new Vector2[_buttons.Length];

        for (int i = 0; i < _buttons.Length; i++)
        {
            _originalPositions[i] = _buttons[i].anchoredPosition;
            _targetPositions[i] = _buttons[i].anchoredPosition;

            int capturedIndex = i;
            RegisterHoverEvents(_buttons[i].gameObject, capturedIndex);
        }

        InitializeFrameElements(_topFrameElements, ref _topOriginalPositions, ref _topTargetPositions);
        InitializeFrameElements(_bottomFrameElements, ref _bottomOriginalPositions, ref _bottomTargetPositions);
        InitializeFloatingElements();

        if (_layoutGroup != null)
            _layoutGroup.enabled = false;
    }

    private void InitializeFrameElements(RectTransform[] elements, ref Vector2[] originals, ref Vector2[] targets)
    {
        if (elements == null || elements.Length == 0) return;

        originals = new Vector2[elements.Length];
        targets = new Vector2[elements.Length];

        for (int i = 0; i < elements.Length; i++)
        {
            originals[i] = elements[i].anchoredPosition;
            targets[i] = elements[i].anchoredPosition;
        }
    }

    /// <summary>
    /// For each floating element, finds the button whose original Y is closest and binds to it.
    /// The element will receive the exact same Y offset as that button during hover.
    /// </summary>
    private void InitializeFloatingElements()
    {
        if (_floatingElements == null || _floatingElements.Length == 0) return;

        _floatingOriginalPositions = new Vector2[_floatingElements.Length];
        _floatingTargetPositions = new Vector2[_floatingElements.Length];
        _floatingNearestButtonIndex = new int[_floatingElements.Length];

        for (int i = 0; i < _floatingElements.Length; i++)
        {
            Vector2 pos = _floatingElements[i].anchoredPosition;
            _floatingOriginalPositions[i] = pos;
            _floatingTargetPositions[i] = pos;

            // Find the nearest button by Y distance
            int nearest = 0;
            float minDist = Mathf.Abs(pos.y - _originalPositions[0].y);
            for (int b = 1; b < _buttons.Length; b++)
            {
                float dist = Mathf.Abs(pos.y - _originalPositions[b].y);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = b;
                }
            }
            _floatingNearestButtonIndex[i] = nearest;
        }
    }

    /// <summary>Adds PointerEnter and PointerExit EventTrigger entries to the button.</summary>
    private void RegisterHoverEvents(GameObject go, int index)
    {
        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = go.AddComponent<EventTrigger>();

        EventTrigger.Entry onEnter = new EventTrigger.Entry();
        onEnter.eventID = EventTriggerType.PointerEnter;
        onEnter.callback.AddListener((_) => OnButtonHover(index));
        trigger.triggers.Add(onEnter);

        EventTrigger.Entry onExit = new EventTrigger.Entry();
        onExit.eventID = EventTriggerType.PointerExit;
        onExit.callback.AddListener((_) => OnButtonExit());
        trigger.triggers.Add(onExit);
    }

    private void OnButtonHover(int hoveredIndex)
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            if (i < hoveredIndex)
                _targetPositions[i] = _originalPositions[i] + Vector2.up * _slideDistance;
            else if (i > hoveredIndex)
                _targetPositions[i] = _originalPositions[i] + Vector2.down * _slideDistance;
            else
                _targetPositions[i] = _originalPositions[i];
        }

        // Top frame follows the topmost button: it moves up unless the topmost button is the hovered one
        float topShift = hoveredIndex > 0 ? _slideDistance : 0f;
        SetFrameTargets(_topFrameElements, _topOriginalPositions, ref _topTargetPositions, topShift);

        // Bottom frame follows the bottommost button: it moves down unless the bottommost button is the hovered one
        float bottomShift = hoveredIndex < _buttons.Length - 1 ? -_slideDistance : 0f;
        SetFrameTargets(_bottomFrameElements, _bottomOriginalPositions, ref _bottomTargetPositions, bottomShift);

        // Floating elements travel with their nearest button
        if (_floatingElements != null && _floatingOriginalPositions != null)
        {
            for (int i = 0; i < _floatingElements.Length; i++)
            {
                int nearest = _floatingNearestButtonIndex[i];
                Vector2 buttonOffset = _targetPositions[nearest] - _originalPositions[nearest];
                _floatingTargetPositions[i] = _floatingOriginalPositions[i] + buttonOffset;
            }
        }
    }

    private void OnButtonExit()
    {
        for (int i = 0; i < _buttons.Length; i++)
            _targetPositions[i] = _originalPositions[i];

        SetFrameTargets(_topFrameElements, _topOriginalPositions, ref _topTargetPositions, 0f);
        SetFrameTargets(_bottomFrameElements, _bottomOriginalPositions, ref _bottomTargetPositions, 0f);

        if (_floatingElements != null && _floatingOriginalPositions != null)
        {
            for (int i = 0; i < _floatingElements.Length; i++)
                _floatingTargetPositions[i] = _floatingOriginalPositions[i];
        }
    }

    /// <summary>
    /// Shifts frame edge elements by the given Y offset from their original positions.
    /// Top elements shift up (positive), bottom elements shift down (negative).
    /// </summary>
    private void SetFrameTargets(RectTransform[] elements, Vector2[] originals, ref Vector2[] targets, float shiftY)
    {
        if (elements == null || originals == null) return;

        for (int i = 0; i < elements.Length; i++)
            targets[i] = originals[i] + new Vector2(0f, shiftY);
    }

    private void Update()
    {
        if (_buttons == null) return;

        float dt = Time.unscaledDeltaTime * _animationSpeed;

        for (int i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].anchoredPosition = Vector2.Lerp(
                _buttons[i].anchoredPosition,
                _targetPositions[i],
                dt
            );
        }

        AnimateFrameElements(_topFrameElements, _topTargetPositions, dt);
        AnimateFrameElements(_bottomFrameElements, _bottomTargetPositions, dt);
        AnimateFrameElements(_floatingElements, _floatingTargetPositions, dt);
    }

    private void AnimateFrameElements(RectTransform[] elements, Vector2[] targets, float dt)
    {
        if (elements == null || targets == null) return;

        for (int i = 0; i < elements.Length; i++)
        {
            elements[i].anchoredPosition = Vector2.Lerp(
                elements[i].anchoredPosition,
                targets[i],
                dt
            );
        }
    }
}
