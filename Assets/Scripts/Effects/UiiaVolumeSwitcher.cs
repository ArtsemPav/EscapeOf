using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Switches global Volume presets in sync with the UVPentagramSequence music.
/// Each preset is a separate global Volume (higher priority than the scene's
/// base volume). When the track reaches a timeline time, the target preset's
/// weight smoothly blends to 1 while others fade to 0 — post-processing and
/// room tone change radically. When the music stops, all preset weights
/// return to 0 and the base scene look is fully restored.
///
/// Place on any always-active GameObject (e.g. the parent of the preset volumes).
/// </summary>
public class UiiaVolumeSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class PresetEntry
    {
        [Tooltip("Track time (seconds) when this preset activates. mm:ss → seconds: 14:13 = 853.")]
        public float time;

        [Tooltip("Index of the preset in the Presets array.")]
        public int presetIndex;
    }

    [Header("Presets")]
    [Tooltip("Global Volume components, one per preset. Weight 0 = inactive.")]
    [SerializeField] private Volume[] _presets;

    [Header("Timeline")]
    [Tooltip("Track times when each preset kicks in, in ascending order.")]
    [SerializeField] private PresetEntry[] _timeline =
    {
        new PresetEntry { time = 853f,  presetIndex = 0 },  // 14:13
        new PresetEntry { time = 960f,  presetIndex = 1 },  // 16:00
        new PresetEntry { time = 1040f, presetIndex = 2 },  // 17:20
        new PresetEntry { time = 1152f, presetIndex = 3 }   // 19:12
    };

    [Header("Blending")]
    [Tooltip("How fast preset weights blend (weight units per second).")]
    [SerializeField, Min(0.1f)] private float _blendSpeed = 2f;

    private float[] _targetWeights;
    private int _nextEntryIndex;
    private bool _wasPlaying;

    private void Awake() => _targetWeights = new float[_presets.Length];

    private void OnDisable() => ClearWeights();

    private void Update()
    {
        bool playing = UVPentagramSequence.MusicPlaying;

        // Music just started — reset the timeline
        if (playing && !_wasPlaying)
        {
            _nextEntryIndex = 0;
            System.Array.Clear(_targetWeights, 0, _targetWeights.Length);
        }

        // Music just ended — drop all presets, base scene look is restored
        if (!playing && _wasPlaying)
            ClearWeights();

        _wasPlaying = playing;

        if (playing && UVPentagramSequence.InstanceMusicSource != null)
        {
            float trackTime = UVPentagramSequence.InstanceMusicSource.time;

            while (_nextEntryIndex < _timeline.Length && _timeline[_nextEntryIndex].time <= trackTime)
            {
                int index = _timeline[_nextEntryIndex].presetIndex;
                Debug.Log($"[UiiaVolumeSwitcher] Switching to preset {index} at {trackTime:F1}s", this);
                for (int i = 0; i < _targetWeights.Length; i++)
                    _targetWeights[i] = i == index ? 1f : 0f;
                _nextEntryIndex++;
            }
        }

        // Smoothly blend all preset volumes toward their target weights
        for (int i = 0; i < _presets.Length; i++)
        {
            if (_presets[i] == null) continue;
            _presets[i].weight = Mathf.MoveTowards(_presets[i].weight, _targetWeights[i], _blendSpeed * Time.deltaTime);
        }
    }

    private void ClearWeights()
    {
        if (_targetWeights == null) return;
        System.Array.Clear(_targetWeights, 0, _targetWeights.Length);
    }
}
