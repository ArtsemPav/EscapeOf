using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Wires hover and click sounds to every Button under this object, including
/// buttons on currently inactive panels (e.g. the settings submenu), through
/// the AudioManager SFX service. Put it on the canvas root of any menu.
/// </summary>
public class MenuButtonSounds : MonoBehaviour
{
    private const float DefaultVolume = 0.7f;

    [Header("Clips")]
    [Tooltip("Played once when the cursor enters any menu button (including submenus).")]
    [SerializeField] private AudioClip _hoverClip;

    [Tooltip("Played when any menu button is clicked.")]
    [SerializeField] private AudioClip _clickClip;

    [Header("Volumes")]
    [Tooltip("Hover sound volume on the SFX bus.")]
    [SerializeField] [Range(0f, 1f)] private float _hoverVolume = DefaultVolume;

    [Tooltip("Click sound volume on the SFX bus.")]
    [SerializeField] [Range(0f, 1f)] private float _clickVolume = DefaultVolume;

    private readonly List<Button> _soundWiredButtons = new List<Button>();

    private void Awake() => WireMenuSounds();

    private void OnDestroy()
    {
        foreach (Button wiredButton in _soundWiredButtons)
        {
            if (wiredButton != null)
                wiredButton.onClick.RemoveListener(PlayClickSound);
        }
    }

    /// <summary>
    /// Wires hover and click sounds to every Button under this object,
    /// including buttons on currently inactive panels.
    /// </summary>
    private void WireMenuSounds()
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            button.onClick.AddListener(PlayClickSound);

            EventTrigger trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            EventTrigger.Entry onEnter = new EventTrigger.Entry();
            onEnter.eventID = EventTriggerType.PointerEnter;
            onEnter.callback.AddListener((_) => PlayHoverSound());
            trigger.triggers.Add(onEnter);

            _soundWiredButtons.Add(button);
        }
    }

    /// <summary>Plays the hover sound through the AudioManager UI SFX source (audible while paused).</summary>
    private void PlayHoverSound()
    {
        if (_hoverClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayUISFX(_hoverClip, _hoverVolume);
    }

    /// <summary>Plays the click sound through the AudioManager UI SFX source (audible while paused).</summary>
    private void PlayClickSound()
    {
        if (_clickClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlayUISFX(_clickClip, _clickVolume);
    }
}
