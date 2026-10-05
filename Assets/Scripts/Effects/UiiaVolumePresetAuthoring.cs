using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Fills the four Uiia_PresetN VolumeProfiles with four radically different looks:
///   Preset 1 — Invert (color-inverted "negative" world)
///   Preset 2 — Fisheye (extreme lens distortion + chromatic aberration)
///   Preset 3 — Hell (deep red, crushed blacks, heavy bloom)
///   Preset 4 — Dream (blurred, warped, oversaturated pastel nightmare)
///
/// Auto-fills on Awake (play mode) if the profiles are not filled yet — no
/// manual steps needed. In the editor you can also bake the profiles to disk
/// via the context menu ("Bake Presets To Disk").
/// </summary>
[DefaultExecutionOrder(-200)]
public class UiiaVolumePresetAuthoring : MonoBehaviour
{
    [SerializeField] private VolumeProfile _preset1;
    [SerializeField] private VolumeProfile _preset2;
    [SerializeField] private VolumeProfile _preset3;
    [SerializeField] private VolumeProfile _preset4;

    private void Awake() => TryAutoFill();

#if UNITY_EDITOR
    [ContextMenu("Bake Presets To Disk")]
    private void BakeToDisk()
    {
        TryAutoFill();
        foreach (var profile in new[] { _preset1, _preset2, _preset3, _preset4 })
        {
            if (profile == null) continue;
            UnityEditor.EditorUtility.SetDirty(profile);
        }
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log("[UiiaVolumePresetAuthoring] Presets baked to disk.", this);
    }
#endif

    private void TryAutoFill()
    {
        if (_preset1 == null || _preset2 == null || _preset3 == null || _preset4 == null)
        {
            Debug.LogWarning("[UiiaVolumePresetAuthoring] Some preset profiles are not assigned.", this);
            return;
        }

        // Always refill with fresh values — earlier runs may have left stale
        // in-memory overrides on the profile assets.
        FillInvert(_preset1);
        FillFisheye(_preset2);
        FillHell(_preset3);
        FillDream(_preset4);

        Debug.Log("[UiiaVolumePresetAuthoring] All 4 presets filled (in memory). " +
                  "Use 'Bake Presets To Disk' in the editor to persist them.", this);
    }

    private static void AddOrGet<T>(VolumeProfile profile) where T : VolumeComponent, new()
    {
        if (!profile.TryGet<T>(out var component))
        {
            component = ScriptableObject.CreateInstance<T>();
            profile.components.Add(component);
        }
        component.active = true;
    }

    // ── Preset 1: DARK NOIR — room goes almost black, cat glows ──────────────
    // A pure color inversion cannot work in a dark room (inverted dark = white).
    // Instead: crush the room to near-black; the cat itself is made emissive
    // by UVPentagramSequence, so its silhouette reads against the darkness.

    private static void FillInvert(VolumeProfile profile)
    {
        AddOrGet<ColorAdjustments>(profile);
        profile.TryGet<ColorAdjustments>(out var color);
        color.postExposure.Override(-2.5f);                       // crush the room
        color.contrast.Override(40f);
        // Neutral-dark filter: darkens without tinting the cat blue
        color.colorFilter.Override(new Color(0.75f, 0.75f, 0.8f));
        color.saturation.Override(-50f);                          // drained, eerie

        AddOrGet<Vignette>(profile);
        profile.TryGet<Vignette>(out var vig);
        vig.color.Override(new Color(0f, 0f, 0f));
        vig.intensity.Override(0.95f);
        vig.smoothness.Override(0.3f);

        MakeGlow(profile, 0.3f, 2.5f, 1f);   // everything glows through the dark

        AddOrGet<FilmGrain>(profile);
        profile.TryGet<FilmGrain>(out var grain);
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(0.9f);
        grain.response.Override(0.9f);
    }

    // ── Preset 2: FISHEYE — warped lens, psychedelic ──────────────────────────

