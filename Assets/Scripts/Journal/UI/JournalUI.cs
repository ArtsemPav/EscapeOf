using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Journal board UI: a clipboard-style board with a left column of category buttons and,
/// on the right, a stack of 3D page prefabs rendered by a dedicated camera into a RawImage.
/// Pages from every unlocked document of the selected category are glued into one flat stack,
/// flipped by clicking the stack edges (or arrow keys / A-D / Space).
/// </summary>
public class JournalUI : MonoBehaviour
{
    public static JournalUI Instance { get; private set; }

    [Header("Panel")]
    [SerializeField] private GameObject _journalPanel;

    [Header("Category Buttons")]
    [SerializeField] private Transform _categoryButtonsContainer;
    [SerializeField] private JournalEntryButton _categoryButtonPrefab;

    [Header("3D Preview")]
    [SerializeField] private RawImage _boardRawImage;
    [SerializeField] private Camera _inspectionCamera;

    [Header("Edge Click Zones")]
    [SerializeField] private UnityEngine.UI.Image _leftClickZone;
    [SerializeField] private UnityEngine.UI.Image _rightClickZone;

    [Header("Hints")]
    [SerializeField] private TextMeshProUGUI _closeHintText;

    [Tooltip("Вертикальный сдвиг верхней страницы в стопке.")]
    [SerializeField] private float _pageStackOffset = 0.1f;

    [Tooltip("Угол наклона перевернутой (прочитанной) страницы, градусы по Z.")]
    [SerializeField] private float _flippedZAngle = 180f;

    [Tooltip("Сдвиг кадра влево: насколько стопка прижата к правой границе кадра (в ширинах листа). Больше — правее, 0.16 — край листа на границе.")]
    [SerializeField, Range(0f, 0.3f)] private float _stackRightShift = 0.16f;

    [Tooltip("Насколько стопка прочитанных страниц выдвигается влево от доски (в ширинах листа).")]
    [SerializeField, Min(0f)] private float _pileShift = 1f;

    [Header("Camera")]
    [SerializeField] private string _inspectionLayerName = "Inspection";

    [Tooltip("Множитель дистанции ортографической камеры от страницы.")]
    [SerializeField] private float _framingMultiplier = 2.2f;

    [Tooltip("Фиксированный размер bounds страницы для кадрирования камеры.")]
    [SerializeField] private Vector3 _fixedBoundsSize = new Vector3(0.44f, 0.025f, 0.31f);

    [Tooltip("Смещение центра bounds относительно корня префаба.")]
    [SerializeField] private Vector3 _fixedBoundsCenterOffset = new Vector3(0f, 0.006f, 0f);

    [Header("Pages")]
    [Tooltip("Имя контейнера страниц внутри префаба документа.")]
    [SerializeField] private string _pagesContainerName = "Pages";

    // ── Constants ────────────────────────────────────────────────────────────

    private static readonly Vector3 InspectionOrigin = new Vector3(0f, -1000f, 0f);
    private const float InspectionDistance = 1.5f;
    private const int RenderTextureWidth = 1536;
    private const int RenderTextureHeight = 1024;
    private const float PileStagger = 0.005f;    // depth step between piled pages
    private const float FanStepDegrees = 2f;     // fan rotation per piled page
    private const float MaxFanDegrees = 20f;     // fan rotation cap

    // ── State ────────────────────────────────────────────────────────────────

    private RenderTexture _renderTexture;
    private int _inspectionLayer;

    // Flattened stack: pages from every unlocked document of the active category, in order.
    private readonly List<GameObject> _pages = new List<GameObject>();
    private readonly List<Quaternion> _pageInitialRotations = new List<Quaternion>();
    private readonly List<Vector3> _pageInitialPositions = new List<Vector3>();
    private int _currentPage;

