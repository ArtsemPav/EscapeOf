using UnityEngine;

/// <summary>
/// Listens to <see cref="InventorySystem.OnCrafted"/> and plays a sound effect
/// whenever a crafting recipe is successfully executed.
/// Place on the same GameObject as <see cref="InventorySystem"/>.
/// </summary>
public class InventoryAudio : MonoBehaviour
{
    [Header("Craft Sound")]
    [Tooltip("Звук, который проигрывается при успешном крафте (совпадении рецепта).")]
    [SerializeField] private AudioClip _craftClip;

    [SerializeField] private float _craftVolume = 1f;

    private void Start()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnCrafted += PlayCraftSound;
    }

    private void OnDestroy()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnCrafted -= PlayCraftSound;
    }

    /// <summary>Plays the crafting sound effect through AudioManager.</summary>
    private void PlayCraftSound()
    {
        if (_craftClip != null)
            AudioManager.Instance?.PlaySFX(_craftClip, _craftVolume);
    }
}
