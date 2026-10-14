using UnityEngine;

/// <summary>
/// Enables emission on a target material when the player enters this trigger zone.
/// Optionally disables it again when the player leaves.
/// Works with URP/Lit: sets _EmissionColor and enables the _EMISSION keyword
/// on a material instance so other objects sharing the material stay unaffected.
/// </summary>
public class EmissionTrigger : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [Header("Trigger")]
    [Tooltip("Tag of the collider that activates the trigger.")]
    [SerializeField] private string _playerTag = "Player";

    [Header("Emission Target")]
    [Tooltip("Renderer whose material gets the emission. If empty, the renderer on this GameObject is used.")]
    [SerializeField] private Renderer _targetRenderer;

    [Tooltip("Emission color to apply. Values above 1 activate Bloom in URP.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _emissionColor = new Color(1f, 0.55f, 0.15f, 1f);

    [Header("Behaviour")]
    [Tooltip("When true, emission turns off again when the player exits the trigger.")]
    [SerializeField] private bool _disableOnExit;

    [Tooltip("When true, the trigger works only once and disables itself after firing.")]
    [SerializeField] private bool _fireOnce = true;

    private Material _material;
    private bool _fired;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();

        if (_targetRenderer != null)
            _material = _targetRenderer.material; // instance, so shared material is untouched
        else
            Debug.LogWarning($"[EmissionTrigger '{name}'] No renderer assigned or found on this GameObject.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_fired && _fireOnce) return;
        if (!other.CompareTag(_playerTag)) return;

        SetEmission(true);
        _fired = true;

        if (_fireOnce)
            enabled = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (_fired && _fireOnce) return;
        if (!other.CompareTag(_playerTag)) return;

        if (_disableOnExit)
            SetEmission(false);
    }

    // ── Visual ─────────────────────────────────────────────────────────────────

    /// <summary>Enables or disables emission on the target material instance.</summary>
    private void SetEmission(bool active)
    {
        if (_material == null) return;

        _material.SetColor(EmissionColorId, active ? _emissionColor : Color.black);
        _material.EnableKeyword("_EMISSION");
    }
}
