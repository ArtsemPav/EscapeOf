/// <summary>
/// Implemented by any device that should react to general power state changes.
/// Register with <see cref="LightingSystem.RegisterConsumer"/> to receive
/// <see cref="OnPowerStateChanged"/> notifications whenever master power changes.
/// The current state is delivered immediately upon registration.
/// </summary>
public interface IPowerConsumer
{
    /// <summary>Called when the general power state changes. Also called once on registration.</summary>
    /// <param name="isPowered">True when master power is on, false when off.</param>
    void OnPowerStateChanged(bool isPowered);
}
