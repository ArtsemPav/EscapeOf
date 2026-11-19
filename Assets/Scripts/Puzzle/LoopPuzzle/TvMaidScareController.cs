using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// One-shot horror event for the paint puzzle TV: after a set number of channel
/// switches, the maid mannequin appears standing still in front of the TV camera,
/// holds for a moment, then vanishes behind a burst of full-screen static.
///
/// The TV channel switch itself is NOT modified — this controller only observes
/// camera changes via <see cref="PeepholeTVCamera.CameraSwitched"/>, so the
/// puzzle logic (hints, cycling, blackout) is untouched.
/// </summary>
public class TvMaidScareController : MonoBehaviour, ISaveable
{
    [Header("Save")]
    [Tooltip("Unique save ID. The fired flag is persisted, so the scare plays only once per game.")]
    [SerializeField] private string _saveId = "paint_tv_maid_scare";

    [Header("TV")]
    [Tooltip("PeepholeTVCamera whose channel switches are observed.")]
    [SerializeField] private PeepholeTVCamera _tvCamera;

    [Tooltip("How many channel switches until the scare fires.")]
    [SerializeField] private int _switchesUntilScare = 4;

    [Tooltip("Camera index the maid is placed in front of. The scare fires when the player switches to this camera for the Nth time.")]
    [SerializeField] private int _maidCameraIndex = 1;

    [Header("Maid")]
    [Tooltip("Maid root GameObject. Must start INACTIVE — it is activated by the scare.")]
    [SerializeField] private GameObject _maid;

    [Tooltip("Seconds the maid stands still before vanishing.")]
    [SerializeField] private float _standDuration = 1.0f;

    [Tooltip("Animator int parameter value selecting the standing pose (State in dummyMaid.controller).")]
    [SerializeField] private int _standStateValue = 3;

    [Header("TV Static")]
    [Tooltip("Peak _GlitchAmount at the vanish moment (1 = full static).")]
    [Range(0f, 1f)]
    [SerializeField] private float _staticPeak = 1f;

    [Tooltip("Seconds for static to fade out after the maid vanishes.")]
    [SerializeField] private float _staticFadeOut = 0.6f;

    [Header("Sound")]
    [Tooltip("Horror SFX played when the maid appears. Played on a dedicated 2D source so it can be cut off when the scare is cancelled.")]
    [SerializeField] private AudioClip _scareSound;

    [Tooltip("Volume of the horror SFX.")]
    [Range(0f, 1f)]
    [SerializeField] private float _scareSoundVolume = 1f;

    [Header("Callbacks")]
    [Tooltip("Fired when the scare starts (maid appears, sound plays).")]
    [SerializeField] private UnityEvent _onScareStarted;

    [Tooltip("Fired when the maid vanishes and static fades out.")]
    [SerializeField] private UnityEvent _onScareFinished;

    private static readonly int GlitchAmountID = Shader.PropertyToID("_GlitchAmount");
    private static readonly int NoiseAmountID  = Shader.PropertyToID("_NoiseAmount");
    private const string AnimatorStateParameter = "State";
    private const string StandStateName = "Stand";

    private Animator _maidAnimator;
    private AudioSource _scareAudioSource;
    private bool _running;
    private bool _maidVisible;
    private bool _hasFired;
    private int _switchCount;
    private Coroutine _scareRoutine;

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    /// <summary>Serializes whether the scare has fired (or was interrupted) — it never replays after a save load.</summary>
    public string GetSaveData() =>
        JsonUtility.ToJson(new SaveData { hasFired = _hasFired || _running });

    public void LoadSaveData(string json)
    {
        _hasFired = JsonUtility.FromJson<SaveData>(json).hasFired;
    }

    [Serializable]
    private struct SaveData { public bool hasFired; }

    // ── Unity Lifecycle ────────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);

        if (_maid != null)
        {
            _maidAnimator = _maid.GetComponent<Animator>();
            // Guarantee she is invisible before the scare, no matter what
            // activated her in the scene (room visibility, scene overrides).
            _maid.SetActive(false);
        }

        // Dedicated 2D source for the scream — PlayOneShot on a shared source
        // cannot be stopped selectively, so cancellation needs its own source.
        _scareAudioSource = gameObject.AddComponent<AudioSource>();
        _scareAudioSource.playOnAwake = false;
        _scareAudioSource.spatialBlend = 0f;
        _scareAudioSource.loop = false;
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void OnEnable()
    {
        if (_tvCamera != null)
            _tvCamera.CameraSwitched += OnCameraSwitched;

        if (_maid != null && !_running)
            _maid.SetActive(false);
    }

    private void OnDisable()
    {
        if (_tvCamera != null)
            _tvCamera.CameraSwitched -= OnCameraSwitched;
    }

