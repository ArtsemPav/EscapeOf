using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// End-of-game cinematic for the final door puzzle.
/// When FinalDoorPuzzleController.OnSolved fires (after the pour cinematic
/// returned the camera to the player):
///   1. waits for the solved-state fade to clear the screen;
///   2. disables player input — control is never returned;
///   3. plays the DoorOpen state on the puzzle Animator;
///   4. walks the player slowly through the door;
///   5. as soon as the player passes the doorway, fades to black
///      and shows the credits canvas (placeholder text for now).
/// </summary>
public class EndGameSequence : MonoBehaviour
{
    [Header("Controller")]
    [Tooltip("FinalDoorPuzzleController whose OnSolved starts this sequence. Auto-found on the same GameObject if empty.")]
    [SerializeField] private FinalDoorPuzzleController _controller;

    [Header("Door Animation")]
    [Tooltip("Animator on the FinalDoorPuzzle root that has the DoorOpen state. Auto-found if empty.")]
    [SerializeField] private Animator _doorAnimator;

    [Tooltip("Name of the door-open animation state.")]
    [SerializeField] private string _doorOpenStateName = "doorOpen";

    [Tooltip("Fallback wait (seconds) when the animator or state is unavailable.")]
    [SerializeField, Min(0.5f)] private float _doorOpenFallbackDuration = 4.5f;

    [Header("Walk Through The Door")]
    [Tooltip("Optional explicit target the player walks to. If empty, the target is " +
             "computed automatically: door center + walking direction (player → door) " +
             "extended past the door by WalkDistanceBeyondDoor.")]
    [SerializeField] private Transform _walkTarget;

    [Tooltip("Door center transform used to compute the automatic walk target.")]
    [SerializeField] private Transform _doorCenter;

    [Tooltip("How far beyond the door the player keeps walking (automatic target only).")]
    [SerializeField, Min(0.5f)] private float _walkDistanceBeyondDoor = 2.5f;

    [Header("Start Position")]
    [Tooltip("Point the player is snapped to when the ending takes over — players who " +
             "entered the puzzle from the side of the skull would break the gaze " +
             "choreography otherwise. Only the horizontal position is used; the " +
             "player's height is preserved so there is no visible jump.")]
    [SerializeField] private Transform _playerStartPoint;

    [Header("Camera Focus Point")]
    [Tooltip("Point the camera slowly turns toward while the player is forced to look at it. " +
             "Applied from the moment input is disabled until the final fade completes.")]
    [SerializeField] private Transform _lookPoint;

    [Header("Gaze Choreography")]
    [Tooltip("Death statue the player slowly turns to look at after the door animation.")]
    [SerializeField] private Transform _deathPoint;

    [Tooltip("Angel statue looked at after Death.")]
    [SerializeField] private Transform _angelPoint;

    [Tooltip("Pause (seconds) after the door animation before the gaze choreography starts.")]
    [SerializeField, Min(0f)] private float _gazeStartDelay = 1f;

    [Tooltip("How long (seconds) the player holds the gaze on each target before moving on.")]
    [SerializeField, Min(0.1f)] private float _gazeHoldTime = 1.2f;

    [Tooltip("Angle at which the gaze is considered settled on the target (deg).")]
    [SerializeField, Min(0.5f)] private float _gazeArrivalAngle = 3f;

    [Tooltip("Safety timeout for a single gaze stage (seconds).")]
    [SerializeField, Min(1f)] private float _gazeTimeout = 8f;

    [Tooltip("How fast the camera turns to look at the focus point (deg/s).")]
    [SerializeField, Min(1f)] private float _cameraTurnSpeed = 120f;

    [Tooltip("Angular acceleration of the aim (deg/s²) — the turn ramps up " +
             "smoothly instead of snapping to full speed.")]
    [SerializeField, Min(1f)] private float _aimAcceleration = 240f;

    [Tooltip("Angle at which the aim starts easing down so it settles onto " +
             "the point instead of overshooting with a snap.")]
    [SerializeField, Min(1f)] private float _aimSettleAngle = 45f;

