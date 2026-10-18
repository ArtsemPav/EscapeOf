using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

/// <summary>
/// MRI room horror cutscene. Started by pressing the green button (see MriCutsceneButton).
///
/// Sequence:
///   1. Player loses control (input, flashlight, cursor) and the view smoothly turns to the Rings.
///   2. The room lamps flicker with their normal light, then start blinking UV light.
///   3. Every UV pulse reveals the hidden Rings sign (HiddenWallSign forced visible),
///      a batch of blood decals, and - during the dark gaps - the body bag and the rubber duck.
///   4. The last UV pulse holds long enough to read the sign, then the lamps flicker
///      back to their default light and control returns to the player.
///
/// Blood decals, the body bag and the duck stay hidden until the cutscene starts and stay
/// visible afterwards. The Rings keep their normal behaviour (visible only in the UV flashlight).
/// Persists "already played" via ISaveable.
/// </summary>
public class MriUvCutscene : MonoBehaviour, ISaveable
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Save")]
    [Tooltip("Stable save key. Never change after the game ships.")]
    [SerializeField] private string _saveId = "mri_room_uv_cutscene";

    [Header("Look")]
    [Tooltip("Point the player's view turns to (the Rings).")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Smooth time of the view turn. Larger = slower, softer turn.")]
    [SerializeField, Min(0.05f)] private float _turnSmoothTime = 0.7f;

    [Tooltip("Safety limit for the turn, in seconds.")]
    [SerializeField, Min(0.5f)] private float _turnTimeout = 3f;

    [Tooltip("Pause between the button press and the start of the turn.")]
    [SerializeField, Min(0f)] private float _startDelay = 0.25f;

    [Tooltip("Pause after the view settled on the target, before the lights start to act up.")]
    [SerializeField, Min(0f)] private float _holdAfterTurn = 0.5f;

    [Header("Lamps")]
    [Tooltip("Room lamp lights. Their colour and intensity are overridden during the cutscene and restored afterwards.")]
    [SerializeField] private Light[] _lampLights;

    [Tooltip("Lamp meshes. Their emission turns UV during pulses (optional).")]
    [SerializeField] private Renderer[] _lampRenderers;

    [Tooltip("Colour of the UV light.")]
    [SerializeField] private Color _uvLightColor = new Color(0.45f, 0.08f, 1f, 1f);

    [Tooltip("UV light intensity relative to the lamp's normal intensity.")]
    [SerializeField, Min(0f)] private float _uvIntensityMultiplier = 1.6f;

    [Tooltip("HDR emission colour of the lamp meshes during UV pulses.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color _uvEmissionColor = new Color(1.2f, 0.2f, 4f, 1f);

    [Header("Signs")]
    [Tooltip("Root of the hidden signs (Rings). All HiddenWallSign components below it appear during UV pulses.")]
    [SerializeField] private Transform _signsRoot;

    [Header("Timing")]
    [Tooltip("Normal-light flicker before the UV pulses start.")]
    [SerializeField, Min(0f)] private float _preFlickerDuration = 0.9f;

    [Tooltip("Number of UV pulses. Blood decals are split evenly between them.")]
    [SerializeField, Range(2, 16)] private int _pulseCount = 6;

    [Tooltip("Random duration of a UV pulse (min, max), seconds. The last pulse uses Final Hold Duration.")]
    [SerializeField] private Vector2 _pulseOnRange = new Vector2(0.15f, 0.45f);

    [Tooltip("Random duration of the dark gap between pulses (min, max), seconds.")]
    [SerializeField] private Vector2 _pulseOffRange = new Vector2(0.1f, 0.35f);

    [Tooltip("How long the last UV pulse stays on so the player can read the sign.")]
    [SerializeField, Min(0.2f)] private float _finalHoldDuration = 2.4f;

    [Tooltip("Flicker back to the default light, seconds.")]
    [SerializeField, Min(0f)] private float _restoreFlickerDuration = 0.8f;

    [Tooltip("Pause after the default light is back, before control returns to the player.")]
    [SerializeField, Min(0f)] private float _endDelay = 0.6f;

    [Header("Reveals")]
    [Tooltip("Blood decals, in reveal order. Hidden until the cutscene, then revealed batch by batch with every UV pulse.")]
    [SerializeField] private DecalProjector[] _bloodDecals;

    [Tooltip("Fade-in time of one blood decal.")]
    [SerializeField, Min(0.01f)] private float _decalFadeDuration = 0.3f;

    [Tooltip("Final opacity range of the blood decals (min, max). Every decal gets its own value in this range, so the blood looks duller and uneven.")]
    [SerializeField] private Vector2 _decalOpacityRange = new Vector2(0.65f, 0.95f);

    [SerializeField] private GameObject _bodyBag;

    [Tooltip("The body bag appears in the dark gap before this UV pulse (0 = first).")]
    [SerializeField, Min(0)] private int _bodyBagPulse = 2;

    [SerializeField] private GameObject _rubberDuck;

    [Tooltip("The rubber duck appears in the dark gap before this UV pulse.")]
    [SerializeField, Min(0)] private int _duckPulse = 4;

    [Header("Audio (optional)")]
    [Tooltip("Fallback source, used only when there is no AudioManager in the scene.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("One random clip is played at every UV pulse start (lamp buzz / electric zap).")]
    [SerializeField] private AudioClip[] _pulseClips;

    [Tooltip("Stuttering lamp buzz: played when the normal light starts to flicker and when it comes back.")]
    [SerializeField] private AudioClip _flickerClip;

    [Tooltip("Low UV lamp hum, played during the last (long) UV pulse.")]
    [SerializeField] private AudioClip _humClip;

    [Tooltip("Bone crunch and tearing flesh: played when the body bag appears.")]
    [SerializeField] private AudioClip _bodyBagClip;

    [Tooltip("Played when the rubber duck appears (optional).")]
    [SerializeField] private AudioClip _revealClip;

    [SerializeField, Range(0f, 1f)] private float _sfxVolume = 1f;

    [Header("Events")]
    [SerializeField] private UnityEvent _onStarted;
    [SerializeField] private UnityEvent _onFinished;

    // ── State ─────────────────────────────────────────────────────────────────

    private class LampState
    {
        public Light Light;
        public LightZone Zone;
        public Color Color;
        public float BaseIntensity;
        public bool WasEnabled;
    }

    private LampState[] _lamps = Array.Empty<LampState>();
    private HiddenWallSign[] _signs = Array.Empty<HiddenWallSign>();
    private MaterialPropertyBlock _block;

    private FPSController _fps;
    private bool _flashlightWasOn;
    private bool _flashlightWasLocked;
    private bool _controlTaken;

    private bool _played;
    private bool _playing;

    /// <summary>True when the cutscene can still be started (not played, not running).</summary>
    public bool CanStart => !_played && !_playing;

    /// <summary>True once the cutscene has been started in this save.</summary>
    public bool HasPlayed => _played;

    // ── ISaveable ─────────────────────────────────────────────────────────────

    public string SaveId => _saveId;

    public string GetSaveData() => JsonUtility.ToJson(new SaveData { played = _played });

    public void LoadSaveData(string json)
    {
        var data = JsonUtility.FromJson<SaveData>(json);
        if (data == null || !data.played) return;

        _played = true;
        if (!_playing)
            ApplyFinalState();
    }

    [Serializable]
    private class SaveData
    {
        public bool played;
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _block = new MaterialPropertyBlock();

        // Everything revealed by the cutscene starts hidden. The prefab keeps these
        // objects active so they can be positioned in the editor.
        SetRevealObjectsActive(false);

        if (_signsRoot != null)
            _signs = _signsRoot.GetComponentsInChildren<HiddenWallSign>(true);

        // Route the fallback source to the SFX bus unless it already has a mixer group.
        if (_audioSource != null && _audioSource.outputAudioMixerGroup == null)
            AudioManager.Instance?.RouteSourceToSfxGroup(_audioSource);

        SaveManager.Instance?.Register(this);
    }

    private void OnDisable()
    {
        // The room can be switched off mid-cutscene (room culling): never leave
        // the player without control or the lamps in UV colour.
        if (!_playing) return;

        StopAllCoroutines();
        SetSignsVisible(false);
        RestoreLamps();
        ApplyFinalState();
        ReleaseControl();
        _playing = false;
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);

        if (_playing)
            ReleaseControl();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Starts the cutscene. Ignored if it is already running or has been played.</summary>
    public void Play()
    {
        if (!CanStart || !isActiveAndEnabled) return;
        StartCoroutine(CutsceneRoutine());
    }

    // ── Sequence ──────────────────────────────────────────────────────────────

    private IEnumerator CutsceneRoutine()
    {
        _playing = true;
        _played = true;
        SaveManager.Instance?.Save();
        _onStarted?.Invoke();

        TakeControl();
        CaptureLamps();

        if (_startDelay > 0f)
            yield return new WaitForSeconds(_startDelay);

        yield return TurnToTarget();

        if (_holdAfterTurn > 0f)
            yield return new WaitForSeconds(_holdAfterTurn);

        // Normal light starts to misbehave
        PlayClip(_flickerClip);
        yield return FlickerDefaultLight(_preFlickerDuration);

        // UV pulses
        int decalsPerPulse = _bloodDecals != null && _bloodDecals.Length > 0
            ? Mathf.CeilToInt(_bloodDecals.Length / (float)_pulseCount)
            : 0;

        for (int i = 0; i < _pulseCount; i++)
        {
            bool last = i == _pulseCount - 1;

            // Dark gap - objects pop in while the player cannot see them
            SetLampsDark();
            if (i == _bodyBagPulse && _bodyBag != null)
            {
                _bodyBag.SetActive(true);
                PlayClip(_bodyBagClip);
            }
            if (i == _duckPulse && _rubberDuck != null)
            {
                _rubberDuck.SetActive(true);
                PlayClip(_revealClip);
            }

            yield return new WaitForSeconds(RandomRange(_pulseOffRange) * (last ? 1.5f : 1f));

            // UV flash
            SetLampsUv();
            SetSignsVisible(true);
            PlayClip(RandomClip(_pulseClips));
            if (last) PlayClip(_humClip);
            RevealDecals(i * decalsPerPulse, decalsPerPulse);

            float onTime = last ? _finalHoldDuration : RandomRange(_pulseOnRange);
            yield return new WaitForSeconds(onTime);

            if (!last)
                SetSignsVisible(false);
        }

        // Safety: anything whose pulse index is out of range still appears
        if (_bodyBag != null) _bodyBag.SetActive(true);
        if (_rubberDuck != null) _rubberDuck.SetActive(true);

        // Back to normal
        SetSignsVisible(false);
        PlayClip(_flickerClip);
        yield return FlickerRestore(_restoreFlickerDuration);
        RestoreLamps();

        if (_endDelay > 0f)
            yield return new WaitForSeconds(_endDelay);

        ReleaseControl();
        _playing = false;
        _onFinished?.Invoke();
    }

    // ── Player control ────────────────────────────────────────────────────────

    private void TakeControl()
    {
        InputManager.Instance?.SetPlayerInputEnabled(false);
        InputManager.Instance?.SetUIInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _fps = FindFirstObjectByType<FPSController>();
        _fps?.SetCinematicControl(true);

        var flashlight = FlashlightController.Instance;
        if (flashlight != null)
        {
            _flashlightWasOn = flashlight.IsOn;
            _flashlightWasLocked = flashlight.IsLocked;
            if (_flashlightWasOn) flashlight.ForceOff();
            flashlight.IsLocked = true;
        }

        _controlTaken = true;
    }

    private void ReleaseControl()
    {
        if (!_controlTaken) return;
        _controlTaken = false;

        // Re-enabling cinematic control re-syncs the controller's pitch with the current
        // camera rotation, so handing control back does not snap the view.
        if (_fps != null)
        {
            _fps.SetCinematicControl(true);
            _fps.SetCinematicControl(false);
        }

        var flashlight = FlashlightController.Instance;
        if (flashlight != null)
        {
            flashlight.IsLocked = _flashlightWasLocked;
            if (_flashlightWasOn) flashlight.ForceOn();
        }

        InputManager.Instance?.SetUIInputEnabled(true);
        InputManager.Instance?.SetPlayerInputEnabled(true);
    }

    private IEnumerator TurnToTarget()
    {
        if (_lookTarget == null || _fps == null || _fps.CameraTransform == null)
            yield break;

        Transform body = _fps.transform;
        Transform cam = _fps.CameraTransform;
        float yawVelocity = 0f;
        float pitchVelocity = 0f;
        float elapsed = 0f;

        while (elapsed < _turnTimeout)
        {
            elapsed += Time.deltaTime;

            Vector3 toTarget = _lookTarget.position - cam.position;
            float horizontal = new Vector2(toTarget.x, toTarget.z).magnitude;
            float targetYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float targetPitch = Mathf.Clamp(-Mathf.Atan2(toTarget.y, horizontal) * Mathf.Rad2Deg, -60f, 60f);

            float yaw = Mathf.SmoothDampAngle(body.eulerAngles.y, targetYaw, ref yawVelocity, _turnSmoothTime);
            float pitch = Mathf.SmoothDampAngle(cam.localEulerAngles.x, targetPitch, ref pitchVelocity, _turnSmoothTime);

            body.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            if (Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw)) < 0.4f &&
                Mathf.Abs(Mathf.DeltaAngle(pitch, targetPitch)) < 0.4f)
                break;

            yield return null;
        }
    }

    // ── Lamps ─────────────────────────────────────────────────────────────────

    private void CaptureLamps()
    {
        int count = _lampLights != null ? _lampLights.Length : 0;
        var list = new System.Collections.Generic.List<LampState>(count);

        for (int i = 0; i < count; i++)
        {
            Light light = _lampLights[i];
            if (light == null) continue;

            var zone = light.GetComponent<LightZone>();
            zone?.StopFlicker();

            list.Add(new LampState
            {
                Light = light,
                Zone = zone,
                Color = light.color,
                BaseIntensity = zone != null ? zone.OriginalIntensity : light.intensity,
                WasEnabled = light.enabled
            });
        }

        _lamps = list.ToArray();
    }

    private void SetLampsUv()
    {
        foreach (var lamp in _lamps)
        {
            if (lamp.Light == null) continue;
            lamp.Light.enabled = true;
            lamp.Light.color = _uvLightColor;
            lamp.Light.intensity = lamp.BaseIntensity * _uvIntensityMultiplier;
        }
        SetLampEmission(_uvEmissionColor);
    }

    private void SetLampsDark()
    {
        foreach (var lamp in _lamps)
            if (lamp.Light != null) lamp.Light.enabled = false;
        SetLampEmission(Color.black);
    }

    /// <summary>Default-coloured light with random intensity drops (only lamps that were on).</summary>
    private void SetLampsDefault(float intensityMultiplier)
    {
        foreach (var lamp in _lamps)
        {
            if (lamp.Light == null || !lamp.WasEnabled) continue;
            lamp.Light.enabled = intensityMultiplier > 0.02f;
            lamp.Light.color = lamp.Color;
            lamp.Light.intensity = lamp.BaseIntensity * intensityMultiplier;
        }
        ClearLampEmission();
    }

    private void RestoreLamps()
    {
        foreach (var lamp in _lamps)
        {
            if (lamp.Light == null) continue;
            lamp.Light.color = lamp.Color;
            lamp.Light.intensity = lamp.BaseIntensity;

            if (lamp.Zone != null)
                lamp.Zone.SetActive(lamp.WasEnabled); // also restarts the lamp's own flicker
            else
                lamp.Light.enabled = lamp.WasEnabled;
        }
        ClearLampEmission();
    }

    private void SetLampEmission(Color color)
    {
        if (_lampRenderers == null) return;

        _block.Clear();
        _block.SetColor(EmissionColorId, color);
        foreach (Renderer r in _lampRenderers)
            if (r != null) r.SetPropertyBlock(_block);
    }

    private void ClearLampEmission()
    {
        if (_lampRenderers == null) return;
        foreach (Renderer r in _lampRenderers)
            if (r != null) r.SetPropertyBlock(null);
    }

    private IEnumerator FlickerDefaultLight(float duration)
    {
        float end = Time.time + duration;
        while (Time.time < end)
        {
            SetLampsDefault(UnityEngine.Random.value < 0.35f ? 0f : UnityEngine.Random.Range(0.3f, 1f));
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.04f, 0.12f));
        }
    }

    /// <summary>Flicker that ends in the default light (same as the restore step, the last frame is full brightness).</summary>
    private IEnumerator FlickerRestore(float duration)
    {
        // The UV light dies first, then the normal light stutters back.
        SetLampsDark();
        yield return new WaitForSeconds(0.15f);

        float end = Time.time + duration;
        while (Time.time < end)
        {
            SetLampsDefault(UnityEngine.Random.value < 0.3f ? 0f : UnityEngine.Random.Range(0.4f, 1f));
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.13f));
        }

        SetLampsDefault(1f);
    }

    // ── Reveals ───────────────────────────────────────────────────────────────

    private void SetSignsVisible(bool visible)
    {
        foreach (HiddenWallSign sign in _signs)
            if (sign != null) sign.SetForcedVisible(visible ? 1f : 0f);
    }

    private void RevealDecals(int startIndex, int count)
    {
        if (_bloodDecals == null) return;

        int end = Mathf.Min(startIndex + count, _bloodDecals.Length);
        for (int i = Mathf.Max(0, startIndex); i < end; i++)
        {
            DecalProjector decal = _bloodDecals[i];
            if (decal == null) continue;

            decal.fadeFactor = 0f;
            decal.gameObject.SetActive(true);
            StartCoroutine(FadeDecalIn(decal, DecalOpacity(i)));
        }
    }

    private IEnumerator FadeDecalIn(DecalProjector decal, float targetOpacity)
    {
        float t = 0f;
        while (t < _decalFadeDuration && decal != null)
        {
            t += Time.deltaTime;
            decal.fadeFactor = Mathf.Clamp01(t / _decalFadeDuration) * targetOpacity;
            yield return null;
        }

        if (decal != null)
            decal.fadeFactor = targetOpacity;
    }

    /// <summary>Stable pseudo-random opacity per decal (golden-ratio sequence), inside the configured range.</summary>
    private float DecalOpacity(int index)
    {
        float k = (index * 0.6180339f) % 1f;
        return Mathf.Lerp(Mathf.Min(_decalOpacityRange.x, _decalOpacityRange.y),
                          Mathf.Max(_decalOpacityRange.x, _decalOpacityRange.y), k);
    }

    private void SetRevealObjectsActive(bool active)
    {
        if (_bloodDecals != null)
            for (int i = 0; i < _bloodDecals.Length; i++)
            {
                DecalProjector decal = _bloodDecals[i];
                if (decal == null) continue;
                decal.fadeFactor = DecalOpacity(i);
                decal.gameObject.SetActive(active);
            }

        if (_bodyBag != null) _bodyBag.SetActive(active);
        if (_rubberDuck != null) _rubberDuck.SetActive(active);
    }

    /// <summary>State after the cutscene: blood, body bag and duck stay in the room.</summary>
    private void ApplyFinalState()
    {
        SetRevealObjectsActive(true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlaySFX(clip, _sfxVolume);
        else if (_audioSource != null)
            _audioSource.PlayOneShot(clip, _sfxVolume);
    }

    private static AudioClip RandomClip(AudioClip[] clips) =>
        clips != null && clips.Length > 0 ? clips[UnityEngine.Random.Range(0, clips.Length)] : null;

    private static float RandomRange(Vector2 range) =>
        UnityEngine.Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
}