    private readonly List<JournalEntryButton> _categoryButtons = new List<JournalEntryButton>();
    private readonly List<int> _categoryButtonIndices = new List<int>();
    private readonly List<GameObject> _spawnedInstances = new List<GameObject>();
    private int _selectedCategory = -1;
    private bool _eventsSubscribed;

    private bool _isOpen;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        if (_journalPanel != null)
            _journalPanel.SetActive(false);

        _inspectionLayer = LayerMask.NameToLayer(_inspectionLayerName);
        if (_inspectionLayer == -1)
            Debug.LogError($"[JournalUI] Layer '{_inspectionLayerName}' not found.", this);

        _renderTexture = new RenderTexture(RenderTextureWidth, RenderTextureHeight, 16);
        _renderTexture.Create();

        if (_inspectionCamera != null)
        {
            _inspectionCamera.allowHDR = false;
            _inspectionCamera.orthographic = true;
            _inspectionCamera.aspect = (float)RenderTextureWidth / RenderTextureHeight;
            _inspectionCamera.clearFlags = CameraClearFlags.SolidColor;
            _inspectionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _inspectionCamera.targetTexture = _renderTexture;
            _inspectionCamera.cullingMask = _inspectionLayer != -1 ? 1 << _inspectionLayer : 0;
            _inspectionCamera.gameObject.SetActive(false);
        }

        if (_boardRawImage != null)
            _boardRawImage.texture = _renderTexture;

        WireEdgeZone(_leftClickZone, FlipLeft);
        WireEdgeZone(_rightClickZone, FlipRight);
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    private void OnEnable()
    {
        _eventsSubscribed = false; // resubscribe lazily — JournalSystem.Awake may run after this
    }

    private void OnDisable()
    {
        UnsubscribeFromJournalEvents();
    }

