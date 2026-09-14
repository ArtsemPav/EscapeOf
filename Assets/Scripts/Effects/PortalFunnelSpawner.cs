using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns transient spiral wave instances around the persistent funnel core.
/// Each wave is a child GameObject with its own helical ribbon mesh: it fades in,
/// drifts toward the narrow end while the pattern scrolls, then fades out and
/// respawns with a randomized shape and shader phase. This creates the feeling
/// of fresh streams of energy constantly being pulled into the portal.
/// </summary>
public class PortalFunnelSpawner : MonoBehaviour
{
    private const int MinTurns = 2;
    private const int MaxTurns = 7;
    private const string TimeOffsetProp = "_TimeOffset";
    private const string OpacityProp = "_Opacity";
    private const string ScrollSpeedProp = "_ScrollSpeed";

    [Header("Core")]
    [Tooltip("Number of persistent core layers that never fade (set on the local generator).")]
    [SerializeField] private int _coreLayerCount = 4;

    [Header("Waves")]
    [SerializeField] private int _waveCount = 3;
    [SerializeField] private float _waveLifetime = 6f;
    [Tooltip("Delay between wave spawns (sec). Also staggers the initial spawn.")]
    [SerializeField] private float _spawnInterval = 2f;
    [SerializeField, Range(0.05f, 0.5f)] private float _fadeInFraction = 0.3f;
    [SerializeField, Range(0.05f, 0.5f)] private float _fadeOutFraction = 0.4f;
    [SerializeField] private int _layersPerWave = 3;

    [Header("Wave Motion")]
    [Tooltip("How far the wave drifts toward the narrow end during its lifetime (meters).")]
    [SerializeField] private float _driftDistance = 2.5f;
    [Tooltip("Extra pattern scroll speed for waves (u units/sec).")]
    [SerializeField] private float _waveScrollBoost = 0.4f;

    [Header("Wave Shape Ranges")]
    [SerializeField] private Vector2 _turnsRange = new Vector2(3f, 5f);
    [SerializeField] private Vector2 _startRadiusRange = new Vector2(2.2f, 2.9f);
    [SerializeField] private Vector2 _widthRange = new Vector2(0.25f, 0.45f);

    private readonly List<WaveInstance> _waves = new List<WaveInstance>();

    private class WaveInstance
    {
        public GameObject Root;
        public Renderer Renderer;
        public MaterialPropertyBlock Mpb;
        public float TimeOffset;
        public float Timer;
    }

    private void Start()
    {
        // Shrink the persistent core so the fading waves stand out
        var generator = GetComponent<PortalFunnelMeshGenerator>();
        if (generator != null && _coreLayerCount > 0)
        {
            generator.SetLayers(BuildCoreLayers(_coreLayerCount));
        }

        for (int i = 0; i < _waveCount; i++)
        {
            var wave = CreateWave(i);
            wave.Timer = -i * _spawnInterval;
            _waves.Add(wave);
        }
    }

    private void Update()
    {
        foreach (var wave in _waves)
        {
            wave.Timer += Time.deltaTime;

            // Respawn with a fresh shape once the lifetime is over
            if (wave.Timer >= _waveLifetime)
            {
                wave.Timer = 0f;
                RandomizeWave(wave.Root);
            }

            if (wave.Timer < 0f)
            {
                SetWaveOpacity(wave, 0f);
                continue;
            }

            float t = wave.Timer / _waveLifetime;

            // Fade envelope: smooth in, hold, smooth out
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, _fadeInFraction, t))
                        * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(1f - _fadeOutFraction, 1f, t));

            // Drift toward the narrow end along the local funnel axis
            var localPos = wave.Root.transform.localPosition;
            localPos.y = Mathf.Clamp01(t) * _driftDistance;
            wave.Root.transform.localPosition = localPos;

            SetWaveOpacity(wave, alpha);
        }
    }

    private WaveInstance CreateWave(int index)
    {
        var root = new GameObject("PortalWave_" + index);
        root.transform.SetParent(transform, false);

        root.AddComponent<MeshFilter>();
        var renderer = root.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Share the funnel material — per-wave look is driven via MaterialPropertyBlock
        renderer.sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;

        var generator = root.AddComponent<PortalFunnelMeshGenerator>();
        generator.SetLayers(BuildWaveLayers());

        return new WaveInstance
        {
            Root = root,
            Renderer = renderer,
            Mpb = new MaterialPropertyBlock(),
            TimeOffset = (index + 1) * 37.317f
        };
    }

    private void RandomizeWave(GameObject waveRoot)
    {
        var generator = waveRoot.GetComponent<PortalFunnelMeshGenerator>();
        if (generator != null)
            generator.SetLayers(BuildWaveLayers());

        // Reset the drift position
        var localPos = waveRoot.transform.localPosition;
        localPos.y = 0f;
        waveRoot.transform.localPosition = localPos;
    }

    private void SetWaveOpacity(WaveInstance wave, float alpha)
    {
        wave.Renderer.GetPropertyBlock(wave.Mpb);
        wave.Mpb.SetFloat(OpacityProp, alpha);
        wave.Mpb.SetFloat(TimeOffsetProp, wave.TimeOffset);
        wave.Mpb.SetFloat(ScrollSpeedProp, _waveScrollBoost);
        wave.Renderer.SetPropertyBlock(wave.Mpb);
    }

    private PortalFunnelMeshGenerator.RibbonLayer[] BuildCoreLayers(int count)
    {
        var layers = new PortalFunnelMeshGenerator.RibbonLayer[count];
        for (int i = 0; i < count; i++)
        {
            float fraction = count > 1 ? (float)i / (count - 1) : 0f;
            layers[i] = new PortalFunnelMeshGenerator.RibbonLayer
            {
                turns = 4,
                startRadius = Mathf.Lerp(2.8f, 1.6f, fraction),
                endRadius = Mathf.Lerp(0.2f, 0.05f, fraction),
                width = Mathf.Lerp(0.4f, 0.2f, fraction),
                angleOffsetDegrees = i * (360f / count),
                heightFactor = 1f,
                startVFade = 0f
            };
        }
        return layers;
    }

    private PortalFunnelMeshGenerator.RibbonLayer[] BuildWaveLayers()
    {
        var layers = new PortalFunnelMeshGenerator.RibbonLayer[_layersPerWave];
        for (int i = 0; i < _layersPerWave; i++)
        {
            layers[i] = new PortalFunnelMeshGenerator.RibbonLayer
            {
                turns = Mathf.RoundToInt(Random.Range(_turnsRange.x, _turnsRange.y)),
                startRadius = Random.Range(_startRadiusRange.x, _startRadiusRange.y),
                endRadius = Random.Range(0.05f, 0.25f),
                width = Random.Range(_widthRange.x, _widthRange.y),
                angleOffsetDegrees = Random.Range(0f, 360f),
                heightFactor = Random.Range(0.9f, 1f),
                startVFade = 0f
            };
        }
        return layers;
    }
}
