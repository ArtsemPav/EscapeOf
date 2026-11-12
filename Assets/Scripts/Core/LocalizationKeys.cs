/// <summary>
/// Central registry of localization keys used from code. Keys map to entries in
/// the per-language text tables (Assets/Data/Localization/Resources/Localization).
/// The full registry with usage notes lives in Assets/Data/Localization/Keys.md.
/// </summary>
public static class LocalizationKeys
{
    // ── UI: settings ────────────────────────────────────────────────────────
    public const string SettingsWindow = "ui.settings.window";
    public const string SettingsWindowBorderless = "ui.settings.window.borderless";
    public const string SettingsWindowWindowed = "ui.settings.window.windowed";
    public const string SettingsResolution = "ui.settings.resolution";
    public const string SettingsFramerate = "ui.settings.framerate";
    public const string SettingsDefaultSuffix = "ui.settings.default_suffix";
    public const string SettingsMusicVolume = "ui.settings.music_volume";
    public const string SettingsSfxVolume = "ui.settings.sfx_volume";
    public const string SettingsLanguage = "ui.settings.language";

    /// <summary>Prefix for displayed language names: ui.settings.language_name.&lt;enum name&gt;.</summary>
    public const string LanguageNamePrefix = "ui.settings.language_name.";

    // ── UI: interaction hints ───────────────────────────────────────────────
    public const string InteractApply = "ui.interact.apply";
    public const string InteractRotate = "ui.interact.rotate";
    public const string InteractPull = "ui.interact.pull";
    public const string InteractRead = "ui.interact.read";
    public const string InteractInspect = "ui.interact.inspect";
    public const string InteractInspectSkull = "ui.interact.inspect_skull";
    public const string InteractOpenDoor = "ui.interact.open_door";
    public const string InteractCloseDoor = "ui.interact.close_door";
    public const string InteractPress = "ui.interact.press";
    public const string InteractPressButton = "ui.interact.press_button";
    public const string InteractLightOn = "ui.interact.light_on";
    public const string InteractLightOff = "ui.interact.light_off";
    public const string InteractLeverPull = "ui.interact.lever_pull";
    public const string InteractLeverReturn = "ui.interact.lever_return";
    public const string InteractLeverSwitchOn = "ui.interact.lever_switch_on";
    public const string InteractLeverSwitchOff = "ui.interact.lever_switch_off";
    public const string InteractDrag = "ui.interact.drag";
    public const string InteractLocked = "ui.interact.locked";
    public const string InteractNoPower = "ui.interact.no_power";
    public const string InteractNeedLights = "ui.interact.need_lights";
    public const string InteractDrainWater = "ui.interact.drain_water";
    public const string InteractEnterCode = "ui.interact.enter_code";
    public const string InteractMissingItem = "ui.interact.missing_item";
    public const string InteractAnswer = "ui.interact.answer";
    public const string InteractLensChange = "ui.interact.lens_change";
    public const string InteractBlocked = "ui.interact.blocked";
    public const string InteractLightSwitchOn = "ui.interact.light_switch_on";
    public const string InteractHorrorDefault = "ui.interact.horror_default";
    public const string InteractPressDuck = "ui.interact.press_duck";
    public const string InteractBreakerInspect = "ui.interact.breaker_inspect";
    public const string InteractSwitch = "ui.interact.switch";
    public const string InteractSwitchCamera = "ui.interact.switch_camera";
    public const string InteractDoorLocked = "ui.interact.door_locked";
    public const string InteractInspectChinesBox = "ui.interact.inspect_chines_box";
    public const string InteractInspectClock = "ui.interact.inspect_clock";
    public const string InteractInspectBreaker = "ui.interact.inspect_breaker";
    public const string InteractInspectLock = "ui.interact.inspect_lock";
    public const string InteractInspectPanel = "ui.interact.inspect_panel";
    public const string InteractStartAnalyzer = "ui.interact.start_analyzer";
    public const string InteractStartCentrifuge = "ui.interact.start_centrifuge";
    public const string InteractAnswerPhone = "ui.interact.answer_phone";
    public const string InteractSpin = "ui.interact.spin";
    public const string InteractFold = "ui.interact.fold";
    public const string InteractLiftPainting = "ui.interact.lift_painting";
    public const string ElevatorCall = "ui.interact.elevator.call";
    public const string ElevatorFloor1 = "ui.interact.elevator.floor_1";
    public const string ElevatorFloor2 = "ui.interact.elevator.floor_2";
    public const string ElevatorMorgue = "ui.interact.elevator.morgue";
    public const string BreakerS1 = "ui.interact.breaker_s1";
    public const string BreakerS2 = "ui.interact.breaker_s2";
    public const string BreakerS3 = "ui.interact.breaker_s3";
    public const string BreakerS4 = "ui.interact.breaker_s4";
    public const string BreakerS5 = "ui.interact.breaker_s5";
    public const string BreakerMaster = "ui.interact.breaker_master";

