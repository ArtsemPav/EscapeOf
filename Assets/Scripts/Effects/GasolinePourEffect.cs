using System.Collections;
using UnityEngine;

namespace Effects
{
    /// <summary>
    /// Controls a gasoline pouring particle effect.
    /// Provides Start/Stop API for gameplay interactions such as
    /// pouring fuel from a canister into a funnel.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class GasolinePourEffect : MonoBehaviour
    {
        private const float DefaultPourDuration = 3f;
        private const float EmissionFadeSpeed = 8f;

        [Header("Emission")]
        [Tooltip("Target emission rate when actively pouring.")]
        [SerializeField] private float _pourEmissionRate = 250f;

        [Tooltip("Emission rate when idle (0 = no particles).")]
        [SerializeField] private float _idleEmissionRate = 0f;

        [Header("Auto-Stop")]
        [Tooltip("If > 0, StartPour() auto-stops after this many seconds.")]
        [SerializeField] private float _autoStopDuration = 0f;

        private ParticleSystem _particleSystem;
        private ParticleSystem.EmissionModule _emissionModule;
        private Coroutine _stopCoroutine;
        private bool _isPouring;

        /// <summary>Whether the effect is currently pouring.</summary>
        public bool IsPouring => _isPouring;

        private void Awake()
        {
            _particleSystem = GetComponent<ParticleSystem>();
            _emissionModule = _particleSystem.emission;
            _emissionModule.rateOverTime = _idleEmissionRate;
        }

        /// <summary>Starts the gasoline pouring effect.</summary>
        public void StartPour()
        {
            if (_stopCoroutine != null)
            {
                StopCoroutine(_stopCoroutine);
                _stopCoroutine = null;
            }

            _isPouring = true;
            _emissionModule.rateOverTime = _pourEmissionRate;
            if (!_particleSystem.isPlaying)
                _particleSystem.Play();

            if (_autoStopDuration > 0f)
                _stopCoroutine = StartCoroutine(StopAfterDelay(_autoStopDuration));
        }

        /// <summary>Starts pouring and auto-stops after the specified duration.</summary>
        /// <param name="duration">Seconds before auto-stop. Falls back to <see cref="_autoStopDuration"/> or default.</param>
        public void StartPour(float duration)
        {
            StartPour();
            float delay = duration > 0f ? duration
                        : _autoStopDuration > 0f ? _autoStopDuration
                        : DefaultPourDuration;
            _stopCoroutine = StartCoroutine(StopAfterDelay(delay));
        }

        /// <summary>Stops the gasoline pouring effect. Existing particles finish their lifetime naturally.</summary>
        public void StopPour()
        {
            if (_stopCoroutine != null)
            {
                StopCoroutine(_stopCoroutine);
                _stopCoroutine = null;
            }

            _isPouring = false;
            _emissionModule.rateOverTime = _idleEmissionRate;
        }

        /// <summary>Immediately stops emission and clears all live particles.</summary>
        public void StopPourImmediate()
        {
            StopPour();
            _particleSystem.Clear();
        }

        private IEnumerator StopAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            StopPour();
            _stopCoroutine = null;
        }
    }
}
