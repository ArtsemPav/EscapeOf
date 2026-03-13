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

    [Header("Idle Spin")]
    [Tooltip("Duration of the opening spin animation in seconds.")]
    [SerializeField] private float idleSpinDuration = 1.8f;
    [Tooltip("Peak rotation speed at the start of the idle spin (degrees/sec).")]
    [SerializeField] private float idleSpinSpeed = 80f;

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
    private FPSController _playerController;
    private bool _isInspecting;
    private bool _ignoreInputThisFrame;
    private float _idleSpinTimer;

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
        _playerController = Object.FindFirstObjectByType<FPSController>();
    }

    private void Update()
    {
        if (!_isInspecting || _inspectionPivot == null) return;

        // Пропускаем первый кадр — та же кнопка E открыла инспекцию
        if (_ignoreInputThisFrame)
        {
            _ignoreInputThisFrame = false;
            return;
        }

        bool userDragging = Mouse.current.leftButton.isPressed;

        // Idle spin: плавно замедляется (ease-out cosine) и останавливается.
        // Прерывается как только пользователь начинает крутить мышью.
        if (_idleSpinTimer > 0f)
        {
            if (userDragging)
            {
                _idleSpinTimer = 0f;
            }
            else
            {
                float t = 1f - (_idleSpinTimer / idleSpinDuration);          // 0 → 1
                float easedSpeed = idleSpinSpeed * Mathf.Cos(t * Mathf.PI * 0.5f); // ease-out
                _inspectionPivot.transform.Rotate(Vector3.up, easedSpeed * Time.deltaTime, Space.World);
                _idleSpinTimer -= Time.deltaTime;
            }
        }

        if (userDragging)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            _inspectionPivot.transform.Rotate(Vector3.up,    -delta.x * rotationSpeed * Time.deltaTime, Space.World);
            _inspectionPivot.transform.Rotate(Vector3.right,  delta.y * rotationSpeed * Time.deltaTime, Space.World);
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
            ConfirmPickup();

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            CancelInspection();
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
        descriptionText.text = item.description;

        // Переприсваиваем текстуру на случай если ссылка была сброшена
        inspectionCamera.targetTexture = _renderTexture;
        previewImage.texture = _renderTexture;

        inspectionCamera.gameObject.SetActive(true);
        inspectionPanel.SetActive(true);
        _isInspecting = true;
        _idleSpinTimer = idleSpinDuration;
        _ignoreInputThisFrame = true; // E использована для открытия — не закрываем в этом же кадре

        _playerController?.SetPlayerInputEnabled(false);
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    /// <summary>Adds item to inventory and closes the inspection view.</summary>
    public void ConfirmPickup()
    {
        if (_currentItem == null) return;
        InventorySystem.Instance.AddItem(_currentItem);
        if (_worldObject != null) Destroy(_worldObject);
        EndInspection();
    }

    /// <summary>Closes the inspection without picking up.</summary>
    public void CancelInspection()
    {
        EndInspection();
    }

    private void EndInspection()
    {
        _isInspecting = false;

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
        inspectionPanel.SetActive(false);

        _playerController?.SetPlayerInputEnabled(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

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
