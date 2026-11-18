using UnityEngine;

/// <summary>
/// Автоматический вход в режим загадки: игрок заходит в триггер-объём —
/// <see cref="PuzzleModeController.EnterPuzzleMode"/> вызывается без нажатия E.
/// Используется для загадок, которые начинаются при подходе к объекту
/// (например, цепь на дверях Tambur).
///
/// Пока режим загадки активен, свой триггер-коллайдер выключается (чтобы не
/// срабатывал повторно и не мешал). При выходе без решения — включается снова;
/// после решения остаётся выключенным навсегда.
/// </summary>
public class PuzzleModeTrigger : MonoBehaviour
{
    [Tooltip("Контроллер загадки, входящий в режим при заходе игрока в триггер.")]
    [SerializeField] private PuzzleModeController _controller;

    [Tooltip("Повторно ли разрешён вход после выхода из режима загадки (выход по Esc — триггер снова активен).")]
    [SerializeField] private bool _reenterOnExit = true;

    [Tooltip("Задержка перед входом в режим загадки, сек — даёт камере время на доезд.")]
    [SerializeField, Min(0f)] private float _enterDelay = 0f;

    private bool _armed = true;
    private Coroutine _enterRoutine;
    private Collider _ownCollider;

    private void Awake()
    {
        _ownCollider = GetComponent<Collider>();
    }

    private void Reset()
    {
        // По умолчанию объём не должен блокировать игрока.
        if (TryGetComponent(out Collider col))
            col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_armed || _controller == null)
            return;

        if (!other.CompareTag("Player"))
            return;

        // Решённая загадка не открывается повторно — разоружаемся.
        if (_controller.IsSolved && !_controller.AllowEnterWhenSolved)
        {
            _armed = false;
            SetColliderEnabled(false);
            return;
        }

        if (_enterDelay > 0f)
        {
            if (_enterRoutine == null)
                _enterRoutine = StartCoroutine(EnterAfterDelay());
        }
        else
        {
            EnterNow();
        }
    }

    private System.Collections.IEnumerator EnterAfterDelay()
    {
        yield return new WaitForSeconds(_enterDelay);
        _enterRoutine = null;
        EnterNow();
    }

    private void EnterNow()
    {
        _armed = false; // разоружаемся на время режима загадки; реарм — в HandleControllerExited
        SetColliderEnabled(false); // триггер входа выключен, пока игрок в загадке
        _controller.EnterPuzzleMode();
    }

    private void SetColliderEnabled(bool isEnabled)
    {
        if (_ownCollider != null)
            _ownCollider.enabled = isEnabled;
    }

    private void OnEnable()
    {
        if (_controller != null)
        {
            _controller.OnExited += HandleControllerExited;

            // Если загадка уже решена (загружен сейв) — триггер не нужен.
            if (_controller.IsSolved && !_controller.AllowEnterWhenSolved)
            {
                _armed = false;
                SetColliderEnabled(false);
            }
        }
    }

    private void OnDisable()
    {
        if (_controller != null)
            _controller.OnExited -= HandleControllerExited;

        if (_enterRoutine != null)
        {
            StopCoroutine(_enterRoutine);
            _enterRoutine = null;
        }
    }

    private void HandleControllerExited()
    {
        if (_reenterOnExit && !_controller.IsSolved)
        {
            _armed = true;
            SetColliderEnabled(true); // вышел не решив — триггер снова ловит игрока
        }
        // Решено — триггер остаётся выключенным.
    }
}
