using UnityEngine;

/// <summary>
/// Tiny UnityEvent helper: GameObject.SetActive cannot be wired as a persistent
/// UnityEvent call (the event target must be a component), so attach this to any
/// GameObject whose active state should be toggled from an event.
/// </summary>
public class GameObjectActiveSetter : MonoBehaviour
{
    /// <summary>
    /// Sets the GameObject's active state. Wire from a UnityEvent with a bool argument.
    /// </summary>
    /// <param name="active">True to activate the GameObject, false to deactivate.</param>
    public void SetActiveState(bool active)
    {
        gameObject.SetActive(active);
    }
}
