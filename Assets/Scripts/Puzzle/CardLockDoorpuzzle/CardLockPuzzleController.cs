using UnityEngine;
using UnityEngine.InputSystem;
using Escape.Core;
using System.Collections;

namespace Escape.Puzzle
{
    /// <summary>
    /// Загадка с карт-ридером. Перетащите префаб в сцену и назначьте _targetDoor и _requiredCard.
    /// Все внутренние ссылки (лампочка, карта, ридер, контроллер) находятся автоматически.
    /// </summary>
    public class CardLockPuzzleController : MonoBehaviour, IPuzzleDropHandler, IPuzzleDropTarget
    {
        [Header("Per-Instance Setup")]
        [Tooltip("Дверь, которая откроется после успешного свайпа.")]
        [SerializeField] private DoorInteraction _targetDoor;
        [Tooltip("Карточка, которой нужно провести по ридеру.")]
        [SerializeField] private ItemData _requiredCard;

        [Header("Audio")]
        [Tooltip("Звук проведения карточки по ридеру.")]
        [SerializeField] private AudioClip _cardSlideClip;
        [Tooltip("Звук отпирания двери (щёлчок замка).")]
        [SerializeField] private AudioClip _doorUnlockClip;
        [Tooltip("Громкость звука слайда карточки.")]
        [SerializeField, Range(0f, 1f)] private float _cardSlideVolume = 1f;
        [Tooltip("Громкость звука отпирания двери.")]
        [SerializeField, Range(0f, 1f)] private float _doorUnlockVolume = 1f;

        [Header("Timing")]
        [Tooltip("Пауза после загорания зелёной лампочки перед возвратом управления игроку (сек).")]
        [SerializeField] private float _delayBeforeReturnControl = 1.5f;
        [Tooltip("Пауза после возврата управления игроку перед отпиранием двери (сек).")]
        [SerializeField] private float _delayBeforeDoorUnlock = 0.5f;

        [Header("Advanced (auto-filled, override if needed)")]
        [SerializeField] private PuzzleModeController _puzzleMode;
        [SerializeField] private Transform _animatedCard;
        [SerializeField] private Collider _dropZone;
        [SerializeField] private MeshRenderer _lampRenderer;
        [SerializeField] private Material _redMaterial;
        [SerializeField] private Material _greenMaterial;
        [SerializeField] private Material _blackMaterial;
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private string _dropHint = "Провести картой";
        [SerializeField] private float _slideDuration = 1.0f;
        [SerializeField] private Vector3 _slideOffset = new Vector3(0, -0.2f, 0);

        private const string LampMaterialPath = "Materials/CardLock/CardLamp_{0}.mat";
        private const string GhostMaterialPath = "Materials/CardLock/CardLamp_Ghost.mat";

        private Vector3 _cardInitialLocalPos;
        private bool _isAnimating;
        private MeshRenderer[] _cardRenderers;
        private Material[][] _originalMaterials;
        private bool _isGhostVisible;

        private void Awake()
        {
            AutoResolveReferences();

            if (_animatedCard != null)
            {
                _cardInitialLocalPos = _animatedCard.localPosition;
                _cardRenderers = _animatedCard.GetComponentsInChildren<MeshRenderer>(true);

                _originalMaterials = new Material[_cardRenderers.Length][];
                for (int i = 0; i < _cardRenderers.Length; i++)
                    _originalMaterials[i] = _cardRenderers[i].sharedMaterials;

                foreach (var col in _animatedCard.GetComponentsInChildren<Collider>(true))
                    col.enabled = false;

                _animatedCard.gameObject.SetActive(false);
            }
        }

        /// <summary>Автоматически находит все внутренние ссылки, если они не назначены вручную.</summary>
        private void AutoResolveReferences()
        {
            if (_puzzleMode == null)
                _puzzleMode = GetComponentInChildren<PuzzleModeController>();

            if (_dropZone == null)
                _dropZone = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();

            // LockLamp — дочерний объект этого же объекта (cardLock)
            if (_lampRenderer == null)
            {
                foreach (Transform child in transform)
                    if (child.name.Contains("Lamp"))
                    { _lampRenderer = child.GetComponent<MeshRenderer>(); break; }
            }

            // IdCard — сиблинг (дочерний объект родителя = корень префаба)
            if (_animatedCard == null)
            {
                Transform prefabRoot = transform.parent;
                if (prefabRoot != null)
                {
                    foreach (Transform child in prefabRoot)
                        if (child.name.Contains("IdCard") || child.name.Contains("idCard"))
                        { _animatedCard = child; break; }
                }
            }

            if (_redMaterial == null)
                _redMaterial = Resources.Load<Material>(string.Format(LampMaterialPath, "Red"));
            if (_greenMaterial == null)
                _greenMaterial = Resources.Load<Material>(string.Format(LampMaterialPath, "Green"));
            if (_blackMaterial == null)
                _blackMaterial = Resources.Load<Material>(string.Format(LampMaterialPath, "Black"));
            if (_ghostMaterial == null)
                _ghostMaterial = Resources.Load<Material>(GhostMaterialPath);
        }

        private void Start()
        {
            UpdateLampState();

            if (LightingSystem.Instance != null)
                LightingSystem.Instance.OnPowerChanged += HandlePowerChanged;
        }

