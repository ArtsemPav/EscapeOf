using System;
using System.Collections;
using ChemicalPuzzle;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Загадка "Перекусить цепь" (PRF_Tambur). Игрок входит в режим загадки, перетаскивает
/// кусачки из <see cref="PuzzleInventoryBar"/> на цепь → ghost-превью над цепью →
/// на дропе проигрывается анимация (кусачки подлетают и перекусывают цепь) →
/// <see cref="ChainCuttable.CutChain"/> включает физику — цепь падает →
/// кусачки расходуются, двери отпираются через OnPuzzleSolved.
///
/// Требует <see cref="PuzzleModeController"/> на том же объекте.
/// Анимация кусачков: Animator на объекте с моделью кусачков, триггер
/// <see cref="CutTrigger"/> запускает клип перекусывания (стейт "Cut").
///
/// После решения при загрузке сейва цепь НЕ падает повторно: она сразу лежит
/// на полу в застывшей позе (<see cref="ChainCuttable.SetCutRestored"/>).
/// </summary>
[RequireComponent(typeof(PuzzleModeController))]
public class ChainCutPuzzleController : MonoBehaviour,
    IPuzzleDropHandler, IPuzzleDropTarget, IPuzzleExitGuard, ISaveable
{
    // ── Constants ────────────────────────────────────────────────────────────────

    private const string CutTrigger = "CutChain";
    private const string CutState = "Cut";
    private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";
    private const string PlayerTag = "Player";
    private const float PlayerChestHeight = 1.2f;
    private const float RaycastDistance = 40f;

    // Имена шейдерных свойств URP/Lit для приведения ghost-материала к прозрачности.
    private const string BlendSrcAlpha = "_SrcBlend";
    private const string BlendDstAlpha = "_DstBlend";
    private const string ZWriteOff = "_ZWrite";
    private const string EmissionColorId = "_EmissionColor";
    private const float CutSettleTimeout = 5f;
    private const float CutAnimationTimeout = 8f;

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Header("Save")]
    [Tooltip("Уникальный ID для системы сохранений. Должен отличаться для каждого экземпляра загадки.")]
    [SerializeField] private string _saveId = "chain_cut_puzzle";

    [Header("Item")]
    [Tooltip("ItemData кусачков, которые нужно перетащить на цепь.")]
    [SerializeField] private ItemData _requiredItem;

    [Header("Auto-Resolved (override if needed)")]
    [Tooltip("Collider цепи — цель рейкаста при дропе. Если null — ищется первый Collider на дочернем объекте с ChainCuttable.")]
    [SerializeField] private Collider _chainCollider;

    [Tooltip("Компонент цепи. Если null — ищется в детях.")]
    [SerializeField] private ChainCuttable _chain;

    [Tooltip("Animator кусачков (объект с моделью, стейт 'Cut' по триггеру CutChain). Если null — ищется в детях.")]
    [SerializeField] private Animator _cutterAnimator;

    [Tooltip("Точка, откуда кусачки стартуют анимацию. Если null — используется текущая позиция модели кусачков.")]
    [SerializeField] private Transform _cutterStartPoint;

    [Header("Ghost Preview")]
    [Tooltip("Материал ghost-превью кусачков. Если пуст — загружается CardLamp_Ghost.mat из Resources.")]
    [SerializeField] private Material _ghostMaterial;

    [Tooltip("Префаб для ghost-превью. Если пуст — используется inspectionPrefab из _requiredItem.")]
    [SerializeField] private GameObject _ghostPrefab;

    [Tooltip("Точка спавна ghost-превью. Если пуст — используется позиция _chainCollider.")]
    [SerializeField] private Transform _ghostSpawnPoint;

    [Header("Animation Timing")]
    [Tooltip("Точка среза на цепи — куда подлетают кусачки. Если пуста — используется позиция _chainCollider.")]
    [SerializeField] private Transform _chainCutPoint;

    [Tooltip("Длительность подлёта кусачков к цепи, сек.")]
    [SerializeField, Min(0.01f)] private float _flyDuration = 1f;

    [Tooltip("Поворот кусачков относительно точки среза (поза перекусывания).")]
    [SerializeField] private Vector3 _cutPointRotationOffset = Vector3.zero;

    [Tooltip("Дистанция, с которой кусачки прилетают к точке среза по направлению от игрока, м.")]
    [SerializeField, Min(0f)] private float _flyStartDistance = 0.8f;

    [Tooltip("Задержка от старта анимации перекусывания до включения физики цепи, сек. Отсчитывается от момента входа в стейт Cut, не от конца анимации — цепь падает в момент смыкания челюстей.")]
    [SerializeField] private float _cutImpactDelay = 0.2f;

    [Header("Light")]
    [Tooltip("Свет, подсвечивающий загадку. Включается на время режима загадки, выключается вне её (и на старте).")]
    [SerializeField] private Light _puzzleLight;

    [Header("Audio")]
    [SerializeField] private AudioClip _cutCompleteClip;
    [SerializeField, Range(0f, 1f)] private float _cutCompleteVolume = 1f;

    // ── State ────────────────────────────────────────────────────────────────────

    private bool _isSolved;
    private bool _isProcessing;
    private bool _isCompleting;

    private PuzzleModeController _puzzleMode;

    // Ghost preview
    private GameObject _ghostPreview;
    private Material _runtimeGhostMaterial;
    private bool _ghostVisible;

    // Поза резака из иерархии (как выставлен префаб) — основа превью.
    private Vector3 _ghostLocalPosition;
    private Quaternion _ghostLocalRotation;
    private Vector3 _ghostLocalScale;

    // Анимация кусачков
    private Transform _cutterRoot;
    private Vector3 _cutterHomePosition;
    private Quaternion _cutterHomeRotation;

    // ── Public API ───────────────────────────────────────────────────────────────

    public bool IsSolved => _isSolved;

    // ── IPuzzleDropTarget ────────────────────────────────────────────────────────

    /// <summary>Returns the localized bolt cutter drop hint.</summary>
    public string GetDropHint()
        => Localization.Get(LocalizationKeys.DropUseChainCutter);

    public bool CanAccept(ItemData item)
    {
        if (item == null || _isSolved || _isProcessing)
            return false;

        return item == _requiredItem;
    }

    // ── ISaveable ────────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { isSolved = _isSolved });
    }

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        _isSolved = data.isSolved;
    }

    [Serializable]
    private struct SaveData
    {
        public bool isSolved;
    }

    // ── Unity Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _puzzleMode = GetComponent<PuzzleModeController>();
        AutoResolveReferences();

        if (_cutterRoot != null)
        {
            _cutterHomePosition = _cutterRoot.localPosition;
            _cutterHomeRotation = _cutterRoot.localRotation;

            // Превью копирует позу резака из иерархии — как выставлен префаб bolt_cutter.
            _ghostLocalPosition = _cutterHomePosition;
            _ghostLocalRotation = _cutterHomeRotation;
            _ghostLocalScale = _cutterRoot.localScale;
        }

        // Резак в покое смотрит челюстями в точку среза — ориентируем его на цепь.
        OrientCutterHomeTowardsTarget();

        SaveManager.Instance?.Register(this);

        // Свет загадки не должен гореть вне режима загадки — гасим сразу на старте.
        SetPuzzleLightEnabled(false);

        // Коллайдер-цель дропа выключен вне загадки: иначе он перехватывает луч
        // наведения игрока и мешает взаимодействию с chainEnter (вход по E).
        SetChainColliderEnabled(false);
    }

    /// <summary>
    /// Поворачивает резак в домашней позе на точку среза (ось Z — вперёд, к цели),
    /// чтобы подлёт начинался уже направленным на цепь.
    /// </summary>
    private void OrientCutterHomeTowardsTarget()
    {
        if (_cutterRoot == null) return;

        Vector3 targetPos = _chainCutPoint != null ? _chainCutPoint.position
            : (_chainCollider != null ? _chainCollider.transform.position : Vector3.zero);
        Vector3 from = ComputeFlyAnchor();
        Vector3 dir = targetPos - from;
        if (dir.sqrMagnitude < 0.0001f) return;

        _cutterRoot.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        _cutterHomeRotation = _cutterRoot.localRotation;
    }

    private void Start()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered += HandlePuzzleEntered;
            _puzzleMode.OnExited += HandlePuzzleExited;
            _puzzleMode.OnSolved += HandlePuzzleSolved;
        }

        if (_isSolved)
            RestoreSolvedState();
    }

    private void OnDestroy()
    {
        if (_puzzleMode != null)
        {
            _puzzleMode.OnEntered -= HandlePuzzleEntered;
            _puzzleMode.OnExited -= HandlePuzzleExited;
            _puzzleMode.OnSolved -= HandlePuzzleSolved;
        }

        SaveManager.Instance?.Unregister(this);

        if (_ghostPreview != null)
            Destroy(_ghostPreview);
    }

    // ── Ghost Preview Update ─────────────────────────────────────────────────────

    private void Update()
    {
        if (_isSolved || _isProcessing || _puzzleMode == null || !_puzzleMode.IsActive)
        {
            if (_ghostVisible) SetGhostVisible(false);
            return;
        }

        if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem != null
            && CanAccept(PuzzleInventoryBar.DraggedItem))
        {
            bool isHovering = IsMouseOverChain();
            if (isHovering && !_ghostVisible)
                SetGhostVisible(true);
            else if (!isHovering && _ghostVisible)
                SetGhostVisible(false);
        }
        else if (_ghostVisible)
        {
            SetGhostVisible(false);
        }
    }

    private bool IsMouseOverChain()
    {
        if (Mouse.current == null || _chainCollider == null) return false;

        Vector2 mousePos = Mouse.current.position.ReadValue();
        if (Camera.main == null) return false;

        return RaycastHitsChain(Camera.main.ScreenPointToRay(mousePos));
    }

    /// <summary>
    /// Луч засчитывает цепь, если она попала в ЛЮБОЙ хит вдоль луча, а не только в ближайший:
    /// коллайдеры створок двери перед цепью иначе перехватывают наведение и дроп.
    /// </summary>
    private bool RaycastHitsChain(Ray ray)
    {
        if (_chainCollider == null) return false;

        int hitCount = Physics.RaycastNonAlloc(ray, RayHits, RaycastDistance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            var hit = RayHits[i];
            if (hit.collider == _chainCollider ||
                (hit.collider.transform != null && hit.collider.transform.IsChildOf(_chainCollider.transform)))
                return true;
        }
        return false;
    }

    private static readonly RaycastHit[] RayHits = new RaycastHit[32];

    private void SetGhostVisible(bool visible)
    {
        if (visible)
        {
            if (_ghostPreview == null)
                CreateGhostPreview();

            if (_ghostPreview != null)
                _ghostPreview.SetActive(true);

            _ghostVisible = true;
        }
        else
        {
            if (_ghostPreview != null)
                _ghostPreview.SetActive(false);

            _ghostVisible = false;
        }
    }

    private void CreateGhostPreview()
    {
        EnsureGhostMaterial();

        // Превью строим из выставленного на сцене резака (bolt_cutter).
        GameObject source = _cutterRoot != null ? _cutterRoot.gameObject : null;
        if (source == null)
            source = _ghostPrefab != null ? _ghostPrefab
                : (_requiredItem != null ? _requiredItem.inspectionPrefab : null);
        if (source == null) return;

        _ghostPreview = Instantiate(source, source.transform.parent);
        _ghostPreview.name = "ChainCutterGhost";

        // Поза и масштаб — ровно как выставлен префаб bolt_cutter в иерархии
        // (программный доворот OrientCutterHomeTowardsTarget в превью не применяется).
        _ghostPreview.transform.localPosition = _ghostLocalPosition;
        _ghostPreview.transform.localRotation = _ghostLocalRotation;
        _ghostPreview.transform.localScale = _ghostLocalScale;

        // Статичная модель: аниматор удаляем — поза замораживается, ничего не проигрывается.
        foreach (var anim in _ghostPreview.GetComponentsInChildren<Animator>(true))
            Destroy(anim);

        foreach (var col in _ghostPreview.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        if (_runtimeGhostMaterial != null)
        {
            foreach (var rend in _ghostPreview.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = _runtimeGhostMaterial;
                rend.sharedMaterials = mats;
            }
        }

        _ghostPreview.SetActive(false);
    }

    /// <summary>Загружает материал ghost-превью и приводит его к честной прозрачности.</summary>
    private void EnsureGhostMaterial()
    {
        if (_runtimeGhostMaterial != null) return;

        if (_ghostMaterial != null)
        {
            _runtimeGhostMaterial = _ghostMaterial;
        }
        else
        {
            _runtimeGhostMaterial = Resources.Load<Material>(GhostMaterialPath);
        }

        if (_runtimeGhostMaterial == null) return;

        // URP-валидация при импорте возвращает премультипликацию и эмиссию — приводим
        // материал к классической прозрачности в рантайме, чтобы превью не было «плотным».
        _runtimeGhostMaterial.shaderKeywords = new[]
        {
            "_SURFACE_TYPE_TRANSPARENT", "_SPECULARHIGHLIGHTS_OFF"
        };
        _runtimeGhostMaterial.SetFloat(BlendSrcAlpha, 5f);        // SrcBlend = SrcAlpha
        _runtimeGhostMaterial.SetFloat(BlendDstAlpha, 10f);       // DstBlend = OneMinusSrcAlpha
        _runtimeGhostMaterial.SetFloat(ZWriteOff, 0f);
        _runtimeGhostMaterial.SetColor(EmissionColorId, Color.clear);
    }

    // ── Auto-Resolve ─────────────────────────────────────────────────────────────

    private void AutoResolveReferences()
    {
        if (_chain == null)
            _chain = GetComponentInChildren<ChainCuttable>(true);

        if (_chainCollider == null && _chain != null)
            _chainCollider = _chain.GetComponent<Collider>();

        if (_chainCollider == null)
            _chainCollider = GetComponentInChildren<Collider>();

        if (_cutterAnimator == null)
            _cutterAnimator = GetComponentInChildren<Animator>(true);

        if (_cutterRoot == null && _cutterAnimator != null)
            _cutterRoot = _cutterAnimator.transform;
    }

    // ── IPuzzleDropHandler ───────────────────────────────────────────────────────

    public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
    {
        replacement = item; // кусачки расходуются только при победе (CutSequence)

        if (_isProcessing || _isSolved)
            return false;

        if (item == null || item != _requiredItem)
            return false;

        if (!PerformRaycast(screenPosition))
            return false;

        _isProcessing = true;

        if (_ghostVisible) SetGhostVisible(false);

        PuzzleInventoryBar.Instance?.Hide();

        StartCoroutine(CutSequence());
        return true;
    }

    // ── IPuzzleExitGuard ─────────────────────────────────────────────────────────

    public bool CanExitPuzzle()
    {
        // Блокируем выход только во время финальной фазы (цепь падает, дверь отпирается).
        return !_isCompleting;
    }

    // ── Puzzle Flow ──────────────────────────────────────────────────────────────

    private void HandlePuzzleEntered()
    {
        SetPuzzleLightEnabled(true);
        SetChainColliderEnabled(true);
        // Ничего не запускаем — срез начинается только после дропа кусачков.
    }

    private void HandlePuzzleExited()
    {
        StopAllCoroutines();

        SetPuzzleLightEnabled(false);
        SetChainColliderEnabled(false);

        if (_ghostVisible) SetGhostVisible(false);

        if (_isProcessing && !_isSolved)
        {
            // Прерывание во время анимации кусачков — возвращаем модель на место.
            ResetCutterToHome();
            _isProcessing = false;
            _isCompleting = false;
        }
    }

    private void HandlePuzzleSolved()
    {
        // Резервный обработчик: уведомление системы о решении.
    }

    private IEnumerator CutSequence()
    {
        // 1. Кусачки стартуют от игрока (по направлению к точке среза) и подлетают к ней.
        if (_cutterRoot != null)
        {
            Transform flyTarget = _chainCutPoint != null ? _chainCutPoint
                : (_chainCollider != null ? _chainCollider.transform : null);

            // Стартовая точка — от игрока (его позиция на момент дропа) в сторону цепи.
            Vector3 startAnchor = ComputeFlyAnchor();
            Vector3 startPos = ComputeFlyStart(startAnchor, flyTarget);
            Quaternion startRot = _cutterRoot.rotation;

            _cutterRoot.gameObject.SetActive(true);
            _cutterAnimator?.gameObject.SetActive(true);

            if (flyTarget != null && _flyDuration > 0f)
                yield return FlyToTarget(startPos, startRot, flyTarget);

            // 2. Челюсти смыкаются после подлёта — стейт "Cut" по триггеру.
            if (_cutterAnimator != null)
            {
                _cutterAnimator.SetTrigger(CutTrigger);

                yield return new WaitUntil(() =>
                    _cutterAnimator != null &&
                    _cutterAnimator.GetCurrentAnimatorStateInfo(0).IsName(CutState));

                // 3. Физика цепи включается по задержке от НАЧАЛА анимации — в момент
                //    смыкания челюстей, не дожидаясь её полного проигрывания.
                yield return new WaitForSeconds(_cutImpactDelay);

                // 4. Параллельно дожидаемся конца анимации (для завершающей секвенции),
                //    но падение цепи ей больше не блокируется.
                float animStart = Time.time;
                yield return new WaitUntil(() =>
                {
                    if (_cutterAnimator == null) return true;
                    var state = _cutterAnimator.GetCurrentAnimatorStateInfo(0);
                    return !state.IsName(CutState)
                        || state.normalizedTime >= 1f
                        || Time.time - animStart > CutAnimationTimeout;
                });
            }
            else
            {
                // Аниматора нет — оставляем прежнюю задержку как единственный тайминг.
                yield return new WaitForSeconds(_cutImpactDelay);
            }
        }

        // 4. Физика цепи: срез, падение, застывание. Ждём OnCutSettled.
        if (_chain != null)
        {
            bool settled = false;
            void OnSettled() => settled = true;
            _chain.OnCutSettled += OnSettled;

            _chain.CutChain();

            float start = Time.time;
            while (!settled && Time.time - start < CutSettleTimeout)
                yield return new WaitForSeconds(0.2f);

            _chain.OnCutSettled -= OnSettled;
        }

        yield return CompletionSequence();
    }

    /// <summary>
    /// Якорь направления подлёта: грудь игрока (резак «вылетает из рук»).
    /// Если игрок не найден — статичная точка старта.
    /// </summary>
    private Vector3 ComputeFlyAnchor()
    {
        var player = GameObject.FindGameObjectWithTag(PlayerTag);
        if (player != null)
            return player.transform.position + Vector3.up * PlayerChestHeight;

        return _cutterStartPoint != null ? _cutterStartPoint.position : _cutterRoot.position;
    }

    /// <summary>Стартовая точка подлёта: на дистанции _flyStartDistance от цели в сторону игрока/якоря.</summary>
    private Vector3 ComputeFlyStart(Vector3 anchor, Transform flyTarget)
    {
        if (flyTarget == null) return anchor;

        Vector3 toAnchor = anchor - flyTarget.position;
        if (toAnchor.sqrMagnitude < 0.0001f)
            return anchor;

        return flyTarget.position + toAnchor.normalized * _flyStartDistance;
    }

    /// <summary>Твин подлёта кусачков к точке среза (SmoothStep по позиции и повороту).</summary>
    private IEnumerator FlyToTarget(Vector3 startPos, Quaternion startRot, Transform target)
    {
        Vector3 endPos = target.position;
        Quaternion endRot = target.rotation * Quaternion.Euler(_cutPointRotationOffset);

        float elapsed = 0f;
        while (elapsed < _flyDuration)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _flyDuration);
            _cutterRoot.position = Vector3.Lerp(startPos, endPos, t);
            _cutterRoot.rotation = Quaternion.Slerp(startRot, endRot, t);
            elapsed += Time.deltaTime;
            yield return null;
        }

        _cutterRoot.position = endPos;
        _cutterRoot.rotation = endRot;
    }

    private IEnumerator CompletionSequence()
    {
        _isCompleting = true;

        if (_cutCompleteClip != null)
            AudioManager.Instance?.PlaySFX(_cutCompleteClip, _cutCompleteVolume);

        // Прячем кусачки после анимации — они «использованы».
        if (_cutterRoot != null)
            _cutterRoot.gameObject.SetActive(false);

        _isSolved = true;
        _isProcessing = false;

        // Кусачки израсходованы — удаляем их из инвентаря.
        if (_requiredItem != null)
            InventorySystem.Instance?.RemoveItem(_requiredItem);

        Debug.Log("Пазл решен");

        SaveManager.Instance?.Save();
        _puzzleMode?.SetSolved(); // SetSolved запускает OnPuzzleSolved → UnlockAndOpen дверей

        yield return null;

        _isCompleting = false;
    }

    /// <summary>Возвращает модель кусачков в исходную позу (при прерывании).</summary>
    private void ResetCutterToHome()
    {
        if (_cutterRoot == null) return;

        _cutterRoot.gameObject.SetActive(false);
        _cutterRoot.localPosition = _cutterHomePosition;
        _cutterRoot.localRotation = _cutterHomeRotation;
    }

    /// <summary>Включает или выключает свет, подсвечивающий загадку.</summary>
    private void SetPuzzleLightEnabled(bool isEnabled)
    {
        if (_puzzleLight != null)
            _puzzleLight.enabled = isEnabled;
    }

    /// <summary>
    /// Включает или выключает коллайдер-цель дропа на цепи. Вне загадки он выключен,
    /// чтобы не перехватывать луч наведения игрока у chainEnter (вход по E).
    /// </summary>
    private void SetChainColliderEnabled(bool isEnabled)
    {
        if (_chainCollider != null)
            _chainCollider.enabled = isEnabled;
    }

    // ── Solved Restore ───────────────────────────────────────────────────────────

    /// <summary>
    /// Восстановление решённой загадки при загрузке сейва: цепь сразу лежит на полу
    /// в застывшей позе (без повторного падения), кусачки спрятаны.
    /// </summary>
    private void RestoreSolvedState()
    {
        if (_chain != null)
            _chain.SetCutRestored();

        if (_cutterRoot != null)
            _cutterRoot.gameObject.SetActive(false);
    }

    // ── Raycast ──────────────────────────────────────────────────────────────────

    private bool PerformRaycast(Vector2 screenPos)
    {
        if (Camera.main == null) return false;
        return RaycastHitsChain(Camera.main.ScreenPointToRay(screenPos));
    }
}
