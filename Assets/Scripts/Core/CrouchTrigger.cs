using UnityEngine;

/// <summary>
/// Fires a horror event when the crouching player enters this trigger zone.
/// Designed for the rat scare: the player must crouch to look under the lab
/// table, which sends the rat running at them.
///
/// Attach to a GameObject with a trigger collider (e.g. ratTrigger).
/// On activation calls  HorrorSystem.Instance.Trigger(_eventId)  once and
/// disables itself — the event never repeats (HasFired protection as well).
/// </summary>
public class CrouchTrigger : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Tag of the collider that activates the trigger.")]
    [SerializeField] private string _playerTag = "Player";

    [Tooltip("Horror event ID fired via HorrorSystem.Instance.Trigger.")]
    [SerializeField] private string _eventId = "rat_scare";

    [Header("Requirements")]
    [Tooltip("When true the event fires only while the player is crouching inside the zone.")]
    [SerializeField] private bool _requireCrouch = true;

    private bool _playerInside;
    private FPSController _playerController;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(_playerTag)) return;
        _playerInside = true;
        _playerController = other.GetComponentInParent<FPSController>();
        TryFire();
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(_playerTag))
            _playerInside = false;
    }

    private void Update()
    {
        if (_playerInside)
            TryFire();
    }

    /// <summary>
    /// Fires the linked horror event when the crouch condition is met,
    /// then disables itself so the trigger works only once.
    /// Also stays disabled if the event already fired (e.g. restored from a save).
    /// </summary>
    private void TryFire()
    {
        if (HorrorSystem.Instance == null)
        {
            Debug.LogWarning($"[CrouchTrigger '{name}'] HorrorSystem not found in scene.", this);
            return;
        }

        if (HorrorSystem.Instance.HasFired(_eventId))
        {
            _playerInside = false;
            enabled = false;
            return;
        }

        if (_requireCrouch)
        {
            if (_playerController == null)
                _playerController = FindFirstObjectByType<FPSController>();
            if (_playerController == null || !_playerController.IsCrouching)
                return;
        }

        HorrorSystem.Instance.Trigger(_eventId);
        _playerInside = false;
        enabled = false;
    }
}
