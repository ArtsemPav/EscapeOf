// This component is no longer used.
// The laptop UI now uses a World Space Canvas with PhysicsRaycaster on the Main Camera,
// which lets Unity handle UI raycasting natively without any custom forwarding logic.
// You can safely remove the LaptopScreenInputForwarder component from LaptopContainer in the scene.

namespace EscapeOf.Puzzle.Laptop
{
    [System.Obsolete("No longer needed. Use World Space Canvas + PhysicsRaycaster on Main Camera instead.")]
    public class LaptopScreenInputForwarder : UnityEngine.MonoBehaviour { }
}
