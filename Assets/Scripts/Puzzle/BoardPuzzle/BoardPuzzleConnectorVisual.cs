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

    [Tooltip("The index of the material to target if the renderer has multiple materials.")]
    [SerializeField, Min(0)] private int _materialIndex = 0;

    [Header("Emission Settings")]
    [Tooltip("Color for the emission. Use HDR color picker for intensity.")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _activeColor = Color.cyan;
    
    [Tooltip("Speed of the fade in/out animation.")]
    [SerializeField] private float _fadeSpeed = 5f;

    private MaterialPropertyBlock _propBlock;
    private float _targetWeight = 0f;
    private float _currentWeight = 0f;

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();

        _propBlock = new MaterialPropertyBlock();
        
        // Ensure emission keyword is enabled on the shared material at the specified index
        if (_targetRenderer != null)
        {
            Material[] sharedMaterials = _targetRenderer.sharedMaterials;
            if (_materialIndex < sharedMaterials.Length && sharedMaterials[_materialIndex] != null)
            {
                sharedMaterials[_materialIndex].EnableKeyword(EmissionKeyword);
            }
        }
    }

    /// <summary>
    /// Sets whether this connector is currently receiving "power" from the start terminal.
    /// </summary>
    public void SetPower(bool hasPower)
    {
        _targetWeight = hasPower ? 1f : 0f;
    }

    private void Update()
    {
        if (_targetRenderer == null) return;
        if (Mathf.Approximately(_currentWeight, _targetWeight)) return;

        _currentWeight = Mathf.MoveTowards(_currentWeight, _targetWeight, _fadeSpeed * Time.deltaTime);
        
        // Use the index-aware overload of GetPropertyBlock and SetPropertyBlock
        _targetRenderer.GetPropertyBlock(_propBlock, _materialIndex);
        
        // Final color = color * current weight (0 to 1)
        Color finalColor = _activeColor * _currentWeight;
        _propBlock.SetColor(EmissionColorId, finalColor);
        
        _targetRenderer.SetPropertyBlock(_propBlock, _materialIndex);
    }
}
