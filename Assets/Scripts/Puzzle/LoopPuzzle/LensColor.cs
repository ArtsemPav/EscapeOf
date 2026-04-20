/// <summary>
/// Spectral filter color installed in a painting spotlight.
/// Green is a synthesized color — it cannot be set directly on a lens;
/// it is produced when L2 and L4 emit Blue and Yellow simultaneously.
/// None means the spotlight has no lens (emits dirty/white light).
/// </summary>
public enum LensColor
{
    None,
    Red,
    Blue,
    Yellow,
    Green
}