    [Tooltip("Horizontal distance to the focus point at which the aim stops " +
             "updating. The point usually sits below eye level, so without a " +
             "hold distance the view would tilt down into the floor on arrival.")]
    [SerializeField, Min(0f)] private float _lookHoldDistance = 1.5f;

    [Tooltip("If the player has not progressed this long (blocked by geometry), " +
             "the walk gives up and the ending fades out anyway.")]
    [SerializeField, Min(0.5f)] private float _maxStallTime = 2.5f;

    [Tooltip("Walking speed of the scripted player (m/s).")]
    [SerializeField, Min(0.1f)] private float _walkSpeed = 0.8f;

    [Tooltip("How fast the player turns to face the walking direction (deg/s).")]
    [SerializeField, Min(0.5f)] private float _turnSpeed = 180f;

    [Tooltip("Walking speed for the final approach — slow, heavy, guilty steps. " +
             "Used instead of WalkSpeed when a look point is assigned.")]
    [SerializeField, Min(0.1f)] private float _guiltWalkSpeed = 0.4f;

    [Tooltip("How long (seconds) the player stands still after looking at Death " +
             "and Angel — taking in the guilt — before walking on.")]
    [SerializeField, Min(0f)] private float _guiltPauseDuration = 2f;

    [Tooltip("Distance to the target at which the player is considered to have passed the door.")]
    [SerializeField, Min(0.05f)] private float _arrivalThreshold = 0.2f;

    [Tooltip("Safety timeout — the ending forces forward even if the walk is blocked.")]
    [SerializeField, Min(2f)] private float _maxWalkDuration = 20f;

    [Header("Fade & Credits")]
    [Tooltip("Delay after OnSolved before the sequence takes over (lets the " +
             "puzzle exit fade finish so the player sees the door).")]
    [SerializeField, Min(0f)] private float _startDelay = 2f;

    [Tooltip("How many seconds before the walk ends the final fade starts. " +
             "Higher values darken earlier over the last steps.")]
    [SerializeField, Min(0.1f)] private float _earlyFadeLead = 5f;

    [Tooltip("Duration of the final fade to black.")]
    [SerializeField, Min(0.1f)] private float _fadeDuration = 2.5f;

    [Tooltip("Credits canvas. Auto-found by type (EndGameCreditsView) if empty.")]
    [SerializeField] private EndGameCreditsView _creditsView;

    [Header("Debug")]
    [SerializeField] private bool _debugLogging;

    private CharacterController _characterController;
    private bool _isPlaying;
    private bool _walkFinished;

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponent<FinalDoorPuzzleController>();

        if (_doorAnimator == null)
            _doorAnimator = GetComponent<Animator>();

        if (_creditsView == null)
            _creditsView = FindFirstObjectByType<EndGameCreditsView>(FindObjectsInactive.Include);

