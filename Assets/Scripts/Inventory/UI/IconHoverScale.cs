using UnityEngine;

/// <summary>
/// Smoothly scales a UI icon up while hovered and back to normal when the pointer leaves.
/// Attach to the icon child of an inventory slot; the slot calls SetHovered on pointer enter/exit.
/// </summary>
public class IconHoverScale : MonoBehaviour
{
    private const float NormalScale = 1f;

    [SerializeField] private float hoverScale = 1.12f;
    [SerializeField] private float scaleSpeed = 8f;

    [Header("Hover Sound")]
    [Tooltip("Звук, который проигрывается при наведении на иконку.")]
    [SerializeField] private AudioClip _hoverClip;
    [SerializeField, Range(0f, 1f)] private float _hoverVolume = 0.7f;

    [Header("Press Sound")]
    [Tooltip("Звук, который проигрывается при нажатии на иконку.")]
    [SerializeField] private AudioClip _pressClip;
    [SerializeField, Range(0f, 1f)] private float _pressVolume = 0.7f;

    private RectTransform _rect;
    private bool _hovered;

    /// <summary>Plays the press sound. Called by inventory slots on pointer down / click.</summary>
    public void PlayPressSound()
    {
        if (_pressClip != null)
            AudioManager.Instance?.PlaySFX(_pressClip, _pressVolume);
    }

    private void Awake()
    {
        _rect = transform as RectTransform;
    }

    private void OnEnable() => ResetScale();

    /// <summary>Sets the hover state; the icon scales up while hovered and returns to normal otherwise.</summary>
    public void SetHovered(bool hovered)
    {
        _hovered = hovered;
        if (hovered && _hoverClip != null)
            AudioManager.Instance?.PlaySFX(_hoverClip, _hoverVolume);
    }

    /// <summary>Instantly returns the icon to its normal scale and clears the hover state.</summary>
    public void ResetScale()
    {
        _hovered = false;
        if (_rect != null) _rect.localScale = Vector3.one;
    }

    private void Update()
    {
        if (_rect == null) return;

        float target = _hovered ? hoverScale : NormalScale;
        float next = Mathf.MoveTowards(_rect.localScale.x, target, scaleSpeed * Time.deltaTime);
        _rect.localScale = new Vector3(next, next, next);
    }
}
