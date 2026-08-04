using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Singleton. Displays a readable document with 3D preview, darkened background,
/// and paginated text. Reuses the Inspection camera pattern from ItemInspector.
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

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _contentText;

    [Tooltip("Родительский объект текстового блока (заголовок + контент). Скрывается до завершения анимации вылета 3D-объекта.")]
    [SerializeField] private GameObject _textBlock;

    [Header("Navigation")]
    [SerializeField] private Button _prevPageButton;
    [SerializeField] private Button _nextPageButton;
    [SerializeField] private TextMeshProUGUI _pageIndicator;

    [Header("Darkening")]
    [Tooltip("Длительность затемнения экрана (секунды).")]
    [SerializeField, Min(0f)] private float _darkenDuration = 0.4f;

    [Tooltip("Целевая непрозрачность затемнения. 1 = полностью чёрный, 0 = прозрачный.")]
    [SerializeField, Range(0f, 1f)] private float _darkenAlpha = 0.85f;

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

    private GameObject _inspectionInstance;
    private GameObject _inspectionPivot;
    private GameObject _inspectionLight;
    private GameObject _inspectionLightRim;

    private bool _isOpen;
    private bool _isAnimating;
    private bool _justOpened;

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

        // Apply typography.
        ApplyTypography(data);

        // Prepare first page text but hide it until the 3D fly-in animation finishes.
        ShowPage(0);
        if (_textBlock != null)
            _textBlock.SetActive(false);

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

        // Activate panel and block player input.
        _panel.SetActive(true);
        UIManager.Instance?.OpenPanel(_panel);

        // Darken the background.
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.FadeIn(_darkenDuration, _darkenAlpha);

        // Play open sound.
        if (data.openClip != null)
            AudioManager.Instance?.PlaySFX(data.openClip);

        // Start scale-in animation — text appears after the 3D object finishes scaling in.
        if (data.documentPrefab != null && _inspectionPivot != null)
            StartCoroutine(ScaleInRoutine());
        else if (_textBlock != null)
            _textBlock.SetActive(true);
    }

    /// <summary>Closes the document panel with a reverse fly-out animation.</summary>
    public void Close()
    {
        if (!_isOpen || _isAnimating) return;

        _isOpen = false;

        // Hide text before the 3D document scales out.
        if (_textBlock != null)
            _textBlock.SetActive(false);

        // Play close sound.
        if (_documentData != null && _documentData.closeClip != null)
            AudioManager.Instance?.PlaySFX(_documentData.closeClip);

        if (_inspectionPivot != null && _documentData != null && _documentData.documentPrefab != null)
            StartCoroutine(ScaleOutRoutine());
        else
            FinishClose();
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    /// <summary>Shows the page at the given index.</summary>
    private void ShowPage(int index)
    {
        if (_documentData == null || _documentData.pages == null || _documentData.pages.Count == 0)
        {
            _contentText.text = string.Empty;
            if (_pageIndicator != null)
                _pageIndicator.text = string.Empty;
            UpdateNavButtons(0, 0);
            return;
        }

        index = Mathf.Clamp(index, 0, _documentData.pages.Count - 1);
        _currentPage = index;

        _contentText.text = _documentData.pages[index];

        if (_pageIndicator != null)
        {
            int total = _documentData.pages.Count;
            _pageIndicator.text = total > 1 ? $"{index + 1} / {total}" : string.Empty;
        }

        UpdateNavButtons(index, _documentData.pages.Count);
    }

    /// <summary>Navigates to the next page if available.</summary>
    private void NextPage()
    {
        if (_documentData == null || _documentData.pages == null) return;
        if (_currentPage < _documentData.pages.Count - 1)
        {
            ShowPage(_currentPage + 1);
            PlayPageTurnSound();
        }
    }

    /// <summary>Navigates to the previous page if available.</summary>
    private void PrevPage()
    {
        if (_currentPage > 0)
        {
            ShowPage(_currentPage - 1);
            PlayPageTurnSound();
        }
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
        if (_prevPageButton != null)
            _prevPageButton.interactable = index > 0;
        if (_nextPageButton != null)
            _nextPageButton.interactable = index < total - 1;
    }

    // ── Typography ───────────────────────────────────────────────────────────

    /// <summary>Applies font, size, color, and alignment from DocumentData to TMP components.</summary>
    private void ApplyTypography(DocumentData data)
    {
        if (_titleText != null)
        {
            _titleText.text = data.title;
            if (data.titleFont != null)
                _titleText.font = data.titleFont;
            _titleText.fontSize = data.titleFontSize;
            _titleText.color = data.titleColor;
            _titleText.alignment = data.titleAlignment;
        }

        if (_contentText != null)
        {
            if (data.font != null)
                _contentText.font = data.font;
            _contentText.fontSize = data.fontSize;
            _contentText.color = data.fontColor;
            _contentText.alignment = data.textAlignment;
        }
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

    // ── Animation ────────────────────────────────────────────────────────────

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

        // Reveal text after the 3D document has finished scaling in.
        if (_textBlock != null)
            _textBlock.SetActive(true);
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