        _characterController = FindFirstObjectByType<CharacterController>();
    }

    private void OnEnable()
    {
        if (_controller != null)
            _controller.OnSolved += HandleSolved;
    }

    private void OnDisable()
    {
        if (_controller != null)
            _controller.OnSolved -= HandleSolved;
    }

    private void HandleSolved()
    {
        if (_isPlaying) return;
        _isPlaying = true;
        StartCoroutine(SequenceRoutine());
    }

    private IEnumerator SequenceRoutine()
    {
        // 1. Take everything over the moment the puzzle is solved. OnSolved
        // fires while the screen is still black from the pour cinematic's
        // fade — so the player snap and the orientation onto the look point
        // are invisible: when the fade reveals the scene, the player already
        // stands at the start point looking at it.
        InputManager.Instance?.SetPlayerInputEnabled(false);
        InputManager.Instance?.SetUIInputEnabled(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        var fps = _characterController != null
            ? _characterController.GetComponent<FPSController>()
            : null;
        fps?.SetCinematicControl(true);

        SnapPlayerToStart();
        OrientCameraToLookPoint();

        // 2. Let the puzzle-exit fade finish so the player sees the scene.
        yield return new WaitForSeconds(_startDelay);

        if (_debugLogging)
            Debug.Log("[EndGameSequence] Input disabled, playing door open animation.");

        // 3. Play the door-open animation.
        bool doorOpened = false;
        if (_doorAnimator != null && !string.IsNullOrEmpty(_doorOpenStateName))
        {
            _doorAnimator.Play(_doorOpenStateName, 0, 0f);
            yield return WaitForStateFinish(_doorAnimator, _doorOpenStateName);
            doorOpened = true;
        }

        if (!doorOpened)
            yield return new WaitForSeconds(_doorOpenFallbackDuration);

        // 4. Gaze choreography: the view is already on the look point (set
        // instantly at solve time); pause a beat, then look around — Death,
        // then Angel. Afterwards the player stands still for a moment,
        // taking in the guilt, before walking on.
        yield return new WaitForSeconds(_gazeStartDelay);
        yield return GazeAtRoutine(_deathPoint);
        yield return GazeAtRoutine(_angelPoint);

        yield return new WaitForSeconds(_guiltPauseDuration);

        // 5. Walk the player through the door toward the look point while the
        //    view stays locked on it (movement changes the direction, so the
        //    aim keeps correcting). The final fade starts a second BEFORE the
        //    walk ends, so the screen darkens over the last steps.
        Coroutine aimRoutine = _lookPoint != null && _characterController != null
            ? StartCoroutine(AimRoutine())
            : null;

        _walkFinished = false;
        Coroutine earlyFade = StartCoroutine(WalkWithEarlyFadeRoutine(aimRoutine));

        yield return WalkThroughDoorRoutine();
        _walkFinished = true;

        // 6. The walk is done — the fade already covered the last second.
        //    Wait it out and show the credits.
        yield return earlyFade;
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);

        if (aimRoutine != null)
            StopCoroutine(aimRoutine); // screen is black, the aim is done

        if (_creditsView != null)
            yield return _creditsView.Show();
        else if (_debugLogging)
            Debug.LogWarning("[EndGameSequence] EndGameCreditsView not found in scene.");
    }

    /// <summary>
    /// Snaps the player to the start point horizontally, preserving their
    /// current height. Players who entered the puzzle from the side of the
    /// skull would otherwise stand outside the gaze choreography. The move
    /// happens while the screen is still black from the puzzle-exit fade,
    /// so the snap is invisible.
    /// </summary>
    private void SnapPlayerToStart()
    {
        // Fallback: the point may be renamed in the prefab — find it by name
        // among the children of this puzzle.
        if (_playerStartPoint == null)
        {
            Transform found = transform.Find("playerFinal");
            if (found == null)
                found = transform.Find("player");
            if (found != null)
                _playerStartPoint = found;
        }

        if (_playerStartPoint == null)
            return;

        // Fallback: the CharacterController found in Awake may be missing
        // (e.g. the player spawned later) — locate it through FPSController.
        if (_characterController == null)
            _characterController = FindFirstObjectByType<FPSController>()
                ?.GetComponent<CharacterController>();

        if (_characterController == null)
            return;

        // Teleport through FPSController.Teleport (the proven pattern from
        // the dev cheats): it disables the CharacterController around the
        // move so the physics state cannot override the position. Only the
        // horizontal position comes from the point — the player's current
        // height and yaw are preserved, so there is no visible jump.
        Transform player = _characterController.transform;
        Vector3 position = player.position;
        position.x = _playerStartPoint.position.x;
        position.z = _playerStartPoint.position.z;

        player.GetComponent<FPSController>()
            ?.Teleport(position, player.eulerAngles.y);

        if (_debugLogging)
            Debug.Log($"[EndGameSequence] Player snapped to {_playerStartPoint.name} (height preserved).");
    }

    /// <summary>
    /// Instantly turns the body and camera onto the look point. Runs while
    /// the screen is still black right after the puzzle is solved, so the
    /// snap of the view is invisible.
    /// </summary>
    private void OrientCameraToLookPoint()
    {
        if (_lookPoint == null || _characterController == null)
            return;

        Transform player = _characterController.transform;
        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;

        // Body yaw toward the point.
        Vector3 flatToLook = _lookPoint.position - player.position;
        flatToLook.y = 0f;
        if (flatToLook.sqrMagnitude > 0.0001f)
            player.rotation = Quaternion.LookRotation(flatToLook.normalized, Vector3.up);

        // Camera pitch onto the point (local space of the body).
        if (camera != null)
        {
            Vector3 toLook = _lookPoint.position - camera.position;
            if (toLook.sqrMagnitude > 0.0001f)
            {
                Quaternion worldLook = Quaternion.LookRotation(toLook.normalized, Vector3.up);
                Quaternion localTarget = Quaternion.Inverse(player.rotation) * worldLook;
                Vector3 euler = localTarget.eulerAngles;
                camera.localRotation = Quaternion.Euler(euler.x, 0f, 0f);
            }
        }
    }

    /// <summary>
    /// One gaze stage: smoothly turns the body and camera toward the target
    /// (same eased speed profile as the aim), holds the view for
    /// GazeHoldTime, then returns. Skipped silently if the target is missing.
    /// </summary>
    private IEnumerator GazeAtRoutine(Transform target)
    {
        if (target == null || _characterController == null)
            yield break;

        Transform player = _characterController.transform;
        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;

        float turnSpeed = 0f;
        float elapsed = 0f;
        float settled = 0f;

        while (elapsed < _gazeTimeout)
        {
            elapsed += Time.deltaTime;
            turnSpeed = AimAtLookPoint(player, camera, target.position, turnSpeed);

            // Consider the gaze settled when the remaining angle is tiny;
            // hold it for GazeHoldTime, then move to the next target.
            if (turnSpeed == 0f || AngleToTarget(camera, target.position) < _gazeArrivalAngle)
            {
                settled += Time.deltaTime;
                if (settled >= _gazeHoldTime)
                    yield break;
            }
            else
            {
                settled = 0f;
            }

            yield return null;
        }

        if (_debugLogging)
            Debug.LogWarning("[EndGameSequence] Gaze stage timed out — moving on.");
    }

    /// <summary>Angle between the camera forward and the direction to the target.</summary>
    private static float AngleToTarget(Transform camera, Vector3 targetPosition)
    {
        if (camera == null) return 0f;
        Vector3 toTarget = targetPosition - camera.position;
        return toTarget.sqrMagnitude < 0.0001f
            ? 0f
            : Vector3.Angle(camera.forward, toTarget.normalized);
    }

    /// <summary>
    /// Runs from the moment input is disabled until the fade completes:
    /// every frame rotates the player body and the camera so they look at
    /// the focus point. Movement is handled by the walk routine — this only
    /// owns rotation.
    /// </summary>
    private IEnumerator AimRoutine()
    {
        Transform player = _characterController.transform;

        var fps = player.GetComponent<FPSController>();
        Transform camera = fps != null ? fps.CameraTransform : null;

        float turnSpeed = 0f;

        while (true)
        {
            turnSpeed = AimAtLookPoint(player, camera, _lookPoint.position, turnSpeed);
            yield return null;
        }
    }

    /// <summary>
    /// One frame of the scripted camera aim. The turn speed ramps up from
    /// zero and eases down as the view closes in on the target angle, so the
    /// takeover never snaps. Within LookHoldDistance the aim is released —
    /// the point sits below eye level, so aiming at it up close would tilt
    /// the view into the floor. Returns the updated turn speed.
    /// </summary>
    private float AimAtLookPoint(Transform player, Transform camera, Vector3 lookPoint, float turnSpeed)
    {
        Vector3 flatToLook = lookPoint - player.position;
        flatToLook.y = 0f;
        float flatDistance = flatToLook.magnitude;

        // Release the aim inside the hold radius — keep the last rotation.
        if (flatDistance <= _lookHoldDistance)
            return 0f;

        // How far the view still is from pointing at the point.
        float angleRemaining = 0f;
        if (camera != null)
        {
            Vector3 toLook = lookPoint - camera.position;
            if (toLook.sqrMagnitude > 0.0001f)
                angleRemaining = Vector3.Angle(camera.forward, toLook.normalized);
        }

        if (angleRemaining < 0.01f)
            return 0f;

        // Ease the turn speed: accelerate from zero, decelerate while
        // settling onto the target angle.
        float speedCap = Mathf.Min(_cameraTurnSpeed,
            angleRemaining / _aimSettleAngle * _cameraTurnSpeed);
        turnSpeed = Mathf.MoveTowards(turnSpeed, speedCap, _aimAcceleration * Time.deltaTime);

        float step = Mathf.Min(turnSpeed * Time.deltaTime, angleRemaining);

        // Body yaw toward the point.
        if (flatToLook.sqrMagnitude > 0.0001f)
        {
            Quaternion bodyRot = Quaternion.LookRotation(flatToLook.normalized, Vector3.up);
            player.rotation = Quaternion.RotateTowards(player.rotation, bodyRot, step);
        }

        // Camera pitch toward the point, in the body's local space.
        if (camera != null)
        {
            Vector3 toLook = lookPoint - camera.position;
            if (toLook.sqrMagnitude > 0.0001f)
            {
                Quaternion worldLook = Quaternion.LookRotation(toLook.normalized, Vector3.up);
                Quaternion localTarget = Quaternion.Inverse(player.rotation) * worldLook;

                Vector3 targetEuler = localTarget.eulerAngles;
                camera.localRotation = Quaternion.RotateTowards(
                    camera.localRotation,
                    Quaternion.Euler(targetEuler.x, 0f, 0f),
                    step);
            }
        }

        return turnSpeed;
    }

    /// <summary>
    /// Moves the player toward the walk target at scripted speed while input is disabled.
    /// Movement uses CharacterController.Move so gravity and collisions keep working.
    /// </summary>
    private IEnumerator WalkThroughDoorRoutine()
    {
        if (_characterController == null)
        {
            if (_debugLogging) Debug.LogWarning("[EndGameSequence] No CharacterController found — skipping walk.");
            yield break;
        }

        Transform player = _characterController.transform;
        Vector3 target = ComputeWalkTarget(player.position);
        Vector3 doorCenterPos = _doorCenter != null ? _doorCenter.position : target;
        bool passedDoor = false;

        // The final approach is slower — heavy, guilty steps after seeing
        // Death and Angel. The regular speed is only used without a look point.
        float walkSpeed = _lookPoint != null ? _guiltWalkSpeed : _walkSpeed;

        // With a look point the player stops at the hold distance instead of
        // crowding the exact point.
        float arrival = _lookPoint != null
            ? Mathf.Max(_arrivalThreshold, _lookHoldDistance)
            : _arrivalThreshold;

        if (_debugLogging)
            Debug.Log($"[EndGameSequence] Walking player from {player.position} to {target}");

        float elapsed = 0f;
        float stallTime = 0f;
        float lastDistance = float.MaxValue;
        Vector3 lastPosition = player.position;
        while (elapsed < _maxWalkDuration)
        {
            elapsed += Time.deltaTime;

            Vector3 toTarget = target - player.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            // Stuck detection: if the player barely moved for MaxStallTime
            // (blocked by a door frame, prop or collider), give up on the
            // walk — the ending must always reach the fade.
            bool moved = (player.position - lastPosition).sqrMagnitude > 0.00001f;
            lastPosition = player.position;
            if (distance > arrival && !moved && distance >= lastDistance - 0.001f)
                stallTime += Time.deltaTime;
            else
                stallTime = 0f;
            lastDistance = distance;

            if (stallTime >= _maxStallTime)
            {
                if (_debugLogging)
                    Debug.LogWarning($"[EndGameSequence] Walk stalled for {_maxStallTime:F1}s — forcing the fade.");
                break;
            }

            if (distance <= arrival && passedDoor)
                break;

        // Face the walking direction — only when no look point is assigned.
        // With a look point, AimAtLookPoint owns the body rotation entirely;
        // turning toward the movement direction here would fight it and the
        // view would end up sideways.
        if (_lookPoint == null && distance > 0.01f)
        {
            Vector3 flatDir = toTarget.normalized;
            Quaternion targetRot = Quaternion.LookRotation(flatDir, Vector3.up);
            player.rotation = Quaternion.RotateTowards(
                player.rotation, targetRot, _turnSpeed * Time.deltaTime);
        }

            // The player has passed the doorway once they crossed the door plane
            // (projection onto the walking direction goes past the door center).
            Vector3 toDoor = doorCenterPos - player.position;
            toDoor.y = 0f;
            if (!passedDoor && Vector3.Dot(toDoor, target - doorCenterPos) < 0f)
                passedDoor = true;
            // Fallback: if we somehow got within threshold without the plane check.
            if (distance <= arrival)
                passedDoor = true;

            if (distance > arrival)
            {
                Vector3 step = toTarget.normalized * Mathf.Min(walkSpeed * Time.deltaTime, distance - arrival);
                _characterController.Move(step);
            }
            else
            {
                // At the target — the walk is done regardless of plane check.
                break;
            }

            yield return null;
        }

        if (_debugLogging)
            Debug.Log($"[EndGameSequence] Walk finished. Passed door: {passedDoor}, elapsed: {elapsed:F1}s");
    }

    /// <summary>
    /// Starts the final fade exactly one second before the walk finishes, so
    /// the screen is already darkening over the player's last steps. Waits
    /// until the walk ends, then completes the fade.
    /// </summary>
    private IEnumerator WalkWithEarlyFadeRoutine(Coroutine aimRoutine)
    {
        float remainingFade = 0f;

        while (!_walkFinished)
        {
            float remaining = RemainingWalkDistance();
            if (remaining < 0f)
                break; // walk finished or impossible

            // EarlyFadeLead seconds of walking left at the (slow) final speed.
            float walkSpeed = _lookPoint != null ? _guiltWalkSpeed : _walkSpeed;
            if (remaining <= walkSpeed * _earlyFadeLead && ScreenFader.Instance != null)
            {
                // One second of walking left — start the fade now.
                StartCoroutine(FadeOutRemainingRoutine());
                remainingFade = _fadeDuration;
                break;
            }

            yield return null;
        }

        if (remainingFade > 0f)
            yield return new WaitForSeconds(remainingFade);
    }

    /// <summary>
    /// Distance the player still has to walk, or -1 when the walk is done.
    /// </summary>
    private float RemainingWalkDistance()
    {
        if (_characterController == null)
            return -1f;

        Vector3 target = ComputeWalkTarget(_characterController.transform.position);
        Vector3 toTarget = target - _characterController.transform.position;
        toTarget.y = 0f;

        float arrival = _lookPoint != null
            ? Mathf.Max(_arrivalThreshold, _lookHoldDistance)
            : _arrivalThreshold;

        float distance = toTarget.magnitude;
        return distance <= arrival ? -1f : distance - arrival;
    }

    /// <summary>Fades to black over FadeDuration — runs alongside the last steps of the walk.</summary>
    private IEnumerator FadeOutRemainingRoutine()
    {
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeIn(_fadeDuration);
        else
            yield return new WaitForSeconds(_fadeDuration);
    }

    /// <summary>
    /// Returns the walk target: the look point if assigned (the player slowly
    /// drifts to it), otherwise the explicit walk target, otherwise extends
    /// the line from the player through the door center.
    /// </summary>
    private Vector3 ComputeWalkTarget(Vector3 playerPosition)
    {
        if (_lookPoint != null)
            return _lookPoint.position;

        if (_walkTarget != null)
            return _walkTarget.position;

        if (_doorCenter == null)
            return playerPosition; // Nowhere to walk — the fade timer still ends the game.

        Vector3 doorCenter = _doorCenter.position;
        Vector3 playerFlat = new Vector3(playerPosition.x, doorCenter.y, playerPosition.z);
        Vector3 dir = doorCenter - playerFlat;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;

        return doorCenter + dir.normalized * _walkDistanceBeyondDoor;
    }

    /// <summary>
    /// Waits until the animator has finished playing the given state.
    /// Falls back to a fixed duration if the animator or state is invalid.
    /// </summary>
    private IEnumerator WaitForStateFinish(Animator animator, string stateName)
    {
        yield return null; // Let the state start.

        float elapsed = 0f;
        while (elapsed < _doorOpenFallbackDuration &&
               !animator.GetCurrentAnimatorStateInfo(0).IsName(stateName))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        while (animator.GetCurrentAnimatorStateInfo(0).IsName(stateName) &&
               animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            yield return null;
    }
}
