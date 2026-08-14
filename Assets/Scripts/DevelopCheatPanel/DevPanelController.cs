using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// In-game developer panel for testing and cheating.
/// Toggle with the backquote (`) key.
/// Provides item giving, puzzle solving, and game progression controls.
/// Attach to any GameObject in the scene — the panel auto-parents to the Canvas.
/// </summary>
public class DevPanelController : MonoBehaviour
{
    [Header("Panel Size")]
    [SerializeField] private float _panelWidth = 520f;
    [SerializeField] private float _panelHeight = 720f;

    [Header("Element Sizes")]
    [SerializeField] private float _headerHeight = 36f;
    [SerializeField] private float _tabHeight = 32f;
    [SerializeField] private float _statusHeight = 28f;
    [SerializeField] private float _buttonHeight = 34f;
    [SerializeField] private float _itemRowHeight = 32f;
    [SerializeField] private float _iconSize = 32f;

    [Header("Item Row Spacing")]
    [SerializeField] private float _iconLeftPadding = 8f;
    [SerializeField] private float _iconTextSpacing = 8f;

    [Header("Layout")]
    [SerializeField] private float _spacing = 4f;
    [SerializeField] private float _padding = 8f;

    [Header("Font")]
    [SerializeField] private int _fontSize = 13;
    [SerializeField] private int _headerFontSize = 16;
    [SerializeField] private int _statusFontSize = 12;

    [Header("Colors")]
    [SerializeField] private Color _panelColor = new(0.12f, 0.12f, 0.12f, 0.96f);
    [SerializeField] private Color _headerColor = new(0.08f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color _tabColor = new(0.18f, 0.18f, 0.18f, 1f);
    [SerializeField] private Color _tabSelectedColor = new(0.25f, 0.5f, 0.8f, 1f);
    [SerializeField] private Color _itemRowColor = new(0.16f, 0.16f, 0.16f, 0.8f);
    [SerializeField] private Color _giveBtnColor = new(0.18f, 0.55f, 0.18f, 1f);
    [SerializeField] private Color _puzzleBtnColor = new(0.5f, 0.35f, 0.15f, 1f);
    [SerializeField] private Color _gameBtnColor = new(0.18f, 0.38f, 0.58f, 1f);
    [SerializeField] private Color _closeBtnColor = new(0.6f, 0.18f, 0.18f, 1f);
    [SerializeField] private Color _statusColor = new(0.08f, 0.08f, 0.08f, 1f);
    [SerializeField] private Color _scrollBgColor = new(0.08f, 0.08f, 0.08f, 0.5f);

    [Header("Target Canvas")]
    [Tooltip("Assign the main ScreenSpaceOverlay Canvas. If left empty, falls back to searching for one.")]
    [SerializeField] private Canvas _targetCanvas;

    [Header("Player Spawn")]
    [Tooltip("Assign a Transform to use as the player reset position. If empty, the player's position at Awake is used.")]
    [SerializeField] private Transform _playerSpawnPoint;

    private Vector3 _cachedSpawnPosition;
    private float _cachedSpawnYaw;

    private GameObject _panel;
    private GameObject _itemsPage;
    private GameObject _puzzlesPage;
    private GameObject _gamePage;
    private Transform _itemsContent;
    private Button _itemsTab;
    private Button _puzzlesTab;
    private Button _gameTab;
    private TMP_Text _statusText;
    private TMP_FontAsset _font;
    private bool _isVisible;
    private bool _wasBackquotePressed;
    private Action _pendingDeferredAction;

    private void Start()
    {
        try
        {
            FindFont();
            CacheSpawnPosition();
            BuildUI();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DevPanel] Initialization failed: {e}");
        }
    }

    /// <summary>Caches the spawn position from _playerSpawnPoint or the player's current transform.</summary>
    private void CacheSpawnPosition()
    {
        if (_playerSpawnPoint != null)
        {
            _cachedSpawnPosition = _playerSpawnPoint.position;
            _cachedSpawnYaw = _playerSpawnPoint.eulerAngles.y;
            return;
        }

        FPSController player = UIManager.Instance?.PlayerController;
        if (player != null)
        {
            _cachedSpawnPosition = player.transform.position;
            _cachedSpawnYaw = player.transform.eulerAngles.y;
        }
    }

