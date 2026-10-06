using UnityEngine;

/// <summary>
/// Вращает объект вокруг локальной оси (имитация вращения кинобабины).
/// Работает только при включённом питании — реализует IPowerConsumer
/// и регистрируется в LightingSystem в Awake.
/// Компонент отключает себя при отсутствии питания и после регистрации,
/// пока питание не придёт — включайте извне (например, FilmReelInstaller).
/// </summary>
public class ReelSpinner : MonoBehaviour, IPowerConsumer
{
    [Header("Rotation")]
    [Tooltip("Локальная ось вращения бабины.")]
    [SerializeField] private Vector3 _localAxis = Vector3.right;

    [Tooltip("Скорость вращения в градусах в секунду.")]
    [SerializeField] private float _degreesPerSecond = 120f;

    [Header("Power")]
    [Tooltip("Включать ли вращение автоматически при включении света. " +
             "Если false — вращение включается извне через SetSpinning (например, после установки бабины).")]
    [SerializeField] private bool _spinOnPower = true;

    private bool _isPowered;
    private bool _spinRequested; // вращение разрешено извне (SetSpinning)

    private void Awake()
    {
        LightingSystem.Instance?.RegisterConsumer(this);
        enabled = false;
    }

    private void OnDestroy() => LightingSystem.Instance?.UnregisterConsumer(this);

    private void Update() =>
        transform.Rotate(_localAxis, _degreesPerSecond * Time.deltaTime, Space.Self);

    /// <summary>
    /// Called by LightingSystem when master power changes (and once on registration).
    /// Спиннер активен только при включённом питании и если вращение разрешено
    /// (_spinOnPower или SetSpinning(true)).
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        _isPowered = isPowered;
        enabled = isPowered && (_spinOnPower || _spinRequested);
    }

    /// <summary>
    /// Разрешает или запрещает вращение извне (например, FilmProjectorPuzzleController
    /// после установки бабины). Вращение работает только при включённом питании,
    /// но «разрешение» сохраняется и применяется при последующем включении света.
    /// </summary>
    public void SetSpinning(bool spinning)
    {
        _spinRequested = spinning;
        enabled = _isPowered && (_spinOnPower || _spinRequested);
    }

    /// <summary>Включено ли сейчас питание (для внешних проверок).</summary>
    public bool IsPowered => _isPowered;
}
