using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Video setting options exposed to the player.
/// Resolutions are built from the modes the current display actually supports,
/// capped at two largest options per aspect ratio so the list stays short, and
/// including non-native aspect ratios: borderless fullscreen (FullScreenWindow)
/// letterboxes mismatched modes with black bars instead of stretching them.
/// </summary>
public static class VideoSettingOptions
{
    /// <summary>Relative tolerance when grouping modes into one aspect ratio.</summary>
    private const float AspectTolerance = 0.01f;

    /// <summary>How many resolutions to keep per display aspect ratio.</summary>
    private const int OptionsPerAspect = 2;

    /// <summary>
    /// Supported resolutions of the current display: the two largest modes of
    /// every aspect ratio the display reports, sorted largest first.
    /// </summary>
    public static IReadOnlyList<ResolutionOption> Resolutions => BuildResolutions();

    /// <summary>Selectable frame-rate caps.</summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 60, 30 };

    /// <summary>Frame rate used when the player has not chosen one yet.</summary>
    public const int DefaultFrameRate = 60;

    /// <summary>
    /// Resolution used when the player has not chosen one yet: the display's
    /// current (native) resolution, or the largest supported one as a fallback.
    /// </summary>
    public static ResolutionOption DefaultResolution
    {
        get
        {
            IReadOnlyList<ResolutionOption> options = Resolutions;
            Resolution current = Screen.currentResolution;
            int currentIndex = IndexOfResolution(new ResolutionOption(current.width, current.height));
            if (currentIndex >= 0) return options[currentIndex];

            return options.OrderByDescending(o => (long)o.Width * o.Height).First();
        }
    }

    /// <summary>A selectable resolution entry shown in the settings UI.</summary>
    [Serializable]
    public readonly struct ResolutionOption : IEquatable<ResolutionOption>
    {
        public readonly int Width;
        public readonly int Height;

        public ResolutionOption(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool Equals(ResolutionOption other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is ResolutionOption other && Equals(other);
        public override int GetHashCode() => (Width << 16) ^ Height;
        public override string ToString() => $"{Width} x {Height}";
    }

    /// <summary>Index of the given resolution in the current list, or -1 when absent.</summary>
    public static int IndexOfResolution(ResolutionOption option)
    {
        IReadOnlyList<ResolutionOption> options = Resolutions;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].Equals(option)) return i;
        }
        return -1;
    }

    /// <summary>All selectable resolutions formatted for UI labels.</summary>
    public static string[] ResolutionLabels()
        => Resolutions.Select(r => r.ToString()).ToArray();

    /// <summary>
    /// Builds the selectable resolutions from the display's supported modes:
    /// deduplicated by width x height, grouped by aspect ratio, and capped at
    /// the <see cref="OptionsPerAspect"/> largest modes of each group, sorted
    /// largest first. Includes every aspect ratio the display reports:
    /// mismatched ones are letterboxed by borderless fullscreen, not stretched.
    /// </summary>
    private static IReadOnlyList<ResolutionOption> BuildResolutions()
        => Screen.resolutions
            .Select(r => new ResolutionOption(r.width, r.height))
            .Distinct()
            .GroupBy(option => option.Height == 0 ? 0f : (float)option.Width / option.Height, new AspectComparer())
            .SelectMany(group => group
                .OrderByDescending(o => (long)o.Width * o.Height)
                .Take(OptionsPerAspect))
            .OrderByDescending(o => (long)o.Width * o.Height)
            .ToList();

    /// <summary>Groups aspect ratios with a relative tolerance so near-identical ratios collapse into one group.</summary>
    private sealed class AspectComparer : IEqualityComparer<float>
    {
        public bool Equals(float x, float y)
            => Mathf.Abs(x - y) <= AspectTolerance * Mathf.Max(x, y);

        public int GetHashCode(float aspect) => 0; // force pairwise comparison via Equals
    }
}
