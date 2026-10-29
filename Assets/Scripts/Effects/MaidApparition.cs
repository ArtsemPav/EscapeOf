using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the maid (MaidHolder prefab instance) in the MRI room cutscene.
///
/// The maid is only ever shown for a moment (the cutscene toggles her between lamp blinks):
///   Bag   - she stands exactly where the MaidHolder instance is placed in the prefab (same spot,
///           same height, same size). She leans over the body bag, lowers both hands onto it and then
///           turns her head over the shoulder to look at the player.
///   Glass - she appears at the control-room glass (Glass spot), hands pressed to it, and screams.
///
/// No animation clip is played: the Animator is switched off and she keeps the pose saved in the prefab.
/// Everything else is procedural in LateUpdate (the rest pose is restored first every frame):
/// spine lean, two-bone IK for both arms, head / neck / spine turn towards the player and a scream shake.
/// Her size and floor height are never touched. Works with the mixamo skeleton of maidMain.fbx.
/// </summary>
public class MaidApparition : MonoBehaviour
{
    public enum SpotId { Bag, Glass }

    [Serializable]
    public class Spot
    {
        [Tooltip("Bag: not used - she stays where the MaidHolder instance stands.\n" +
                 "Glass: where she appears (X/Z) and the direction she faces (blue arrow). Her height is not changed.")]
        public Transform anchor;

        [Tooltip("Bag: fallback hand target if the body bag has no collider.\n" +
                 "Glass: a point on the glass surface - the hands are pressed to the plane through this point.")]
        public Transform leftHand;

        [Tooltip("Same as the left hand (only the plane / fallback is used).")]
        public Transform rightHand;

        [Tooltip("Not used any more - see Bag Lean / Glass Lean on the component.")]
        [Range(0f, 50f)] public float spineLean = 25f;

        [Tooltip("Seconds after she appears before the hands start moving to the surface.")]
        [Min(0f)] public float handsDelay = 0.3f;

        [Tooltip("Seconds the hands need to reach the surface.")]
        [Min(0.05f)] public float handsReachTime = 0.7f;

        [Tooltip("Look at the player right from the moment she appears.")]
        public bool lookAtPlayerOnShow;

        [Header("Hand fine tuning (metres, in her own frame: X = her right, Y = up, Z = forward)")]
        [Tooltip("Moves the contact point of the LEFT hand. Use Preview in the inspector to see the result.")]
        public Vector3 leftHandOffset;

        [Tooltip("Moves the contact point of the RIGHT hand.")]
        public Vector3 rightHandOffset;
    }

    private struct HandGoal
    {
        public Vector3 position;
        public Vector3 fingers;
        public bool valid;
    }

    [Header("Maid")]
    [Tooltip("Root of the MaidHolder instance. Its position, rotation and scale in the prefab are used as she is.")]
    [SerializeField] private Transform _maidRoot;

    [Header("Spots")]
    [SerializeField] private Spot _bagSpot = new Spot();
    [SerializeField] private Spot _glassSpot = new Spot { spineLean = 18f, handsDelay = 0.15f, handsReachTime = 0.35f, lookAtPlayerOnShow = true };

    [Tooltip("Glass spot: how far her shoulders stand from the glass, as a part of her arm length (smaller = closer).")]
    [SerializeField, Range(0.3f, 1.2f)] private float _glassDistance = 0.7f;

    [Header("Pose")]
    [Tooltip("Forward lean over the body bag, degrees. This is the minimum: she leans more if the hands would not reach the bag.")]
    [SerializeField, Range(0f, 50f)] private float _bagLean = 22f;

    [Tooltip("Forward lean at the glass, degrees (she leans on it). Also a minimum.")]
    [SerializeField, Range(0f, 60f)] private float _glassLean = 30f;

    [Tooltip("Largest lean she may use to reach the surface.")]
    [SerializeField, Range(30f, 75f)] private float _maxLean = 60f;

    [Tooltip("How far each hand is placed to the side of its shoulder, in arm lengths (0 = straight ahead, 0.5 = arms clearly spread).")]
    [SerializeField, Range(0f, 1f)] private float _handSpread = 0.5f;