        private void OnDestroy()
        {
            if (LightingSystem.Instance != null)
                LightingSystem.Instance.OnPowerChanged -= HandlePowerChanged;
        }

        private void Update()
        {
            if (_isAnimating || (_puzzleMode != null && _puzzleMode.IsSolved))
            {
                if (_isGhostVisible) SetGhostVisible(false);
                return;
            }

            if (PuzzleInventoryBar.IsDragging && PuzzleInventoryBar.DraggedItem == _requiredCard)
            {
                bool isHovering = IsMouseOverReader();
                if (isHovering && !_isGhostVisible)
                    SetGhostVisible(true);
                else if (!isHovering && _isGhostVisible)
                    SetGhostVisible(false);
            }
            else if (_isGhostVisible)
            {
                SetGhostVisible(false);
            }
        }

        private bool IsMouseOverReader()
        {
            if (Mouse.current == null) return false;
            return PerformRaycast(Mouse.current.position.ReadValue());
        }

        private bool PerformRaycast(Vector2 screenPos)
        {
            if (Camera.main == null || _dropZone == null) return false;

            Ray ray = Camera.main.ScreenPointToRay(screenPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                return hit.collider == _dropZone || hit.collider.transform.IsChildOf(_dropZone.transform);
            }
            return false;
        }

        private void SetGhostVisible(bool visible)
        {
            _isGhostVisible = visible;
            if (_animatedCard == null) return;

            _animatedCard.gameObject.SetActive(visible);
            _animatedCard.localPosition = _cardInitialLocalPos;

            if (visible && _ghostMaterial != null)
            {
                foreach (var renderer in _cardRenderers)
                {
                    Material[] ghostMats = new Material[renderer.sharedMaterials.Length];
                    for (int i = 0; i < ghostMats.Length; i++) ghostMats[i] = _ghostMaterial;
                    renderer.sharedMaterials = ghostMats;
                }
            }
            else
            {
                for (int i = 0; i < _cardRenderers.Length; i++)
                    _cardRenderers[i].sharedMaterials = _originalMaterials[i];
            }
        }

        // ── IPuzzleDropTarget ──────────────────────────────────────────────────

        public string GetDropHint() => _dropHint;

        public bool CanAccept(ItemData item) => item == _requiredCard && (_puzzleMode != null && !_puzzleMode.IsSolved);

        // ── IPuzzleDropHandler ──────────────────────────────────────────────────

        public bool HandleDrop(ItemData item, Vector2 screenPosition, out ItemData replacement)
        {
            replacement = null;

            if (_isAnimating || (_puzzleMode != null && _puzzleMode.IsSolved))
                return false;

            if (item == _requiredCard && (_isGhostVisible || PerformRaycast(screenPosition)))
            {
                StartCoroutine(ProcessCardSwipe());
                return true;
            }

            return false;
        }

        private IEnumerator ProcessCardSwipe()
        {
            _isAnimating = true;
            SetGhostVisible(false);

            if (_cardSlideClip != null)
                AudioManager.Instance?.PlaySFX(_cardSlideClip, _cardSlideVolume);

            if (_animatedCard != null)
            {
                _animatedCard.gameObject.SetActive(true);
                for (int i = 0; i < _cardRenderers.Length; i++)
                    _cardRenderers[i].sharedMaterials = _originalMaterials[i];

                float elapsed = 0;
                Vector3 targetPos = _cardInitialLocalPos + _slideOffset;

                while (elapsed < _slideDuration)
                {
                    elapsed += Time.deltaTime;
                    _animatedCard.localPosition = Vector3.Lerp(_cardInitialLocalPos, targetPos, elapsed / _slideDuration);
                    yield return null;
                }

                _animatedCard.gameObject.SetActive(false);
            }

            bool hasPower = LightingSystem.Instance != null && LightingSystem.Instance.IsPowered;

            if (hasPower)
            {
                // 1. Загорается зелёная лампочка
                UpdateLampState(true);

                // 2. Пауза — игрок видит зелёную лампочку
                yield return new WaitForSeconds(_delayBeforeReturnControl);

                // 3. Отпираем и открываем дверь ДО возврата управления
                if (_doorUnlockClip != null)
                    AudioManager.Instance?.PlaySFX(_doorUnlockClip, _doorUnlockVolume);

                if (_targetDoor != null) _targetDoor.UnlockAndOpen();

                // 4. Ждём пока дверь начнёт открываться
                yield return new WaitForSeconds(_delayBeforeDoorUnlock);

                // 5. Возвращаем игроку контроль — дверь уже разблокирована и открывается
                if (_puzzleMode != null) _puzzleMode.SetSolved();
            }
            else
            {
                UpdateLampState();
            }

            _isAnimating = false;
        }

        private void HandlePowerChanged(bool isPowered) => UpdateLampState();

        private void UpdateLampState(bool solved = false)
        {
            if (_lampRenderer == null) return;

            bool hasPower = LightingSystem.Instance != null && LightingSystem.Instance.IsPowered;
            bool isSolved = solved || (_puzzleMode != null && _puzzleMode.IsSolved);

            if (!hasPower)
            {
                if (_blackMaterial != null) _lampRenderer.material = _blackMaterial;
            }
            else if (isSolved)
            {
                if (_greenMaterial != null) _lampRenderer.material = _greenMaterial;
            }
            else
            {
                if (_redMaterial != null) _lampRenderer.material = _redMaterial;
            }
        }
    }
}