    /// <summary>Teleports the player to the cached spawn position.</summary>
    private void ResetPlayerPosition()
    {
        FPSController player = UIManager.Instance?.PlayerController;
        if (player == null)
        {
            Log("Player not found — cannot reset position.");
            return;
        }

        player.Teleport(_cachedSpawnPosition, _cachedSpawnYaw);
        Log($"Player teleported to {_cachedSpawnPosition}.");
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        bool isBackquotePressed = Keyboard.current != null && Keyboard.current[Key.Backquote].isPressed;
        if (isBackquotePressed && !_wasBackquotePressed)
        {
            if (_isVisible) Close();
            else Toggle();
        }
        _wasBackquotePressed = isBackquotePressed;

        if (_pendingDeferredAction != null && Time.timeScale > 0f)
        {
            Action action = _pendingDeferredAction;
            _pendingDeferredAction = null;
            action.Invoke();
            Log("Deferred cheat action executed.");
        }
#endif
    }

    /// <summary>Opens the dev panel and manages cursor/input via UIManager.</summary>
    private void Toggle()
    {
        if (_panel == null)
        {
            Debug.LogWarning("[DevPanel] Panel not built — attempting rebuild.");
            try { BuildUI(); } catch (System.Exception e) { Debug.LogError($"[DevPanel] Rebuild failed: {e}"); return; }
            if (_panel == null) return;
        }
        if (_isVisible) return;
        _isVisible = true;
        _panel.SetActive(true);
        ShowPage(_itemsPage, _itemsTab);
        UIManager.Instance?.OpenPanel(_panel);
        PopulateItems();
        Log("Dev panel opened — press ` to close.");
    }

    /// <summary>Closes the dev panel. Used by the X button and the toggle key.</summary>
    private void Close()
    {
        if (_panel == null) return;
        if (!_isVisible) return;
        _isVisible = false;
        _panel.SetActive(false);
        UIManager.Instance?.ClosePanel(_panel);
    }

    // ── UI Construction ───────────────────────────────────────────────────

    private void FindFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
        {
            _font = TMP_Settings.defaultFontAsset;
            return;
        }