    // ── Trigger ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Counts channel switches and starts the scare when the counter is reached
    /// AND the player is about to watch the camera the maid is placed in front of.
    /// </summary>
    private void OnCameraSwitched(int cameraIndex)
    {
        // The maid is currently shown and the player switched away mid-show:
        // cancel quietly (hide her, cut the scream, no static burst). This MUST
        // be checked before the one-shot gate — StartScare sets _hasFired the
        // moment the show begins, so the gate would swallow the cancel otherwise.
        if (_maidVisible)
        {
            CancelScareQuietly();
            return;
        }

        // One-shot: once fired (and nothing is currently showing), never again.
        if (_hasFired) return;

        if (_running || cameraIndex != _maidCameraIndex) return;
        _switchCount++;
        if (_switchCount < _switchesUntilScare) return;
        StartScare();
    }

    /// <summary>
    /// Hides the maid without any static. The scream keeps playing to the end —
    /// the player earned the jump scare by triggering the show. The event stays
    /// fired forever.
    /// </summary>
    private void CancelScareQuietly()
    {
        _maidVisible = false;
        _running = false;
        _hasFired = true;

        if (_scareRoutine != null)
        {
            StopCoroutine(_scareRoutine);
            _scareRoutine = null;
        }

        if (_maid != null)
            _maid.SetActive(false);

        // If cancelled during the static fade-out, the routine stops mid-lerp —
        // restore the screen to its normal noise level here.
        if (_tvCamera != null && _tvCamera.ScreenMaterial != null)
        {
            _tvCamera.ScreenMaterial.SetFloat(GlitchAmountID, 0f);
            _tvCamera.ScreenMaterial.SetFloat(NoiseAmountID, _tvCamera.NoiseAmount);
        }

        _onScareFinished?.Invoke();
    }

    /// <summary>Starts the scare manually (cheats, tests, UnityEvents).</summary>
    public void StartScare()
    {
        if (_running || _hasFired || _maid == null) return;
        _running = true;
        _hasFired = true;
        _maidVisible = true;
        _scareRoutine = StartCoroutine(ScareRoutine());
    }

    // ── Scare sequence ─────────────────────────────────────────────────────────

    private IEnumerator ScareRoutine()
    {
        Material screenMaterial = _tvCamera != null ? _tvCamera.ScreenMaterial : null;
        float normalNoise = _tvCamera != null ? _tvCamera.NoiseAmount : 0.12f;

        // Maid appears standing still at her placed position.
        _maid.SetActive(true);

        if (_maidAnimator != null)
        {
            _maidAnimator.enabled = true;

            // Select the standing pose through the int parameter (AnyState
            // transitions in dummyMaid.controller map State == 3 to Stand)
            // and force the state directly for an immediate switch.
            _maidAnimator.SetInteger(AnimatorStateParameter, _standStateValue);
            _maidAnimator.Play(StandStateName, 0, 0f);
        }

        // Horror cue on top of the TV ambience. Played on the dedicated source
        // so CancelScareQuietly can cut it off if the player switches away.
        if (_scareSound != null && _scareAudioSource != null)
        {
            _scareAudioSource.clip = _scareSound;
            _scareAudioSource.volume = _scareSoundVolume;
            _scareAudioSource.Play();
        }

        _onScareStarted?.Invoke();

        yield return new WaitForSeconds(_standDuration);

        // Vanish behind the full static burst: heavy glitch + full-screen noise.
        if (screenMaterial != null)
        {
            screenMaterial.SetFloat(GlitchAmountID, _staticPeak);
            screenMaterial.SetFloat(NoiseAmountID, 1f);
        }
        _maid.SetActive(false);
        _maidVisible = false; // switches during the fade-out are no longer a "cancel"

        // Static fades out, TV returns to normal.
        yield return StartCoroutine(FadeStaticOut(screenMaterial, normalNoise));

        _onScareFinished?.Invoke();
    }

    /// <summary>
    /// Fades the static burst back to the TV's normal noise level
    /// over _staticFadeOut seconds.
    /// </summary>
    private IEnumerator FadeStaticOut(Material screenMaterial, float normalNoise)
    {
        if (screenMaterial == null) yield break;

        float elapsed = 0f;
        while (elapsed < _staticFadeOut)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(_staticFadeOut, 0.001f));
            screenMaterial.SetFloat(GlitchAmountID, _staticPeak * (1f - t));
            screenMaterial.SetFloat(NoiseAmountID, Mathf.Lerp(1f, normalNoise, t));
            yield return null;
        }

        screenMaterial.SetFloat(GlitchAmountID, 0f);
        screenMaterial.SetFloat(NoiseAmountID, normalNoise);
    }
}
