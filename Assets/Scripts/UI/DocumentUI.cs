using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Singleton. Displays a readable document with 3D preview, darkened background,
/// and paginated text baked into the prefab as 3D TextMeshPro components.
/// Reuses the Inspection camera pattern from ItemInspector.
/// </summary>
public class DocumentUI : MonoBehaviour
{
    public static DocumentUI Instance { get; private set; }

    // ── Inspector: UI References ──────────────────────────────────────────────

    [Header("Panel")]
    [SerializeField] private GameObject _panel;

    [Header("3D Preview")]
    [SerializeField] private RawImage _documentPreview;
    [SerializeField] private Camera _inspectionCamera;

    [Tooltip("Имя родительского объекта страниц в префабе. Если пусто — ищутся все TMP на дочерних объектах.")]
    [SerializeField] private string _pagesContainerName = "Pages";

    [Header("Navigation")]
    [SerializeField] private Button _prevPageButton;
    [SerializeField] private Button _nextPageButton;
    [SerializeField] private TextMeshProUGUI _pageIndicator;

    [Header("Darkening")]
    [Tooltip("Длительность затемнения экрана (секунды).")]
    [SerializeField, Min(0f)] private float _darkenDuration = 0.4f;

    [Tooltip("Целевая непрозрачность затемнения. 1 = полностью чёрный, 0 = прозрачный.")]
    [SerializeField, Range(0f, 1f)] private float _darkenAlpha = 0.85f;

    [Header("Page Flip")]
    [Tooltip("Длительность анимации переворота страницы (секунды).")]
    [SerializeField, Min(0f)] private float _pageFlipDuration = 0.4f;

    [Tooltip("Кривая анимации переворота. По умолчанию EaseInOut.")]
    [SerializeField] private AnimationCurve _pageFlipCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Сдвиг next-страницы по локальной Y относительно current. Базовое значение 0.001.")]
    [SerializeField] private float _pageStackOffset = 0.001f;

    [Header("Scale Animation")]
    [Tooltip("Длительность анимации появления 3D-объекта (секунды).")]
    [SerializeField, Min(0f)] private float _scaleInDuration = 0.5f;

    [Tooltip("Кривая анимации появления. По умолчанию EaseOut.")]
    [SerializeField] private AnimationCurve _scaleInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Длительность анимации исчезновения 3D-объекта (секунды).")]
    [SerializeField, Min(0f)] private float _scaleOutDuration = 0.35f;

    [Tooltip("Кривая анимации исчезновения. По умолчанию EaseIn.")]
    [SerializeField] private AnimationCurve _scaleOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Camera")]
    [SerializeField] private string _inspectionLayerName = "Inspection";

    [Tooltip("Множитель для дистанции камеры от модели. Больше = модель меньше.")]
    [SerializeField] private float _framingMultiplier = 2.2f;

    [Tooltip("Глобальная начальная эйлерова ротация 3D-модели. Используется, когда в DocumentData useCustomPreviewRotation = false.")]
    [SerializeField] private Vector3 _initialRotation = new Vector3(15f, -35f, 0f);

    // ── Constants ────────────────────────────────────────────────────────────

    private static readonly Vector3 InspectionOrigin = new Vector3(0f, -1000f, 0f);
    private const float InspectionDistance = 1.5f;

    // ── State ────────────────────────────────────────────────────────────────

    private RenderTexture _renderTexture;
    private int _inspectionLayer;

    private DocumentData _documentData;
    private int _currentPage;
    private bool _isNote;

    private GameObject _inspectionInstance;
    private GameObject _inspectionPivot;
    private GameObject _inspectionLight;
    private GameObject _inspectionLightRim;

    private List<GameObject> _pages = new List<GameObject>();
    private List<Quaternion> _pageInitialRotations = new List<Quaternion>();
    private List<Vector3> _pageInitialPositions = new List<Vector3>();

    private bool _isOpen;
    private bool _isAnimating;
    private bool _justOpened;
    private float _singlePageZAngle;

