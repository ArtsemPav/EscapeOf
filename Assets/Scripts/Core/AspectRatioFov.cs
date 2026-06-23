using UnityEngine;

/// <summary>
/// Keeps the game's field of view consistent with a 16:9 reference design while filling the
/// ENTIRE screen on ANY monitor aspect ratio. It NEVER adds black bars ("expand" behaviour).
///
/// How it behaves:
///  - On screens WIDER than the reference (e.g. 21:9 / 16:10): the vertical FOV stays as authored,
///    so the player simply sees MORE to the left/right. This is Unity's natural perspective
///    behaviour, so nothing is distorted.
///  - On screens NARROWER than the reference (e.g. 4:3): the vertical FOV is widened so the
///    horizontal view that was designed for 16:9 is NEVER cropped — the player sees more vertically.
///
/// Cinemachine friendly:
///  This component lives on the rendering Camera (the one with the CinemachineBrain). Thanks to the
///  high DefaultExecutionOrder it runs AFTER the brain has written the active virtual camera's FOV
///  to this Camera each frame. It reads that value as the "16:9 reference vertical FOV" for the
///  frame and only corrects it for the real aspect ratio. Because the reference is refreshed every
///  frame, it keeps working while zooming (see CameraZoom.cs) or blending between cameras.
///
/// ============================ FINALIZATION NOTES (safe to change) ============================
///  - referenceAspectWidth / referenceAspectHeight: change only if you redesign for a non-16:9 base.
///  - This effect is purely "expand". If you ever want a STRICT 16:9 look with black bars instead,
///    use the commented letterbox block at the bottom of LateUpdate() and disable the FOV section.
///  - It reacts to the CURRENT screen size automatically, so it is already correct after the player
///    changes the resolution through ResolutionManager / a future settings menu. No extra call needed.
/// =============================================================================================
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(10000)] // run after CinemachineBrain so its output is our reference FOV
public class AspectRatioFov : MonoBehaviour
{
    [Header("Reference design aspect (width / height)")]
    [Tooltip("The aspect ratio the game's FOV is authored for. 16:9 is the default.")]
    [SerializeField] private float referenceAspectWidth = 16f;
    [SerializeField] private float referenceAspectHeight = 9f;

    private Camera _cam;

    // Used to tell apart an externally-provided FOV (brain / zoom) from the value we wrote last
    // frame, so we never compound our own correction when no brain resets the camera.
    private float _referenceVerticalFov;
    private float _lastAppliedFov = -1f;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        // Guarantee a full-screen viewport: removes any leftover rect that could cause black bars.
        _cam.rect = new Rect(0f, 0f, 1f, 1f);
        _referenceVerticalFov = _cam.fieldOfView;
    }

    private void LateUpdate()
    {
        if (_cam == null || _cam.orthographic)
            return;

        // If the current FOV differs from what we wrote last frame, an external source
        // (CinemachineBrain, CameraZoom, a blend...) provided a fresh reference value.
        if (!Mathf.Approximately(_cam.fieldOfView, _lastAppliedFov))
            _referenceVerticalFov = _cam.fieldOfView;

        float referenceAspect = referenceAspectWidth / Mathf.Max(0.0001f, referenceAspectHeight);
        float actualAspect = (float)Screen.width / Mathf.Max(1, Screen.height);

        float resultFov;
        if (actualAspect >= referenceAspect)
        {
            // Wider than (or equal to) reference: keep vertical FOV; horizontal expands naturally.
            resultFov = _referenceVerticalFov;
        }
        else
        {
            // Narrower than reference: widen vertical FOV so the 16:9 horizontal view is preserved.
            float halfV = _referenceVerticalFov * 0.5f * Mathf.Deg2Rad;
            float refHorizontalHalf = Mathf.Atan(Mathf.Tan(halfV) * referenceAspect);
            float newHalfV = Mathf.Atan(Mathf.Tan(refHorizontalHalf) / actualAspect);
            resultFov = newHalfV * 2f * Mathf.Rad2Deg;
        }

        _cam.fieldOfView = resultFov;
        _lastAppliedFov = resultFov;

        // ===== OPTIONAL: strict 16:9 with black bars (letterbox) =====
        // To use this instead of the expand behaviour above, comment out the FOV block and
        // uncomment the following:
        //
        // float target = referenceAspect;
        // float scaleHeight = actualAspect / target;
        // if (scaleHeight < 1f)
        //     _cam.rect = new Rect(0f, (1f - scaleHeight) / 2f, 1f, scaleHeight);
        // else
        // {
        //     float scaleWidth = 1f / scaleHeight;
        //     _cam.rect = new Rect((1f - scaleWidth) / 2f, 0f, scaleWidth, 1f);
        // }
    }
}
