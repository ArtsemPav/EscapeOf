namespace ChemicalPuzzle
{
    /// <summary>
    /// Implemented by puzzle components that can block the player from exiting
    /// puzzle mode via Esc while devices are processing or results are pending.
    /// </summary>
    public interface IPuzzleExitGuard
    {
        /// <summary>
        /// Returns true if the player is allowed to exit puzzle mode now.
        /// Returns false if any device is busy or results are pending delivery.
        /// </summary>
        bool CanExitPuzzle();
    }
}