    private Vector3 _pivotTargetPosition;
    private Quaternion _pivotTargetRotation;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        _panel.SetActive(false);

        _inspectionLayer = LayerMask.NameToLayer(_inspectionLayerName);
        if (_inspectionLayer == -1)
            Debug.LogError($"[DocumentUI] Layer '{_inspectionLayerName}' not found.", this);

        _renderTexture = new RenderTexture(1536, 1024, 16);
        _renderTexture.Create();

        if (_inspectionCamera != null)
        {
            _inspectionCamera.allowHDR        = false;
            _inspectionCamera.orthographic    = true;
            _inspectionCamera.aspect          = 1.5f;
            _inspectionCamera.clearFlags      = CameraClearFlags.SolidColor;
            _inspectionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _inspectionCamera.targetTexture   = _renderTexture;
            _inspectionCamera.cullingMask     = _inspectionLayer != -1 ? 1 << _inspectionLayer : 0;
            _inspectionCamera.gameObject.SetActive(false);
        }

        if (_documentPreview != null)
            _documentPreview.texture = _renderTexture;

        // Wire up navigation buttons.
        if (_prevPageButton != null)
            _prevPageButton.onClick.AddListener(PrevPage);
        if (_nextPageButton != null)
            _nextPageButton.onClick.AddListener(NextPage);
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    private void Update()
    {
        if (!_isOpen || _isAnimating) return;

        if (_justOpened)
        {
            _justOpened = false;
            return;
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)
            PrevPage();

        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            NextPage();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Opens the document panel, darkens the screen, and animates the 3D preview.</summary>
    public void Open(DocumentData data)
    {
        if (data == null || _isOpen) return;

        _documentData = data;
        _currentPage   = 0;
        _isOpen        = true;
        _justOpened    = true;
        _isNote        = data.isNote;
        _singlePageZAngle = 0f;

        // Spawn 3D document and set up camera.
        if (data.documentPrefab != null)
        {
            SpawnDocument(data);
            _documentPreview.gameObject.SetActive(true);

            // Slightly darken the 3D preview so it doesn't blend with the text.
            float dim = data.previewDimAmount;
            _documentPreview.color = new Color(1f - dim, 1f - dim, 1f - dim, 1f);
        }
        else
        {
            _documentPreview.gameObject.SetActive(false);
        }

        // Show first page (3D TMP baked in prefab).
        ShowPage(0);

        // Hide navigation UI for single-page notes.
        if (_isNote)
        {
            if (_prevPageButton != null) _prevPageButton.gameObject.SetActive(false);
            if (_nextPageButton != null) _nextPageButton.gameObject.SetActive(false);
            if (_pageIndicator != null) _pageIndicator.gameObject.SetActive(false);
        }
        else
        {
            if (_prevPageButton != null) _prevPageButton.gameObject.SetActive(true);
            if (_nextPageButton != null) _nextPageButton.gameObject.SetActive(true);
            if (_pageIndicator != null) _pageIndicator.gameObject.SetActive(true);
        }

        // Activate panel and block player input.
        _panel.SetActive(true);
        UIManager.Instance?.OpenPanel(_panel);

        // Darken the background.
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeIn(_darkenDuration, _darkenAlpha);

        // Play open sound.
        if (data.openClip != null)
            AudioManager.Instance?.PlaySFX(data.openClip);

        // Start scale-in animation.
        if (data.documentPrefab != null && _inspectionPivot != null)
            StartCoroutine(ScaleInRoutine());
    }

    /// <summary>Closes the document panel with a reverse fly-out animation.</summary>
    public void Close()
    {
        if (!_isOpen || _isAnimating) return;

        _isOpen = false;

        // Play close sound.
        if (_documentData != null && _documentData.closeClip != null)
            AudioManager.Instance?.PlaySFX(_documentData.closeClip);

        if (_inspectionPivot != null && _documentData != null && _documentData.documentPrefab != null)
            StartCoroutine(ScaleOutRoutine());
        else
            FinishClose();
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    /// <summary>Shows the page at the given index with previous/current/next pages active.</summary>
    private void ShowPage(int index)
    {
        if (_pages.Count == 0)
        {
            UpdatePageIndicator(0, 0);
            UpdateNavButtons(0, 0);
            return;
        }

        index = Mathf.Clamp(index, 0, _pages.Count - 1);
        _currentPage = index;

        // Reset all pages: inactive, base rotation and position.
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].SetActive(false);
            _pages[i].transform.localRotation = _pageInitialRotations[i];
            _pages[i].transform.localPosition = _pageInitialPositions[i];
        }

        // Activate previous at 180° (already-read page, flipped back) at base position.
        if (index - 1 >= 0)
        {
            _pages[index - 1].SetActive(true);
            _pages[index - 1].transform.localRotation = _pageInitialRotations[index - 1] * Quaternion.Euler(0f, 0f, 180f);
            _pages[index - 1].transform.localPosition = _pageInitialPositions[index - 1];
        }

        // Activate current at 0° with Y offset (above the stack).
        _pages[index].SetActive(true);
        _pages[index].transform.localRotation = _pageInitialRotations[index];
        _pages[index].transform.localPosition = _pageInitialPositions[index] + new Vector3(0f, _pageStackOffset, 0f);

        // Activate next at 0° at base position.
        if (index + 1 < _pages.Count)
        {
            _pages[index + 1].SetActive(true);
            _pages[index + 1].transform.localRotation = _pageInitialRotations[index + 1];
            _pages[index + 1].transform.localPosition = _pageInitialPositions[index + 1];
        }

        UpdatePageIndicator(index, _pages.Count);
        UpdateNavButtons(index, _pages.Count);
    }

