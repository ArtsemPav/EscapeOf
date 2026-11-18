using System.Collections;
using UnityEngine;

/// <summary>
/// Физическая реакция предмета на землетрясение. Вешается на отдельный предмет
/// (колбу, бутылку, инструмент и т.п.). В момент старта землетрясения добавляет
/// предмету Rigidbody (и BoxCollider по габаритам рендереров, если своего коллайдера нет),
/// периодически толкает предмет случайными импульсами, сила которых масштабируется
/// огибающей землетрясения (fade-in/fade-out). После окончания Rigidbody и добавленный
/// коллайдер удаляются — предмет остаётся там, куда его привела физика.
/// Запуск и остановка приходят статическими событиями от EarthquakeFOVPulse,
/// поэтому защита «один раз за сейв» и тайминги общие с тряской камеры.
/// </summary>
public class EarthquakePhysicsProp : MonoBehaviour
{
    [Header("Impulse")]
    [Tooltip("Максимальная сила одного случайного импульса (при полном усилении огибающей).")]
    [SerializeField, Min(0f)] private float _impulseForce = 0.5f;

    [Tooltip("Средняя пауза между импульсами (секунды). Реальная пауза случайна в пределах ±30%.")]
    [SerializeField, Min(0.05f)] private float _impulseInterval = 0.2f;

    [Tooltip("Вертикальная составляющая импульса: 0 — только горизонтальные толчки, больше — предмет подпрыгивает.")]
    [SerializeField, Min(0f)] private float _upwardBias = 0.4f;

    [Tooltip("Максимальный вращательный импульс (закручивание предмета).")]
    [SerializeField, Min(0f)] private float _torqueForce = 0.15f;

    [Header("Rigidbody")]
    [Tooltip("Масса добавляемого Rigidbody.")]
    [SerializeField, Min(0.05f)] private float _mass = 1f;

    [Tooltip("Гравитация у добавляемого Rigidbody.")]
    [SerializeField] private bool _useGravity = true;

    private Rigidbody _addedBody;
    private Collider _addedCollider;
    private Coroutine _shakeRoutine;

    private void OnEnable()
    {
        EarthquakeFOVPulse.ShakeStarted += OnShakeStarted;
        EarthquakeFOVPulse.ShakeEnded += OnShakeEnded;
    }

    private void OnDisable()
    {
        EarthquakeFOVPulse.ShakeStarted -= OnShakeStarted;
        EarthquakeFOVPulse.ShakeEnded -= OnShakeEnded;
        StopShake();
    }

    /// <summary>
    /// Реакция на старт землетрясения: подготовить физику и запустить цикл толчков.
    /// Если у предмета нет коллайдера — создаётся BoxCollider по габаритам рендереров.
    /// </summary>
    private void OnShakeStarted(float duration, float fadeIn, float fadeOut)
    {
        StopShake();

        Rigidbody body = GetOrAddBody();
        if (body == null)
        {
            Debug.LogWarning($"[{nameof(EarthquakePhysicsProp)}] '{name}' has no renderers for a fallback collider — prop skipped.", this);
            return;
        }

        _shakeRoutine = StartCoroutine(ShakeRoutine(body, duration, fadeIn, fadeOut));
    }

    /// <summary>Реакция на конец землетрясения: снять добавленную физику.</summary>
    private void OnShakeEnded()
    {
        StopShake();
    }

    /// <summary>
    /// Найти существующий Rigidbody или создать новый вместе с коллайдером.
    /// Существующий Rigidbody не изменяется и не удаляется.
    /// </summary>
    private Rigidbody GetOrAddBody()
    {
        Rigidbody existing = GetComponent<Rigidbody>();
        if (existing != null)
            return existing;

        if (!HasSolidCollider())
        {
            _addedCollider = CreateBoundsCollider();
            if (_addedCollider == null)
                return null;
        }

        _addedBody = gameObject.AddComponent<Rigidbody>();
        _addedBody.mass = _mass;
        _addedBody.useGravity = _useGravity;
        _addedBody.interpolation = RigidbodyInterpolation.Interpolate;
        return _addedBody;
    }

    /// <summary>Есть ли на предмете или его детях хотя бы один нетриггерный коллайдер.</summary>
    private bool HasSolidCollider()
    {
        foreach (Collider collider in GetComponentsInChildren<Collider>())
        {
            if (!collider.isTrigger)
                return true;
        }

        return false;
    }

    /// <summary>Создать BoxCollider, охватывающий все рендереры предмета.</summary>
    private Collider CreateBoundsCollider()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return null;

        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(bounds.center);

        Vector3 lossyScale = transform.lossyScale;
        Vector3 size = transform.InverseTransformDirection(bounds.size);
        box.size = new Vector3(
            Mathf.Abs(size.x / Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f)),
            Mathf.Abs(size.y / Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f)),
            Mathf.Abs(size.z / Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f)));

        return box;
    }

    private IEnumerator ShakeRoutine(Rigidbody body, float duration, float fadeIn, float fadeOut)
    {
        float elapsed = 0f;
        float nextImpulseTime = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float fadeInFactor = fadeIn > 0f ? Mathf.Clamp01(elapsed / fadeIn) : 1f;
            float fadeOutFactor = fadeOut > 0f ? Mathf.Clamp01((duration - elapsed) / fadeOut) : 1f;
            float envelope = Mathf.Min(fadeInFactor, fadeOutFactor);

            if (elapsed >= nextImpulseTime)
            {
                ApplyImpulse(body, envelope);
                nextImpulseTime = elapsed + _impulseInterval * Random.Range(0.7f, 1.4f);
            }

            yield return null;
        }

        _shakeRoutine = null;
        StopShake();
    }

    /// <summary>Один случайный толчок с силой, масштабированной огибающей землетрясения.</summary>
    private void ApplyImpulse(Rigidbody body, float envelope)
    {
        Vector3 direction = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(0f, _upwardBias),
            Random.Range(-1f, 1f)).normalized;

        body.AddForce(direction * (_impulseForce * envelope), ForceMode.Impulse);
        body.AddTorque(Random.insideUnitSphere * (_torqueForce * envelope), ForceMode.Impulse);
    }

    /// <summary>Остановить тряску и удалить добавленные Rigidbody и коллайдер.</summary>
    private void StopShake()
    {
        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            _shakeRoutine = null;
        }

        if (_addedBody != null)
        {
            Destroy(_addedBody);
            _addedBody = null;
        }

        if (_addedCollider != null)
        {
            Destroy(_addedCollider);
            _addedCollider = null;
        }
    }
}
