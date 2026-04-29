using UnityEngine;

/// <summary>
/// Controls the emission of a puzzle connector based on its connection status.
/// Uses MaterialPropertyBlock for efficiency.
/// </summary>
public class BoardPuzzleConnectorVisual : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    [Header("References")]
    [Tooltip("The renderer that will be highlighted. If null, tries to get Renderer on this GameObject.")]
    [SerializeField] private Renderer _targetRenderer;

    [Header("Emission Settings")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _activeColor = new Color(0f, 1f, 1f, 1f); // Cyan HDR
    [SerializeField] private float _fadeSpeed = 5f;

    private MaterialPropertyBlock _propBlock;
    private float _targetIntensity = 0f;
    private float _currentIntensity = 0f;

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();

        _propBlock = new MaterialPropertyBlock();
        
        // Ensure emission keyword is enabled on the shared material
        if (_targetRenderer != null && _targetRenderer.sharedMaterial != null)
        {
            _targetRenderer.sharedMaterial.EnableKeyword(EmissionKeyword);
        }
    }

    /// <summary>
    /// Sets whether this connector is currently receiving "power" from the start terminal.
    /// </summary>
    public void SetPower(bool hasPower)
    {
        _targetIntensity = hasPower ? 1f : 0f;
    }

    private void Update()
    {
        if (_targetRenderer == null) return;
        if (Mathf.Approximately(_currentIntensity, _targetIntensity)) return;

        _currentIntensity = Mathf.MoveTowards(_currentIntensity, _targetIntensity, _fadeSpeed * Time.deltaTime);
        
        _targetRenderer.GetPropertyBlock(_propBlock);
        _propBlock.SetColor(EmissionColorId, _activeColor * _currentIntensity);
        _targetRenderer.SetPropertyBlock(_propBlock);
    }
}
