using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// How the portal acts on the player: camera shake and FOV punch on activation, a low rumble and
/// post-processing (bloom, chromatic aberration, vignette, lens pull) that grow as the player
/// approaches, and a full-screen flash plus FOV stretch when the player enters the core.
/// Camera offsets are applied additively in LateUpdate and removed first thing next frame, so
/// the player's own camera scripts are never overwritten.
/// </summary>
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(PortalFinaleEffects))]
public class PortalPlayerImpact : MonoBehaviour
{
    private const float ImpactDecaySeconds = 1.6f;
    private const float ShakeNoiseSpeed = 22f;
    private const float VolumePriority = 50f;
    private const float OverlaySortingOrder = 5000f;
    private const float EnterFlashInSeconds = 1.1f;
    private const float EnterHoldSeconds = 0.6f;
    private const float EnterFlashOutSeconds = 1.2f;

    [Header("Range")]
    [Tooltip("Distance from the core where the portal starts to affect the player.")]
    [SerializeField] private float _effectRange = 16f;
    [Tooltip("Distance at which proximity effects reach full strength.")]
    [SerializeField] private float _fullEffectDistance = 3f;

    [Header("Camera Shake")]
    [SerializeField] private float _impactShakeAmplitude = 0.14f;
    [SerializeField] private float _rumbleAmplitude = 0.012f;
    [SerializeField] private float _impactFovKick = 7f;

    [Header("Post Processing (at full strength)")]
    [SerializeField] private float _bloomIntensity = 1.6f;
    [SerializeField, Range(0f, 1f)] private float _bloomThreshold = 0.85f;
    [SerializeField, Range(0f, 1f)] private float _chromaticAberration = 0.45f;
    [SerializeField, Range(0f, 1f)] private float _vignette = 0.32f;
    [SerializeField, Range(-1f, 0f)] private float _lensDistortion = -0.22f;

    [Header("Enter Portal")]
    [SerializeField] private float _enterRadius = 1.6f;
    [SerializeField] private float _enterFovStretch = 28f;
    [Tooltip("Invoked at the peak of the white flash when the player steps into the core (e.g. load the ending).")]
    [SerializeField] private UnityEvent _onPlayerEntered;

    private PortalFinaleEffects _finale;
    private Camera _camera;
    private Transform _cameraTransform;
    private Vector3 _appliedPositionOffset;
    private float _appliedFovOffset;
    private float _trauma;
    private float _enterFov;
    private bool _isEntering;
    private bool _hasEntered;

    private Volume _volume;
    private VolumeProfile _profile;
    private Bloom _bloom;
    private ChromaticAberration _chromatic;
    private Vignette _vignetteOverride;
    private LensDistortion _lens;

    private Canvas _overlayCanvas;
    private Image _overlayImage;

    private void Awake()
    {
        _finale = GetComponent<PortalFinaleEffects>();
        BuildVolume();
    }

    private void OnEnable()
    {
        if (_finale != null)
            _finale.ImpactOccurred += OnImpact;
    }

    private void OnDisable()
    {
        if (_finale != null)
            _finale.ImpactOccurred -= OnImpact;
    }

    private void OnDestroy()
    {
        if (_profile != null)
            Destroy(_profile);
        if (_overlayCanvas != null)
            Destroy(_overlayCanvas.gameObject);
    }

    // Runs before player camera scripts (execution order -1000): undo last frame's offsets
    private void Update()
    {
        RemoveAppliedOffsets();
    }

    private void LateUpdate()
    {
        if (!TryGetCamera())
            return;

        float distance = Vector3.Distance(_cameraTransform.position, _finale.CoreWorldPosition);
        float proximity = 1f - Mathf.InverseLerp(_fullEffectDistance, _effectRange, distance);
        proximity = Mathf.Clamp01(proximity);
        float power = _finale.Power;

        UpdateVolume(power, proximity);
        UpdateEnterTrigger(distance, power);
        ApplyCameraEffects(power, proximity);
    }

