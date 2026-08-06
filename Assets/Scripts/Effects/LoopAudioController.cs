using UnityEngine;

/// <summary>
/// Управление зацикленным 3D-звуком через AudioManager.
/// Регистрирует AudioSource в системе mute/unmute и конфигурирует
/// 3D-настройки в коде для предсказуемого поведения.
/// Также поддерживает проигрывание однократных SFX-клипов.
/// </summary>
public class LoopAudioController : MonoBehaviour
{
    [Header("Loop Source")]
    [Tooltip("Зацикленный аудио-источник. Регистрируется в AudioManager для отслеживания mute/unmute.")]
    [SerializeField] private AudioSource _loopAudio;

    [Tooltip("3D расстояние, в пределах которого звук играет на полную громкость.")]
    [SerializeField] private float _loopMinDistance = 3f;

    [Tooltip("3D расстояние, на котором звук затухает до нуля. " +
             "Должно примерно соответствовать размеру комнаты.")]
    [SerializeField] private float _loopMaxDistance = 6f;

    [Header("SFX")]
    [Tooltip("Однократный звук, проигрываемый через AudioManager.")]
    [SerializeField] private AudioClip _sfxClip;

    [Header("Volumes")]
    [SerializeField, Range(0f, 1f)] private float _loopVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float _sfxVolume  = 1f;

    [Header("Auto Start")]
    [Tooltip("Если включено — зацикленный звук запускается автоматически при активации компонента.")]
    [SerializeField] private bool _playOnEnable;

    private bool _loopRegistered;

    private void OnEnable()
    {
        if (_playOnEnable)
            StartLoop();
    }

    private void OnDisable()
    {
        StopLoop();
    }

    /// <summary>Проигрывает однократный SFX через AudioManager singleton.</summary>
    public void PlaySFX()
    {
        if (_sfxClip != null)
            AudioManager.Instance?.PlaySFX(_sfxClip, _sfxVolume);
    }

    /// <summary>
    /// Запускает зацикленный звук и регистрирует его в AudioManager
    /// для отслеживания mute/unmute. 3D-настройки конфигурируются в коде,
    /// чтобы поведение было предсказуемым независимо от состояния в Inspector.
    /// </summary>
    public void StartLoop()
    {
        if (_loopAudio == null || _loopRegistered) return;

        _loopAudio.spatialBlend = 1f;
        _loopAudio.rolloffMode  = AudioRolloffMode.Linear;
        _loopAudio.minDistance  = _loopMinDistance;
        _loopAudio.maxDistance  = _loopMaxDistance;
        _loopAudio.dopplerLevel = 0f;
        _loopAudio.spread       = 0f;

        _loopAudio.enabled = true;
        _loopAudio.Play();
        AudioManager.Instance?.RegisterLoopSource(_loopAudio, _loopVolume);
        _loopRegistered = true;
    }

    /// <summary>Останавливает зацикленный звук и разрегистрирует его из AudioManager.</summary>
    public void StopLoop()
    {
        if (_loopAudio == null || !_loopRegistered) return;

        AudioManager.Instance?.UnregisterLoopSource(_loopAudio);
        _loopAudio.enabled = false;
        _loopRegistered = false;
    }
}
