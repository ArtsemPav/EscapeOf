using UnityEngine;

/// <summary>
/// Button in the control room that advances the height of two linked paintings.
/// Locked until S6 master switch is powered. Glows while paintings are in motion
/// and locks all column buttons during that time.
/// </summary>
public class PaintingColumnTrigger : MonoBehaviour, IInteractable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionMapId   = Shader.PropertyToID("_EmissionMap");
    private static readonly int BaseMapId       = Shader.PropertyToID("_BaseMap");

    [Header("Interaction")]
    [SerializeField] private string _interactText       = "Нажать";
    [SerializeField] private string _lockedInteractText = "Заблокировано";
    [SerializeField] private string _noPowerText        = "Нет питания";

    [Header("Linked Columns")]
    [Tooltip("The primary painting column this button controls.")]
    [SerializeField] private PaintingColumn _primaryColumn;
    [Tooltip("The next column in the cycle that also advances on press.")]
    [SerializeField] private PaintingColumn _linkedColumn;

    [Header("Power")]
    [Tooltip("Optional override. If not assigned, searched automatically via GetComponentInParent.")]
    [SerializeField] private LoopPuzzlePowerCircuit _powerCircuit;

    [Header("Indicator")]
    [SerializeField] private Renderer _indicatorRenderer;

    [Header("Emission — Active")]
    [Tooltip("HDR emission color while the paintings are moving. Values above 1 activate Bloom.")]
    [ColorUsage(showAlpha: false, hdr: true)]
    [SerializeField] private Color _activeEmissionColor = new Color(0f, 4f, 0.5f, 1f);

    private Material _indicatorMaterial;
    private Texture  _albedoTexture;
    private int      _pendingMoves;

    // ── Lock flags (independent reasons to block interaction) ──────────────────
    private bool _isLockedByPuzzle;
    private bool _isLockedByPower = true; // locked until S6 is ON

    private bool IsBlocked => _isLockedByPuzzle || _isLockedByPower || PaintingColumn.IsAnyMoving;

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_indicatorRenderer != null)
        {
            _indicatorMaterial = _indicatorRenderer.material;
            _indicatorMaterial.EnableKeyword("_EMISSION");
            _albedoTexture = _indicatorMaterial.GetTexture(BaseMapId);
        }

        SubscribeToColumn(_primaryColumn);
        SubscribeToColumn(_linkedColumn);

        SetGlow(false);
    }

    private void Start()
    {
        if (_powerCircuit == null)
            _powerCircuit = GetComponentInParent<LoopPuzzlePowerCircuit>();

        if (_powerCircuit != null)
        {
            _powerCircuit.OnMasterToggled += OnMasterPowerToggled;
            _isLockedByPower = !_powerCircuit.IsMasterOn;
        }
        else
        {
            _isLockedByPower = false;
            Debug.LogWarning("[PaintingColumnTrigger] LoopPuzzlePowerCircuit not found in parents. Power lock disabled.", this);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromColumn(_primaryColumn);
        UnsubscribeFromColumn(_linkedColumn);

        if (_powerCircuit != null)
            _powerCircuit.OnMasterToggled -= OnMasterPowerToggled;
    }

    // ── Power ──────────────────────────────────────────────────────────────────

    private void OnMasterPowerToggled(bool isOn)
    {
        _isLockedByPower = !isOn;

        if (_isLockedByPower)
            SetGlow(false);
    }

    // ── Column event subscriptions ─────────────────────────────────────────────

    private void SubscribeToColumn(PaintingColumn column)
    {
        if (column == null) return;
        column.OnMoveFinished += OnLinkedColumnFinished;
    }

    private void UnsubscribeFromColumn(PaintingColumn column)
    {
        if (column == null) return;
        column.OnMoveFinished -= OnLinkedColumnFinished;
    }

    /// <summary>Called when one of the linked columns finishes its animation.</summary>
    private void OnLinkedColumnFinished()
    {
        _pendingMoves = Mathf.Max(0, _pendingMoves - 1);
        if (_pendingMoves == 0)
            SetGlow(false);
    }

    // ── Visual ─────────────────────────────────────────────────────────────────

    /// <summary>Enables or disables the emission glow on the indicator renderer.</summary>
    private void SetGlow(bool active)
    {
        if (_indicatorMaterial == null) return;

        if (active)
        {
            _indicatorMaterial.SetTexture(EmissionMapId, _albedoTexture);
            _indicatorMaterial.SetColor(EmissionColorId, _activeEmissionColor);
        }
        else
        {
            _indicatorMaterial.SetTexture(EmissionMapId, null);
            _indicatorMaterial.SetColor(EmissionColorId, Color.black);
        }
    }

    // ── IInteractable ──────────────────────────────────────────────────────────

    /// <summary>Locks the button permanently. Called by LoopPuzzleController when the puzzle is solved.</summary>
    public void SetLocked(bool locked) => _isLockedByPuzzle = locked;

    public void Interact()
    {
        // General power off (enabled set by LoopPuzzleController.OnPowerStateChanged).
        // The button press animation still plays so the player sees the button respond.
        if (!enabled) return;
        if (IsBlocked) return;

        _pendingMoves  = (_primaryColumn != null ? 1 : 0)
                       + (_linkedColumn  != null ? 1 : 0);

        if (_pendingMoves > 0)
            SetGlow(true);

        _primaryColumn?.AdvanceHeight();
        _linkedColumn?.AdvanceHeight();
    }

    public bool   IsPickable()      => false;
    public bool   UseLMBClick       => true;
    public string GetInteractText()
    {
        if (!enabled)                                            return _noPowerText;
        if (_isLockedByPuzzle || PaintingColumn.IsAnyMoving)     return _lockedInteractText;
        if (_isLockedByPower)                                    return _noPowerText;
        return _interactText;
    }
}