    private bool TryGetCamera()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
            _cameraTransform = _camera != null ? _camera.transform : null;
        }
        return _camera != null;
    }

    private void RemoveAppliedOffsets()
    {
        if (_camera == null)
            return;

        _cameraTransform.localPosition -= _appliedPositionOffset;
        _camera.fieldOfView -= _appliedFovOffset;
        _appliedPositionOffset = Vector3.zero;
        _appliedFovOffset = 0f;
    }

    private void OnImpact()
    {
        if (!TryGetCamera())
            return;

        // Impact felt anywhere in range, stronger up close
        float distance = Vector3.Distance(_cameraTransform.position, _finale.CoreWorldPosition);
        float reach = 1f - Mathf.InverseLerp(4f, _effectRange * 1.6f, distance);
        _trauma = Mathf.Max(_trauma, Mathf.Clamp01(reach));
    }

    private void ApplyCameraEffects(float power, float proximity)
    {
        _trauma = Mathf.MoveTowards(_trauma, 0f, Time.deltaTime / ImpactDecaySeconds);

        float shake = _trauma * _trauma * _impactShakeAmplitude
                    + power * proximity * proximity * _rumbleAmplitude;

        float time = Time.time * ShakeNoiseSpeed;
        var offset = new Vector3(
            Mathf.PerlinNoise(time, 0.1f) - 0.5f,
            Mathf.PerlinNoise(time, 7.3f) - 0.5f,
            0f) * 2f * shake;

        _appliedPositionOffset = offset;
        _cameraTransform.localPosition += offset;

        _appliedFovOffset = _trauma * _trauma * _impactFovKick + _enterFov;
        _camera.fieldOfView += _appliedFovOffset;
    }

    // ------------------------------------------------------- post processing

    private void BuildVolume()
    {
        var volumeObject = new GameObject("PortalPostProcess");
        volumeObject.transform.SetParent(transform, false);

        _volume = volumeObject.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _bloom = _profile.Add<Bloom>(true);
        _chromatic = _profile.Add<ChromaticAberration>(true);
        _vignetteOverride = _profile.Add<Vignette>(true);
        _lens = _profile.Add<LensDistortion>(true);
        _volume.sharedProfile = _profile;
    }

    private void UpdateVolume(float power, float proximity)
    {
        // Subtle everywhere in range (so the glow always blooms), strong when close
        float strength = power * Mathf.Lerp(0.45f, 1f, proximity);
        float flashBoost = 1f + _finale.Flash * 1.5f;

        _volume.weight = Mathf.Clamp01(strength);
        _bloom.intensity.value = _bloomIntensity * flashBoost;
        _bloom.threshold.value = _bloomThreshold;
        _chromatic.intensity.value = _chromaticAberration * Mathf.Lerp(0.3f, 1f, proximity) + _finale.Flash * 0.5f;
        _vignetteOverride.intensity.value = _vignette * proximity;
        _lens.intensity.value = _lensDistortion * proximity;
    }

    // ----------------------------------------------------------- entering

    private void UpdateEnterTrigger(float distance, float power)
    {
        if (_isEntering)
            return;

        if (distance > _enterRadius * 1.5f)
            _hasEntered = false;

        if (!_hasEntered && power > 0.8f && distance <= _enterRadius)
        {
            _hasEntered = true;
            StartCoroutine(EnterRoutine());
        }
    }

    private IEnumerator EnterRoutine()
    {
        _isEntering = true;
        EnsureOverlay();

        // Stretch FOV and burn the screen to white
        float timer = 0f;
        while (timer < EnterFlashInSeconds)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / EnterFlashInSeconds);
            _enterFov = Mathf.SmoothStep(0f, _enterFovStretch, t);
            SetOverlayAlpha(Mathf.Pow(t, 2f));
            yield return null;
        }

        _onPlayerEntered?.Invoke();

        yield return new WaitForSeconds(EnterHoldSeconds);

        // Recover (if the event did not load another scene)
        timer = 0f;
        while (timer < EnterFlashOutSeconds)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / EnterFlashOutSeconds);
            _enterFov = Mathf.Lerp(_enterFovStretch, 0f, t);
            SetOverlayAlpha(1f - t);
            yield return null;
        }

        _enterFov = 0f;
        SetOverlayAlpha(0f);
        _isEntering = false;
    }

    private void EnsureOverlay()
    {
        if (_overlayCanvas != null)
            return;

        var canvasObject = new GameObject("PortalEnterOverlay");
        canvasObject.transform.SetParent(transform, false);
        _overlayCanvas = canvasObject.AddComponent<Canvas>();
        _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _overlayCanvas.sortingOrder = (int)OverlaySortingOrder;

        var imageObject = new GameObject("Flash");
        imageObject.transform.SetParent(canvasObject.transform, false);
        _overlayImage = imageObject.AddComponent<Image>();
        _overlayImage.color = new Color(1f, 0.97f, 0.9f, 0f);
        _overlayImage.raycastTarget = false;

        var rect = _overlayImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void SetOverlayAlpha(float alpha)
    {
        var color = _overlayImage.color;
        color.a = alpha;
        _overlayImage.color = color;
    }
}
