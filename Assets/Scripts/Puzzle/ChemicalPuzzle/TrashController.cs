using ChemicalPuzzle;
using UnityEngine;

/// <summary>
/// Attached to the trash drop-zone inside the Chemical Synthesis puzzle.
/// Handles visual highlight when the player hovers a flask over it during a drag.
/// Actual drop logic (consuming the flask and returning an empty one) is handled
/// by <see cref="ChemicalSynthesisController"/>.
/// </summary>
public class TrashController : MonoBehaviour
{
    [Tooltip("Collider used as the drop zone. Auto-resolved from this GameObject if not assigned.")]
    [SerializeField] private Collider _dropZoneCollider;

    [Tooltip("Renderers to highlight. Auto-collected from all children if left empty.")]
    [SerializeField] private Renderer[] _renderers;

    [Tooltip("Emission color applied when highlighting (non-HDR keeps it subtle).")]
    [SerializeField] private Color _highlightColor = new Color(0.6f, 0.1f, 0.1f, 1f);

    [Header("Audio")]
    [Tooltip("Played once when an item is discarded into the trash.")]
    [SerializeField] private AudioClip _dropClip;

    [SerializeField] [Range(0f, 1f)] private float _dropVolume = 1f;

    /// <summary>The collider that acts as the trash drop zone.</summary>
    public Collider DropZoneCollider => _dropZoneCollider;

    private bool _highlightActive;

    private void Awake()
    {
        if (_dropZoneCollider == null)
            _dropZoneCollider = GetComponent<Collider>();

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>Applies a color-brighten highlight to the trash renderers via MaterialPropertyBlock.</summary>
    public void ShowHighlight()
    {
        if (_highlightActive || _renderers.Length == 0) return;
        _highlightActive = true;
        for (int i = 0; i < _renderers.Length; i++)
            DeviceHighlightHelper.ShowHighlight(_renderers[i], _highlightColor);
    }

    /// <summary>Restores original appearance by clearing MaterialPropertyBlocks.</summary>
    public void HideHighlight()
    {
        if (!_highlightActive || _renderers.Length == 0) return;
        _highlightActive = false;
        for (int i = 0; i < _renderers.Length; i++)
            DeviceHighlightHelper.HideHighlight(_renderers[i]);
    }

    /// <summary>Plays the drop sound through AudioManager when an item is discarded.</summary>
    public void PlayDropSound()
    {
        if (_dropClip != null)
            AudioManager.Instance?.PlaySFX(_dropClip, _dropVolume);
    }
}
