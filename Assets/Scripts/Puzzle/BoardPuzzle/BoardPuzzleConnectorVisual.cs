using UnityEngine;

/// <summary>
/// Controls the emission of a puzzle connector based on its connection status.
/// Uses MaterialPropertyBlock for efficiency.
/// </summary>
public class BoardPuzzleConnectorVisual : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";
    private const float EmissionOffset = 0.001f;

    [Header("References")]
    [Tooltip("The renderer that will be highlighted. If null, tries to get Renderer on this GameObject.")]
    [SerializeField] private Renderer _targetRenderer;

    [Tooltip("The index of the material to target if the renderer has multiple materials.")]
    [SerializeField, Min(0)] private int _materialIndex = 0;

    [Header("Emission Settings")]
    [Tooltip("If true, this object is treated as a terminal and won't light up unless it's part of the active sequence.")]
    [SerializeField] private bool _isTerminal = false;
    public bool IsTerminal => _isTerminal;

    public Renderer TargetRenderer => _targetRenderer;
    public int MaterialIndex => _materialIndex;


    [Tooltip("Color for the emission. Use HDR color picker for intensity.")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _activeColor = Color.cyan;

    [Tooltip("Color for the terminal when it is reached in the correct sequence.")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _correctTerminalColor = Color.green;

    [Tooltip("Color for the terminal when it is reached out of order or is incorrect.")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _incorrectTerminalColor = Color.red;
    
    [Tooltip("Speed of the fade in/out animation.")]
    [SerializeField] private float _fadeSpeed = 5f;

    private MaterialPropertyBlock _propBlock;
    private float _targetWeight = 0f;
    private float _currentWeight = 0f;
    private Color _currentTargetColor;
    private readonly Color _blackColor = new Color(0f, 0f, 0f, 0f);
    private bool _isDirty = true;
    private Vector3 _originalLocalPosition;
    private bool _isLifted = false;

    private void Awake()
    {
        if (_targetRenderer == null)
            _targetRenderer = GetComponent<Renderer>();

        _propBlock = new MaterialPropertyBlock();
        _currentTargetColor = _activeColor;
        
        // Ensure emission keyword is enabled on the shared material at the specified index
        if (_targetRenderer != null)
        {
            _originalLocalPosition = _targetRenderer.transform.localPosition;

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
    /// <param name="hasPower">Does this connector have power?</param>
    /// <param name="isAllowedTerminal">If this is a terminal, is it allowed to light up?</param>
    /// <param name="isCorrect">Is this terminal reached in the correct sequence order?</param>
    public void SetPower(bool hasPower, bool isAllowedTerminal = true, bool isCorrect = true)
    {
        // If it's a terminal but NOT the one currently targeted in the sequence,
        // we still light it up as "incorrect" if it has power, instead of not lighting up at all.
        bool finalPower = hasPower; 
        float newTarget = finalPower ? 1f : 0f;
        
        Color targetColor = _isTerminal ? (isCorrect ? _correctTerminalColor : _incorrectTerminalColor) : _activeColor;

        if (!Mathf.Approximately(_targetWeight, newTarget) || _currentTargetColor != targetColor)
        {
            _targetWeight = newTarget;
            _currentTargetColor = targetColor;
            _isDirty = true;
        }
    }

    private void Update()
    {
        if (_targetRenderer == null) return;
        
        if (!_isDirty && Mathf.Approximately(_currentWeight, _targetWeight)) return;

        _currentWeight = Mathf.MoveTowards(_currentWeight, _targetWeight, _fadeSpeed * Time.deltaTime);
        
        // Use the index-aware overload of GetPropertyBlock and SetPropertyBlock
        _targetRenderer.GetPropertyBlock(_propBlock, _materialIndex);
        
        // Use Lerp to smoothly transition from black to target color
        Color finalColor = Color.Lerp(_blackColor, _currentTargetColor, _currentWeight);
        _propBlock.SetColor(EmissionColorId, finalColor);
        
        _targetRenderer.SetPropertyBlock(_propBlock, _materialIndex);

        // Lift when emission starts, restore when fully off
        bool shouldBeLifted = _currentWeight > 0f;
        if (shouldBeLifted != _isLifted)
        {
            _isLifted = shouldBeLifted;
            _targetRenderer.transform.localPosition = _isLifted
                ? _originalLocalPosition + new Vector3(0f, EmissionOffset, 0f)
                : _originalLocalPosition;
        }

        if (Mathf.Approximately(_currentWeight, _targetWeight))
        {
            _isDirty = false;
        }
    }
}