    private void Update()
    {
        EnsureSubscribedToJournalEvents();

        if (!_isOpen) return;

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            JournalSystem.Instance?.CloseJournal();
            return;
        }

        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame)
            FlipLeft();

        if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ||
            keyboard.spaceKey.wasPressedThisFrame)
            FlipRight();
    }

    // ── Event Subscription ───────────────────────────────────────────────────

    // JournalSystem.Awake can run after JournalUI.OnEnable during scene load
    // (script execution order is not guaranteed), so the subscription is retried
    // lazily from Update instead of being a one-shot in OnEnable.
    private void EnsureSubscribedToJournalEvents()
    {
        if (_eventsSubscribed || JournalSystem.Instance == null) return;

        JournalSystem.Instance.OnJournalOpened += HandleJournalOpened;
        JournalSystem.Instance.OnJournalClosed += HandleJournalClosed;
        JournalSystem.Instance.OnEntryAdded += HandleEntryAdded;
        _eventsSubscribed = true;
    }

    private void UnsubscribeFromJournalEvents()
    {
        if (!_eventsSubscribed || JournalSystem.Instance == null) return;

        JournalSystem.Instance.OnJournalOpened -= HandleJournalOpened;
        JournalSystem.Instance.OnJournalClosed -= HandleJournalClosed;
        JournalSystem.Instance.OnEntryAdded -= HandleEntryAdded;
        _eventsSubscribed = false;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Opens the journal when closed, closes it when open.</summary>
    public void Toggle()
    {
        if (JournalSystem.Instance == null) return;

        if (JournalSystem.Instance.IsOpen)
            JournalSystem.Instance.CloseJournal();
        else
            JournalSystem.Instance.OpenJournal();
    }

    /// <summary>
    /// Selects a category: rebuilds the page stack from all unlocked documents of
    /// that category and shows the first page.
    /// </summary>
    public void SelectCategory(int index)
    {
        var categories = JournalSystem.Instance != null ? JournalSystem.Instance.Categories : null;
        if (categories == null || index < 0 || index >= categories.Count) return;

        _selectedCategory = index;
        RebuildPageStack(categories[index]);
        HighlightSelectedCategory();
    }

    /// <summary>Shows the next page (click on the right stack edge).</summary>
    public void FlipRight()
    {
        if (!_isOpen || _pages.Count == 0) return;
        if (_currentPage >= _pages.Count - 1) return;

        ShowPage(_currentPage + 1);
    }

    /// <summary>Shows the previous page (click on the left stack edge).</summary>
    public void FlipLeft()
    {
        if (!_isOpen || _pages.Count == 0) return;
        if (_currentPage <= 0) return;

        ShowPage(_currentPage - 1);
    }

    // ── Open / Close ─────────────────────────────────────────────────────────

    private void HandleJournalOpened()
    {
        _isOpen = true;

        if (_journalPanel != null)
            _journalPanel.SetActive(true);

        UIManager.Instance?.OpenPanel(_journalPanel);

        RebuildCategoryButtons();

        // Show the first category that has at least one unlocked document.
        int firstAvailable = FindFirstAvailableCategory();
        if (firstAvailable >= 0)
            SelectCategory(firstAvailable);
        else
            ClearPageStack();
    }

    private void HandleJournalClosed()
    {
        _isOpen = false;

        if (_journalPanel != null)
            _journalPanel.SetActive(false);

        UIManager.Instance?.ClosePanel(_journalPanel);

        ClearPageStack();
    }

    private void HandleEntryAdded(DocumentData data)
    {
        if (!_isOpen) return;

        // A new document may unlock a previously hidden category button.
        RebuildCategoryButtons();

        // If the new page belongs to the open category, append it to the stack live.
        if (_selectedCategory >= 0 &&
            JournalSystem.Instance != null &&
            JournalSystem.Instance.IsUnlocked(data) &&
            IsDocumentInCategory(JournalSystem.Instance.Categories[_selectedCategory], data))
        {
            AppendDocumentPages(data);
        }
    }

    private int FindFirstAvailableCategory()
    {
        var categories = JournalSystem.Instance != null ? JournalSystem.Instance.Categories : null;
        if (categories == null) return -1;

        for (int i = 0; i < categories.Count; i++)
            if (CountUnlocked(categories[i]) > 0)
                return i;

        return -1;
    }

    private static bool IsDocumentInCategory(JournalSystem.JournalCategory category, DocumentData data)
    {
        if (category == null || category.documents == null) return false;

        for (int i = 0; i < category.documents.Count; i++)
            if (category.documents[i] == data)
                return true;

        return false;
    }

    // ── Category Buttons ─────────────────────────────────────────────────────

    private void RebuildCategoryButtons()
    {
        ClearCategoryButtons();

        var categories = JournalSystem.Instance != null ? JournalSystem.Instance.Categories : null;
        if (_categoryButtonsContainer == null || _categoryButtonPrefab == null || categories == null) return;

        for (int i = 0; i < categories.Count; i++)
        {
            // Categories with no unlocked documents stay hidden.
            if (CountUnlocked(categories[i]) == 0) continue;

            int categoryIndex = i;
            var button = Instantiate(_categoryButtonPrefab, _categoryButtonsContainer);
            button.Setup(categories[i].categoryName, () => SelectCategory(categoryIndex));
            _categoryButtons.Add(button);
            _categoryButtonIndices.Add(categoryIndex);
        }

        HighlightSelectedCategory();
    }

    private void ClearCategoryButtons()
    {
        for (int i = 0; i < _categoryButtons.Count; i++)
            if (_categoryButtons[i] != null)
                Destroy(_categoryButtons[i].gameObject);

        _categoryButtons.Clear();
        _categoryButtonIndices.Clear();
    }

    private void HighlightSelectedCategory()
    {
        for (int i = 0; i < _categoryButtons.Count; i++)
            _categoryButtons[i].SetSelected(_categoryButtonIndices[i] == _selectedCategory);
    }

    private int CountUnlocked(JournalSystem.JournalCategory category)
    {
        if (category == null || category.documents == null || JournalSystem.Instance == null) return 0;

        int count = 0;
        for (int i = 0; i < category.documents.Count; i++)
            if (JournalSystem.Instance.IsUnlocked(category.documents[i]))
                count++;

        return count;
    }

    // ── Page Stack ───────────────────────────────────────────────────────────

    private void RebuildPageStack(JournalSystem.JournalCategory category)
    {
        ClearPageStack();

        if (category == null || category.documents == null || JournalSystem.Instance == null) return;

        for (int i = 0; i < category.documents.Count; i++)
        {
            var data = category.documents[i];
            if (data == null || !JournalSystem.Instance.IsUnlocked(data)) continue;

            AppendDocumentPages(data);
        }

        ShowPage(0);
    }

    private void AppendDocumentPages(DocumentData data)
    {
        if (data.documentPrefab == null || _inspectionCamera == null) return;

        // Instantiate unparented at the offscreen inspection origin — parenting under
        // the Canvas marker would apply canvas scaling to the 3D pages.
        var instance = Instantiate(data.documentPrefab, InspectionOrigin, data.GetPreviewRotation());

        // Match the document's world-preview size, then normalize into the fixed frame
        // so the shared camera shows every document at the same footprint (in DocumentUI
        // the preview scale divides out of the camera framing — here we do it explicitly).
        float previewScale = data.previewScale > 0f ? data.previewScale : 1f;
        instance.transform.localScale = Vector3.one * previewScale;
        FitInstanceIntoFixedFrame(instance);
        _spawnedInstances.Add(instance);
        SetLayerRecursively(instance, _inspectionLayer);

        // Collect baked 3D TMP pages (Page_N) from the Pages container.
        Transform pagesContainer = FindChildRecursive(instance.transform, _pagesContainerName);
        if (pagesContainer == null)
        {
            Debug.LogWarning($"[JournalUI] Pages container '{_pagesContainerName}' not found in prefab '{instance.name}'.", this);
            return;
        }

        var documentPages = new List<GameObject>();
        for (int i = 0; i < pagesContainer.childCount; i++)
            documentPages.Add(pagesContainer.GetChild(i).gameObject);

        // Page_N order.
        documentPages.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.Ordinal));

        foreach (var page in documentPages)
        {
            _pages.Add(page);
            _pageInitialRotations.Add(page.transform.localRotation);
            _pageInitialPositions.Add(page.transform.localPosition);
        }
    }

    private void ShowPage(int index)
    {
        if (_pages.Count == 0) return;

        index = Mathf.Clamp(index, 0, _pages.Count - 1);
        _currentPage = index;

        // Reset all pages: hidden, base pose.
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].SetActive(false);
            _pages[i].transform.localRotation = _pageInitialRotations[i];
            _pages[i].transform.localPosition = _pageInitialPositions[i];
        }

        // Already-read pages: fanned pile on the left.
        for (int i = 0; i < index; i++)
        {
            _pages[i].SetActive(true);
            _pages[i].transform.localPosition = GetPilePose(i, out Quaternion pileRotation);
            _pages[i].transform.localRotation = pileRotation;
        }

        // Current page: raised above the stack.
        _pages[index].SetActive(true);
        _pages[index].transform.localPosition = _pageInitialPositions[index] + Vector3.up * _pageStackOffset;

        // Next page: base position, under the current one.
        if (index + 1 < _pages.Count)
            _pages[index + 1].SetActive(true);

        FrameCamera();
        UpdateEdgeZoneAvailability();
    }

    /// <summary>Enables the edge arrows only where a page exists to flip to.</summary>
    private void UpdateEdgeZoneAvailability()
    {
        SetZoneEnabled(_leftClickZone, _currentPage > 0);
        SetZoneEnabled(_rightClickZone, _currentPage < _pages.Count - 1);
    }

    private void SetZoneEnabled(UnityEngine.UI.Image zone, bool enabled)
    {
        if (zone == null) return;

        var button = zone.GetComponent<Button>();
        if (button != null) button.interactable = enabled;
        zone.enabled = enabled; // hides the sprite as well
    }

    private void ClearPageStack()
    {
        for (int i = 0; i < _spawnedInstances.Count; i++)
            if (_spawnedInstances[i] != null)
                Destroy(_spawnedInstances[i]);

        _spawnedInstances.Clear();
        _pages.Clear();
        _pageInitialRotations.Clear();
        _pageInitialPositions.Clear();
        _currentPage = 0;

        if (_inspectionCamera != null)
            _inspectionCamera.gameObject.SetActive(false);
    }

    /// <summary>Pose of a read page in the left pile: fanned out, slid left by _pileShift page widths.</summary>
    private Vector3 GetPilePose(int pageIndex, out Quaternion pileRotation)
    {
        Transform page = _pages[pageIndex].transform;
        Vector3 localSlide = page.parent.InverseTransformDirection(Vector3.left) * (_fixedBoundsSize.x * _pileShift);
        pileRotation = _pageInitialRotations[pageIndex] *
                       Quaternion.Euler(0f, 0f, Mathf.Min(pageIndex * FanStepDegrees, MaxFanDegrees));
        return _pageInitialPositions[pageIndex] + localSlide + Vector3.up * ((pageIndex + 1) * PileStagger);
    }

    private void ComputeFrame(out Vector3 center, out float orthoSize)
    {
        float aspect = _inspectionCamera != null ? _inspectionCamera.aspect : 1.5f;
        float pageWidth = _fixedBoundsSize.x;

        // Fixed framing on the current page only — original page size restored.
        // The stack sits as far right as the frame allows (_stackRightShift); the
        // read-page pile slides left mostly out of frame (_pileShift).
        float halfWidth = pageWidth * 0.55f;
        float centerX = -pageWidth * _stackRightShift;

        orthoSize = Mathf.Max(_fixedBoundsSize.z * 0.5f, halfWidth / aspect) * _framingMultiplier;
        center = InspectionOrigin + _fixedBoundsCenterOffset + new Vector3(centerX, 0f, 0f);
    }

    /// <summary>Frames the orthographic inspection camera on the fixed page bounds.</summary>
    private void FrameCamera()
    {
        if (_inspectionCamera == null) return;

        ComputeFrame(out Vector3 center, out float orthoSize);
        _inspectionCamera.orthographicSize = orthoSize;
        _inspectionCamera.transform.position = center + new Vector3(0f, 0f, -InspectionDistance);
        _inspectionCamera.transform.LookAt(center);
        _inspectionCamera.gameObject.SetActive(true);
    }

    /// <summary>Scales the spawned document so its pages fit the fixed inspection frame.</summary>
    private void FitInstanceIntoFixedFrame(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        // After the preview rotation the page stands upright: width on X, height on Y.
        const float FillFraction = 0.9f;
        float fit = Mathf.Min(
            bounds.size.x > 0.0001f ? _fixedBoundsSize.x * FillFraction / bounds.size.x : 1f,
            bounds.size.y > 0.0001f ? _fixedBoundsSize.z * FillFraction / bounds.size.y : 1f);

        instance.transform.localScale *= fit;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void WireEdgeZone(UnityEngine.UI.Image zone, UnityEngine.Events.UnityAction action)
    {
        if (zone == null) return;

        // Raycastable strip over the stack edge — the sprite stays visible as a hint.
        zone.raycastTarget = true;

        var button = zone.GetComponent<Button>();
        if (button == null)
            button = zone.gameObject.AddComponent<Button>();

        // ColorTint multiplies the Image color: hover and press tints.
        button.targetGraphic = zone;
        button.transition = Selectable.Transition.ColorTint;

        var colors = button.colors;
        colors.normalColor = Color.white;                     // idle state
        colors.highlightedColor = new Color(1f, 0.92f, 0.7f); // hover — warm highlight
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);    // press — darkened
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.1f;
        button.colors = colors;

        button.onClick.AddListener(action);
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;

        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

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
}
