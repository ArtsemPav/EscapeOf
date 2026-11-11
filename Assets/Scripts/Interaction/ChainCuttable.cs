using System.Collections.Generic;
using Escape.Core;
using UnityEngine;

/// <summary>
/// Тестовая версия: цепь висит как статичные меши; при вызове <see cref="CutChain"/>
/// звено-точка среза скрывается, остальным звеньям включается физика
/// (Rigidbody + convex MeshCollider), соседние звенья связываются CharacterJoint'ами —
/// цепь естественно падает на пол. Настройка загадки (кускачки, puzzle mode) будет
/// добавлена позже поверх этого скрипта.
/// </summary>
public class ChainCuttable : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────────────────────────

    private const string LinksNodeName = "chain";
    private const float MaxAngularVelocity = 30f;

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Cut")]
    [Tooltip("Имя звена-точки среза. Скрывается в момент среза.")]
    [SerializeField] private string _cutLinkName = "Chain0";

    [Tooltip("Малый случайный импульс первому звену, чтобы падение не выглядело идеально вертикальным.")]
    [SerializeField] private float _cutImpulse = 0.3f;

    [Header("Links Physics")]
    [Tooltip("Масса одного звена.")]
    [SerializeField] private float _linkMass = 0.2f;

    [Tooltip("Итерации позиционного солвера на звене — стабильность суставов при сильной натяжке.")]
    [SerializeField] [Min(1)] private int _solverIterations = 30;

    [Tooltip("Итерации солвера скоростей на звене — мягкость и предсказуемость суставов.")]
    [SerializeField] [Min(1)] private int _solverVelocityIterations = 15;

    [Tooltip("Максимальная скорость выталкивания из коллизий (м/с). Меньше — мягче расправляется переплетение звеньев.")]
    [SerializeField] [Min(0.01f)] private float _maxDepenetrationVelocity = 0.5f;

    [Tooltip("Дистанция проекции (м): звено дальше стыка мгновенно возвращается на место.")]
    [SerializeField] [Min(0.001f)] private float _projectionDistance = 0.02f;

    [Tooltip("Угол проекции (градусы): при превышении ориентация звена мгновенно выправляется.")]
    [SerializeField] [Min(1f)] private float _projectionAngle = 30f;

    [Tooltip("Стартовая скорость цепи вниз при срезе (м/с) — помогает свисающей петле сорваться с ручек.")]
    [SerializeField] [Min(0f)] private float _initialDropSpeed = 1.5f;

    [Tooltip("Через сколько после среза играет звук удара о пол, сек.")]
    [SerializeField] [Min(0.1f)] private float _dropSoundDelay = 0.5f;

    [Tooltip("Страховочный таймаут ожидания касания пола всеми звеньями, сек — после него цепь замораживается принудительно.")]
    [SerializeField] [Min(0.5f)] private float _settleTimeout = 2f;

    [Tooltip("Порядок соединения звеньев суставами. Hierarchy — как звенья расставлены в узле (обычно это и есть порядок цепи). Spatial — жадный обход по пространству от точки среза (если расставлял не по порядку).")]
    [SerializeField] private LinkOrder _linkOrder = LinkOrder.Hierarchy;

    /// <summary>Порядок соединения звеньев суставами.</summary>
    private enum LinkOrder { Hierarchy, SpatialNearestFromCut }

    [Header("Demo: Door-Tug Break (easy to remove)")]
    [Tooltip("Включить демо-режим: дверь начала открываться → цепь рвётся сама. Для плейтеста выключи.")]
    [SerializeField] private bool _demoDoorTugBreak = true;

    [Tooltip("Дверь (DoorInteraction), рывок которой рвёт цепь. Если пусто — берётся родитель.")]
    [SerializeField] private DoorInteraction _demoDoor;

    [Tooltip("Задержка от начала открытия двери до разрыва, сек (рывок цепи).")]
    [SerializeField] [Min(0f)] private float _demoBreakDelay = 0.05f;

    [Header("Demo Audio")]
    [Tooltip("Звук рывка цепи, когда дверь тронули.")]
    [SerializeField] private AudioClip _doorTugClip;

    [Tooltip("Звук разрыва цепи.")]
    [SerializeField] private AudioClip _chainCutClip;

    [Tooltip("Звук падения цепи на пол.")]
    [SerializeField] private AudioClip _chainDropClip;

    [Tooltip("Громкость демо-звуков.")]
    [Range(0f, 1f)] [SerializeField] private float _demoVolume = 0.8f;

    // ── State ────────────────────────────────────────────────────────────────────

    private bool _isCut;
    private bool _demoArmed = true;
    private DoorInteraction[] _demoDoorsCache; // фолбэк-набор дверей комнаты

    /// <summary>Цепь уже срезана?</summary>
    public bool IsCut => _isCut;

    // ── Unity lifecycle ─────────────────────────────────────────────────────────

    /// <summary>Демо-режим: любая отслеживаемая дверь начала открываться → рывок → разрыв цепи.</summary>
    private void Update()
    {
        if (!_demoDoorTugBreak || !_demoArmed || _isCut)
            return;

        foreach (DoorInteraction door in ResolveDemoDoors())
        {
            // Порог: дверь сдвинулась хоть немного — считаем, что её начали открывать.
            if (door != null && !door.IsFullyClosed)
            {
                _demoArmed = false;
                OnDemoDoorTugged();
                break;
            }
        }
    }

    // ── Demo mode ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Возвращает отслеживаемые демо-двери: назначенная в инспекторе, иначе —
    /// все DoorInteraction ближайшего общего предка с цепью (дверь — сосед по комнате).
    /// </summary>
    private IEnumerable<DoorInteraction> ResolveDemoDoors()
    {
        if (_demoDoor != null)
        {
            yield return _demoDoor;
            yield break;
        }

        if (_demoDoorsCache == null)
        {
            // Поднимаемся вверх до ближайшего узла, в детях которого есть дверь —
            // это корень комнаты, где дверь и цепь соседи.
            Transform searchRoot = transform;
            while (searchRoot.parent != null &&
                   searchRoot.GetComponentsInChildren<DoorInteraction>(true).Length == 0)
            {
                searchRoot = searchRoot.parent;
            }

            _demoDoorsCache = searchRoot.GetComponentsInChildren<DoorInteraction>(true);
            if (_demoDoorsCache.Length == 0)
                Debug.LogWarning($"[{nameof(ChainCuttable)}] Демо-режим: двери не найдены.", this);
        }

        foreach (DoorInteraction door in _demoDoorsCache)
            yield return door;
    }

    /// <summary>Дверь тронули: рывок цепи (звук), затем разрыв после _demoBreakDelay.</summary>
    private void OnDemoDoorTugged()
    {
        if (_doorTugClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_doorTugClip, _demoVolume);

        StartCoroutine(DemoBreakAfterDelay());
    }

    /// <summary>Пауза-рывок между началом открытия двери и разрывом цепи.</summary>
    private System.Collections.IEnumerator DemoBreakAfterDelay()
    {
        yield return new WaitForSeconds(_demoBreakDelay);
        CutChain();
    }

    /// <summary>
    /// Срезает цепь: скрывает звено-точку среза, включает физику остальных звеньев
    /// и связывает их суставами. Повторный вызов игнорирует.
    /// </summary>
    [ContextMenu("Test Cut Chain")]
    public void CutChain()
    {
        if (_isCut)
            return;
        _isCut = true;

        Transform linksNode = FindLinksNode();
        if (linksNode == null)
        {
            Debug.LogWarning($"[{nameof(ChainCuttable)}] Узел со звеньями '{LinksNodeName}' не найден.", this);
            return;
        }

        HideCutLink(linksNode);

        List<Rigidbody> bodies = EnablePhysicsOnLinks(linksNode);
        if (bodies.Count == 0)
        {
            Debug.LogWarning($"[{nameof(ChainCuttable)}] Звенья не найдены под '{linksNode.name}'.", this);
            return;
        }

        ConnectLinksWithJoints(ApplyLinkOrder(bodies));
        ApplyCutImpulse(bodies);

        StartCoroutine(PlayDropSoundAndFreeze(bodies));
        foreach (Rigidbody body in bodies)
            body.gameObject.AddComponent<ChainLinkFloorProbe>();

        if (_chainCutClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_chainCutClip, _demoVolume);

        Debug.Log($"[{nameof(ChainCuttable)}] Цепь срезана, физика включена на {bodies.Count} звеньях.", this);
    }

    // ── Internals ────────────────────────────────────────────────────────────────

    /// <summary>Находит узел, содержащий звенья цепи (дочерний 'chain' или сам объект).</summary>
    private Transform FindLinksNode()
    {
        Transform linksNode = transform.Find(LinksNodeName);
        return linksNode != null ? linksNode : transform;
    }

    /// <summary>Скрывает звено-точку среза.</summary>
    private void HideCutLink(Transform linksNode)
    {
        Transform cutLink = linksNode.Find(_cutLinkName);
        if (cutLink != null)
            cutLink.gameObject.SetActive(false);
        else
            Debug.LogWarning($"[{nameof(ChainCuttable)}] Звено среза '{_cutLinkName}' не найдено.", this);
    }

    /// <summary>Включает Rigidbody и convex MeshCollider на каждом звене-меше. Возвращает список тел в порядке иерархии.</summary>
    private List<Rigidbody> EnablePhysicsOnLinks(Transform linksNode)
    {
        var bodies = new List<Rigidbody>();
        foreach (Transform child in linksNode)
        {
            if (!child.gameObject.activeSelf || child.GetComponent<MeshFilter>() == null)
                continue;

            MeshCollider collider = child.gameObject.AddComponent<MeshCollider>();
            collider.convex = true;

            Rigidbody body = child.gameObject.AddComponent<Rigidbody>();
            body.mass = _linkMass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.maxAngularVelocity = MaxAngularVelocity;
            // Устойчивость: суставы цепи «мягкие», а звенья стартуют с переплетением —
            // повышаем итерации солвера и гасим выталкивание из коллизий, чтобы
            // цепь не «разрывалась» и не собиралась обратно при включении физики.
            body.solverIterations = _solverIterations;
            body.solverVelocityIterations = _solverVelocityIterations;
            body.maxDepenetrationVelocity = _maxDepenetrationVelocity;

            bodies.Add(body);
        }

        ApplyFrictionlessMaterial(bodies);
        return bodies;
    }

    // Скольжение: цепь срывается с ручек и соскальзывает по двери, а не цепляется.
    private const float FrictionlessBounciness = 0.15f;
    private const string FrictionlessMaterialName = "ChainLink_Frictionless";

    /// <summary>
    /// Назначает звеньям физматериал без трения (и с лёгкой упругостью металла):
    /// нижняя петля цепи силой тяжести стягивает свисающие куски с ручек —
    /// цепь соскальзывает целиком вместо того, чтобы висеть на ручке.
    /// </summary>
    private void ApplyFrictionlessMaterial(List<Rigidbody> bodies)
    {
        var frictionless = new PhysicsMaterial(FrictionlessMaterialName)
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = FrictionlessBounciness,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Average,
        };

        foreach (Rigidbody body in bodies)
        {
            Collider linkCollider = body.GetComponent<Collider>();
            if (linkCollider != null)
                linkCollider.material = frictionless;
        }
    }

    /// <summary>Возвращает тела в порядке соединения суставами — по выбору _linkOrder.</summary>
    private List<Rigidbody> ApplyLinkOrder(List<Rigidbody> bodies)
    {
        return _linkOrder == LinkOrder.SpatialNearestFromCut
            ? SortLinksAlongChain(bodies)
            : bodies;
    }

    /// <summary>
    /// Сортирует тела вдоль цепи: жадный обход по пространственной смежности,
    /// начиная с точки среза. Порядок иерархии может не совпадать с порядком
    /// звеньев вдоль цепи — суставы по иерархии соединяют чужие звенья,
    /// и цепь распадается на сегменты.
    /// </summary>
    private List<Rigidbody> SortLinksAlongChain(List<Rigidbody> bodies)
    {
        if (bodies.Count < 3)
            return bodies;

        // Старт — звено, ближайшее к точке среза (Chain0).
        Vector3 cutPoint = bodies[0].position;
        var remaining = new List<Rigidbody>(bodies);
        var sorted = new List<Rigidbody>(bodies.Count);

        Rigidbody current = FindNearest(remaining, cutPoint);
        sorted.Add(current);
        remaining.Remove(current);

        while (remaining.Count > 0)
        {
            current = FindNearest(remaining, current.position);
            sorted.Add(current);
            remaining.Remove(current);
        }
        return sorted;
    }

    /// <summary>Ближайшее тело к точке.</summary>
    private Rigidbody FindNearest(List<Rigidbody> candidates, Vector3 point)
    {
        Rigidbody nearest = candidates[0];
        float nearestDist = (nearest.position - point).sqrMagnitude;
        foreach (Rigidbody candidate in candidates)
        {
            float dist = (candidate.position - point).sqrMagnitude;
            if (dist < nearestDist)
            {
                nearest = candidate;
                nearestDist = dist;
            }
        }
        return nearest;
    }

    /// <summary>Связывает соседние звенья ConfigurableJoint'ами с проекцией и жёстким лимитом дистанции.</summary>
    private void ConnectLinksWithJoints(List<Rigidbody> bodies)
    {
        for (int i = 0; i < bodies.Count - 1; i++)
        {
            Rigidbody current = bodies[i];
            Rigidbody next = bodies[i + 1];

            Vector3 worldMid = (current.transform.position + next.transform.position) * 0.5f;

            var joint = current.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = next;
            joint.anchor = current.transform.InverseTransformPoint(worldMid);
            joint.connectedAnchor = next.transform.InverseTransformPoint(worldMid);

            // Позиция: залочена полностью — дистанция между звеньями жёсткая,
            // сустав ведёт себя как настоящее звено цепи. Линейные лимиты не
            // используем: ConfigurableJoint масштабирует их скейлом тела,
            // что и давало «рассыпание» при звеньях со scale 3.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            // Вращение: свободный шар — самый стабильный вариант для цепи,
            // угловые лимиты-конусы в переплетённой цепи конфликтуют и дрожат.
            joint.angularXMotion = ConfigurableJointMotion.Free;
            joint.angularYMotion = ConfigurableJointMotion.Free;
            joint.angularZMotion = ConfigurableJointMotion.Free;

            // Проекция-страховка: если звено всё же дрейфовало от стыка —
            // мгновенный возврат. Позиция залочена, поэтому проекция сработает
            // только на остаточный дрейф солвера, не раньше.
            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = _projectionDistance;
            joint.projectionAngle = _projectionAngle;

            // Сустав не должен ломаться от нагрузки.
            joint.breakForce = Mathf.Infinity;
            joint.breakTorque = Mathf.Infinity;

            // Препроцессинг сустава на переплетённой цепи даёт джиттер — отключаем.
            joint.enablePreprocessing = false;
        }

        // Переплетённые звенья перекрываются коллайдерами не только с соседями
        // (convex-халл звена залезает на звено через одно) — исключаем коллизии
        // между ВСЕМИ парами, иначе физика при старте взрывообразно расталкивает
        // цепь и суставы потом стягивают её обратно («рассыпалась и собралась»).
        for (int i = 0; i < bodies.Count; i++)
        {
            for (int j = i + 1; j < bodies.Count; j++)
            {
                Collider a = bodies[i].GetComponent<Collider>();
                Collider b = bodies[j].GetComponent<Collider>();
                if (a != null && b != null)
                    Physics.IgnoreCollision(a, b, true);
            }
        }
    }

    /// <summary>Стартовая скорость вниз + лёгкий случайный импульс первому звену для естественного срыва цепи.</summary>
    private void ApplyCutImpulse(List<Rigidbody> bodies)
    {
        foreach (Rigidbody body in bodies)
        {
            if (_initialDropSpeed > 0f)
                body.AddForce(Vector3.down * _initialDropSpeed, ForceMode.VelocityChange);

            if (_cutImpulse > 0f)
            {
                Vector3 randomOffset = new Vector3(
                    Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));
                body.AddForce(randomOffset * _cutImpulse, ForceMode.VelocityChange);
            }
        }
    }

    /// <summary>
    /// Звук удара о пол, затем ожидание: все звенья должны коснуться пола
    /// (или страховочный таймаут) — только после этого физика останавливается.
    /// </summary>
    private System.Collections.IEnumerator PlayDropSoundAndFreeze(List<Rigidbody> bodies)
    {
        yield return new WaitForSeconds(_dropSoundDelay);

        if (_chainDropClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_chainDropClip, _demoVolume);

        // Ждём, пока все звенья лягут на пол (или таймаут — страховка от «вечного звена»).
        float start = Time.time;
        while (Time.time - start < _settleTimeout)
        {
            bool allTouched = true;
            foreach (Rigidbody body in bodies)
            {
                var probe = body.GetComponent<ChainLinkFloorProbe>();
                if (probe == null || !probe.TouchedFloor) { allTouched = false; break; }
            }
            if (allTouched)
                break;

            yield return new WaitForSeconds(0.1f);
        }

        foreach (Rigidbody body in bodies)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true; // заморозка позы — физика цепи закончена
        }
        Debug.Log($"[{nameof(ChainCuttable)}] Цепь улеглась и заморожена ({bodies.Count} звеньев).", this);
    }
}
