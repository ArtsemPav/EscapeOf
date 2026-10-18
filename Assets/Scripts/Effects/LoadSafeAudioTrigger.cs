using UnityEngine;

/// <summary>
/// Обёртка над <see cref="AudioSource"/> для вызова из UnityEvent.
/// Проигрывает клип только когда игра полностью загрузилась. Вызовы во время
/// восстановления сейва (SaveManager ещё не settled — например, повтор
/// OnPuzzleSolved из <see cref="PuzzleModeController.LoadSaveData"/>) игнорируются,
/// поэтому звук не воспроизводится заново при каждом запуске игры.
/// Используйте этот компонент вместо прямого AudioSource.Play в UnityEvent
/// одноразовых сюжетных звуков.
/// </summary>
public class LoadSafeAudioTrigger : MonoBehaviour
{
    [Tooltip("Источник звука. Если не задан — берётся с этого же объекта.")]
    [SerializeField] private AudioSource _audioSource;

    private void Awake()
    {
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();

        // Route to the SFX bus unless the source already has a mixer group from the Inspector.
        if (_audioSource != null && _audioSource.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_audioSource);
    }

    /// <summary>Проиграть звук. Игнорируется во время начальной загрузки сейва.</summary>
    public void Play()
    {
        if (!SaveManager.InstanceHasSettled) return;

        _audioSource?.Play();
    }
}
