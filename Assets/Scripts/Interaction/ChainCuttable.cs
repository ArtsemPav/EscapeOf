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
    private const float DropSoundDelay = 0.5f; // звук пола после разрыва

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Cut")]
    [Tooltip("Имя звена-точки среза. Скрывается в момент среза.")]
    [SerializeField] private string _cutLinkName = "Chain0";

    [Tooltip("Малый случайный импульс первому звену, чтобы падение не выглядело идеально вертикальным.")]
    [SerializeField] private float _cutImpulse = 0.3f;

    [Header("Links Physics")]
    [Tooltip("Масса одного звена.")]
    [SerializeField] private float _linkMass = 0.2f;

    [Tooltip("Конус качания сустава между звеньями (градусы).")]
    [SerializeField] private float _swingLimit = 60f;

    [Tooltip("Скрутка сустава между звеньями (градусы, ±).")]
    [SerializeField] private float _twistLimit = 40f;

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

        ConnectLinksWithJoints(bodies);
        ApplyCutImpulse(bodies);

        if (_chainCutClip != null && AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_chainCutClip, _demoVolume);
        if (_chainDropClip != null && AudioManager.Instance != null)
            AudioManager.Instance.StartCoroutine(PlayDropSoundDelayed());

        Debug.Log($"[{nameof(ChainCuttable)}] Цепь срезана, физика включена на {bodies.Count} звеньях.", this);
    }

    /// <summary>Задержанный звук цепи, коснувшейся пола.</summary>
    private System.Collections.IEnumerator PlayDropSoundDelayed()
    {
        yield return new WaitForSeconds(DropSoundDelay);
        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(_chainDropClip, _demoVolume);
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

            bodies.Add(body);
        }
        return bodies;
    }

    /// <summary>Связывает соседние звенья CharacterJoint'ами с якорем в точке стыка.</summary>
    private void ConnectLinksWithJoints(List<Rigidbody> bodies)
    {
        for (int i = 0; i < bodies.Count - 1; i++)
        {
            Rigidbody current = bodies[i];
            Rigidbody next = bodies[i + 1];

            var joint = current.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = next;

            Vector3 worldMid = (current.transform.position + next.transform.position) * 0.5f;
            joint.anchor = current.transform.InverseTransformPoint(worldMid);
            joint.connectedAnchor = next.transform.InverseTransformPoint(worldMid);

            SoftJointLimit swing = new SoftJointLimit { limit = _swingLimit };
            joint.swing1Limit = swing;
            joint.swing2Limit = swing;

            SoftJointLimit twistLow = new SoftJointLimit { limit = -_twistLimit };
            SoftJointLimit twistHigh = new SoftJointLimit { limit = _twistLimit };
            joint.lowTwistLimit = twistLow;
            joint.highTwistLimit = twistHigh;

            // Соседние звенья перекрываются в стыке — исключаем их коллизии,
            // чтобы цепь не дрожала и не взрывалась при старте физики.
            Physics.IgnoreCollision(current.GetComponent<Collider>(), next.GetComponent<Collider>(), true);
        }
    }

    /// <summary>Даёт первому звену лёгкий случайный импульс для естественного падения.</summary>
    private void ApplyCutImpulse(List<Rigidbody> bodies)
    {
        if (_cutImpulse <= 0f)
            return;

        Vector3 randomOffset = new Vector3(
            Random.Range(-1f, 1f), Random.Range(-0.5f, 0.5f), Random.Range(-1f, 1f));
        bodies[0].AddForce(randomOffset * _cutImpulse, ForceMode.VelocityChange);
    }
}
