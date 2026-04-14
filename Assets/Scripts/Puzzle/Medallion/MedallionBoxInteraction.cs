using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Attach to the chinesBox GameObject.
/// On LMB click — activates ChinesBoxCamera and opens MedallionBoxPanel.
/// Closes on: CloseButton click, ESC, WASD, or click on the left/right side zones.
/// Player input is restored only after the camera blend-back animation finishes.
/// Fires <see cref="OnPuzzleSolved"/> (UnityEvent) when all medallions are correctly placed.
/// Implements ISaveable to persist puzzle state (which holes are filled, solved flag).
///
/// <para><b>Execution order: -7.</b> Must run after SaveManager (-10) so <see cref="LoadSaveData"/>
/// is called before <c>Start</c>, and before MedallionCollectionTracker (-5) so
/// <see cref="ApplyPendingLoad"/> fills the holes before the tracker's startup sync can
/// trigger a <c>Save()</c> that would snapshot the holes as empty.</para>
/// </summary>
[DefaultExecutionOrder(-7)] // After SaveManager (-10), before MedallionCollectionTracker (-5)
[RequireComponent(typeof(Collider))]
public class MedallionBoxInteraction : MonoBehaviour, IInteractable, ISaveable
{
    [Header("References")]
    [Tooltip("The CinemachineCamera that frames the box.")]
    [SerializeField] private CinemachineCamera _boxCamera;

    [Tooltip("Root panel GameObject in Canvas.")]
    [SerializeField] private GameObject _panel;

    [Tooltip("GameObject to activate when the puzzle is solved (e.g. the 'solved' light).")]
    [SerializeField] private GameObject _solvedObject;

    [Header("Puzzle — Medallion Order")]
    [Tooltip("Assign in order: slot 0=Fire, 1=Earth, 2=Iron, 3=Water, 4=Wood. " +
             "Must match the MedallionHole expected items on Hole_0..4.")]
    [SerializeField] private ItemData[] _medallionOrder;

    [Header("Settings")]
    [SerializeField] private string _interactText = "Осмотреть шкатулку";

    [Tooltip("Duration of camera blend in seconds (both zoom-in and zoom-out).")]
    [SerializeField] private float _blendDuration = 0.75f;

    [Tooltip("Fraction of screen width on each side that acts as a click-to-close zone. " +
             "The center area is left free for box interaction.")]
    [Range(0.05f, 0.49f)]
    [SerializeField] private float _sideZoneWidth = 0.25f;

    [Header("Events")]
    [Tooltip("Fired when all medallions are placed in the correct holes.")]
    [SerializeField] private UnityEvent _onPuzzleSolved;

    // ── State ─────────────────────────────────────────────────────────────────

    private bool _isOpen;
    private bool _puzzleSolved;
    private Button _closeButton;
    private readonly List<Button> _sideButtons = new();
    private CinemachineBrain _brain;
    private float _originalBlendTime;

    // Pending save data stored between LoadSaveData() and Start()
    private PuzzleSaveData? _pendingLoad;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => "medallion_puzzle";

    /// <summary>Serializes solved flag and per-hole item IDs.</summary>
    public string GetSaveData()
    {
        var boxUI = _panel?.GetComponent<MedallionBoxUI>();
        var holeStates = boxUI?.GetHoleStates() ?? Array.Empty<ItemData>();

        var ids = new string[holeStates.Length];
        for (int i = 0; i < holeStates.Length; i++)
            ids[i] = holeStates[i]?.ItemId ?? string.Empty;

        return JsonUtility.ToJson(new PuzzleSaveData { solved = _puzzleSolved, placedItemIds = ids });
    }

    /// <summary>Stores the loaded data — applied in Start() once all systems are ready.</summary>
    public void LoadSaveData(string json)
    {
        _pendingLoad = JsonUtility.FromJson<PuzzleSaveData>(json);
    }

    [Serializable]
    private struct PuzzleSaveData
    {
        public bool     solved;
        public string[] placedItemIds;
    }

    // ── IInteractable ─────────────────────────────────────────────────────────

    public bool IsPickable() => false;
    public bool UseLMBClick => true;
    public string GetInteractText() => _interactText;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Read;

    /// <summary>Box is interactable only when the panel is closed and puzzle is not yet solved.</summary>
    public bool CanInteract() => !_isOpen && !_puzzleSolved;

