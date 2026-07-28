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

    [Header("Additional Renderers")]
    [Tooltip("Additional renderers that light up in sync with the main target. Useful for child objects like runes.")]
    [SerializeField] private Renderer[] _additionalRenderers;

    private MaterialPropertyBlock _propBlock;
    private float _targetWeight = 0f;
    private float _currentWeight = 0f;
    private Color _currentTargetColor;
    private readonly Color _blackColor = new Color(0f, 0f, 0f, 0f);
    private bool _isDirty = true;
    private Vector3 _originalLocalPosition;
    private bool _isLifted = false;

    // Per-additional-renderer runtime state
    private float[] _addTargetWeights;
    private float[] _addCurrentWeights;
    private Color[] _addCurrentColors;

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

        // Initialize additional renderer state: read emission color from each material
        if (_additionalRenderers != null && _additionalRenderers.Length > 0)
        {
            _addTargetWeights = new float[_additionalRenderers.Length];
            _addCurrentWeights = new float[_additionalRenderers.Length];
            _addCurrentColors = new Color[_additionalRenderers.Length];

            for (int i = 0; i < _additionalRenderers.Length; i++)
            {
                if (_additionalRenderers[i] == null) continue;

                Material mat = _additionalRenderers[i].sharedMaterial;
                if (mat != null)
                {
                    mat.EnableKeyword(EmissionKeyword);
                    _addCurrentColors[i] = mat.GetColor(EmissionColorId);
                }
            }
        }
    }

    /// <summary>
    /// Sets whether this connector is currently receiving "power" from the start terminal.
    /// </summary>
    /// <param name="hasPower">Does this connector have power?</param>
    /// <param name="isAllowedTerminal">If false, this terminal is incorrect (physically reached but wrong).</param>
    /// <param name="isCorrect">Is this terminal reached in the correct sequence order?</param>
    /// <param name="isSolved">Is the puzzle fully solved? When true, runes on correct terminals stay on even without power.</param>
    public void SetPower(bool hasPower, bool isAllowedTerminal = true, bool isCorrect = true, bool isSolved = false)
    {
        float newTarget = hasPower ? 1f : 0f;

        Color targetColor = _isTerminal ? (isCorrect ? _correctTerminalColor : _incorrectTerminalColor) : _activeColor;

        bool mainChanged = !Mathf.Approximately(_targetWeight, newTarget) || _currentTargetColor != targetColor;

        if (mainChanged)
        {
            _targetWeight = newTarget;
            _currentTargetColor = targetColor;
        }

        // Additional renderers: ON only when terminal is correct and (powered or solved).
        bool addChanged = false;
        if (_additionalRenderers != null && _addTargetWeights != null)
        {
            bool addPowered = isCorrect && ((hasPower && isAllowedTerminal) || isSolved);
            float addNewTarget = addPowered ? 1f : 0f;

            for (int i = 0; i < _additionalRenderers.Length; i++)
            {
                if (_additionalRenderers[i] == null) continue;

                if (!Mathf.Approximately(_addTargetWeights[i], addNewTarget))
                {
                    _addTargetWeights[i] = addNewTarget;
                    addChanged = true;
                }
            }
        }

        if (mainChanged || addChanged)
        {
            _isDirty = true;
        }
    }

    private void Update()
    {
        if (_targetRenderer == null) return;

        // Check if main renderer needs updating
        bool mainNeedsUpdate = _isDirty || !Mathf.Approximately(_currentWeight, _targetWeight);

        // Check if any additional renderer still needs to fade
        bool addNeedsUpdate = false;
        if (_addTargetWeights != null)
        {
            for (int i = 0; i < _addTargetWeights.Length; i++)
            {
                if (!Mathf.Approximately(_addCurrentWeights[i], _addTargetWeights[i]))
                {
                    addNeedsUpdate = true;
                    break;
                }
            }
        }

        if (!mainNeedsUpdate && !addNeedsUpdate) return;

        // Main renderer fade
        _currentWeight = Mathf.MoveTowards(_currentWeight, _targetWeight, _fadeSpeed * Time.deltaTime);
        _targetRenderer.GetPropertyBlock(_propBlock, _materialIndex);
        Color finalColor = Color.Lerp(_blackColor, _currentTargetColor, _currentWeight);
        _propBlock.SetColor(EmissionColorId, finalColor);
        _targetRenderer.SetPropertyBlock(_propBlock, _materialIndex);

        // Additional renderers fade (independent weight per renderer)
        if (_addTargetWeights != null)
        {
            for (int i = 0; i < _additionalRenderers.Length; i++)
            {
                if (_additionalRenderers[i] == null) continue;

                _addCurrentWeights[i] = Mathf.MoveTowards(
                    _addCurrentWeights[i], _addTargetWeights[i], _fadeSpeed * Time.deltaTime);

                Color addFinalColor = Color.Lerp(_blackColor, _addCurrentColors[i], _addCurrentWeights[i]);

                _additionalRenderers[i].GetPropertyBlock(_propBlock);
                _propBlock.SetColor(EmissionColorId, addFinalColor);
                _additionalRenderers[i].SetPropertyBlock(_propBlock);
            }
        }

        // Lift when emission starts, restore when fully off
        bool shouldBeLifted = _currentWeight > 0f;
        if (shouldBeLifted != _isLifted)
        {
            _isLifted = shouldBeLifted;
            _targetRenderer.transform.localPosition = _isLifted
                ? _originalLocalPosition + new Vector3(0f, EmissionOffset, 0f)
                : _originalLocalPosition;
        }

        // Clear dirty flag only when all renderers reach their targets
        if (Mathf.Approximately(_currentWeight, _targetWeight))
        {
            bool allAddDone = true;
            if (_addTargetWeights != null)
            {
                for (int i = 0; i < _addTargetWeights.Length; i++)
                {
                    if (!Mathf.Approximately(_addCurrentWeights[i], _addTargetWeights[i]))
                    {
                        allAddDone = false;
                        break;
                    }
                }
            }
            if (allAddDone)
            {
                _isDirty = false;
            }
        }
    }
}