    private static void FillFisheye(VolumeProfile profile)
    {
        AddOrGet<LensDistortion>(profile);
        profile.TryGet<LensDistortion>(out var lens);
        lens.intensity.Override(0.85f);
        lens.xMultiplier.Override(1f);
        lens.yMultiplier.Override(1f);
        lens.center.Override(new Vector2(0.5f, 0.5f));
        lens.scale.Override(0.7f);

        AddOrGet<ChromaticAberration>(profile);
        profile.TryGet<ChromaticAberration>(out var chroma);
        chroma.intensity.Override(1f);

        AddOrGet<PaniniProjection>(profile);
        profile.TryGet<PaniniProjection>(out var panini);
        panini.distance.Override(1f);
        panini.cropToFit.Override(1f);

        AddOrGet<ColorAdjustments>(profile);
        profile.TryGet<ColorAdjustments>(out var color);
        color.postExposure.Override(0f);
        color.contrast.Override(20f);
        color.hueShift.Override(30f);
        color.saturation.Override(40f);
        color.colorFilter.Override(Color.white);

        MakeGlow(profile, 0.25f, 3f, 0.9f);  // psychedelic glow wash
    }

    // ── Preset 3: HELL — blood red, crushing dark, hot bloom ─────────────────

    private static void FillHell(VolumeProfile profile)
    {
        AddOrGet<ColorAdjustments>(profile);
        profile.TryGet<ColorAdjustments>(out var color);
        color.postExposure.Override(-0.5f);
        color.contrast.Override(80f);
        color.saturation.Override(-30f);
        color.hueShift.Override(0f);
        color.colorFilter.Override(new Color(1f, 0.15f, 0.05f));

        AddOrGet<WhiteBalance>(profile);
        profile.TryGet<WhiteBalance>(out var wb);
        wb.temperature.Override(80f);
        wb.tint.Override(40f);

        MakeGlow(profile, 0.4f, 4f, 1f, new Color(1f, 0.2f, 0.1f)); // hellfire glow

        AddOrGet<Vignette>(profile);
        profile.TryGet<Vignette>(out var vig);
        vig.color.Override(new Color(0.25f, 0f, 0f));
        vig.intensity.Override(0.9f);
        vig.smoothness.Override(0.4f);

        AddOrGet<Tonemapping>(profile);
        profile.TryGet<Tonemapping>(out var tone);
        tone.mode.Override(TonemappingMode.ACES);

        AddOrGet<FilmGrain>(profile);
        profile.TryGet<FilmGrain>(out var grain);
        grain.type.Override(FilmGrainLookup.Medium2);
        grain.intensity.Override(1f);
        grain.response.Override(1f);
    }

    // ── Preset 4: DREAM — warped pastel nightmare ─────────────────────────────

    private static void FillDream(VolumeProfile profile)
    {
        AddOrGet<ColorAdjustments>(profile);
        profile.TryGet<ColorAdjustments>(out var color);
        color.postExposure.Override(0.4f);
        color.contrast.Override(-30f);
        color.saturation.Override(70f);
        color.hueShift.Override(-40f);
        color.colorFilter.Override(new Color(0.85f, 0.8f, 1f));

        AddOrGet<LensDistortion>(profile);
        profile.TryGet<LensDistortion>(out var lens);
        lens.intensity.Override(-0.4f);
        lens.xMultiplier.Override(0.3f);
        lens.yMultiplier.Override(1f);
        lens.center.Override(new Vector2(0.5f, 0.5f));
        lens.scale.Override(1.1f);

        MakeGlow(profile, 0.3f, 3f, 1f);  // dreamy pastel glow

        AddOrGet<ChromaticAberration>(profile);
        profile.TryGet<ChromaticAberration>(out var chroma);
        chroma.intensity.Override(0.7f);

        AddOrGet<WhiteBalance>(profile);
        profile.TryGet<WhiteBalance>(out var wb);
        wb.temperature.Override(-60f);
        wb.tint.Override(-35f);

        AddOrGet<FilmGrain>(profile);
        profile.TryGet<FilmGrain>(out var grain);
        grain.type.Override(FilmGrainLookup.Medium6);
        grain.intensity.Override(0.6f);
        grain.response.Override(0.5f);
    }

    // ── Shared bloom configuration — "everything glows" ──────────────────────

    /// <summary>
    /// Low threshold + high intensity + wide scatter = the whole scene softly glows.
    /// </summary>
    private static void MakeGlow(VolumeProfile profile, float threshold, float intensity, float scatter)
    {
        MakeGlow(profile, threshold, intensity, scatter, Color.white);
    }

    private static void MakeGlow(VolumeProfile profile, float threshold, float intensity, float scatter, Color tint)
    {
        AddOrGet<Bloom>(profile);
        profile.TryGet<Bloom>(out var bloom);
        bloom.threshold.Override(threshold);
        bloom.intensity.Override(intensity);
        bloom.scatter.Override(scatter);
        bloom.tint.Override(tint);
    }
}