    [Tooltip("Start pose: how far the relaxed arms hang away from the body (0 = glued to the body, 0.2 = slightly apart).")]
    [SerializeField, Range(0f, 0.5f)] private float _armsAway = 0.12f;

    [Tooltip("Start pose: how far the forearms are bent forward (0 = straight down).")]
    [SerializeField, Range(0f, 0.8f)] private float _forearmForward = 0.22f;

    [Tooltip("How deep the palms press into the body bag, metres (the bag is soft, so the hands do not hover).")]
    [SerializeField, Range(0f, 0.1f)] private float _bagPress = 0.04f;

    [Header("Look")]
    [Tooltip("What she looks at. Empty = the player camera.")]
    [SerializeField] private Transform _lookTarget;

    [Tooltip("Seconds the head needs to turn to the player (smooth time).")]
    [SerializeField, Min(0.05f)] private float _headTurnTime = 0.4f;

    [Tooltip("Largest horizontal turn of the head + neck + upper spine, in degrees.")]
    [SerializeField, Range(30f, 180f)] private float _maxLookYaw = 175f;

    [Header("Scream")]
    [Tooltip("How far the head is thrown back while screaming, degrees.")]
    [SerializeField, Range(0f, 60f)] private float _screamHeadBack = 38f;

    [Tooltip("Shake of the head and hands while screaming, degrees.")]
    [SerializeField, Range(0f, 15f)] private float _screamShake = 6f;

    // bones
    private Transform _spine, _spine1, _spine2, _neck, _head;
    private Transform _lArm, _lFore, _lHand, _rArm, _rFore, _rHand;
    private Transform _toeL, _footL, _toeR, _footR, _shoulderL, _shoulderR;

    private readonly Dictionary<Transform, Quaternion> _restLocalRotation = new Dictionary<Transform, Quaternion>();
    private Animator _animator;
    private Vector3 _placedLocalPosition;
    private Quaternion _placedLocalRotation;

    private bool _visible;
    private Spot _spot;
    private Vector3 _facing = Vector3.forward;
    private HandGoal _leftGoal, _rightGoal;
    private float _time;
    private float _lean;
    private bool _lookOn;
    private float _headYaw, _headYawVelocity;
    private float _lookWeight;
    private float _screamTime = -1f;
    private float _screamDuration;
    private Transform _cameraTransform;

    public bool IsVisible => _visible;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private bool _initialized;

    /// <summary>Finds the bones and remembers the pose saved in the prefab (the one seen in the editor).</summary>
    private void EnsureInitialized()
    {
        if (_initialized || _maidRoot == null) return;
        _initialized = true;

        _animator = _maidRoot.GetComponentInChildren<Animator>(true);

        FindBones();
        _restLocalRotation.Clear();
        foreach (Transform bone in new[] { _spine, _spine1, _spine2, _neck, _head, _lArm, _lFore, _lHand, _rArm, _rFore, _rHand })
            if (bone != null) _restLocalRotation[bone] = bone.localRotation;

        _placedLocalPosition = _maidRoot.localPosition;
        _placedLocalRotation = _maidRoot.localRotation;
    }

    private void Awake()
    {
        if (_maidRoot == null) return;

        EnsureInitialized();

        // No clip: she keeps the pose saved in the prefab.
        if (_animator != null) _animator.enabled = false;

        _maidRoot.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        // Also called on script reload in the editor - never hide her in the prefab then.
        if (!Application.isPlaying) return;

        _visible = false;
        if (_maidRoot != null) _maidRoot.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!_visible || _spot == null) return;

        // Start from the rest pose every frame - the animator is off and nothing resets the bones.
        RestorePose();

