using System.Collections;
using UnityEngine;

/// <summary>
/// Interactive rubber duck for the toilet horror event.
/// The duck is initially invisible — it appears when the master power turns on.
/// The player clicks the duck to make it dive underwater and resurface with buoyancy.
/// After <see cref="MaxDives"/> clicks, the duck dives and does not return;
/// instead a horror event is triggered via <see cref="HorrorSystem.Trigger"/>.
/// Implements <see cref="IInteractable"/> for click interaction and
/// <see cref="ISaveable"/> for persistence.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DuckHorrorInteractable : MonoBehaviour, IInteractable, ISaveable
{
    private const int MaxDives = 5;
    private const int RippleSlotCount = 4;

    // Shader property IDs for ripple effect.
    private static readonly int[] RippleIds =
    {
        Shader.PropertyToID("_Ripple0"),
        Shader.PropertyToID("_Ripple1"),
        Shader.PropertyToID("_Ripple2"),
        Shader.PropertyToID("_Ripple3")
    };

    [Header("Dive Settings")]
    [Tooltip("How far the duck dives below its surface position (local Y units).")]
    [SerializeField] private float _diveDepth = 0.12f;

    [Tooltip("Duration of the dive-down animation in seconds.")]
    [SerializeField] private float _diveDuration = 0.5f;

    [Tooltip("Time the duck stays underwater between dives.")]
    [SerializeField] private float _underwaterDuration = 1.4f;

    [Tooltip("Time the duck stays underwater on the final dive before the horror event.")]
    [SerializeField] private float _finalUnderwaterDuration = 2.5f;

    [Tooltip("Duration of the surfacing animation in seconds.")]
    [SerializeField] private float _surfaceDuration = 0.65f;

    [Tooltip("How high the duck overshoots above the surface when surfacing (local Y units).")]
    [SerializeField] private float _surfaceOvershoot = 0.06f;

    [Header("Ripple")]
    [Tooltip("Name of the sibling object with the liquid renderer. If empty, searches children of parent.")]
    [SerializeField] private string _liquidObjectName = "toiletLiquid";

    [Tooltip("Strength of the splash ripple when the duck dives.")]
    [SerializeField] private float _diveRippleStrength = 1.5f;

    [Tooltip("Strength of the splash ripple when the duck surfaces.")]
    [SerializeField] private float _surfaceRippleStrength = 2.0f;

    [Tooltip("Strength of continuous ripples while the duck is underwater.")]
    [SerializeField] private float _underwaterRippleStrength = 0.5f;

    [Tooltip("Interval between underwater ripples in seconds.")]
    [SerializeField] private float _underwaterRippleInterval = 0.4f;

    [Header("Interaction")]
    [SerializeField] private string _interactText = "Нажать на уточку";

    [Tooltip("Water splash sound played when the duck is clicked.")]
    [SerializeField] private AudioClip _splashClip;

    [Tooltip("Volume of the splash sound.")]
    [SerializeField, Range(0f, 1f)] private float _splashVolume = 0.8f;

    [Header("Horror")]
    [Tooltip("Event ID to trigger via HorrorSystem after the final dive.")]
    [SerializeField] private string _horrorEventId = "duck_horror";

    [Header("Save")]
    [SerializeField] private string _saveId;

    private Rigidbody _body;
    private Renderer[] _renderers;
    private Collider _collider;
    private Vector3 _surfaceLocalPos;
    private Quaternion _baseLocalRot;
    private int _diveCount;
    private bool _isBusy;
    private bool _horrorTriggered;
    private bool _isShown;

    private Renderer _liquidRenderer;
    private MaterialPropertyBlock _rippleBlock;
    private int _rippleSlot;

    public string SaveId => _saveId;
    public bool UseLMBClick => true;

    /// <summary>Returns true when the duck is visible and ready for interaction.</summary>
    public bool CanInteract() => _isShown && !_isBusy && !_horrorTriggered;

    public bool IsPickable() => false;
    public string GetInteractText() => _interactText;
    public CrosshairMode GetCrosshairMode() => CrosshairMode.Hand;
    public string GetBlockedHint() => string.Empty;

    /// <summary>Called by FPSController when the player clicks the duck.</summary>
    public void Interact()
    {
        if (!CanInteract()) return;

        // Play water splash sound through AudioManager.
        if (_splashClip != null)
            AudioManager.Instance?.PlaySFX(_splashClip, _splashVolume);

        StartCoroutine(DiveRoutine());
    }

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _renderers = GetComponentsInChildren<Renderer>();
        _collider = GetComponent<Collider>();
        _surfaceLocalPos = transform.localPosition;
        _baseLocalRot = transform.localRotation;
        _body.isKinematic = true;
        _body.useGravity = false;
        SetVisible(false);
        FindLiquidRenderer();
    }

    /// <summary>Finds the liquid renderer on a sibling object to push ripple data.</summary>
    private void FindLiquidRenderer()
    {
        if (transform.parent == null) return;

        Transform liquid = transform.parent.Find(_liquidObjectName);
        if (liquid == null)
        {
            // Search all siblings for a Renderer with LiquidWobble.
            foreach (Transform sibling in transform.parent)
            {
                if (sibling == transform) continue;
                var rend = sibling.GetComponent<Renderer>();
                if (rend != null && rend.sharedMaterial != null)
                {
                    var shaderName = rend.sharedMaterial.shader.name;
                    if (shaderName.Contains("LiquidFlaskToilet"))
                    {
                        liquid = sibling;
                        break;
                    }
                }
            }
        }

        if (liquid != null)
            _liquidRenderer = liquid.GetComponent<Renderer>();

        if (_liquidRenderer != null)
            _rippleBlock = new MaterialPropertyBlock();
    }

    /// <summary>Triggers a circular ripple on the liquid surface at the duck's world position.</summary>
    private void TriggerRipple(float strength)
    {
        if (_liquidRenderer == null || _rippleBlock == null) return;

        Vector3 worldPos = transform.position;
        Vector4 rippleData = new Vector4(worldPos.x, Time.time, worldPos.z, strength);

        _liquidRenderer.GetPropertyBlock(_rippleBlock);
        _rippleBlock.SetVector(RippleIds[_rippleSlot], rippleData);
        _liquidRenderer.SetPropertyBlock(_rippleBlock);

        _rippleSlot = (_rippleSlot + 1) % RippleSlotCount;
    }

    private void OnEnable()
    {
        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnPowerChanged += OnPowerChanged;
    }

    private void OnDisable()
    {
        if (LightingSystem.Instance != null)
            LightingSystem.Instance.OnPowerChanged -= OnPowerChanged;
    }

    private void Start()
    {
        SaveManager.Instance?.Register(this);

        // If power is already on, show the duck immediately.
        if (LightingSystem.Instance != null && LightingSystem.Instance.IsPowered)
            OnPowerChanged(true);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void OnPowerChanged(bool isPowered)
    {
        if (isPowered && !_isShown && !_horrorTriggered)
            SetVisible(true);
    }

    private void SetVisible(bool visible)
    {
        _isShown = visible;
        foreach (Renderer r in _renderers)
            r.enabled = visible;
        if (_collider != null)
            _collider.enabled = visible;
    }

    private IEnumerator DiveRoutine()
    {
        _isBusy = true;
        _diveCount++;

        // Splash ripple when duck dives.
        TriggerRipple(_diveRippleStrength);

        // Dive down with a slight nose-down tilt offset from base rotation.
        yield return AnimateLocalY(
            _surfaceLocalPos.y,
            _surfaceLocalPos.y - _diveDepth,
            _diveDuration,
            25f);

        // Wait underwater — emit periodic ripples.
        float waitTime = (_diveCount >= MaxDives) ? _finalUnderwaterDuration : _underwaterDuration;
        yield return UnderwaterRippleRoutine(waitTime);

        if (_diveCount >= MaxDives)
        {
            // Horror event — duck stays hidden underwater.
            _horrorTriggered = true;
            SaveManager.Instance?.Save();

            if (HorrorSystem.Instance != null)
                HorrorSystem.Instance.Trigger(_horrorEventId);

            SetVisible(false);
            yield break;
        }

        // Splash ripple when duck surfaces.
        TriggerRipple(_surfaceRippleStrength);

        // Surface with buoyant pop.
        yield return SurfaceRoutine();
        _isBusy = false;
    }

    /// <summary>Emits small ripples at regular intervals while the duck is underwater.</summary>
    private IEnumerator UnderwaterRippleRoutine(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            TriggerRipple(_underwaterRippleStrength);
            float wait = Mathf.Min(_underwaterRippleInterval, duration - elapsed);
            yield return new WaitForSeconds(wait);
            elapsed += wait;
        }
    }

    /// <summary>
    /// Simulates a hollow rubber duck full of air: fast buoyant rise,
    /// momentum overshoot above the surface, then damped bounces as it settles.
    /// All rotations are applied as offsets on top of the base local rotation.
    /// </summary>
    private IEnumerator SurfaceRoutine()
    {
        float startY = _surfaceLocalPos.y - _diveDepth;
        float peakY = _surfaceLocalPos.y + _surfaceOvershoot;

        float phase1Dur = Mathf.Max(_surfaceDuration * 0.25f, 0.01f);
        float phase2Dur = Mathf.Max(_surfaceDuration * 0.75f, 0.01f);

        // Phase 1 — fast buoyant rise to peak.
        float t = 0f;
        while (t < phase1Dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / phase1Dur);
            float y = startY + (1f - Mathf.Pow(1f - p, 2f)) * (peakY - startY);
            SetLocalY(y);
            SetTiltOffset(-12f * (1f - p));
            yield return null;
        }

        // Phase 2 — fall back to surface + damped bounces.
        t = 0f;
        while (t < phase2Dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / phase2Dur);
            float bounce = Mathf.Sin(p * Mathf.PI * 3f) * Mathf.Pow(1f - p, 1.5f) * 0.02f;
            float y = peakY + (_surfaceLocalPos.y - peakY) * p + bounce;
            SetLocalY(y);
            float tilt = 8f * (1f - p) + Mathf.Sin(p * Mathf.PI * 3f) * 3f * Mathf.Pow(1f - p, 1.5f);
            SetTiltOffset(tilt);
            yield return null;
        }

        SetLocalY(_surfaceLocalPos.y);
        SetTiltOffset(0f);
    }

    /// <summary>Animates local Y from fromY to toY while tilting nose by tiltDeg degrees.</summary>
    private IEnumerator AnimateLocalY(float fromY, float toY, float duration, float tiltDeg)
    {
        float dur = Mathf.Max(duration, 0.01f);
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / dur);
            SetLocalY(Mathf.Lerp(fromY, toY, p));
            SetTiltOffset(Mathf.Lerp(0f, tiltDeg, p));
            yield return null;
        }
    }

    /// <summary>Sets local Y while preserving X and Z. Guards against NaN.</summary>
    private void SetLocalY(float y)
    {
        if (float.IsNaN(y)) return;
        Vector3 pos = transform.localPosition;
        pos.y = y;
        transform.localPosition = pos;
    }

    /// <summary>Applies a nose tilt offset (X rotation in degrees) on top of the base local rotation.</summary>
    private void SetTiltOffset(float tiltDeg)
    {
        if (float.IsNaN(tiltDeg)) return;
        transform.localRotation = _baseLocalRot * Quaternion.Euler(tiltDeg, 0f, 0f);
    }

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData
        {
            diveCount = _diveCount,
            horrorTriggered = _horrorTriggered,
            isShown = _isShown
        });
    }

    public void LoadSaveData(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        _diveCount = data.diveCount;
        _horrorTriggered = data.horrorTriggered;

        if (_horrorTriggered)
        {
            // Horror already played — keep the duck hidden.
            SetVisible(false);
        }
    }

    [System.Serializable]
    private class SaveData
    {
        public int diveCount;
        public bool horrorTriggered;
        public bool isShown;
    }
}
