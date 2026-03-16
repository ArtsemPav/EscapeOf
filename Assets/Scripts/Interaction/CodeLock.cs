using System.Text;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Electronic code lock. Opens CodeLockUI when the player interacts.
/// Generates a random numeric code each session when Randomize On Start is enabled.
/// After unlocking, the lock becomes fully non-interactable (collider disabled).
/// Optionally requires an item in the inventory to access the panel — it is consumed on unlock.
/// Wire the target door's UnlockAndOpen() to OnUnlocked in the Inspector.
/// </summary>
public class CodeLock : MonoBehaviour, IInteractable
{
    [Header("Lock Settings")]
    [Tooltip("Generate a new random code every time the game starts.")]
    [SerializeField] private bool _randomizeOnStart = true;
    [Tooltip("Number of digits when Randomize On Start is enabled.")]
    [SerializeField] private int _codeLength = 4;
    [Tooltip("Fixed code used when Randomize On Start is disabled.")]
    [SerializeField] private string _secretCode = "1234";
    [SerializeField] private string _interactText = "Ввести код";

    [Header("Item Requirement (optional)")]
    [Tooltip("If set, the player must have this item to open the panel. It is consumed on unlock.")]
    [SerializeField] private ItemData _requiredItem;
    [Tooltip("Hint shown when the player lacks the required item.")]
    [SerializeField] private string _missingItemHint = "Нужен предмет";

    [Header("References")]
    [SerializeField] private CodeLockUI _lockUI;

    [Header("Events")]
    [Tooltip("Called when the player opens the code panel. Wire horror effects that should disappear here.")]
    [SerializeField] private UnityEvent _onPanelOpened;
    [Tooltip("Called when the correct code is entered. Wire door.UnlockAndOpen() here.")]
    [SerializeField] private UnityEvent _onUnlocked;

    private string _activeCode;
    private Collider _collider;

    public bool IsUnlocked { get; private set; }

    /// <summary>Number of digits in the active code. Drives the numpad display slot count.</summary>
    public int CodeLength => _activeCode.Length;

    /// <summary>Returns the active code for this session. Used by CodeHintDisplay.</summary>
    public string GetCode() => _activeCode;

    private void Awake()
    {
        _activeCode = _randomizeOnStart
            ? GenerateRandomCode(_codeLength)
            : _secretCode;

        _collider = GetComponent<Collider>();
    }

    /// <summary>Opens the numpad UI if requirements are met.</summary>
    public void Interact()
    {
        if (IsUnlocked) return;

        if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem))
            return;

        _onPanelOpened.Invoke();
        _lockUI.Open(this);
    }

    public string GetInteractText() => _interactText;
    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;

    /// <summary>Validates the entered code. Returns true and fires OnUnlocked if correct.</summary>
    public bool TryUnlock(string enteredCode)
    {
        if (enteredCode != _activeCode) return false;

        IsUnlocked = true;

        if (_requiredItem != null)
            InventorySystem.Instance.RemoveItem(_requiredItem);

        _onUnlocked.Invoke();

        if (_collider != null)
            _collider.enabled = false;

        return true;
    }

    public string GetBlockedHint()
    {
        if (IsUnlocked) return string.Empty;
        if (_requiredItem != null && !InventorySystem.Instance.HasItem(_requiredItem))
            return _missingItemHint;
        return string.Empty;
    }

    private static string GenerateRandomCode(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(Random.Range(0, 10));
        return sb.ToString();
    }
}