        _time += Time.deltaTime;
        ApplyPose(Time.deltaTime);
    }

    /// <summary>Applies the lean, the look, the arms IK and the scream to the (already restored) rest pose.</summary>
    private void ApplyPose(float dt)
    {
        Vector3 facing = _facing;
        Vector3 right = Vector3.Cross(Vector3.up, facing);
        float screamEnv = ScreamEnvelope();

        // 1. Lean forward (the spine bones carry the arms and head with them)
        float lean = _lean * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_time / 0.6f)) - screamEnv * 12f;
        ApplyLean(lean, right);

        // 2. Look at the player: the upper spine takes part of the turn, so the head does not twist off
        Transform target = ResolveLookTarget();
        float yawGoal = 0f;
        float pitchGoal = 0f;
        if (_lookOn && target != null && _head != null)
        {
            Vector3 to = target.position - _head.position;
            Vector3 flat = Vector3.ProjectOnPlane(to, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f)
            {
                yawGoal = Mathf.Clamp(Vector3.SignedAngle(facing, flat, Vector3.up), -_maxLookYaw, _maxLookYaw);
                pitchGoal = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg, -35f, 35f);
            }
        }
        _headYaw = Mathf.SmoothDampAngle(_headYaw, _lookOn ? yawGoal : 0f, ref _headYawVelocity, _headTurnTime, Mathf.Infinity, dt);
        _lookWeight = Mathf.MoveTowards(_lookWeight, _lookOn ? 1f : 0f, dt / _headTurnTime);

        Yaw(_spine, _headYaw * 0.08f);
        Yaw(_spine1, _headYaw * 0.10f);
        Yaw(_spine2, _headYaw * 0.12f);

        // 3. Start pose: relaxed, arms hanging down (the pose saved in the prefab is a T-pose)
        RelaxArms(facing, right);

        // Arms: the hands go from the relaxed pose to the surface
        float armWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_time - _spot.handsDelay) / _spot.handsReachTime));
        Vector3 tremble = screamEnv > 0f ? UnityEngine.Random.insideUnitSphere * (0.004f * screamEnv) : Vector3.zero;
        SolveArm(_lArm, _lFore, _lHand, _leftGoal, armWeight, -1f, facing, right, tremble);
        SolveArm(_rArm, _rFore, _rHand, _rightGoal, armWeight, 1f, facing, right, -tremble);

        // 4. Neck and head (after the arms, so they do not disturb the IK)
        Yaw(_neck, _headYaw * 0.25f);
        Yaw(_head, _headYaw * 0.45f);

        Vector3 headRight = Vector3.Cross(Vector3.up, Quaternion.AngleAxis(_headYaw, Vector3.up) * facing);
        float pitch = pitchGoal * 0.7f * _lookWeight;
        if (screamEnv > 0f)
        {
            pitch = Mathf.Lerp(pitch, -_screamHeadBack, screamEnv);

            float n = Time.time * 38f;
            float shakeYaw = (Mathf.PerlinNoise(n, 0.3f) - 0.5f) * 2f * _screamShake * screamEnv;
            float shakeRoll = (Mathf.PerlinNoise(0.7f, n) - 0.5f) * 2f * _screamShake * screamEnv;
            Yaw(_head, shakeYaw);
            Roll(_head, shakeRoll, Quaternion.AngleAxis(_headYaw, Vector3.up) * facing);
        }
        Lean(_head, pitch, headRight);
        Lean(_neck, pitch * 0.3f, headRight);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the maid at the given spot (she snaps there, the light blink hides the pop).
    /// <paramref name="surfaceObject"/> is the object the hands are put on (the body bag for the Bag spot).
    /// </summary>
    public void Show(SpotId id, GameObject surfaceObject = null)
    {
        if (_maidRoot == null) return;
        EnsureInitialized();

        _spot = id == SpotId.Bag ? _bagSpot : _glassSpot;
        if (_spot == null) return;

        _maidRoot.gameObject.SetActive(true);
        RestorePose();

        // Back to the pose she was placed in
        _maidRoot.localPosition = _placedLocalPosition;
        _maidRoot.localRotation = _placedLocalRotation;
        _facing = MeasureFacing();

        if (id == SpotId.Glass)
            PlaceAtGlass(_spot);

        Collider collider = surfaceObject != null ? surfaceObject.GetComponentInChildren<Collider>() : null;
        Renderer surfaceRenderer = surfaceObject != null ? surfaceObject.GetComponentInChildren<Renderer>() : null;
        ComputeHandGoals(id, _spot, collider, surfaceRenderer);

        // Lean as little as the pose asks for, but as much as needed to really reach the surface
        _lean = FindLean(id == SpotId.Bag ? _bagLean : _glassLean);

        _time = 0f;
        _headYaw = 0f;
        _headYawVelocity = 0f;
        _lookWeight = 0f;
        _lookOn = _spot.lookAtPlayerOnShow;
        _screamTime = -1f;
        _visible = true;
    }

    /// <summary>Hides the maid instantly (the cutscene calls this in the dark gap of a blink).</summary>
    public void Hide()
    {
        _visible = false;
        _screamTime = -1f;
        if (_maidRoot != null) _maidRoot.gameObject.SetActive(false);
    }

    /// <summary>Turns her head to the player (true) or back (false).</summary>
    public void LookAtPlayer(bool on) => _lookOn = on;

    /// <summary>Throws the head back and shakes for the given time.</summary>
    public void Scream(float duration)
    {
        _screamTime = 0f;
        _screamDuration = Mathf.Max(0.2f, duration);
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    /// <summary>Moves her to the glass anchor (X/Z only, her height is kept), faces her along the anchor and steps her to the glass.</summary>
    private void PlaceAtGlass(Spot spot)
    {
        if (spot.anchor == null)
        {
            Debug.LogWarning("[MaidApparition] Glass spot has no anchor - she stays at the bag.", this);
            return;
        }

        Vector3 wanted = Vector3.ProjectOnPlane(spot.anchor.forward, Vector3.up);
        if (wanted.sqrMagnitude < 1e-6f) wanted = Vector3.forward;
        wanted.Normalize();

        float turn = Vector3.SignedAngle(_facing, wanted, Vector3.up);
        _maidRoot.rotation = Quaternion.AngleAxis(turn, Vector3.up) * _maidRoot.rotation;
        _facing = wanted;

        Vector3 pos = _maidRoot.position;
        pos.x = spot.anchor.position.x;
        pos.z = spot.anchor.position.z;
        _maidRoot.position = pos;

        // Step to the right distance from the glass plane, so the arms really reach it
        if (spot.leftHand != null && _lArm != null && _rArm != null)
        {
            Vector3 shoulders = (_lArm.position + _rArm.position) * 0.5f;
            float distance = Vector3.Dot(spot.leftHand.position - shoulders, _facing);
            _maidRoot.position += _facing * (distance - ArmLength() * _glassDistance);
        }
    }

    private void ComputeHandGoals(SpotId id, Spot spot, Collider surface, Renderer surfaceRenderer)
    {
        float armLength = ArmLength();
        _leftGoal = BuildGoal(id, spot, surface, surfaceRenderer, _lArm, spot.leftHand, -1f, armLength);
        _rightGoal = BuildGoal(id, spot, surface, surfaceRenderer, _rArm, spot.rightHand, 1f, armLength);
    }

    private HandGoal BuildGoal(SpotId id, Spot spot, Collider surface, Renderer surfaceRenderer,
                               Transform arm, Transform fallback, float side, float armLength)
    {
        var goal = new HandGoal();
        if (arm == null) return goal;

        Vector3 right = Vector3.Cross(Vector3.up, _facing);
        // Each hand is placed to the side of its shoulder: the arms are spread, not parallel
        Vector3 spread = right * (side * armLength * _handSpread);

        if (id == SpotId.Bag)
        {
            // Fingers point forward and a little outwards
            goal.fingers = (_facing + right * (side * 0.5f)).normalized;

            // The Bag_LeftHand / Bag_RightHand targets placed in the prefab are exact: the palm goes
            // right there (fingers along the target's up axis). The bag collider is only a fallback.
            Vector3 p = arm.position + _facing * (armLength * 0.55f) + spread;
            if (fallback != null)
            {
                goal.position = fallback.position;
                goal.fingers = fallback.up;
                goal.valid = true;
            }
            else if (surface != null)
            {
                Vector3 hitPoint;
                if (!DropOnto(surface, p, out hitPoint))
                {
                    // Not above the bag: take the nearest point of the bag and move a bit towards its middle
                    Vector3 q = Vector3.MoveTowards(surface.ClosestPoint(p), surface.bounds.center, 0.12f);
                    if (!DropOnto(surface, q, out hitPoint))
                        hitPoint = surface.ClosestPoint(p);
                }

                // The collider can be taller than the visible bag: never go above the mesh top
                if (surfaceRenderer != null)
                    hitPoint.y = Mathf.Min(hitPoint.y, surfaceRenderer.bounds.max.y);

                // The bag is soft: the palms press into it a little
                goal.position = hitPoint + Vector3.down * _bagPress;
                goal.valid = true;
            }
            else if (fallback != null)
            {
                goal.position = fallback.position;
                goal.fingers = fallback.up;
                goal.valid = true;
            }
        }
        else
        {
            // Fingers up and slightly outwards, like leaning on the glass with both hands
            goal.fingers = (Vector3.up + right * (side * 0.35f)).normalized;

            // At shoulder height (a little lower), spread apart, pressed to the glass plane
            Vector3 planePoint = fallback != null ? fallback.position : arm.position + _facing * armLength;
            Vector3 p = arm.position + spread;
            p += _facing * Vector3.Dot(planePoint - p, _facing);
            p.y -= armLength * 0.15f;
            goal.position = p;
            goal.valid = true;
        }

        if (goal.valid)
        {
            Vector3 offset = side < 0f ? spot.leftHandOffset : spot.rightHandOffset;
            goal.position += right * offset.x + Vector3.up * offset.y + _facing * offset.z;
        }
        return goal;
    }

    /// <summary>Smallest lean (not below <paramref name="baseLean"/>) at which both hands reach their goals.</summary>
    private float FindLean(float baseLean)
    {
        Vector3 right = Vector3.Cross(Vector3.up, _facing);
        float reach = ArmLength() * 0.97f;
        float lean = baseLean;

        for (; lean < _maxLean; lean += 2f)
        {
            RestorePose();
            ApplyLean(lean, right);
            if (InReach(_lArm, _leftGoal, reach) && InReach(_rArm, _rightGoal, reach))
                break;
        }

        RestorePose();
        return Mathf.Min(lean, _maxLean);
    }

    private static bool InReach(Transform arm, HandGoal goal, float reach) =>
        arm == null || !goal.valid || (goal.position - arm.position).magnitude <= reach;

    private void ApplyLean(float degrees, Vector3 right)
    {
        Lean(_spine, degrees * 0.25f, right);
        Lean(_spine1, degrees * 0.35f, right);
        Lean(_spine2, degrees * 0.40f, right);
    }

    // ── Relaxed start pose ────────────────────────────────────────────────────

    /// <summary>
    /// Turns the T-pose arms into a relaxed standing pose: the upper arms hang down (a little away from the body),
    /// the forearms are slightly bent forward, the hands hang loose. Works in world directions, so it also
    /// holds when the torso is leaning.
    /// </summary>
    private void RelaxArms(Vector3 facing, Vector3 right)
    {
        RelaxArm(_lArm, _lFore, _lHand, -1f, facing, right);
        RelaxArm(_rArm, _rFore, _rHand, 1f, facing, right);
    }

    private void RelaxArm(Transform upper, Transform lower, Transform hand, float side, Vector3 facing, Vector3 right)
    {
        if (upper == null || lower == null || hand == null) return;

        Vector3 upperDir = (Vector3.down + right * (side * _armsAway) + facing * 0.04f).normalized;
        AimBone(upper, lower.position - upper.position, upperDir);

        Vector3 foreDir = (Vector3.down + facing * _forearmForward + right * (side * _armsAway * 0.4f)).normalized;
        AimBone(lower, hand.position - lower.position, foreDir);

        // The hand hangs loose, continuing the forearm with a slight droop forward
        Vector3 handDir = (Vector3.down + facing * (_forearmForward * 0.8f)).normalized;
        AimBone(hand, hand.up, handDir);
    }

    private static void AimBone(Transform bone, Vector3 currentDirection, Vector3 wantedDirection)
    {
        if (currentDirection.sqrMagnitude < 1e-10f) return;
        bone.rotation = Quaternion.FromToRotation(currentDirection, wantedDirection) * bone.rotation;
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>Casts a ray straight down from above the point; returns the top surface of the collider there.</summary>
    private static bool DropOnto(Collider surface, Vector3 point, out Vector3 hitPoint)
    {
        Bounds b = surface.bounds;
        var ray = new Ray(new Vector3(point.x, b.max.y + 1f, point.z), Vector3.down);
        if (surface.Raycast(ray, out RaycastHit hit, b.size.y + 2f))
        {
            hitPoint = hit.point;
            return true;
        }
        hitPoint = point;
        return false;
    }

    private void FindBones()
    {
        var map = new Dictionary<string, Transform>();
        foreach (Transform t in _maidRoot.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int colon = n.IndexOf(':');
            if (colon >= 0) n = n.Substring(colon + 1);
            if (!map.ContainsKey(n)) map[n] = t;
        }

        Transform Get(string n)
        {
            if (map.TryGetValue(n, out Transform t)) return t;
            Debug.LogWarning($"[MaidApparition] Bone '{n}' not found under '{_maidRoot.name}'.", this);
            return null;
        }

        _spine = Get("Spine"); _spine1 = Get("Spine1"); _spine2 = Get("Spine2");
        _neck = Get("Neck"); _head = Get("Head");
        _lArm = Get("LeftArm"); _lFore = Get("LeftForeArm"); _lHand = Get("LeftHand");
        _rArm = Get("RightArm"); _rFore = Get("RightForeArm"); _rHand = Get("RightHand");

        _toeL = Get("LeftToeBase"); _footL = Get("LeftFoot");
        _toeR = Get("RightToeBase"); _footR = Get("RightFoot");
        _shoulderL = Get("LeftShoulder"); _shoulderR = Get("RightShoulder");
    }

    private void RestorePose()
    {
        foreach (var pair in _restLocalRotation)
            if (pair.Key != null) pair.Key.localRotation = pair.Value;
    }

    /// <summary>Horizontal direction the maid faces (from the feet; fallback: from the shoulders).</summary>
    private Vector3 MeasureFacing()
    {
        Vector3 forward = Vector3.zero;
        if (_toeL != null && _footL != null) forward += _toeL.position - _footL.position;
        if (_toeR != null && _footR != null) forward += _toeR.position - _footR.position;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);

        if (forward.sqrMagnitude < 1e-6f && _shoulderL != null && _shoulderR != null)
        {
            Vector3 rightDir = _shoulderR.position - _shoulderL.position;
            forward = Vector3.Cross(rightDir, Vector3.up);
        }
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.ProjectOnPlane(_maidRoot.forward, Vector3.up);

        return forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
    }

    private float ArmLength()
    {
        if (_lArm == null || _lFore == null || _lHand == null) return 0.6f;
        return (_lFore.position - _lArm.position).magnitude + (_lHand.position - _lFore.position).magnitude;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Transform ResolveLookTarget()
    {
        if (_lookTarget != null) return _lookTarget;

        if (_cameraTransform == null)
        {
            var fps = FindFirstObjectByType<FPSController>();
            if (fps != null && fps.CameraTransform != null) _cameraTransform = fps.CameraTransform;
            else if (Camera.main != null) _cameraTransform = Camera.main.transform;
        }
        return _cameraTransform;
    }

    private float ScreamEnvelope()
    {
        if (_screamTime < 0f) return 0f;

        _screamTime += Time.deltaTime;
        if (_screamTime >= _screamDuration + 0.2f)
        {
            _screamTime = -1f;
            return 0f;
        }

        float attack = Mathf.Clamp01(_screamTime / 0.08f);
        float release = Mathf.Clamp01((_screamDuration + 0.2f - _screamTime) / 0.2f);
        return Mathf.Min(attack, release);
    }

    private static void Lean(Transform bone, float degrees, Vector3 axis)
    {
        if (bone != null && Mathf.Abs(degrees) > 0.001f)
            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    private static void Yaw(Transform bone, float degrees)
    {
        if (bone != null && Mathf.Abs(degrees) > 0.001f)
            bone.rotation = Quaternion.AngleAxis(degrees, Vector3.up) * bone.rotation;
    }

    private static void Roll(Transform bone, float degrees, Vector3 forward)
    {
        if (bone != null && Mathf.Abs(degrees) > 0.001f)
            bone.rotation = Quaternion.AngleAxis(degrees, forward) * bone.rotation;
    }

    /// <summary>Two-bone IK: the hand moves from its current position towards the goal by <paramref name="weight"/>.</summary>
    private static void SolveArm(Transform upper, Transform lower, Transform end, HandGoal goal,
                                 float weight, float side, Vector3 facing, Vector3 right, Vector3 offset)
    {
        if (upper == null || lower == null || end == null || !goal.valid || weight <= 0.001f)
            return;

        Vector3 a = upper.position;
        Vector3 b = lower.position;
        Vector3 c = end.position;

        Vector3 target = Vector3.Lerp(c, goal.position + offset, weight);
        // The elbow points down, out to the side and a little back
        Vector3 pole = a + right * (side * 0.8f) + Vector3.down * 0.4f - facing * 0.2f;

        float la = (b - a).magnitude;
        float lb = (c - b).magnitude;
        if (la < 1e-4f || lb < 1e-4f) return;

        float dist = Mathf.Clamp((target - a).magnitude, Mathf.Abs(la - lb) + 0.01f * la, la + lb - 0.005f * la);

        // Bend the elbow to the angle that gives the needed reach
        Vector3 u = b - a;
        Vector3 v = c - b;
        float currentBend = Vector3.Angle(u, v);
        float cosInterior = (la * la + lb * lb - dist * dist) / (2f * la * lb);
        float interior = Mathf.Acos(Mathf.Clamp(cosInterior, -1f, 1f)) * Mathf.Rad2Deg;
        float wantedBend = 180f - interior;

        Vector3 bendAxis = Vector3.Cross(u, v);
        if (bendAxis.sqrMagnitude < 1e-8f)
            bendAxis = Vector3.Cross(u, pole - a);
        if (bendAxis.sqrMagnitude > 1e-10f)
            lower.rotation = Quaternion.AngleAxis(wantedBend - currentBend, bendAxis.normalized) * lower.rotation;

        // Aim the whole arm at the target
        Vector3 toEnd = end.position - a;
        Vector3 toTarget = target - a;
        if (toEnd.sqrMagnitude > 1e-10f && toTarget.sqrMagnitude > 1e-10f)
            upper.rotation = Quaternion.FromToRotation(toEnd, toTarget) * upper.rotation;

        // Swing the elbow towards the pole around the shoulder-target axis
        Vector3 axis = toTarget.normalized;
        Vector3 elbowDir = Vector3.ProjectOnPlane(lower.position - a, axis);
        Vector3 poleDir = Vector3.ProjectOnPlane(pole - a, axis);
        if (elbowDir.sqrMagnitude > 1e-8f && poleDir.sqrMagnitude > 1e-8f)
        {
            float twist = Vector3.SignedAngle(elbowDir, poleDir, axis);
            upper.rotation = Quaternion.AngleAxis(twist, axis) * upper.rotation;
        }

        // Fingers point along the goal direction
        if (goal.fingers.sqrMagnitude > 1e-6f)
            end.rotation = Quaternion.Slerp(end.rotation,
                Quaternion.FromToRotation(end.up, goal.fingers.normalized) * end.rotation, weight);
    }

#if UNITY_EDITOR
    // ── Editor preview (edit mode, no Play needed) ────────────────────────────

    /// <summary>All maids that are currently posed by the editor preview.</summary>
    public static readonly List<MaidApparition> EditorPreviews = new List<MaidApparition>();

    private bool _previewing;
    private bool _previewActiveBefore;
    private SpotId _previewSpot;
    private bool _previewStart;

    public bool IsEditorPreviewing => _previewing;
    public SpotId EditorPreviewSpot => _previewSpot;
    public bool EditorPreviewStart => _previewStart;

    /// <summary>
    /// Poses the maid in the final pose of the given spot (hands on the surface), as at the end of the cutscene
    /// movement. Everything is reverted by <see cref="EditorResetPreview"/>.
    /// While the Bag preview is on, moving the MaidHolder instance moves her "placed" position.
    /// </summary>
    public void EditorPreview(SpotId id, bool startPose = false)
    {
        if (_maidRoot == null || Application.isPlaying) return;

        if (!_previewing)
        {
            EnsureInitialized();
            _previewActiveBefore = _maidRoot.gameObject.activeSelf;
            _placedLocalPosition = _maidRoot.localPosition;
            _placedLocalRotation = _maidRoot.localRotation;
            _previewing = true;
            if (!EditorPreviews.Contains(this)) EditorPreviews.Add(this);
        }
        else if (_previewSpot == SpotId.Bag)
        {
            // She may have been moved by hand while the bag preview was on
            _placedLocalPosition = _maidRoot.localPosition;
            _placedLocalRotation = _maidRoot.localRotation;
        }

        _previewSpot = id;
        _previewStart = startPose;
        Physics.SyncTransforms();

        var cutscene = GetComponentInParent<MriUvCutscene>();
        Show(id, cutscene != null ? cutscene.BodyBag : null);

        // Final pose (hands on the surface, leaned) or the start pose (relaxed, nothing leaned yet); no head turn
        _time = startPose ? 0f : 100f;
        _lookOn = false;
        _lookWeight = 0f;
        _headYaw = 0f;
        RestorePose();
        ApplyPose(0f);
    }

    /// <summary>Puts the maid back exactly as she was before the preview.</summary>
    public void EditorResetPreview()
    {
        if (!_previewing) return;
        _previewing = false;
        _visible = false;
        EditorPreviews.Remove(this);

        if (_maidRoot == null) return;
        RestorePose();
        _maidRoot.localPosition = _placedLocalPosition;
        _maidRoot.localRotation = _placedLocalRotation;
        _maidRoot.gameObject.SetActive(_previewActiveBefore);
    }

    /// <summary>Distance in metres between each hand bone and its target (0 = the palm is exactly on the surface point).</summary>
    public void EditorHandGaps(out float left, out float right)
    {
        left = _leftGoal.valid && _lHand != null ? Vector3.Distance(_lHand.position, _leftGoal.position) : -1f;
        right = _rightGoal.valid && _rHand != null ? Vector3.Distance(_rHand.position, _rightGoal.position) : -1f;
    }

    /// <summary>Lean in degrees chosen for the previewed spot (grows if the hands would not reach).</summary>
    public float EditorLean => _lean;

    private void OnDrawGizmos()
    {
        if (!_previewing) return;

        DrawGoal(_leftGoal, _lHand, new Color(1f, 0.9f, 0.2f));
        DrawGoal(_rightGoal, _rHand, new Color(1f, 0.3f, 1f));
    }

    private static void DrawGoal(HandGoal goal, Transform hand, Color color)
    {
        if (!goal.valid) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(goal.position, 0.03f);
        Gizmos.DrawRay(goal.position, goal.fingers.normalized * 0.12f);
        if (hand != null) Gizmos.DrawLine(hand.position, goal.position);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (_glassSpot == null) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f);

        if (_glassSpot.anchor != null)
        {
            Gizmos.DrawWireSphere(_glassSpot.anchor.position, 0.12f);
            Gizmos.DrawRay(_glassSpot.anchor.position, Vector3.ProjectOnPlane(_glassSpot.anchor.forward, Vector3.up).normalized * 0.6f);
        }
        if (_glassSpot.leftHand != null) Gizmos.DrawWireSphere(_glassSpot.leftHand.position, 0.05f);
        if (_glassSpot.rightHand != null) Gizmos.DrawWireSphere(_glassSpot.rightHand.position, 0.05f);
    }
}
