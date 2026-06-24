using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Universal screen resolution / fullscreen manager.
///
/// Works out of the box WITHOUT a settings menu:
///  - On launch it restores the player's previously chosen resolution &amp; fullscreen mode
///    (saved in PlayerPrefs). If nothing was saved yet, the platform default is kept.
///
/// Public API for a FUTURE settings menu (just call these from your UI):
///  - GetSupportedResolutions()  : every resolution the monitor supports (de-duplicated, sorted)
///  - Get16by9Resolutions()      : only the 16:9 entries, handy for a curated dropdown
///  - SetResolution(w, h[, mode]): apply &amp; persist a resolution
///  - SetFullScreenMode(mode)    : apply &amp; persist a fullscreen mode
///  - SetFullscreen(bool)        : quick fullscreen-windowed / windowed toggle
///  - CurrentWidth / CurrentHeight / CurrentMode
///
/// Note: the camera framing adapts automatically (see AspectRatioFov.cs), so after any resolution
/// change the view fills the screen correctly with no extra calls.
///
/// ============================ FINALIZATION NOTES (safe to change) ============================
///  - applyOnStart: disable if you want ONLY the settings menu to ever change the resolution.
///  - persist (PlayerPrefs): disable / remove if you store video settings in your own save system.
///  - In the Editor a runtime resolution change only affects the Game view; it is fully meaningful
///    only in a standalone build.
/// =============================================================================================
/// </summary>
[DefaultExecutionOrder(-50)] // apply saved video settings early, before gameplay scripts read them
public class ResolutionManager : MonoBehaviour
{
    public static ResolutionManager Instance { get; private set; }

    [Header("Startup")]
    [Tooltip("Restore the saved resolution / fullscreen mode on launch.")]
    [SerializeField] private bool applyOnStart = true;

    [Tooltip("Persist changes to PlayerPrefs so they survive between sessions.")]
    [SerializeField] private bool persist = true;

    private const string KeyWidth  = "video_width";
    private const string KeyHeight = "video_height";
    private const string KeyMode   = "video_mode";

    public int CurrentWidth  => Screen.width;
    public int CurrentHeight => Screen.height;
    public FullScreenMode CurrentMode => Screen.fullScreenMode;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Enforce a strict 60 FPS limit on startup to prevent GPU from overheating, regardless of monitor refresh rate
        QualitySettings.vSyncCount = 0; // Turn off VSync so Application.targetFrameRate is strictly respected at 60 FPS
        Application.targetFrameRate = 60;

        if (applyOnStart && persist && PlayerPrefs.HasKey(KeyWidth))
        {
            int w = PlayerPrefs.GetInt(KeyWidth);
            int h = PlayerPrefs.GetInt(KeyHeight);
            var mode = (FullScreenMode)PlayerPrefs.GetInt(KeyMode, (int)Screen.fullScreenMode);
            Screen.SetResolution(w, h, mode);
        }
    }

    /// <summary>Apply (and optionally persist) a resolution, keeping the current fullscreen mode.</summary>
    public void SetResolution(int width, int height) =>
        SetResolution(width, height, Screen.fullScreenMode);

    /// <summary>Apply (and optionally persist) a resolution and fullscreen mode.</summary>
    public void SetResolution(int width, int height, FullScreenMode mode)
    {
        Screen.SetResolution(width, height, mode);
        if (persist)
        {
            PlayerPrefs.SetInt(KeyWidth, width);
            PlayerPrefs.SetInt(KeyHeight, height);
            PlayerPrefs.SetInt(KeyMode, (int)mode);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Change just the fullscreen mode, keeping the current resolution.</summary>
    public void SetFullScreenMode(FullScreenMode mode) =>
        SetResolution(Screen.width, Screen.height, mode);

    /// <summary>Convenience toggle: borderless fullscreen vs windowed.</summary>
    public void SetFullscreen(bool fullscreen) =>
        SetFullScreenMode(fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

    /// <summary>
    /// All distinct resolutions the current monitor supports (one entry per width x height,
    /// keeping the highest refresh rate), sorted ascending by width then height.
    /// </summary>
    public List<Resolution> GetSupportedResolutions()
    {
        var best = new Dictionary<long, Resolution>();
        foreach (Resolution r in Screen.resolutions)
        {
            long key = ((long)r.width << 20) | (long)r.height;
            if (!best.TryGetValue(key, out Resolution existing) ||
                r.refreshRateRatio.value > existing.refreshRateRatio.value)
            {
                best[key] = r;
            }
        }

        var result = new List<Resolution>(best.Values);
        result.Sort((a, b) => a.width != b.width ? a.width - b.width : a.height - b.height);
        return result;
    }

    /// <summary>Only the 16:9 resolutions from the supported list (small tolerance for rounding).</summary>
    public List<Resolution> Get16by9Resolutions()
    {
        const float target = 16f / 9f;
        var result = new List<Resolution>();
        foreach (Resolution r in GetSupportedResolutions())
        {
            if (r.height > 0 && Mathf.Abs((float)r.width / r.height - target) < 0.01f)
                result.Add(r);
        }
        return result;
    }
}
