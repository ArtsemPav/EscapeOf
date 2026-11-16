using System.Collections;
using UnityEngine;

/// <summary>
/// Interactive rubber duck for the toilet horror event.
/// The duck is initially invisible — it only appears when the player delivers
/// a draggable RubberDuck from the pantry into the toilet
/// (detected by <see cref="DuckToiletTrigger"/>, which calls <see cref="DeliverFromToilet"/>).
/// The player clicks the duck to make it dive underwater and resurface with buoyancy.
/// After <see cref="MaxDives"/> clicks, the duck dives and does not return;
/// instead a horror event is triggered via <see cref="HorrorSystem.Trigger"/>.
/// Implements <see cref="IInteractable"/> for click interaction and
/// <see cref="ISaveable"/> for persistence.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DuckHorrorInteractable : MonoBehaviour, IInteractable, ISaveable
{
    private const int MaxDives = 3;
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

    [Header("Delivery")]
    [Tooltip("Draggable duck in the pantry. Hidden and deactivated when delivered into the toilet.")]
    [SerializeField] private GameObject _pantryDuck;

    [Tooltip("Depth below the water surface where the delivered duck starts its surfacing rise (local Y units).")]
    [SerializeField] private float _deliveryStartDepth = 0.45f;

    [Tooltip("Strength of the splash ripple when the event duck surfaces after delivery.")]
    [SerializeField] private float _deliveryRippleStrength = 2.5f;

    [Tooltip("Volume of the splash sound played when the event duck surfaces.")]
    [SerializeField, Range(0f, 1f)] private float _deliverySplashVolume = 1f;

    [Header("Interaction")]

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
    private bool _delivered;

    private Renderer _liquidRenderer;
    private MaterialPropertyBlock _rippleBlock;
    private int _rippleSlot;

    private DuckSoundZone _soundZone;

    public string SaveId => _saveId;
    public bool UseLMBClick => true;

    /// <summary>Draggable pantry duck GameObject consumed on delivery.</summary>
    public GameObject PantryDuck => _pantryDuck;

    /// <summary>True once the draggable duck has been delivered into the toilet.</summary>
    public bool HasBeenDelivered => _delivered;

    /// <summary>True once the horror jumpscare has played (the duck is gone for good).</summary>
    public bool HorrorTriggered => _horrorTriggered;

    /// <summary>
    /// Restores the delivered state during save loading, without animation or
    /// extra saving: the duck appears in the toilet, the pantry duck stays hidden.
    /// Used by <see cref="DuckToiletTrigger"/> as a consistency fallback.
    /// </summary>
    public void RestoreDelivered()
    {
        if (_horrorTriggered) return;
        _delivered = true;
        SetVisible(true);
        if (_pantryDuck != null)
            _pantryDuck.SetActive(false);
        _soundZone?.SetLureActive(true);
    }

    /// <summary>Returns true when the duck is ready to be interacted with.</summary>
    public bool CanInteract() => !_isBusy && !_horrorTriggered;

    public bool IsPickable() => false;
    /// <summary>Returns the localized duck interaction hint.</summary>
    public string GetInteractText()
        => Localization.Get(LocalizationKeys.InteractPressDuck);
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
        FindSoundZone();

        // Register in Awake so SaveManager.Start() can deliver saved data
        // before this script's Start() runs (same pattern as HorrorEvent).
        SaveManager.Instance?.Register(this);
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

    /// <summary>Finds the DuckSoundZone on a sibling object to disable it after horror.</summary>
    private void FindSoundZone()
    {
        if (transform.parent == null) return;
        _soundZone = transform.parent.GetComponentInChildren<DuckSoundZone>();
    }

    /// <summary>Disables the lure sound zone after the horror event has passed.</summary>
    private void DisableSoundZone()
    {
        if (_soundZone != null)
            _soundZone.enabled = false;
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

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    /// <summary>
    /// Called by <see cref="DuckToiletTrigger"/> when the player drops the draggable
    /// pantry duck into the toilet. The pantry duck is hidden, and this duck
    /// surfaces from the water with a splash. No-op if already delivered
    /// or the horror event has already played.
    /// </summary>
    public void DeliverFromToilet()
    {
        if (_delivered || _horrorTriggered) return;
        _delivered = true;

        if (_pantryDuck != null)
            _pantryDuck.SetActive(false);

        // Splash as the delivered duck hits the toilet water.
        if (_splashClip != null)
            AudioManager.Instance?.PlaySFX(_splashClip, _deliverySplashVolume);

        // Persist immediately — the pantry duck must stay gone even if the
        // session ends before the surfacing animation finishes.
        SaveManager.Instance?.Save();

        StartCoroutine(DeliveryRoutine());
    }

    /// <summary>Plays the surfacing animation after delivery, then enables the lure sound.</summary>
    private IEnumerator DeliveryRoutine()
    {
        // Duck starts fully submerged below the water surface.
        SetLocalY(_surfaceLocalPos.y - _deliveryStartDepth);
        SetVisible(true);

        // Ripple + splash as the duck starts rising, then periodic underwater ripples.
        TriggerRipple(_deliveryRippleStrength);
        yield return UnderwaterRippleRoutine(_underwaterDuration);

        // Splash sound as the duck pops out of the water.
        if (_splashClip != null)
            AudioManager.Instance?.PlaySFX(_splashClip, _deliverySplashVolume);

        // Buoyant pop out of the water (same animation as the dive surfacing).
        yield return SurfaceRoutine(_deliveryStartDepth);

        // Now the duck is in the toilet — the lure sound may start calling the player.
        _soundZone?.SetLureActive(true);
    }

    private void SetVisible(bool visible)
    {
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

        // On the final dive, hide the duck as soon as it goes underwater.
        bool isFinalDive = _diveCount >= MaxDives;

        // Dive down with a slight nose-down tilt offset from base rotation.
        yield return AnimateLocalY(
            _surfaceLocalPos.y,
            _surfaceLocalPos.y - _diveDepth,
            _diveDuration,
            25f);

        if (isFinalDive)
        {
            // Duck is now underwater — disable renderer immediately.
            _horrorTriggered = true;
            SetVisible(false);
            DisableSoundZone();
            SaveManager.Instance?.Save();
        }

        // Wait underwater — emit periodic ripples.
        float waitTime = isFinalDive ? _finalUnderwaterDuration : _underwaterDuration;
        yield return UnderwaterRippleRoutine(waitTime);

        if (isFinalDive)
        {
            // Horror event — duck is already hidden, just trigger it.

            if (HorrorSystem.Instance != null)
                HorrorSystem.Instance.Trigger(_horrorEventId);

            yield break;
        }

        // Splash ripple when duck surfaces.
        TriggerRipple(_surfaceRippleStrength);

        // Surface with buoyant pop.
        yield return SurfaceRoutine(_diveDepth);
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
    /// Simulates a hollow rubber duck full of air: fast buoyant rise from
    /// <paramref name="startDepth"/> below the surface, momentum overshoot
    /// above the surface, then damped bounces as it settles.
    /// All rotations are applied as offsets on top of the base local rotation.
    /// </summary>
    private IEnumerator SurfaceRoutine(float startDepth)
    {
        float startY = _surfaceLocalPos.y - startDepth;
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
            delivered = _delivered
        });
    }

    public void LoadSaveData(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        SaveData data = JsonUtility.FromJson<SaveData>(json);
        _diveCount = data.diveCount;
        _horrorTriggered = data.horrorTriggered;
        _delivered = _horrorTriggered || data.delivered;

        if (_horrorTriggered)
        {
            // Horror already played — hide the duck and disable lure sound.
            SetVisible(false);
            DisableSoundZone();
            // Deactivate the entire GameObject so nothing can re-enable
            // the renderer or collider. Update() safety net won't be needed.
            gameObject.SetActive(false);
            return;
        }

        if (_delivered)
        {
            // Duck was delivered but the horror has not played yet —
            // it stays in the toilet and can be clicked; pantry duck stays hidden.
            RestoreDelivered();
        }
    }

    [System.Serializable]
    private class SaveData
    {
        public int diveCount;
        public bool horrorTriggered;
        public bool delivered;
    }
}
