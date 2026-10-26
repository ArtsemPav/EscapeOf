using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Always-visible embedded 3D preview panel on the right side of the inventory.
/// Attach this to the RawImage that renders the preview.
/// Call Show(item) when the player clicks a slot; Clear() when inventory closes.
/// Drag over the preview image to manually rotate the model; idle spin otherwise.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class InventoryItemPreview : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static InventoryItemPreview Instance { get; private set; }

    [Header("Camera")]
    [Tooltip("Dedicated camera for the inventory preview. Assign 'InventoryPreviewCamera' from the scene.")]
    [SerializeField] private Camera previewCamera;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Rotation")]
    [Tooltip("Degrees per second for the automatic idle spin.")]
    [SerializeField] private float idleSpinSpeed = 30f;
    [Tooltip("Drag sensitivity for manual rotation over the preview image.")]
    [SerializeField] private float dragRotationSpeed = 0.4f;
    [Tooltip("Initial euler rotation applied to the model when it first appears.")]
    [SerializeField] private Vector3 initialRotation = new Vector3(15f, -35f, 0f);

    [Header("Settings")]
    [SerializeField] private string inspectionLayerName = "Inspection";
    [Tooltip("Multiplier for camera orthographic size relative to model bounds. Higher = model appears smaller.")]
    [SerializeField] private float framingMultiplier = 2.2f;

    [Header("Lighting")]
    [Tooltip("Intensity of the key light (main, right-top-front).")]
    [SerializeField] private float keyLightIntensity = 2f;
    [Tooltip("Intensity of the fill light (left, opposite the key). Softens the dark side of rotating models.")]
    [SerializeField] private float fillLightIntensity = 1f;
    [Tooltip("Intensity of the rim light behind the model. Highlights the silhouette against the dark side.")]
    [SerializeField] private float rimLightIntensity = 1.5f;
    [Tooltip("Disable to render with the key light only (old single-light look).")]
    [SerializeField] private bool useThreePointLighting = true;

    // Offset from ItemInspector's origin (0,-1000,0) to avoid camera conflicts.
    private static readonly Vector3 PreviewOrigin = new Vector3(500f, -1000f, 0f);

    private RenderTexture _renderTexture;
    private RawImage      _rawImage;
    private int           _previewLayer;
    private GameObject    _previewPivot;
    private GameObject    _previewKeyLight;
    private GameObject    _previewFillLight;
    private GameObject    _previewRimLight;
    private bool          _hasModel;
    private bool          _isDragging;
    private ItemData      _currentItem;

    private void Awake()
    {
        Instance     = this;
        _rawImage    = GetComponent<RawImage>();
        _previewLayer = LayerMask.NameToLayer(inspectionLayerName);

        // Use a fixed square RT so the orthographic camera and RawImage aspect ratios match.
        // The RawImage must have an AspectRatioFitter (FitInParent, 1:1) on its GameObject.
        _renderTexture = new RenderTexture(512, 512, 16);
        _renderTexture.Create();

        SetupCamera();
        _rawImage.texture = _renderTexture;

        ClearText();
    }

    private void Update()
    {
        if (!_hasModel || _previewPivot == null || _isDragging) return;
        if (_currentItem != null && (_currentItem.disableIdleSpin || _currentItem.lockRotationY)) return;
        _previewPivot.transform.Rotate(Vector3.up, idleSpinSpeed * Time.deltaTime, Space.World);
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    // ── Drag-to-rotate ────────────────────────────────────────────────────────

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            _isDragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_hasModel || _previewPivot == null || !_isDragging) return;
        if (_currentItem == null || !_currentItem.lockRotationY)
            _previewPivot.transform.Rotate(Vector3.up,    -eventData.delta.x * dragRotationSpeed, Space.World);
        if (_currentItem == null || !_currentItem.lockRotationX)
            _previewPivot.transform.Rotate(Vector3.right,  eventData.delta.y * dragRotationSpeed, Space.World);
    }

    public void OnPointerUp(PointerEventData eventData) => _isDragging = false;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows the 3D preview and description for the given item.</summary>
    public void Show(ItemData item)
    {
        if (item == null) { Clear(); return; }

        _currentItem = item;
        DestroyModel();

        if (itemNameText    != null) itemNameText.text    = item.itemName;
        if (descriptionText != null) descriptionText.text = item.description;

        if (item.inspectionPrefab != null)
            SpawnModel(item.inspectionPrefab, item.useCustomPreviewRotation ? item.previewRotation : initialRotation);
    }

    /// <summary>Destroys the 3D model and clears all text.</summary>
    public void Clear()
    {
        _currentItem = null;
        DestroyModel();
        ClearText();
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SetupCamera()
    {
        if (previewCamera == null)
            previewCamera = GameObject.Find("InventoryPreviewCamera")?.GetComponent<Camera>();

        if (previewCamera == null)
        {
            Debug.LogWarning("[InventoryItemPreview] Preview camera not found. " +
                             "Add 'InventoryPreviewCamera' to the scene or assign it in the Inspector.", this);
            return;
        }

        previewCamera.allowHDR        = false;
        previewCamera.orthographic    = true;
        previewCamera.aspect          = 1.0f; // match the square RenderTexture
        previewCamera.clearFlags      = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.targetTexture   = _renderTexture;
        previewCamera.cullingMask     = _previewLayer != -1 ? 1 << _previewLayer : 0;
        previewCamera.gameObject.SetActive(false);
    }

    private void SpawnModel(GameObject prefab, Vector3 startRotation)
    {
        var instance  = Instantiate(prefab, PreviewOrigin, Quaternion.identity);
        SetLayerRecursively(instance, _previewLayer);

        // Важно: инициализируем bounds с первого рендерера, а не с PreviewOrigin.
        // Если сеять с PreviewOrigin, эта точка включается в Encapsulate и тянет
        // center в сторону от реального геометрического центра модели.
        var renderers = instance.GetComponentsInChildren<Renderer>();
        Bounds bounds;
        if (renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
        }
        else
        {
            bounds = new Bounds(PreviewOrigin, Vector3.zero);
        }

        Vector3 center  = bounds.center;
        float   maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        _previewPivot = new GameObject("InventoryPreviewPivot");
        _previewPivot.transform.position = center;
        instance.transform.SetParent(_previewPivot.transform, worldPositionStays: true);
        _previewPivot.transform.rotation = Quaternion.Euler(startRotation);

        if (previewCamera != null)
        {
            previewCamera.orthographicSize   = maxSize * framingMultiplier * 0.5f;
            previewCamera.transform.position = center + new Vector3(0f, 0f, -5f);
            previewCamera.transform.LookAt(center);
            previewCamera.targetTexture      = _renderTexture;
            previewCamera.gameObject.SetActive(true);
        }

        CreatePreviewLights(center);

        _hasModel = true;
    }

    /// <summary>
    /// Creates a three-point lighting rig around the model center. Offsets are
    /// relative to the preview camera (which always sits at center + (0,0,-5)),
    /// so the rig stays consistent for items of any size.
    /// </summary>
    private void CreatePreviewLights(Vector3 center)
    {
        _previewKeyLight = CreateLight(center + new Vector3(0.5f, 1f, -1.5f), keyLightIntensity);

        if (!useThreePointLighting) return;

        // Fill: left side, slightly below — softens the shadow side of the model.
        _previewFillLight = CreateLight(center + new Vector3(-1.5f, -0.5f, -1.0f), fillLightIntensity);

        // Rim: behind and above — outlines the silhouette against dark backgrounds.
        _previewRimLight = CreateLight(center + new Vector3(0f, 1.5f, 2.0f), rimLightIntensity);
    }

    private GameObject CreateLight(Vector3 position, float intensity)
    {
        var lightObject = new GameObject("InventoryPreviewLight");
        var light       = lightObject.AddComponent<Light>();
        light.type      = LightType.Point;
        light.range     = 10f;
        light.intensity = intensity;
        light.color     = Color.white;
        lightObject.transform.position = position;
        return lightObject;
    }

    private void DestroyModel()
    {
        _hasModel   = false;
        _isDragging = false;

        if (_previewPivot != null) { Destroy(_previewPivot); _previewPivot = null; }
        if (_previewKeyLight != null) { Destroy(_previewKeyLight); _previewKeyLight = null; }
        if (_previewFillLight != null) { Destroy(_previewFillLight); _previewFillLight = null; }
        if (_previewRimLight != null) { Destroy(_previewRimLight); _previewRimLight = null; }

        if (previewCamera != null) previewCamera.gameObject.SetActive(false);
    }

    private void ClearText()
    {
        if (itemNameText    != null) itemNameText.text    = string.Empty;
        if (descriptionText != null) descriptionText.text = string.Empty;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
