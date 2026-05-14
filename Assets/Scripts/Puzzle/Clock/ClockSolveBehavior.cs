/// <summary>
/// Defines how the clock behaves before and after the puzzle is solved.
/// </summary>
public enum ClockSolveBehavior
{
    /// <summary>Clock ticks and pendulum swings from the start; stops when the puzzle is solved.</summary>
    StopOnSolve,

    /// <summary>Clock is silent and pendulum is still initially; starts when the puzzle is solved.</summary>
    StartOnSolve
}
