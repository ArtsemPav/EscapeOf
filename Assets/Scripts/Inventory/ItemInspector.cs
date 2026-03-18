using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Singleton. Shows a 3D inspection view of a world item before adding it to inventory.
/// Uses a dedicated camera rendering to a runtime RenderTexture displayed via RawImage.
/// Left mouse drag rotates the item. E — take, Escape — cancel.
/// </summary>
public class ItemInspector : MonoBehaviour
{
    public static ItemInspector Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private Camera inspectionCamera;

    [Header("UI")]
    [SerializeField] private GameObject inspectionPanel;
    [SerializeField] private RectTransform previewFrame;
    [SerializeField] private RawImage previewImage;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("Rotation")]
    [SerializeField] private float rotationSpeed = 180f;
    [Tooltip("Initial euler rotation applied to the model when inspection opens. Gives a 3/4 perspective look by default.")]
    [SerializeField] private Vector3 initialRotation = new Vector3(15f, -35f, 0f);

    [Header("Scale-In Animation")]
    [Tooltip("Duration of the scale-in animation when inspection opens (seconds).")]
    [SerializeField] private float scaleInDuration = 0.45f;

    [Header("Idle Spin")]
    [Tooltip("Continuous rotation speed while the player is not dragging (degrees/sec).")]
    [SerializeField] private float idleSpinSpeed = 40f;

    [Header("Settings")]
    [SerializeField] private string inspectionLayerName = "Inspection";
    [Tooltip("Multiplier for camera distance from model. Higher = model appears smaller.")]
    [SerializeField] private float framingMultiplier = 2.2f;

    private static readonly Vector3 InspectionOrigin = new Vector3(0f, -1000f, 0f);
    private const float InspectionDistance = 1.5f;

    private RenderTexture _renderTexture;
    private int _inspectionLayer;
    private ItemData _currentItem;
    private GameObject _worldObject;
    private GameObject _inspectionInstance;
    private GameObject _inspectionPivot;
    private GameObject _inspectionLight;
    private bool _isInspecting;
    private bool _ignoreInputThisFrame;
    private float _scaleInTimer;

    // True when the panel is open as a read-only preview from the inventory (no pickup).
    private bool _isPreviewMode;

    private const float DragThresholdPx = 8f;
    private Vector2 _mouseDownPos;
    private bool _mouseWasDragged;

    // True when inspection was opened while LMB was already held (e.g. LMB pickup).
    // Prevents the held click from immediately cancelling the idle spin or confirming pickup.
    private bool _waitForMouseRelease;

    private void Awake()
    {
        Instance = this;

        try
        {
            _inspectionLayer = LayerMask.NameToLayer(inspectionLayerName);
            if (_inspectionLayer == -1)
                Debug.LogError($"ItemInspector: Layer '{inspectionLayerName}' not found.", this);

            // RenderTexture в размерах экрана — точное совпадение с aspect ratio PreviewImage
            int rtWidth  = Mathf.Max(Screen.width,  128);
            int rtHeight = Mathf.Max(Screen.height, 128);
            _renderTexture = new RenderTexture(rtWidth, rtHeight, 16);
            _renderTexture.Create();

            if (inspectionCamera != null)
            {
                inspectionCamera.allowHDR        = false;
                inspectionCamera.orthographic    = true;   // нет перспективного искажения
                inspectionCamera.clearFlags      = CameraClearFlags.SolidColor;
                inspectionCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                inspectionCamera.targetTexture   = _renderTexture;
                inspectionCamera.cullingMask     = _inspectionLayer != -1 ? 1 << _inspectionLayer : 0;
            }

            if (previewImage != null)
                previewImage.texture = _renderTexture;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ItemInspector] Awake exception: {e}", this);
        }
    }

    private void Start()
    {
    }

    private void Update()
    {
        if (!_isInspecting || _inspectionPivot == null) return;

        // Пропускаем первый кадр — та же кнопка открыла инспекцию
        if (_ignoreInputThisFrame)
        {
            _ignoreInputThisFrame = false;
            return;
        }

        // If inspection was opened while LMB was held, wait until the player releases it.
        // This prevents the lingering click from cancelling the idle spin or confirming pickup.
        if (_waitForMouseRelease)
        {
            if (Mouse.current.leftButton.isPressed)
                return;

            // LMB just released this frame — clear the flag but skip this frame entirely
            // so wasReleasedThisFrame doesn't immediately trigger ConfirmPickup.
            _waitForMouseRelease = false;
            return;
        }

        bool userDragging = Mouse.current.leftButton.isPressed;

        // Scale-in: ease-out cubic from 0 → 1 over scaleInDuration.
        if (_scaleInTimer > 0f)
        {
            float t     = 1f - (_scaleInTimer / scaleInDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            _inspectionPivot.transform.localScale = Vector3.one * eased;
            _scaleInTimer -= Time.deltaTime;

            if (_scaleInTimer <= 0f)
                _inspectionPivot.transform.localScale = Vector3.one;
        }

        // Track click vs drag: record position on press, flag if moved beyond threshold.
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _mouseDownPos    = Mouse.current.position.ReadValue();
            _mouseWasDragged = false;
        }

        if (userDragging && (Mouse.current.position.ReadValue() - _mouseDownPos).magnitude > DragThresholdPx)
            _mouseWasDragged = true;

        if (_isPreviewMode)
        {
            // In preview mode: rotation only, close on RMB / Escape / E.
            if (userDragging)
            {
                Vector2 delta = Mouse.current.delta.ReadValue();
                _inspectionPivot.transform.Rotate(Vector3.up,    -delta.x * rotationSpeed * Time.deltaTime, Space.World);
                _inspectionPivot.transform.Rotate(Vector3.right,  delta.y * rotationSpeed * Time.deltaTime, Space.World);
            }
            else
            {
                _inspectionPivot.transform.Rotate(Vector3.up, idleSpinSpeed * Time.deltaTime, Space.World);
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame ||
                Keyboard.current.eKey.wasPressedThisFrame ||
                Mouse.current.rightButton.wasPressedThisFrame)
            {
                EndInspection();
            }
            return;
        }

        // Short click (no drag) anywhere on screen → pick up.
        if (Mouse.current.leftButton.wasReleasedThisFrame && !_mouseWasDragged)
        {
            ConfirmPickup();
            return;
        }

        if (userDragging)
        {
            // Manual rotation while dragging.
            Vector2 delta = Mouse.current.delta.ReadValue();
            _inspectionPivot.transform.Rotate(Vector3.up,    -delta.x * rotationSpeed * Time.deltaTime, Space.World);
            _inspectionPivot.transform.Rotate(Vector3.right,  delta.y * rotationSpeed * Time.deltaTime, Space.World);
        }
        else
        {
            // Continuous idle spin when not dragging.
            _inspectionPivot.transform.Rotate(Vector3.up, idleSpinSpeed * Time.deltaTime, Space.World);
        }

        if (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ConfirmPickup();
            return;
        }
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    /// <summary>
    /// Shows 3D inspection view. If itemData has no inspectionPrefab, picks up directly.
    /// </summary>
    public void BeginInspection(ItemData item, GameObject worldObject)
    {
        if (item == null) return;

        if (item.inspectionPrefab == null)
        {
            InventorySystem.Instance.AddItem(item);
            Destroy(worldObject);
            return;
        }

        _currentItem = item;
        _worldObject = worldObject;
        _isPreviewMode = false;

        SpawnPreview(item);

        UIManager.Instance?.OpenPanel(inspectionPanel, CursorLockMode.Confined);
        _isInspecting         = true;
        _scaleInTimer         = scaleInDuration;
        _inspectionPivot.transform.localScale = Vector3.zero;
        _ignoreInputThisFrame = true;
        _waitForMouseRelease  = Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    /// <summary>
    /// Opens a read-only 3D preview of an inventory item (no pickup, no world object).
    /// Close with RMB, E, or Escape.
    /// </summary>
    public void BeginPreview(ItemData item)
    {
        if (item == null || item.inspectionPrefab == null) return;
        if (_isInspecting) return;

        _currentItem   = item;
        _worldObject   = null;
        _isPreviewMode = true;

        SpawnPreview(item);

        UIManager.Instance?.OpenPanel(inspectionPanel, CursorLockMode.Confined);
        _isInspecting         = true;
        _scaleInTimer         = scaleInDuration;
        _inspectionPivot.transform.localScale = Vector3.zero;
        _ignoreInputThisFrame = true;
        _waitForMouseRelease  = false;
    }

    private void SpawnPreview(ItemData item)
    {
        _inspectionInstance = Instantiate(item.inspectionPrefab, InspectionOrigin, Quaternion.identity);
        SetLayerRecursively(_inspectionInstance, _inspectionLayer);

        // Вычисляем геометрический центр модели по bounds всех рендереров
        var renderers = _inspectionInstance.GetComponentsInChildren<Renderer>();
        var bounds = new Bounds(InspectionOrigin, Vector3.zero);
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        Vector3 itemCenter = bounds.center;
        float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);

        // Пивот в геометрическом центре — вращение без смещения
        _inspectionPivot = new GameObject("InspectionPivot");
        _inspectionPivot.transform.position = itemCenter;
        _inspectionInstance.transform.SetParent(_inspectionPivot.transform, worldPositionStays: true);
        // Применяем начальный поворот после парентинга — иначе worldPositionStays компенсирует его
        _inspectionPivot.transform.rotation = Quaternion.Euler(initialRotation);

        // Orthographic: размер вида = половина maxSize × множитель
        inspectionCamera.orthographicSize = maxSize * framingMultiplier * 0.5f;
        inspectionCamera.transform.position = itemCenter + new Vector3(0f, 0f, -5f);
        inspectionCamera.transform.LookAt(itemCenter);

        // Создаём Point Light в пространстве инспекции
        _inspectionLight = new GameObject("InspectionLight");
        var light = _inspectionLight.AddComponent<Light>();
        light.type      = LightType.Point;
        light.range     = 10f;
        light.intensity = 2f;
        light.color     = Color.white;
        _inspectionLight.transform.position = itemCenter + new Vector3(0.5f, 1f, -1.5f);

        itemNameText.text = item.itemName;
        itemNameText.gameObject.SetActive(!_isPreviewMode);

        // В режиме превью из инвентаря описание не показываем —
        // игрок уже видел его в тултипе слота.
        if (descriptionText != null)
        {
            descriptionText.text    = _isPreviewMode ? string.Empty : item.description;
            descriptionText.gameObject.SetActive(!_isPreviewMode);
        }

        // Переприсваиваем текстуру на случай если ссылка была сброшена
        inspectionCamera.targetTexture = _renderTexture;
        previewImage.texture           = _renderTexture;
        inspectionCamera.gameObject.SetActive(true);
    }

    /// <summary>
    /// Forcibly closes a preview-mode inspection without touching the inventory or world object.
    /// Called by InventoryUI when the inventory is closed while preview is active.
    /// Does nothing if not currently in preview mode.
    /// </summary>
    public void CancelPreviewIfActive()
    {
        if (_isInspecting && _isPreviewMode)
            EndInspection();
    }

    /// <summary>Adds item to inventory and closes the inspection view.</summary>
    public void ConfirmPickup()
    {
        if (_currentItem == null) return;
        if (!_isPreviewMode)
        {
            InventorySystem.Instance.AddItem(_currentItem);
            if (_worldObject != null) Destroy(_worldObject);
        }
        EndInspection();
    }

    /// <summary>Closes the inspection without picking up the item.</summary>
    public void CancelInspection()
    {
        // Cancelling is no longer supported — the item is always picked up.
        ConfirmPickup();
    }

    private void EndInspection()
    {
        _isInspecting  = false;
        _isPreviewMode = false;

        // Восстанавливаем текстовые элементы — они могли быть скрыты в режиме превью
        if (itemNameText != null)
            itemNameText.gameObject.SetActive(true);
        if (descriptionText != null)
            descriptionText.gameObject.SetActive(true);

        // Уничтожаем пивот — instance уничтожится как дочерний объект
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

        inspectionCamera.gameObject.SetActive(false);
        UIManager.Instance?.ClosePanel(inspectionPanel);

        _currentItem = null;
        _worldObject = null;
    }

    private static void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer == -1) return;
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}
