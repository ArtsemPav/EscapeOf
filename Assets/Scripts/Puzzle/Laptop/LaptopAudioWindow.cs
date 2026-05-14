using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>Audio player window with play, pause, stop controls and a progress slider.</summary>
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

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;

        private void Awake()
        {
            _playButton.onClick.AddListener(Play);
            _pauseButton.onClick.AddListener(Pause);
            _stopButton.onClick.AddListener(Stop);

            if (_progressSlider != null)
                _progressSlider.onValueChanged.AddListener(OnSliderSeeked);
        }

        private void Update()
        {
            if (_audioSource == null || !_audioSource.isPlaying
                || _audioSource.clip == null || _audioSource.clip.length <= 0f) return;

            float progress = _audioSource.time / _audioSource.clip.length;
            _progressSlider?.SetValueWithoutNotify(progress);

            if (_timeDisplay != null)
                _timeDisplay.text = $"{FormatTime(_audioSource.time)} / "
                                  + FormatTime(_audioSource.clip.length);
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
