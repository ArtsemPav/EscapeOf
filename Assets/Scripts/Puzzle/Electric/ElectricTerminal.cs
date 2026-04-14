using UnityEngine;

/// <summary>
/// Placed on each terminal collider (colored or neutral).
/// Holds state only — no IInteractable needed.
/// ElectricPuzzleController raycasts against these colliders directly
/// using Camera.main.ScreenPointToRay (same pattern as MedallionBoxUI).
/// </summary>
[RequireComponent(typeof(Collider))]
public class ElectricTerminal : MonoBehaviour
{
    // ── Terminal type ─────────────────────────────────────────────────────────

    public enum TerminalType { Colored, Neutral }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [SerializeField] private TerminalType _terminalType;
    [SerializeField] private int          _terminalIndex;

    // ── Public props ──────────────────────────────────────────────────────────

    public TerminalType Type  => _terminalType;
    public int          Index => _terminalIndex;

    /// <summary>Wire currently occupying this terminal (null if free).</summary>
    public ElectricWire ConnectedWire { get; private set; }

    /// <summary>True when no wire is connected to this terminal.</summary>
    public bool IsFree => ConnectedWire == null;

    /// <summary>Registers a wire connection on this terminal.</summary>
    public void AttachWire(ElectricWire wire) => ConnectedWire = wire;

    /// <summary>Clears the wire reference when disconnected.</summary>
    public void DetachWire() => ConnectedWire = null;
}
