using UnityEngine;

/// <summary>
/// Deactivates this GameObject while the flashlight is on in the specified mode (e.g. UV).
/// When the light is turned off or switched to another mode, the GameObject is activated again.
/// The script keeps listening to flashlight events even while the GameObject is inactive,
/// so it always comes back when the light goes away.
/// </summary>
public class HiddenInUvLight : MonoBehaviour
{
    [Tooltip("Flashlight mode that hides this object.")]
    [SerializeField] private FlashlightMode hiddenInMode = FlashlightMode.UV;

    private bool _subscribed;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void OnDestroy()
    {
        var controller = FlashlightController.Instance;
        if (!_subscribed || controller == null) return;
        controller.OnModeChanged -= HandleModeChanged;
        _subscribed = false;
    }

    private void TrySubscribe()
    {
        if (_subscribed) return;

        var controller = FlashlightController.Instance;
        if (controller == null) return;

        controller.OnModeChanged += HandleModeChanged;
        _subscribed = true;

        UpdateVisibility();
    }

    private void HandleModeChanged(FlashlightMode _)
    {
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var controller = FlashlightController.Instance;
        if (controller == null) return;

        // Inverse of HiddenWallSign: deactivated while the UV light is on, active otherwise.
        gameObject.SetActive(!(controller.IsOn && controller.CurrentMode == hiddenInMode));
    }
}
