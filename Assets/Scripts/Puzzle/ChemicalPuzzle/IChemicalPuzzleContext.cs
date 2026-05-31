/// <summary>
/// Shared puzzle-level services injected into every chemical device controller by
/// <see cref="ChemicalSynthesisController"/>. Centralises the accepted-items whitelist
/// and the unknown→identified equivalence map so they only need to be configured once.
/// </summary>
public interface IChemicalPuzzleContext
{
    /// <summary>
    /// Returns true when <paramref name="item"/> (or its normalised counterpart)
    /// belongs to the puzzle's global accepted-items list.
    /// </summary>
    bool IsAccepted(ItemData item);

    /// <summary>
    /// Returns the identified counterpart for <paramref name="item"/> when a mapping
    /// exists in the shared equivalence table, otherwise returns <paramref name="item"/> itself.
    /// </summary>
    ItemData Normalize(ItemData item);
}