        var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        if (fonts != null && fonts.Length > 0)
            _font = fonts[0];
    }

    private void BuildUI()
    {
        Canvas canvas = _targetCanvas;
        if (canvas == null)
            canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            var canvasObj = new GameObject("DevCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        _panel = CreateUIObject("DevPanel", canvas.transform);
        _panel.SetActive(false);
        RectTransform panelRect = _panel.GetComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(_panelWidth, _panelHeight);
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);

        Image panelImage = _panel.AddComponent<Image>();
        panelImage.color = _panelColor;

        VerticalLayoutGroup panelLayout = _panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(
            (int)_padding, (int)_padding, (int)_padding, (int)_padding);
        panelLayout.spacing = _spacing;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        BuildHeader(_panel.transform);
        BuildTabBar(_panel.transform);

        // Content area — holds the three pages
        GameObject contentArea = CreateUIObject("ContentArea", _panel.transform);
        AddLayoutElement(contentArea, flexibleWidth: 1f, flexibleHeight: 1f);

        _itemsPage = CreateUIObject("ItemsPage", contentArea.transform);
        Stretch(_itemsPage);
        _itemsContent = BuildScrollArea(_itemsPage);

        _puzzlesPage = CreateUIObject("PuzzlesPage", contentArea.transform);
        Stretch(_puzzlesPage);
        _puzzlesPage.SetActive(false);
        Transform puzzlesContent = BuildScrollArea(_puzzlesPage);
        PopulatePuzzles(puzzlesContent);

        _gamePage = CreateUIObject("GamePage", contentArea.transform);
        Stretch(_gamePage);
        _gamePage.SetActive(false);
        AddVerticalLayout(_gamePage);
        PopulateGameActions(_gamePage.transform);

        BuildStatusBar(_panel.transform);
    }

    private void BuildHeader(Transform parent)
    {
        GameObject header = CreateUIObject("Header", parent);
        AddLayoutElement(header, minHeight: _headerHeight, flexibleWidth: 1f);
        AddHorizontalLayout(header);
        header.AddComponent<Image>().color = _headerColor;

        GameObject title = CreateUIObject("Title", header.transform);
        AddLayoutElement(title, flexibleWidth: 1f, minHeight: _headerHeight);
        TMP_Text titleText = CreateText(title, "DEV PANEL", _headerFontSize, Color.white);
        titleText.alignment = TextAlignmentOptions.MidlineLeft;
        titleText.GetComponent<RectTransform>().offsetMin = new Vector2(8, 0);

        GameObject closeBtn = CreateUIObject("CloseBtn", header.transform);
        AddLayoutElement(closeBtn, minWidth: 36, minHeight: _headerHeight);
        CreateButton(closeBtn, "X", _closeBtnColor, Close);
    }

    private void BuildTabBar(Transform parent)
    {
        GameObject tabbar = CreateUIObject("TabBar", parent);
        AddLayoutElement(tabbar, minHeight: _tabHeight, flexibleWidth: 1f);
        AddHorizontalLayout(tabbar, spacing: 2, padding: 0);

        _itemsTab = CreateTabButton(tabbar.transform, "Items",
            () => ShowPage(_itemsPage, _itemsTab));
        _puzzlesTab = CreateTabButton(tabbar.transform, "Puzzles",
            () => ShowPage(_puzzlesPage, _puzzlesTab));
        _gameTab = CreateTabButton(tabbar.transform, "Game",
            () => ShowPage(_gamePage, _gameTab));
    }

    private Button CreateTabButton(Transform parent, string label, Action onClick)
    {
        GameObject btn = CreateUIObject(label + "Tab", parent);
        AddLayoutElement(btn, flexibleWidth: 1f, minHeight: _tabHeight);
        return CreateButton(btn, label, _tabColor, onClick);
    }

    private void UpdateTabColors()
    {
        SetTabColor(_itemsTab, _itemsPage.activeSelf);
        SetTabColor(_puzzlesTab, _puzzlesPage.activeSelf);
        SetTabColor(_gameTab, _gamePage.activeSelf);
    }

    private void SetTabColor(Button btn, bool selected)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img != null)
            img.color = selected ? _tabSelectedColor : _tabColor;
    }

    private void ShowPage(GameObject page, Button tab)
    {
        _itemsPage.SetActive(false);
        _puzzlesPage.SetActive(false);
        _gamePage.SetActive(false);
        page.SetActive(true);
        UpdateTabColors();
    }

    private void BuildStatusBar(Transform parent)
    {
        GameObject status = CreateUIObject("StatusBar", parent);
        AddLayoutElement(status, minHeight: _statusHeight, flexibleWidth: 1f);
        status.AddComponent<Image>().color = _statusColor;

        _statusText = CreateText(status, "", _statusFontSize, new Color(0.7f, 0.7f, 0.7f, 1f));
        _statusText.alignment = TextAlignmentOptions.MidlineLeft;
        RectTransform rt = _statusText.GetComponent<RectTransform>();
        rt.offsetMin = new Vector2(8, 0);
        rt.offsetMax = new Vector2(-8, 0);
    }

    /// <summary>Creates a ScrollRect with viewport, mask, and a vertical-layout content root.</summary>
    private Transform BuildScrollArea(GameObject parent)
    {
        parent.AddComponent<Image>().color = _scrollBgColor;

        ScrollRect scrollRect = parent.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 20f;

        GameObject viewport = CreateUIObject("Viewport", parent.transform);
        Stretch(viewport);
        viewport.AddComponent<RectMask2D>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();

        GameObject content = CreateUIObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;

        AddVerticalLayout(content, spacing: 2, padding: 4);
        AddContentSizeFitter(content, vertical: ContentSizeFitter.FitMode.PreferredSize);

        scrollRect.content = contentRect;
        return content.transform;
    }

    // ── Page population ───────────────────────────────────────────────────

    private void PopulateItems()
    {
        for (int i = _itemsContent.childCount - 1; i >= 0; i--)
            Destroy(_itemsContent.GetChild(i).gameObject);

        InventorySystem inventory = InventorySystem.Instance;
        if (inventory == null)
        {
            Log("InventorySystem not found.");
            return;
        }

        ItemData[] items = inventory.AllItems;
        if (items == null || items.Length == 0)
        {
            Log("No items registered — run Tools > Inventory > Refresh.");
            return;
        }

        int shown = 0;
        foreach (ItemData item in items)
        {
            if (item == null || !item.showInDevPanel) continue;
            CreateItemRow(_itemsContent, item);
            shown++;
        }

        Log($"Loaded {shown} of {items.Length} items.");
    }

    private void CreateItemRow(Transform parent, ItemData item)
    {
        GameObject row = CreateUIObject("Item_" + item.ItemId, parent);
        AddLayoutElement(row, minHeight: _itemRowHeight, flexibleWidth: 1f);
        AddHorizontalLayout(row, spacing: 0, padding: 0);
        row.AddComponent<Image>().color = _itemRowColor;

        // Left padding before icon
        CreateSpacer(row.transform, width: _iconLeftPadding);

        if (item.icon != null)
        {
            GameObject iconObj = CreateUIObject("Icon", row.transform);
            AddLayoutElement(iconObj, minWidth: _iconSize, minHeight: _iconSize,
                preferredWidth: _iconSize, preferredHeight: _iconSize, flexibleWidth: 0f);
            Image iconImage = iconObj.AddComponent<Image>();
            iconImage.sprite = item.icon;
            iconImage.preserveAspect = true;
            RectTransform iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.sizeDelta = new Vector2(_iconSize, _iconSize);
        }

        // Spacing between icon and text
        CreateSpacer(row.transform, width: _iconTextSpacing);

        GameObject nameObj = CreateUIObject("Name", row.transform);
        AddLayoutElement(nameObj, flexibleWidth: 1f, minHeight: _itemRowHeight);
        TMP_Text nameText = CreateText(nameObj, item.itemName ?? item.name, _fontSize, Color.white);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject giveBtn = CreateUIObject("Give", row.transform);
        AddLayoutElement(giveBtn, minWidth: 100, preferredHeight: _buttonHeight,
            preferredWidth: 200, flexibleWidth: 0f, flexibleHeight: 0f);
        CreateButton(giveBtn, "Give", _giveBtnColor, () => GiveItem(item));

        // Right padding after Give button
        CreateSpacer(row.transform, width: _iconLeftPadding);
    }

    /// <summary>Creates an empty transparent spacer with a fixed width.</summary>
    private void CreateSpacer(Transform parent, float width)
    {
        GameObject spacer = CreateUIObject("Spacer", parent);
        AddLayoutElement(spacer, minWidth: width, preferredWidth: width, flexibleWidth: 0f);
    }

    private void PopulatePuzzles(Transform content)
    {
        var puzzles = new (string label, Func<DevPuzzleCheats.CheatResult> solver)[]
        {
            ("Solve Board Puzzle",        DevPuzzleCheats.SolveBoardPuzzle),
            ("Solve Metamorf Puzzle",     DevPuzzleCheats.SolveMetamorfPuzzle),
            ("Solve Electric Puzzle",     DevPuzzleCheats.SolveElectricPuzzle),
            ("Solve Generator Puzzle",    DevPuzzleCheats.SolveGeneratorPuzzle),
            ("Solve Fifteen Puzzle",      DevPuzzleCheats.SolveFifteenPuzzle),
            ("Solve Paint (Loop) Puzzle", DevPuzzleCheats.SolvePaintPuzzle),
            ("Unlock Procedural Safes",   DevPuzzleCheats.SolveProceduralSafes),
            ("Unlock Da Vinci (Room 4)",   DevPuzzleCheats.SolveDaVinciPuzzle),
            ("Unlock Padlock (Room 2)",    DevPuzzleCheats.SolvePadlockPuzzle),
            ("Unlock Doctor Room Safes",  DevPuzzleCheats.SolveDoctorSafes),
        };

        foreach (var (label, solver) in puzzles)
        {
            GameObject btn = CreateUIObject("Btn_" + label, content);
            AddLayoutElement(btn, minHeight: _buttonHeight, flexibleWidth: 1f);
            CreateButton(btn, label, _puzzleBtnColor, () => ExecuteCheat(solver));
        }
    }

    private void PopulateGameActions(Transform parent)
    {
        CreateGameButton(parent, "Reset Position", ResetPlayerPosition);

        CreateGameButton(parent, "Clear Inventory", () =>
        {
            InventorySystem.Instance?.ClearAll();
            Log("Inventory cleared.");
        });

        CreateGameButton(parent, "Save Game", () =>
        {
            SaveManager.Instance?.Save();
            Log("Game saved.");
        });

        CreateGameButton(parent, "Activate Building Power", ActivatePower);
    }

    private void CreateGameButton(Transform parent, string label, Action onClick)
    {
        GameObject btn = CreateUIObject("Btn_" + label, parent);
        AddLayoutElement(btn, minHeight: _buttonHeight, flexibleWidth: 1f);
        CreateButton(btn, label, _gameBtnColor, onClick);
    }

    // ── Cheat actions ─────────────────────────────────────────────────────

    private void GiveItem(ItemData item)
    {
        if (item == null || InventorySystem.Instance == null)
        {
            Log("Cannot give item — inventory or item is null.");
            return;
        }

        bool added = InventorySystem.Instance.AddItem(item);
        Log(added
            ? $"Added '{item.itemName}' to inventory."
            : "Inventory is full — item not added.");
    }

    private void ExecuteCheat(Func<DevPuzzleCheats.CheatResult> cheat)
    {
        DevPuzzleCheats.CheatResult result = cheat();
        Log(result.Message);

        if (result.DeferredAction != null)
        {
            _pendingDeferredAction = result.DeferredAction;
            Log("Action deferred — executes when game is unpaused.");
        }
    }

    private void ActivatePower()
    {
        Type lightingType = FindType("LightingSystem");
        if (lightingType == null)
        {
            Log("LightingSystem not found.");
            return;
        }

        var instanceProp = lightingType.GetProperty("Instance");
        object instance = instanceProp?.GetValue(null);
        lightingType.GetMethod("ActivatePower")?.Invoke(instance, null);
        Log("Building power activated.");
    }

    // ── UI Helpers ────────────────────────────────────────────────────────

    private void Log(string message)
    {
        Debug.Log("[DevPanel] " + message);
        if (_statusText != null)
            _statusText.text = message;
    }

    private GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private TMP_Text CreateText(GameObject go, string text, int fontSize, Color color)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        if (_font != null) tmp.font = _font;
        Stretch(go);
        return tmp;
    }

    private Button CreateButton(GameObject go, string label, Color bgColor, Action onClick)
    {
        Image img = go.AddComponent<Image>();
        img.color = bgColor;
        Button btn = go.AddComponent<Button>();

        GameObject labelObj = CreateUIObject("Label", go.transform);
        Stretch(labelObj);
        CreateText(labelObj, label, _fontSize, Color.white);

        btn.onClick.AddListener(() => onClick?.Invoke());
        return btn;
    }

    private void Stretch(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private VerticalLayoutGroup AddVerticalLayout(GameObject go,
        float spacing = 4, float padding = 4,
        TextAnchor childAlignment = TextAnchor.UpperCenter)
    {
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(
            (int)padding, (int)padding, (int)padding, (int)padding);
        layout.childAlignment = childAlignment;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private HorizontalLayoutGroup AddHorizontalLayout(GameObject go,
        float spacing = 4, float padding = 4,
        TextAnchor childAlignment = TextAnchor.MiddleLeft)
    {
        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(
            (int)padding, (int)padding, (int)padding, (int)padding);
        layout.childAlignment = childAlignment;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        return layout;
    }

    private void AddContentSizeFitter(GameObject go,
        ContentSizeFitter.FitMode horizontal = ContentSizeFitter.FitMode.Unconstrained,
        ContentSizeFitter.FitMode vertical = ContentSizeFitter.FitMode.PreferredSize)
    {
        var fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = horizontal;
        fitter.verticalFit = vertical;
    }

    private void AddLayoutElement(GameObject go,
        float? minHeight = null, float? minWidth = null,
        float? flexibleWidth = null, float? flexibleHeight = null,
        float? preferredWidth = null, float? preferredHeight = null)
    {
        var element = go.AddComponent<LayoutElement>();
        if (minHeight.HasValue) element.minHeight = minHeight.Value;
        if (minWidth.HasValue) element.minWidth = minWidth.Value;
        if (flexibleWidth.HasValue) element.flexibleWidth = flexibleWidth.Value;
        if (flexibleHeight.HasValue) element.flexibleHeight = flexibleHeight.Value;
        if (preferredWidth.HasValue) element.preferredWidth = preferredWidth.Value;
        if (preferredHeight.HasValue) element.preferredHeight = preferredHeight.Value;
    }

    private Type FindType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName);
            if (type != null) return type;
        }
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var t in assembly.GetTypes())
            {
                if (t.Name == typeName) return t;
            }
        }
        return null;
    }
}
