using UnityEngine;

/// <summary>
/// Shows a ParticleSystem only while the player camera is looking
/// at the specified Target object within a configurable angle threshold.
/// When the player looks away, particles stop emitting and fade out smoothly.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class ParticleLookTrigger : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Объект, на который должен смотреть игрок, чтобы появились частицы.")]
    [SerializeField] private Transform _lookTarget;

    [Header("Detection")]
    [Tooltip("Камера игрока. Если пусто — берётся Camera.main автоматически.")]
    [SerializeField] private Camera _playerCamera;

    [Tooltip("Минимальный dot-произведение для считания взгляда.\n" +
             "0.95 ≈ 7°  |  0.9 ≈ 18°  |  0.8 ≈ 37°  |  0.7 ≈ 45°")]
    [SerializeField] private float _lookThreshold = 0.9f;

    [Tooltip("Максимальное расстояние от камеры до Target, на котором срабатывает триггер.\n" +
             "0 = без ограничений.")]
    [SerializeField] private float _maxDistance = 0f;

    private ParticleSystem _particles;
    private ParticleSystemRenderer _renderer;
    private bool _isShowing;
    private bool _isFading;

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        _renderer = GetComponent<ParticleSystemRenderer>();

        _renderer.enabled = false;
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Start()
    {
        if (_playerCamera == null)
            _playerCamera = Camera.main;
    }

    private void Update()
    {
        if (_lookTarget == null || _playerCamera == null)
        {
            if (_isShowing) StartFadeOut();
            FinishFadeOutIfNeeded();
            return;
        }

        Vector3 camPos = _playerCamera.transform.position;
        Vector3 toTarget = (_lookTarget.position - camPos).normalized;
        float dot = Vector3.Dot(_playerCamera.transform.forward, toTarget);

        bool distanceOk = _maxDistance <= 0f ||
                          Vector3.Distance(camPos, _lookTarget.position) <= _maxDistance;

        if (dot >= _lookThreshold && distanceOk)
        {
            if (!_isShowing) Show();
        }
        else
        {
            if (_isShowing) StartFadeOut();
        }

        FinishFadeOutIfNeeded();
    }

    private void Show()
    {
        _isShowing = true;
        _isFading = false;
        _renderer.enabled = true;
        _particles.Play(true);
    }

    /// <summary>Stops emitting new particles — existing ones fade out over their remaining lifetime.</summary>
    private void StartFadeOut()
    {
        _isShowing = false;
        _isFading = true;
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    /// <summary>Disables the renderer once all particles have finished their lifecycle.</summary>
    private void FinishFadeOutIfNeeded()
    {
        if (!_isFading) return;

        if (!_particles.IsAlive(true))
        {
            _renderer.enabled = false;
            _isFading = false;
        }
    }
}