    /// <summary>Opens the camera view and panel.</summary>
    public void Interact()
    {
        if (_isOpen) return;
        Open();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        _brain = Camera.main?.GetComponent<CinemachineBrain>();
        if (_brain != null)
            _originalBlendTime = _brain.DefaultBlend.Time;

        // Register before SaveManager.Start() calls Load()
        SaveManager.Instance?.Register(this);
    }

    private void Start()
    {
        if (_panel != null)
        {
            CreateSideZone("BackdropLeft",  new Vector2(0f, 0f),                   new Vector2(_sideZoneWidth, 1f));
            CreateSideZone("BackdropRight", new Vector2(1f - _sideZoneWidth, 0f),  new Vector2(1f, 1f));

            Transform t = _panel.transform.Find("CloseButton");
            if (t != null)
            {
                _closeButton = t.GetComponent<Button>();
                if (_closeButton != null)
                    _closeButton.onClick.AddListener(Close);
            }
        }

        // Apply save data now that InventorySystem and MedallionBoxUI are both ready
        ApplyPendingLoad();
    }

    private void OnDestroy()
    {
        if (_closeButton != null)
            _closeButton.onClick.RemoveListener(Close);

        foreach (var btn in _sideButtons)
            if (btn != null) btn.onClick.RemoveListener(Close);

        SaveManager.Instance?.Unregister(this);
    }

    private void Update()
    {
        if (!_isOpen) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame
            || kb.wKey.isPressed || kb.sKey.isPressed
            || kb.aKey.isPressed || kb.dKey.isPressed)
        {
            Close();
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private void ApplyPendingLoad()
    {
        if (_pendingLoad == null) return;

        var load = _pendingLoad.Value;
        _pendingLoad = null;

        // Restore coin visuals in holes.
        // Called in Start() at order -7, before MedallionCollectionTracker.Start() (-5),
        // so any Save() triggered by the tracker's startup sync captures the correct hole state.
        if (load.placedItemIds != null && load.placedItemIds.Length > 0)
        {
            var boxUI = _panel?.GetComponent<MedallionBoxUI>();
            boxUI?.RestoreState(load.placedItemIds, _medallionOrder);
        }

        // Restore solved state
        if (load.solved)
        {
            _puzzleSolved = true;
            if (_solvedObject != null)
                _solvedObject.SetActive(true);
        }
    }

    private void Open()
    {
        _isOpen = true;
        SetBlendDuration(_blendDuration);

        if (_boxCamera != null)
            _boxCamera.gameObject.SetActive(true);

        if (_panel != null)
            UIManager.Instance?.OpenPanel(_panel);

        var boxUI = _panel?.GetComponent<MedallionBoxUI>();
        if (boxUI != null)
        {
            boxUI.OnPuzzleSolved -= HandlePuzzleSolved;
            boxUI.OnPuzzleSolved += HandlePuzzleSolved;
            boxUI.Populate(_medallionOrder);

            PuzzleInventoryBar.Instance?.Show(boxUI);
        }
    }

    private void HandlePuzzleSolved()
    {
        _puzzleSolved = true;

        if (_solvedObject != null)
            _solvedObject.SetActive(true);

        _onPuzzleSolved?.Invoke();
        SaveManager.Instance?.Save();
        ForceClose();
    }

    private void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        PuzzleInventoryBar.Instance?.Hide();
        SetBlendDuration(_blendDuration);

        if (_panel != null)
            _panel.SetActive(false);

        if (_boxCamera != null)
            _boxCamera.gameObject.SetActive(false);

        StartCoroutine(RestoreInputAfterBlend());
    }

    private IEnumerator RestoreInputAfterBlend()
    {
        yield return null;

        while (_brain != null && _brain.IsBlending)
            yield return null;

        SetBlendDuration(_originalBlendTime);

        if (_panel != null)
            UIManager.Instance?.ClosePanel(_panel);
    }

    private void SetBlendDuration(float duration)
    {
        if (_brain == null) return;
        var blend = _brain.DefaultBlend;
        blend.Time = duration;
        _brain.DefaultBlend = blend;
    }

    private void CreateSideZone(string zoneName, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(zoneName, typeof(RectTransform));
        go.transform.SetParent(_panel.transform, false);
        go.transform.SetAsFirstSibling();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var cb = btn.colors;
        cb.normalColor = cb.highlightedColor = cb.pressedColor = cb.selectedColor = Color.white;
        btn.colors = cb;

        btn.onClick.AddListener(Close);
        _sideButtons.Add(btn);
    }

    /// <summary>Force-close from external code.</summary>
    public void ForceClose() => Close();
}