    /// <summary>Updates the page indicator text ("1 / N" or empty).</summary>
    private void UpdatePageIndicator(int index, int total)
    {
        if (_pageIndicator != null)
            _pageIndicator.text = total > 1 ? $"{index + 1} / {total}" : string.Empty;
    }

    /// <summary>Navigates to the next page with a flip animation. Blocked for notes.</summary>
    private void NextPage()
    {
        if (_isNote || _pages.Count == 0 || _isAnimating) return;
        if (_pages.Count == 1)
        {
            PlayPageTurnSound();
            StartCoroutine(SinglePageFlipRoutine(forward: true));
            return;
        }
        if (_currentPage >= _pages.Count - 1) return;
        PlayPageTurnSound();
        StartCoroutine(PageFlipRoutine(_currentPage, _currentPage + 1, forward: true));
        _currentPage++;
        UpdatePageIndicator(_currentPage, _pages.Count);
        UpdateNavButtons(_currentPage, _pages.Count);
    }

    /// <summary>Navigates to the previous page with a flip animation. Blocked for notes.</summary>
    private void PrevPage()
    {
        if (_isNote || _pages.Count == 0 || _isAnimating) return;
        if (_pages.Count == 1)
        {
            PlayPageTurnSound();
            StartCoroutine(SinglePageFlipRoutine(forward: false));
            return;
        }
        if (_currentPage <= 0) return;
        PlayPageTurnSound();
        StartCoroutine(PageFlipRoutine(_currentPage, _currentPage - 1, forward: false));
        _currentPage--;
        UpdatePageIndicator(_currentPage, _pages.Count);
        UpdateNavButtons(_currentPage, _pages.Count);
    }

    /// <summary>Plays the page turn sound from the current DocumentData if assigned.</summary>
    private void PlayPageTurnSound()
    {
        if (_documentData != null && _documentData.pageTurnClip != null)
            AudioManager.Instance?.PlaySFX(_documentData.pageTurnClip);
    }

    /// <summary>Updates navigation button interactable state.</summary>
    private void UpdateNavButtons(int index, int total)
    {
        if (_isNote) return;
        if (_prevPageButton != null)
            _prevPageButton.interactable = total <= 1 || index > 0;
        if (_nextPageButton != null)
            _nextPageButton.interactable = total <= 1 || index < total - 1;
    }