    // ── UI: context hints (popups) ──────────────────────────────────────────
    public const string HintGenBoth = "ui.hint.gen_both";
    public const string HintGenFirst = "ui.hint.gen_first";
    public const string HintGenSecond = "ui.hint.gen_second";
    public const string HintProjector = "ui.hint.projector";
    public const string HintNeedPlunger = "ui.hint.need_plunger";
    public const string HintExitEsc = "ui.hint.exit_esc";

    // ── UI: laptop ──────────────────────────────────────────────────────────
    public const string LaptopAccessGranted = "ui.laptop.access_granted";
    public const string LaptopWrongPassword = "ui.laptop.wrong_password";
    /// <summary>Prefix for laptop file display names: laptop.file.&lt;id&gt;.</summary>
    public const string LaptopFilePrefix = "laptop.file.";

    // ── UI: drop hints ──────────────────────────────────────────────────────
    public const string DropSwipeCard = "ui.drop.swipe_card";
    public const string DropInsertGenerator = "ui.drop.insert_generator";
    public const string DropUseScrewdriver = "ui.drop.use_screwdriver";
    public const string DropInsertReel = "ui.drop.insert_reel";
    public const string DropOnSafe = "ui.drop.on_safe";

    // ── UI: panels ──────────────────────────────────────────────────────────
    public const string PanelBreakerOn = "ui.panel.breaker_on";
    public const string PanelBreakerOff = "ui.panel.breaker_off";
    public const string PanelBreakerNoPower = "ui.panel.breaker_no_power";

    // ── UI: tooltips ────────────────────────────────────────────────────────
    public const string TooltipRunePouch = "ui.tooltip.rune_pouch";
    public const string TooltipRuneMaster = "ui.tooltip.rune_master";

    // ── UI: code lock ───────────────────────────────────────────────────────
    public const string CodeLockSuccess = "ui.codelock.success";
    public const string CodeLockWrong = "ui.codelock.wrong";

    // ── UI: minigames / puzzles ─────────────────────────────────────────────
    public const string MinigameMisses = "ui.minigame.misses";
    public const string MinigameMissesLimited = "ui.minigame.misses_limited";
    public const string PuzzleSynthesisSuccess = "puzzle.synthesis.success";
    public const string PuzzleFifteenMove = "ui.puzzle.fifteen.move";
    public const string PuzzleGeneratorFirst = "ui.puzzle.generator_first";
    public const string PuzzleNoFuse = "ui.puzzle.no_fuse";

    // ── Journal category labels ─────────────────────────────────────────────
    /// <summary>Prefix for journal category labels: journal.category.&lt;categoryNameKey&gt;.</summary>
    public const string JournalCategoryPrefix = "journal.category.";
    public const string JournalCategoryLearningCat = "journal.category.LearningCat";
    public const string JournalCategoryMorrowCat = "journal.category.MorrowCat";
    public const string JournalCategoryVeinCat = "journal.category.VeinCat";
    public const string JournalCategorySuCat = "journal.category.SuCat";
    public const string JournalCategoryHibaCat = "journal.category.HibaCat";
    public const string JournalCategoryPirsCat = "journal.category.PirsCat";
    public const string JournalCategoryBuravCat = "journal.category.BuravCat";
    public const string JournalCategoryBarretCat = "journal.category.BarretCat";
    public const string JournalCategoryEilerCat = "journal.category.EilerCat";

    // ── Chemical synthesis puzzle ───────────────────────────────────────────
    public const string SynthesisCentrifugeTitle = "puzzle.synthesis.centrifuge_title";
    public const string SynthesisCentrifugeDesc = "puzzle.synthesis.centrifuge_desc";
    public const string SynthesisBurnerTitle = "puzzle.synthesis.burner_title";
    public const string SynthesisBurnerDesc = "puzzle.synthesis.burner_desc";
    public const string SynthesisMixerTitle = "puzzle.synthesis.mixer_title";
    public const string SynthesisMixerDesc = "puzzle.synthesis.mixer_desc";
    public const string SynthesisAnalyzerTitle = "puzzle.synthesis.analyzer_title";
    public const string SynthesisAnalyzerDesc = "puzzle.synthesis.analyzer_desc";
    public const string SynthesisTrashTitle = "puzzle.synthesis.trash_title";
    public const string SynthesisTrashDesc = "puzzle.synthesis.trash_desc";
}
