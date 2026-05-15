using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace EscapeOf.Puzzle.Laptop
{
    /// <summary>
    /// Video player window. Uses a VideoPlayer that renders to a runtime RenderTexture
    /// displayed via a RawImage.
    /// </summary>
    public class LaptopVideoWindow : LaptopWindow
    {
        [Header("Display")]
        [SerializeField] private RawImage _videoDisplay;
        [SerializeField] private TMP_Text _timeDisplay;

        [Header("Controls")]
        [SerializeField] private Button _playButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _stopButton;
        [SerializeField] private Slider _progressSlider;

        [Header("Video")]
        [SerializeField] private VideoPlayer _videoPlayer;

        private RenderTexture _renderTexture;
        private bool          _isPrepared;

        protected override void Awake()
        {
            base.Awake();
            _playButton.onClick.AddListener(Play);
            _pauseButton.onClick.AddListener(Pause);
            _stopButton.onClick.AddListener(Stop);

            _videoPlayer.prepareCompleted += OnPrepared;

            if (_progressSlider != null)
                _progressSlider.onValueChanged.AddListener(OnSliderSeeked);
        }

        private void Update()
        {
            if (!_isPrepared || !_videoPlayer.isPlaying || _videoPlayer.frameCount == 0) return;

            float progress = (float)_videoPlayer.frame / (float)_videoPlayer.frameCount;
            _progressSlider?.SetValueWithoutNotify(progress);

            if (_timeDisplay != null)
                _timeDisplay.text = $"{FormatTime(_videoPlayer.time)} / "
                                  + FormatTime(_videoPlayer.length);
        }

        protected override void OnOpen(LaptopFileData file)
        {
            if (!(file is LaptopVideoFile videoFile)) return;

            _isPrepared = false;
            _videoPlayer.clip = videoFile.clip;

            ReleaseRenderTexture();

            uint w = videoFile.clip != null ? videoFile.clip.width  : 1280;
            uint h = videoFile.clip != null ? videoFile.clip.height : 720;
            _renderTexture = new RenderTexture((int)w, (int)h, 0);

            _videoPlayer.targetTexture = _renderTexture;
            _videoDisplay.texture      = _renderTexture;

            _videoPlayer.Prepare();
        }

        protected override void OnClose()
        {
            _videoPlayer.Stop();
            _videoPlayer.clip = null;
            ReleaseRenderTexture();
        }

        private void OnDestroy() => ReleaseRenderTexture();

        private void OnPrepared(VideoPlayer _)
        {
            _isPrepared = true;
            _progressSlider?.SetValueWithoutNotify(0f);

            if (_timeDisplay != null)
                _timeDisplay.text = $"0:00 / {FormatTime(_videoPlayer.length)}";
        }

        /// <summary>Starts or resumes video playback.</summary>
        public void Play() => _videoPlayer.Play();

        /// <summary>Pauses video playback.</summary>
        public void Pause() => _videoPlayer.Pause();

        /// <summary>Stops playback and resets to the beginning.</summary>
        public void Stop()
        {
            _videoPlayer.Stop();
            _progressSlider?.SetValueWithoutNotify(0f);
        }

        private void OnSliderSeeked(float value)
        {
            if (!_isPrepared || _videoPlayer.frameCount == 0) return;
            _videoPlayer.frame = (long)(value * (float)_videoPlayer.frameCount);
        }

        private void ReleaseRenderTexture()
        {
            if (_renderTexture == null) return;
            _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        private static string FormatTime(double seconds)
        {
            int m = Mathf.FloorToInt((float)seconds / 60f);
            int s = Mathf.FloorToInt((float)seconds % 60f);
            return $"{m}:{s:00}";
        }
    }
}