    // ── Page Discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Collects child GameObjects of the Pages container in the spawned prefab clone.
    /// Each child is one page (e.g. Page_0, Page_1) and may contain multiple
    /// TextMeshPro (3D) components for title, content, images, etc.
    /// Pages are sorted by GameObject name.
    /// </summary>
    private void FindPages(GameObject clone)
    {
        _pages.Clear();

        Transform container = null;
        if (!string.IsNullOrEmpty(_pagesContainerName))
            container = FindChildRecursive(clone.transform, _pagesContainerName);

        if (container == null)
        {
            Debug.LogWarning($"[DocumentUI] Pages container '{_pagesContainerName}' not found in prefab '{clone.name}'.", this);
            return;
        }

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            _pages.Add(child.gameObject);
            _pageInitialRotations.Add(child.localRotation);
            _pageInitialPositions.Add(child.localPosition);
        }

        // Sort by name to ensure Page_0, Page_1, Page_2... order.
        _pages.Sort((a, b) =>
            string.Compare(a.name, b.name, System.StringComparison.Ordinal));
    }

    /// <summary>Recursively finds a child Transform by name.</summary>
    private static Transform FindChildRecursive(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name == name)
                return child;
            var found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    // ── 3D Document Spawn ────────────────────────────────────────────────────

    /// <summary>
    /// Instantiates the document prefab on the Inspection layer, sets up camera and lights.
    /// Follows the same pattern as ItemInspector.SpawnPreview.
    /// </summary>
    private void SpawnDocument(DocumentData data)
    {
        // Instantiate as visual-only clone (strip gameplay components).
        _inspectionInstance = InstantiatePreview(data.documentPrefab);
        SetLayerRecursively(_inspectionInstance, _inspectionLayer);
        ResetRenderingLayerMask(_inspectionInstance);

        // Find baked 3D TMP pages in the prefab.
        FindPages(_inspectionInstance);

        // Calculate bounds for camera framing.
        var renderers = _inspectionInstance.GetComponentsInChildren<Renderer>();
        Bounds bounds;
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(InspectionOrigin, Vector3.zero);
        }

        Vector3 itemCenter = bounds.center;
        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        // Pivot at geometric center — animation without offset drift.
        _inspectionPivot = new GameObject("DocumentPivot");
        _inspectionPivot.transform.position = itemCenter;
        _inspectionInstance.transform.SetParent(_inspectionPivot.transform, worldPositionStays: true);

        // Per-document override takes priority over the global initialRotation setting.
        var startRotation = data.useCustomPreviewRotation ? data.previewRotation : _initialRotation;
        _pivotTargetRotation = Quaternion.Euler(startRotation);
        _pivotTargetPosition = itemCenter;
        _inspectionPivot.transform.rotation = _pivotTargetRotation;

        // Orthographic camera framing.
        float effectiveScale = data.previewScale > 0f ? data.previewScale : 1f;
        _inspectionCamera.orthographicSize = maxSize * _framingMultiplier * 0.5f / effectiveScale;
        _inspectionCamera.transform.position = itemCenter + new Vector3(0f, 0f, -InspectionDistance);
        _inspectionCamera.transform.LookAt(itemCenter);

        // Key light.
        _inspectionLight = new GameObject("DocumentLight_Key");
        var lightKey = _inspectionLight.AddComponent<Light>();
        lightKey.type = LightType.Point;
        lightKey.range = 10f;
        lightKey.intensity = 5f;
        lightKey.color = Color.white;
        lightKey.renderingLayerMask = -1;
        _inspectionLight.transform.position = itemCenter + new Vector3(1f, 1.5f, -2f);

        // Rim light.
        _inspectionLightRim = new GameObject("DocumentLight_Rim");
        var lightRim = _inspectionLightRim.AddComponent<Light>();
        lightRim.type = LightType.Point;
        lightRim.range = 10f;
        lightRim.intensity = 3f;
        lightRim.color = new Color(0.9f, 0.95f, 1f);
        lightRim.renderingLayerMask = -1;
        _inspectionLightRim.transform.position = itemCenter + new Vector3(-1.5f, 0.5f, 1f);

        // Re-assign render texture in case it was cleared.
        _inspectionCamera.targetTexture = _renderTexture;
        _documentPreview.texture = _renderTexture;
        _inspectionCamera.gameObject.SetActive(true);
    }

    /// <summary>
    /// Creates a visual-only clone of the prefab, stripping ISaveable components
    /// before activation to prevent save-system conflicts.
    /// </summary>
    private GameObject InstantiatePreview(GameObject prefab)
    {
        GameObject holder = new GameObject("DocumentHolder");
        holder.SetActive(false);

        GameObject clone = Instantiate(prefab, holder.transform);
        StripGameplayComponents(clone);

        clone.transform.SetParent(null, worldPositionStays: false);
        clone.transform.SetPositionAndRotation(InspectionOrigin, Quaternion.identity);
        Destroy(holder);
        return clone;
    }

    /// <summary>Removes ISaveable components from a preview clone before activation.</summary>
    private void StripGameplayComponents(GameObject clone)
    {
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is ISaveable)
                DestroyImmediate(behaviour);
    }

    // ── Page Flip Animation ──────────────────────────────────────────────────

    /// <summary>
    /// Animates a page flip between two page indices.
    /// Three pages are active: previous (180°, base), current (0°, Y offset), next (0°, base).
    /// Forward: current rotates 0 -> 180, old previous deactivated, new next activated.
    /// Backward: previous rotates 180 -> 0, old next deactivated, new previous activated.
    /// </summary>
    private IEnumerator PageFlipRoutine(int fromIndex, int toIndex, bool forward)
    {
        _isAnimating = true;

        GameObject flipPage = forward ? _pages[fromIndex] : _pages[toIndex];
        Quaternion flipBase = forward ? _pageInitialRotations[fromIndex] : _pageInitialRotations[toIndex];

        Quaternion startRot = flipPage.transform.localRotation;
        Quaternion endRot = flipBase * Quaternion.Euler(0f, 0f, forward ? 180f : 0f);

        // Animate the flip.
        float elapsed = 0f;
        while (elapsed < _pageFlipDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _pageFlipDuration);
            float eased = _pageFlipCurve.Evaluate(t);
            flipPage.transform.localRotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        flipPage.transform.localRotation = endRot;

        if (forward)
        {
            // Deactivate old previous.
            if (fromIndex - 1 >= 0)
                _pages[fromIndex - 1].SetActive(false);

            // Move old current (now previous, flipped to 180) back to base position.
            _pages[fromIndex].transform.localPosition = _pageInitialPositions[fromIndex];

            // Move new current (was next) to Y offset position.
            _pages[toIndex].transform.localPosition = _pageInitialPositions[toIndex] + new Vector3(0f, _pageStackOffset, 0f);

            // Activate new next at 0° at base position (if exists).
            if (toIndex + 1 < _pages.Count)
            {
                _pages[toIndex + 1].SetActive(true);
                _pages[toIndex + 1].transform.localRotation = _pageInitialRotations[toIndex + 1];
                _pages[toIndex + 1].transform.localPosition = _pageInitialPositions[toIndex + 1];
            }
        }
        else
        {
            // Deactivate old next.
            if (fromIndex + 1 < _pages.Count)
                _pages[fromIndex + 1].SetActive(false);

            // Move old current (now next) back to base position.
            _pages[fromIndex].transform.localPosition = _pageInitialPositions[fromIndex];

            // Move new current (was previous, animated 180 -> 0) to Y offset position.
            _pages[toIndex].transform.localPosition = _pageInitialPositions[toIndex] + new Vector3(0f, _pageStackOffset, 0f);

            // Activate new previous at 180° at base position (if exists).
            if (toIndex - 1 >= 0)
            {
                _pages[toIndex - 1].SetActive(true);
                _pages[toIndex - 1].transform.localRotation = _pageInitialRotations[toIndex - 1] * Quaternion.Euler(0f, 0f, 180f);
                _pages[toIndex - 1].transform.localPosition = _pageInitialPositions[toIndex - 1];
            }
        }

        _isAnimating = false;
    }

    /// <summary>
    /// Flips a single page 180° on Z axis and leaves it flipped.
    /// Used when isNote = false and there is only one page.
    /// Angle is clamped to [0, 180] degrees.
    /// NextPage flips 0 -> 180, PrevPage flips 180 -> 0.
    /// </summary>
    private IEnumerator SinglePageFlipRoutine(bool forward)
    {
        _isAnimating = true;

        GameObject page = _pages[0];
        Quaternion startRot = page.transform.localRotation;

        float targetZ = forward ? 180f : 0f;

        // Already at the target — no animation needed.
        if (Mathf.Approximately(targetZ, _singlePageZAngle))
        {
            _isAnimating = false;
            yield break;
        }

        Quaternion endRot = Quaternion.Euler(0f, 0f, targetZ);

        float elapsed = 0f;
        while (elapsed < _pageFlipDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _pageFlipDuration);
            float eased = _pageFlipCurve.Evaluate(t);
            page.transform.localRotation = Quaternion.Slerp(startRot, endRot, eased);
            yield return null;
        }

        page.transform.localRotation = endRot;
        _singlePageZAngle = targetZ;
        _isAnimating = false;
    }

    // ── Scale Animation ──────────────────────────────────────────────────────

    /// <summary>Animates the 3D document scaling in from 0 to full size.</summary>
    private IEnumerator ScaleInRoutine()
    {
        _isAnimating = true;

        _inspectionPivot.transform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < _scaleInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _scaleInDuration);
            float eased = _scaleInCurve.Evaluate(t);

            _inspectionPivot.transform.localScale = Vector3.one * eased;

            yield return null;
        }

        _inspectionPivot.transform.localScale = Vector3.one;
        _isAnimating = false;
    }

    /// <summary>Animates the 3D document scaling out from full size to 0, then closes.</summary>
    private IEnumerator ScaleOutRoutine()
    {
        _isAnimating = true;

        Vector3 startScale = _inspectionPivot.transform.localScale;

        float elapsed = 0f;
        while (elapsed < _scaleOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _scaleOutDuration);
            float eased = _scaleOutCurve.Evaluate(t);

            _inspectionPivot.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, eased);

            yield return null;
        }

        _isAnimating = false;
        FinishClose();
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────

    /// <summary>Finalizes the close: undarkens screen, closes panel, destroys 3D objects.</summary>
    private void FinishClose()
    {
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeOut(_darkenDuration);

        UIManager.Instance?.ClosePanel(_panel);
        _panel.SetActive(false);

        Cleanup3D();

        _documentData = null;
        _currentPage  = 0;
        _isNote       = false;
    }

    /// <summary>Destroys all spawned 3D inspection objects and deactivates the camera.</summary>
    private void Cleanup3D()
    {
        if (_inspectionPivot != null)
        {
            Destroy(_inspectionPivot);
            _inspectionPivot = null;
            _inspectionInstance = null;
        }

        if (_inspectionLight != null)
        {
            Destroy(_inspectionLight);
            _inspectionLight = null;
        }

        if (_inspectionLightRim != null)
        {
            Destroy(_inspectionLightRim);
            _inspectionLightRim = null;
        }

        if (_inspectionCamera != null)
            _inspectionCamera.gameObject.SetActive(false);

        _pages.Clear();
        _pageInitialRotations.Clear();
        _pageInitialPositions.Clear();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void ResetRenderingLayerMask(GameObject obj)
    {
        foreach (var rend in obj.GetComponentsInChildren<Renderer>(includeInactive: true))
            rend.renderingLayerMask = 1;
    }
}
