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

    /// <summary>The collider that acts as the trash drop zone.</summary>
    public Collider DropZoneCollider => _dropZoneCollider;

    private Material[] _originalMaterials;
    private Material[] _highlightMaterials;
    private bool _highlightActive;

    private void Awake()
    {
        if (_dropZoneCollider == null)
            _dropZoneCollider = GetComponent<Collider>();

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);

        // Build swappable highlight materials per renderer.
        _originalMaterials  = new Material[_renderers.Length];
        _highlightMaterials = new Material[_renderers.Length];

        for (int i = 0; i < _renderers.Length; i++)
        {
            _originalMaterials[i]  = _renderers[i].sharedMaterial;

            if (_renderers[i].sharedMaterial != null)
            {
                _highlightMaterials[i] = new Material(_renderers[i].sharedMaterial);
                _highlightMaterials[i].EnableKeyword("_EMISSION");
                _highlightMaterials[i].SetColor("_EmissionColor", _highlightColor);
            }
            else
            {
                _highlightMaterials[i] = _originalMaterials[i];
            }
        }
    }

    /// <summary>Applies an emission highlight to the trash renderers.</summary>
    public void ShowHighlight()
    {
        if (_highlightActive || _renderers.Length == 0) return;
        _highlightActive = true;
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].material = _highlightMaterials[i];
    }

    /// <summary>Restores original materials on the trash renderers.</summary>
    public void HideHighlight()
    {
        if (!_highlightActive || _renderers.Length == 0) return;
        _highlightActive = false;
        for (int i = 0; i < _renderers.Length; i++)
            _renderers[i].material = _originalMaterials[i];
    }
}
