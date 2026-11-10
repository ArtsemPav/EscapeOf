using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Blood growth")]
    [Tooltip("Decals that grow after they appear (the wall and door blood). Each one grows by its own random factor.")]
    [SerializeField] private DecalProjector[] _growingDecals;

    [Tooltip("Growth factor range (min, max): how many times larger a stain becomes.")]
    [SerializeField] private Vector2 _decalGrowRange = new Vector2(2f, 3f);

    [Tooltip("Seconds a stain needs to reach its full size (short = almost instant pop, it still eases out).")]
    [SerializeField, Min(0.1f)] private float _decalGrowDuration = 0.3f;

    [Tooltip("A stain never grows beyond this size in metres (the factor is reduced for already big stains).")]
    [SerializeField, Min(0.5f)] private float _decalMaxGrownSize = 3.5f;

    [SerializeField] private GameObject _bodyBag;

    [Tooltip("The body bag appears in the dark gap before this UV pulse (0 = first).")]
    [SerializeField, Min(0)] private int _bodyBagPulse = 2;

    [SerializeField] private GameObject _rubberDuck;

    [Tooltip("The rubber duck appears in the dark gap before this UV pulse.")]
    [SerializeField, Min(0)] private int _duckPulse = 4;

    [Header("Maid")]
    [Tooltip("The maid apparition. Shown only inside the light flashes; she vanishes in every dark blink.")]
    [SerializeField] private MaidApparition _maid;

    [Tooltip("How long she is visible at each appearance (the strobe lights flicker meanwhile). The final jumpscare has its own time.")]
    [SerializeField, Min(0.1f)] private float _maidVisibleTime = 0.5f;

    [Tooltip("UV pulse of the first appearance: she just stands next to the body bag, arms down.")]
    [SerializeField, Min(0)] private int _maidStandPulse = 2;

    [Tooltip("UV pulse of the second appearance: she leans over the bag, both hands on it.")]
    [SerializeField, Min(0)] private int _maidHandsPulse = 4;

    [Tooltip("UV pulse of the third appearance: hands on the bag and she looks at the player over her shoulder.")]
    [SerializeField, Min(0)] private int _maidLookPulse = 5;

    [Header("Strobe")]
    [Tooltip("While she is visible the UV lamps strobe: seconds of light per flicker (min, max).")]
    [SerializeField] private Vector2 _strobeOn = new Vector2(0.03f, 0.06f);

    [Tooltip("Seconds of darkness per flicker (min, max).")]
    [SerializeField] private Vector2 _strobeOff = new Vector2(0.02f, 0.05f);

    [Tooltip("Chance that a strobe flash is normal white light instead of UV (0 = UV only, 1 = white only).")]
    [SerializeField, Range(0f, 1f)] private float _strobeWhiteChance = 0.5f;

    [Tooltip("Brightness of a white flash relative to the normal intensity of the lamp.")]
    [SerializeField, Min(0f)] private float _whiteIntensityMultiplier = 2f;

    [Tooltip("HDR emission colour of the lamp meshes during a white flash.")]
    [ColorUsage(false, true)]
    [SerializeField] private Color _whiteEmissionColor = new Color(2.5f, 2.5f, 2.5f, 1f);

    [Tooltip("Every UV pulse (except the last) starts with a very short white flash of this length in seconds (0 = off).")]
    [SerializeField, Min(0f)] private float _pulseWhitePop = 0.05f;

    [Header("Jumpscare (the very end)")]
    [Tooltip("After the last UV flash (the sign) the light goes dark, then she appears at the glass for a split second and screams.")]
    [SerializeField] private bool _endJumpscare = true;

    [Tooltip("Darkness and silence before the scare. The longer it is, the more the player expects it - and the harder it hits.")]
    [SerializeField, Min(0.05f)] private float _scareDarkGap = 1.9f;

    [Tooltip("How long she stays visible at the glass. Keep it short.")]
    [SerializeField, Min(0.1f)] private float _scareDuration = 0.5f;

    [Tooltip("Strobe the lamps while she is at the glass (the first moment stays lit so the hit lands).")]
    [SerializeField] private bool _scareStrobe = true;

    [Tooltip("Seconds of steady light at the start of the scare before the strobe begins.")]
    [SerializeField, Min(0f)] private float _scareSolidStart = 0.15f;

    [Tooltip("How far the head is thrown back while she screams: 0 = she keeps staring at the player, 1 = full scream pose.")]
    [SerializeField, Range(0f, 1f)] private float _scareHeadBack = 0.15f;

    [Tooltip("Head shake while she screams: 0 = still, 1 = full shake.")]
    [SerializeField, Range(0f, 1f)] private float _scareShake = 0.4f;

    [Tooltip("Hard knock of her hands hitting the glass.")]
    [SerializeField] private AudioClip _glassThumpClip;

    [Tooltip("Short sharp scream.")]
    [SerializeField] private AudioClip _screamClip;

    [Tooltip("Faint whisper / breath when she turns to the player (optional).")]
    [SerializeField] private AudioClip _maidLookClip;

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
        public ILampZone Zone;
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

    /// <summary>The body bag object (the maid puts her hands on it).</summary>
    public GameObject BodyBag => _bodyBag;

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

        PrepareGrowth();

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
        _maid?.Hide();
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
        {
            // Never leave the signs forced visible (SetForcedVisible leaks otherwise:
            // _forcedAmount = 1 keeps them glowing in any light until the next
            // SetSignsVisible(false), which a destroyed cutscene never issues).
            SetSignsVisible(false);
            ReleaseControl();
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Starts the cutscene. Ignored if it is already running or has been played.</summary>
    public void Play()
    {
        if (!CanStart || !isActiveAndEnabled) return;
        StartCoroutine(CutsceneRoutine());
    }

    /// <summary>
    /// DEV: puts the room back to the state before the cutscene so it can be played again:
    /// stops the sequence, restores the lamps, hides the maid / blood / body bag / duck / signs,
    /// gives the control back and clears the saved "already played" flag. Remove together with the dev button.
    /// </summary>
    public void DevResetState()
    {
        StopAllCoroutines();

        _maid?.Hide();
        SetSignsVisible(false);
        RestoreLamps();
        ReleaseControl();
        SetRevealObjectsActive(false);

        _playing = false;
        _played = false;
        SaveManager.Instance?.Save();
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

            // Dark gap - objects pop in (and the maid vanishes) while the player cannot see
            SetLampsDark();
            _maid?.Hide();
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

            // UV flash, hard: it opens with a very short white pop
            if (_pulseWhitePop > 0f && !last)
            {
                SetLampsWhite();
                yield return new WaitForSeconds(_pulseWhitePop);
            }
            SetLampsUv();
            SetSignsVisible(true);
            PlayClip(RandomClip(_pulseClips));
            if (last) PlayClip(_humClip);
            RevealDecals(i * decalsPerPulse, decalsPerPulse);

            // She is visible only for a moment (the lamps strobe), then the pulse goes on without her
            if (_maid != null && TryGetMaidStage(i, out MaidApparition.BagStage stage))
                yield return MaidWindow(stage);

            float onTime = last ? _finalHoldDuration : RandomRange(_pulseOnRange);
            yield return new WaitForSeconds(onTime);

            if (!last)
                SetSignsVisible(false);
        }

        // Safety: anything whose pulse index is out of range still appears
        if (_bodyBag != null) _bodyBag.SetActive(true);
        if (_rubberDuck != null) _rubberDuck.SetActive(true);

        // The very end: darkness, then she is suddenly at the glass for a moment
        if (_endJumpscare && _maid != null)
        {
            SetSignsVisible(false);
            SetLampsDark();
            yield return new WaitForSeconds(_scareDarkGap);

            SetLampsWhite();   // the hit lands in a hard white flash
            _maid.Show(MaidApparition.SpotId.Glass);
            _maid.Scream(_scareDuration, _scareHeadBack, _scareShake);
            PlayClip(_glassThumpClip);
            PlayClip(_screamClip);

            if (_scareStrobe)
                yield return Strobe(_scareDuration, _scareSolidStart, true);
            else
                yield return new WaitForSeconds(_scareDuration);

            // Cut: dark and gone in the same frame
            SetLampsDark();
            _maid.Hide();
            yield return new WaitForSeconds(0.35f);
        }

        // Back to normal
        _maid?.Hide();
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

    // ── Maid appearances ──────────────────────────────────────────────────────

    private bool TryGetMaidStage(int pulse, out MaidApparition.BagStage stage)
    {
        stage = MaidApparition.BagStage.Standing;
        if (pulse == _maidStandPulse) return true;

        if (pulse == _maidHandsPulse)
        {
            stage = MaidApparition.BagStage.HandsOnBag;
            return true;
        }
        if (pulse == _maidLookPulse)
        {
            stage = MaidApparition.BagStage.Looking;
            return true;
        }
        return false;
    }

    /// <summary>
    /// One short appearance at the body bag: she pops in, the lamps strobe for the visible time,
    /// then she vanishes while the lamps are dark and the pulse goes on.
    /// </summary>
    private IEnumerator MaidWindow(MaidApparition.BagStage stage)
    {
        _maid.Show(MaidApparition.SpotId.Bag, _bodyBag, stage);
        if (stage == MaidApparition.BagStage.Looking)
            PlayClip(_maidLookClip);

        yield return Strobe(_maidVisibleTime, 0f);   // ends dark

        _maid.Hide();
        yield return new WaitForSeconds(0.06f);
        SetLampsUv();                                // back to the normal pulse light
    }

    /// <summary>Flickers the lamps (UV on/off at high speed) for the given time. The lamps are dark at the end.</summary>
    private IEnumerator Strobe(float duration, float solidStart, bool firstWhite = false)
    {
        float end = Time.time + duration;
        Flash(firstWhite || UnityEngine.Random.value < _strobeWhiteChance);

        if (solidStart > 0f)
            yield return new WaitForSeconds(Mathf.Min(solidStart, duration));

        bool on = true;
        while (Time.time < end)
        {
            yield return new WaitForSeconds(RandomRange(on ? _strobeOn : _strobeOff));
            if (Time.time >= end) break;

            on = !on;
            if (on) Flash(UnityEngine.Random.value < _strobeWhiteChance);
            else Blackout();
        }

        Blackout();
    }

    /// <summary>A flash of light (white or UV). The maid exists only inside the flashes.</summary>
    private void Flash(bool white)
    {
        if (white) SetLampsWhite();
        else SetLampsUv();
        _maid?.SetShown(true);
    }

    private void Blackout()
    {
        SetLampsDark();
        _maid?.SetShown(false);
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

            var zone = light.GetComponentInParent<ILampZone>();
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

    /// <summary>Normal white light of the lamps, brighter than usual (a hard flash).</summary>
    private void SetLampsWhite()
    {
        foreach (var lamp in _lamps)
        {
            if (lamp.Light == null) continue;
            lamp.Light.enabled = true;
            lamp.Light.color = lamp.Color;
            lamp.Light.intensity = lamp.BaseIntensity * _whiteIntensityMultiplier;
        }
        SetLampEmission(_whiteEmissionColor);
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
            if (_growFactor.ContainsKey(decal))
                StartCoroutine(GrowDecal(decal));
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

    // ── Blood growth ──────────────────────────────────────────────────────────

    private readonly Dictionary<DecalProjector, Vector3> _baseSize = new Dictionary<DecalProjector, Vector3>();
    private readonly Dictionary<DecalProjector, float> _growFactor = new Dictionary<DecalProjector, float>();

    /// <summary>Remembers the authored size of every growing decal and picks its (stable) growth factor.</summary>
    private void PrepareGrowth()
    {
        _baseSize.Clear();
        _growFactor.Clear();
        if (_growingDecals == null) return;

        float min = Mathf.Min(_decalGrowRange.x, _decalGrowRange.y);
        float max = Mathf.Max(_decalGrowRange.x, _decalGrowRange.y);

        for (int i = 0; i < _growingDecals.Length; i++)
        {
            DecalProjector decal = _growingDecals[i];
            if (decal == null || _baseSize.ContainsKey(decal)) continue;

            Vector3 size = decal.size;
            float k = (i * 0.7548777f + 0.31f) % 1f;
            float factor = Mathf.Lerp(min, max, k);
            float biggest = Mathf.Max(size.x, size.y);
            if (biggest > 0.01f)
                factor = Mathf.Min(factor, _decalMaxGrownSize / biggest);

            _baseSize[decal] = size;
            _growFactor[decal] = Mathf.Max(1f, factor);
        }
    }

    private Vector3 GrownSize(DecalProjector decal, float amount)
    {
        Vector3 size = _baseSize[decal];
        float f = Mathf.Lerp(1f, _growFactor[decal], amount);
        // Only the width and height grow, the projection depth stays (no paint on far objects)
        return new Vector3(size.x * f, size.y * f, size.z);
    }

    private IEnumerator GrowDecal(DecalProjector decal)
    {
        float t = 0f;
        while (t < _decalGrowDuration && decal != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / _decalGrowDuration);
            decal.size = GrownSize(decal, 1f - (1f - k) * (1f - k)); // ease-out
            yield return null;
        }

        if (decal != null)
            decal.size = GrownSize(decal, 1f);
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
                if (_growFactor.ContainsKey(decal))
                    decal.size = GrownSize(decal, active ? 1f : 0f);
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
