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

    // Offset from ItemInspector's origin (0,-1000,0) to avoid camera conflicts.
    private static readonly Vector3 PreviewOrigin = new Vector3(500f, -1000f, 0f);

    private RenderTexture _renderTexture;
    private RawImage      _rawImage;
    private int           _previewLayer;
    private GameObject    _previewPivot;
    private GameObject    _previewLight;
    private bool          _hasModel;
    private bool          _isDragging;

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
        _previewPivot.transform.Rotate(Vector3.up,    -eventData.delta.x * dragRotationSpeed, Space.World);
        _previewPivot.transform.Rotate(Vector3.right,  eventData.delta.y * dragRotationSpeed, Space.World);
    }

    public void OnPointerUp(PointerEventData eventData) => _isDragging = false;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Shows the 3D preview and description for the given item.</summary>
    public void Show(ItemData item)
    {
        if (item == null) { Clear(); return; }

        DestroyModel();

        if (itemNameText    != null) itemNameText.text    = item.itemName;
        if (descriptionText != null) descriptionText.text = item.description;

        if (item.inspectionPrefab != null)
            SpawnModel(item.inspectionPrefab);
    }

    /// <summary>Destroys the 3D model and clears all text.</summary>
    public void Clear()
    {
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

    private void SpawnModel(GameObject prefab)
    {
        var instance  = Instantiate(prefab, PreviewOrigin, Quaternion.identity);
        SetLayerRecursively(instance, _previewLayer);

        var renderers = instance.GetComponentsInChildren<Renderer>();
        var bounds    = new Bounds(PreviewOrigin, Vector3.zero);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);

        Vector3 center  = bounds.center;
        float   maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        _previewPivot = new GameObject("InventoryPreviewPivot");
        _previewPivot.transform.position = center;
        instance.transform.SetParent(_previewPivot.transform, worldPositionStays: true);
        _previewPivot.transform.rotation = Quaternion.Euler(initialRotation);

        if (previewCamera != null)
        {
            previewCamera.orthographicSize   = maxSize * framingMultiplier * 0.5f;
            previewCamera.transform.position = center + new Vector3(0f, 0f, -5f);
            previewCamera.transform.LookAt(center);
            previewCamera.targetTexture      = _renderTexture;
            previewCamera.gameObject.SetActive(true);
        }

        _previewLight = new GameObject("InventoryPreviewLight");
        var light     = _previewLight.AddComponent<Light>();
        light.type      = LightType.Point;
        light.range     = 10f;
        light.intensity = 2f;
        light.color     = Color.white;
        _previewLight.transform.position = center + new Vector3(0.5f, 1f, -1.5f);

        _hasModel = true;
    }

    private void DestroyModel()
    {
        _hasModel   = false;
        _isDragging = false;

        if (_previewPivot != null) { Destroy(_previewPivot); _previewPivot = null; }
        if (_previewLight != null) { Destroy(_previewLight); _previewLight = null; }

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
