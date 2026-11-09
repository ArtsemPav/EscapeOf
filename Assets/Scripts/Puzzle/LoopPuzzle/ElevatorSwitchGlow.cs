using System.Collections;
using UnityEngine;

namespace Escape.Interaction
{
    /// <summary>
    /// Включает эмиссию на кнопке вызова лифта при нажатии (только при включённом
    /// электричестве) и гасит её, когда лифт доехал до этажа этой кнопки.
    /// Если лифт уже занят другим рейсом, свечение живёт только на время
    /// анимации нажатия — рейс не запланирован, запрос игнорируется.
    /// </summary>
    public class ElevatorSwitchGlow : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Рендерер светящейся части кнопки (elevator_switch.001).")]
        [SerializeField] private Renderer _glowRenderer;

        [Tooltip("Кнопка вызова, чей FloorIndex определяет этаж этого свитча.")]
        [SerializeField] private ElevatorButton _callButton;

        [Tooltip("Контроллер лифта. Если пуст — ищется на сцене.")]
        [SerializeField] private ElevatorController _controller;

        [Tooltip("Анимация нажатия, чьё событие OnPressed включает свечение.")]
        [SerializeField] private ButtonPressAnimation _button;

        [Header("Materials")]
        [Tooltip("Материал без эмиссии (elevatorMat).")]
        [SerializeField] private Material _glowOffMaterial;

        [Tooltip("Материал с эмиссией (elevatorMat_emit).")]
        [SerializeField] private Material _glowOnMaterial;

        [Header("Busy Flash")]
        [Tooltip("Длительность короткого свечения, когда лифт занят и запрос проигнорирован.")]
        [SerializeField] private float _busyFlashFallbackDuration = 0.2f;

        private Coroutine _glowRoutine;

        private int FloorIndex => _callButton != null ? _callButton.FloorIndex : -1;

        private void Awake()
        {
            if (_glowRenderer == null) _glowRenderer = GetComponentInChildren<Renderer>();
            if (_callButton   == null) _callButton   = GetComponent<ElevatorButton>();
            if (_controller   == null) _controller   = FindFirstObjectByType<ElevatorController>();
            if (_button       == null) _button       = GetComponent<ButtonPressAnimation>();
        }

        private void Start()
        {
            SetGlow(false);
        }

        private void OnEnable()
        {
            if (_button != null) _button.OnPressed += HandlePressed;
        }

        private void OnDisable()
        {
            if (_button != null) _button.OnPressed -= HandlePressed;
        }

        private void HandlePressed()
        {
            // Без электричества свечение не включаем — кнопка мёртвая.
            if (_controller == null || !_controller.HasPower) return;

            // Запрос проигнорирован контроллером (занят / уже здесь с открытыми дверями)?
            bool requestIgnored = _callButton != null && !_callButton.LastCallAccepted;

            if (_glowRoutine != null) StopCoroutine(_glowRoutine);
            _glowRoutine = StartCoroutine(GlowRoutine(requestIgnored));
        }

        /// <summary>
        /// Свечение с момента нажатия до прибытия лифта на этаж этой кнопки.
        /// Если запрос проигнорирован — короткая вспышка на время анимации нажатия.
        /// </summary>
        private IEnumerator GlowRoutine(bool requestIgnored)
        {
            SetGlow(true);

            if (requestIgnored)
            {
                // Короткая вспышка — рейса не будет.
                float flashDuration = _button != null
                    ? _button.PressDuration + _button.ReturnDuration
                    : _busyFlashFallbackDuration;
                yield return new WaitForSeconds(flashDuration);
            }
            else
            {
                // Рейс принят: горим, пока лифт не приедет на этаж этой кнопки
                // и не откроет двери. Таймаут — страховка от зависшей корутины.
                const float maxWait = 60f;
                float waited = 0f;
                while (waited < maxWait &&
                       !(_controller.CurrentFloor == FloorIndex && _controller.AreDoorsOpen))
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }

            SetGlow(false);
            _glowRoutine = null;
        }

        private void SetGlow(bool glowing)
        {
            if (_glowRenderer != null)
                _glowRenderer.sharedMaterial = glowing ? _glowOnMaterial : _glowOffMaterial;
        }
    }
}
