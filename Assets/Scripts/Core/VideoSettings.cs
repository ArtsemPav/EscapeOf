using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Curated video setting options exposed to the player.
/// Kept as plain data so a future settings UI (dropdowns, steppers) can enumerate them.
/// </summary>
public static class VideoSettingOptions
{
    /// <summary>Selectable screen resolutions (width x height).</summary>
    public static readonly IReadOnlyList<ResolutionOption> Resolutions = new[]
    {
        new ResolutionOption(1920, 1080),
        new ResolutionOption(1280, 720),
    };

    /// <summary>Selectable frame-rate caps.</summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 60, 30 };

    /// <summary>Frame rate used when the player has not chosen one yet.</summary>
    public const int DefaultFrameRate = 60;

    /// <summary>Resolution used when the player has not chosen one yet.</summary>
    public static readonly ResolutionOption DefaultResolution = Resolutions[0];

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

    /// <summary>Index of the given resolution in the curated list, or -1 when absent.</summary>
    public static int IndexOfResolution(ResolutionOption option)
    {
        for (int i = 0; i < Resolutions.Count; i++)
        {
            if (Resolutions[i].Equals(option)) return i;
        }
        return -1;
    }

    /// <summary>All selectable resolutions formatted for UI labels.</summary>
    public static string[] ResolutionLabels()
        => Resolutions.Select(r => r.ToString()).ToArray();
}
