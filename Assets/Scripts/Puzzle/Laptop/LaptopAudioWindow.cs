using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Audio player window with play, pause, stop controls, progress slider and spectrum visualizer (UI and Shader based).</summary>
    public class LaptopAudioWindow : LaptopWindow
    {
        [Header("Display")]
        [SerializeField] private TMP_Text _trackName;
        [SerializeField] private TMP_Text _timeDisplay;

        [Header("Controls")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Slider _progressSlider;

        [Header("Visualizer Settings")]
        [SerializeField] private Material _visualizerMaterial;
        private int _bandCount = 8;
        [SerializeField] private float _smoothness = 15f;
        [SerializeField] private float _spectrumMultiplier = 50f;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;

        private float[] _spectrum = new float[128];
        private float[] _bands;
        private static readonly int BandsPropertyId = Shader.PropertyToID("_Bands");

        protected override void Awake()
        {
            base.Awake();
            _playButton.onClick.AddListener(Play);
            _pauseButton.onClick.AddListener(Pause);
            _stopButton.onClick.AddListener(Stop);

            if (_progressSlider != null)
                _progressSlider.onValueChanged.AddListener(OnSliderSeeked);

            _bands = new float[_bandCount];
        }

        private void Update()
        {
            UpdateUI();
            UpdateVisualizer();
        }

        private void UpdateUI()
        {
            if (_audioSource == null || !_audioSource.isPlaying
                || _audioSource.clip == null || _audioSource.clip.length <= 0f) return;

            float progress = _audioSource.time / _audioSource.clip.length;
            _progressSlider?.SetValueWithoutNotify(progress);

            if (_timeDisplay != null)
                _timeDisplay.text = $"{FormatTime(_audioSource.time)} / "
                                  + FormatTime(_audioSource.clip.length);
        }

        private void UpdateVisualizer()
        {
            bool isPlaying = _audioSource != null && _audioSource.isPlaying;
            
            if (isPlaying)
            {
                _audioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
                
                // Calculate bands (shared for both UI and Shader)
                for (int i = 0; i < _bands.Length; i++)
                {
                    float value = _spectrum[i] * _spectrumMultiplier;
                    _bands[i] = Mathf.Lerp(_bands[i], value, Time.deltaTime * _smoothness);
                }
            }
            else
            {
                for (int i = 0; i < _bands.Length; i++)
                    _bands[i] = Mathf.Lerp(_bands[i], 0f, Time.deltaTime * _smoothness);
            }

            // Apply to Shader
            if (_visualizerMaterial != null)
            {
                _visualizerMaterial.SetFloatArray(BandsPropertyId, _bands);
            }
        }

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopAudioFile audioFile)) return;

            _audioSource.clip = audioFile.clip;

            if (_trackName != null) _trackName.text = audioFile.fileName;
            _progressSlider?.SetValueWithoutNotify(0f);

            if (_timeDisplay != null && audioFile.clip != null)
                _timeDisplay.text = $"0:00 / {FormatTime(audioFile.clip.length)}";
        }

        protected override void OnClose() => _audioSource.Stop();

        /// <summary>Starts or resumes playback.</summary>
        public void Play()  { if (_audioSource.clip != null) _audioSource.Play(); }

        /// <summary>Pauses playback.</summary>
        public void Pause() => _audioSource.Pause();

        /// <summary>Stops playback and resets to beginning.</summary>
        public void Stop()
        {
            _audioSource.Stop();
            _audioSource.time = 0f;
            _progressSlider?.SetValueWithoutNotify(0f);
        }

        private void OnSliderSeeked(float value)
        {
            if (_audioSource.clip != null)
                _audioSource.time = value * _audioSource.clip.length;
        }

        private static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60f);
            int s = Mathf.FloorToInt(seconds % 60f);
            return $"{m}:{s:00}";
        }
    }
}
