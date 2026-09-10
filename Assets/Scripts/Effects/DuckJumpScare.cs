using System.Collections;
using UnityEngine;

/// <summary>
/// Horror jump-scare sequence for the duck entity.
/// When activated (by HorrorEvent enabling the GameObject):
///   1. Jumps out of the water toward the player's face.
///   2. Grows in size as it approaches.
///   3. Freezes with eyes at the player's eye level, face toward the camera.
/// The HorrorEvent deactivates the GameObject after its disappear delay.
/// Attach to the scare-duck GameObject that is initially inactive.
/// Place two child Collider objects at the duck's eyes and assign their names
/// to _leftEyeName / _rightEyeName so the script can measure real eye positions.
/// </summary>
public class DuckJumpScare : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Time for the duck to travel from its start position to the freeze point (seconds).")]
    [SerializeField] private float _jumpDuration = 0.6f;

    [Tooltip("Distance from the camera to the duck's eyes when frozen (meters).")]
    [SerializeField] private float _freezeDistance = 0.3f;

    [Tooltip("Curve controlling the jump arc. X=0 is start, X=1 is freeze point.")]
    [SerializeField] private AnimationCurve _jumpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Scaling")]
    [Tooltip("Scale at the start of the jump (relative to the prefab's initial scale).")]
    [SerializeField] private float _startScaleMultiplier = 1f;

    [Tooltip("Scale when the duck reaches the freeze point (relative to the prefab's initial scale).")]
    [SerializeField] private float _endScaleMultiplier = 2f;

    [Tooltip("Vertical arc height added on top of the straight-line path (meters).")]
    [SerializeField] private float _arcHeight = 0.3f;

    [Header("Eyes")]
    [Tooltip("Name of the left eye child object.")]
    [SerializeField] private string _leftEyeName = "aye2";

    [Tooltip("Name of the right eye child object.")]
    [SerializeField] private string _rightEyeName = "aye1";

    [Tooltip("If true, the duck's face is on local -Z. Set to false if the face is on +Z.")]
    [SerializeField] private bool _faceOnNegativeZ = false;

    [Tooltip("Downward tilt in degrees when frozen, so the duck stares down at the player.")]
    [SerializeField, Range(0f, 45f)] private float _freezeTiltDown = 10f;

    [Tooltip("Extra height added to the eye position when frozen (meters). " +
             "0 = eyes at camera height. Positive raises the duck above eye level.")]
    [SerializeField] private float _freezeYOffset = 0f;

    [Header("Camera")]
    [Tooltip("Player camera. Auto-assigned to Camera.main if left empty.")]
    [SerializeField] private Camera _playerCamera;

    // Pre-computed freeze state.
    private Vector3 _startPos;
    private Quaternion _startRot;
    private Vector3 _startScale;
    private Vector3 _freezePivotPos;
    private Quaternion _freezeRot;
    private float _elapsed;

    /// <summary>Finds a direct child transform by name.</summary>
    private Transform FindEye(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return transform.Find(name);
    }

    /// <summary>
    /// Measures the world-space midpoint between the two eye child objects.
    /// Must be called AFTER setting transform.rotation, transform.localScale,
    /// and transform.position to get an accurate reading.
    /// </summary>
    private Vector3 MeasureEyeMidpointWorld()
    {
        Transform leftEye = FindEye(_leftEyeName);
        Transform rightEye = FindEye(_rightEyeName);

        if (leftEye != null && rightEye != null)
            return (leftEye.position + rightEye.position) * 0.5f;
        if (leftEye != null)
            return leftEye.position;
        if (rightEye != null)
            return rightEye.position;

        return transform.position;
    }

    /// <summary>
    /// Computes the rotation that makes the duck's face point toward the camera.
    /// If _faceOnNegativeZ is true, face is on -Z so +Z points away from camera.
    /// If false, face is on +Z so +Z points toward camera.
    /// </summary>
    private Quaternion ComputeFaceRotation(Vector3 camForward)
    {
        Vector3 lookDir = _faceOnNegativeZ ? camForward : -camForward;
        return Quaternion.LookRotation(lookDir, Vector3.up);
    }

    /// <summary>
    /// Pre-computes the freeze position and rotation using a measure-and-correct approach:
    /// 1. Set the final rotation and scale on the transform.
    /// 2. Place the pivot at the camera position temporarily.
    /// 3. Measure where the eyes actually land in world space.
    /// 4. Calculate the correction so eyes end up at the desired position.
    /// 5. Apply the correction to the pivot.
    /// This accounts for the real model geometry, parent transforms, scale, and tilt.
    /// </summary>
    private void ComputeFreezeTransform()
    {
        if (_playerCamera == null)
        {
            _freezePivotPos = _startPos;
            _freezeRot = _startRot;
            return;
        }

        Vector3 camPos = _playerCamera.transform.position;
        Vector3 camForward = _playerCamera.transform.forward;

        // Final rotation: face toward camera + tilt down.
        Quaternion faceRot = ComputeFaceRotation(camForward);
        _freezeRot = faceRot;
        if (_freezeTiltDown > 0f)
            _freezeRot *= Quaternion.Euler(-_freezeTiltDown, 0f, 0f);

        // Final scale.
        Vector3 finalScale = _startScale * _endScaleMultiplier;

        // Temporarily apply final rotation, scale, and place pivot at camera.
        transform.rotation = _freezeRot;
        transform.localScale = finalScale;
        transform.position = camPos;

        // Measure where the eyes actually are now.
        Vector3 measuredEyePos = MeasureEyeMidpointWorld();

        // Where the eyes SHOULD be: in front of camera, at camera eye level + offset.
        Vector3 desiredEyePos = camPos + camForward * _freezeDistance;
        desiredEyePos.y += _freezeYOffset;

        // Correction: shift the pivot by the difference.
        _freezePivotPos = camPos + (desiredEyePos - measuredEyePos);
    }

    private void OnEnable()
    {
        _elapsed = 0f;
        _startPos = transform.position;
        _startRot = transform.rotation;
        _startScale = transform.localScale;

        if (_playerCamera == null)
            _playerCamera = Camera.main;

        ComputeFreezeTransform();

        StartCoroutine(JumpSequence());
    }

    /// <summary>
    /// Phase 1: Jump from start to freeze position with arc and growth.
    /// Phase 2: Freeze completely.
    /// </summary>
    private IEnumerator JumpSequence()
    {
        // ── Phase 1: Jump ──
        while (_elapsed < _jumpDuration)
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _jumpDuration);
            float eased = _jumpCurve.Evaluate(t);

            // Position: lerp from start to freeze pivot, plus vertical arc.
            Vector3 pos = Vector3.Lerp(_startPos, _freezePivotPos, eased);
            pos.y += Mathf.Sin(t * Mathf.PI) * _arcHeight;
            transform.position = pos;

            // Scale: grow from start to end multiplier.
            float scaleMul = Mathf.Lerp(_startScaleMultiplier, _endScaleMultiplier, eased);
            transform.localScale = _startScale * scaleMul;

            // Rotation: interpolate from start to freeze rotation.
            transform.rotation = Quaternion.Slerp(_startRot, _freezeRot, eased);

            yield return null;
        }

        // ── Phase 2: Freeze — completely static ──
        transform.position = _freezePivotPos;
        transform.localScale = _startScale * _endScaleMultiplier;
        transform.rotation = _freezeRot;

        yield return null;
    }
}
