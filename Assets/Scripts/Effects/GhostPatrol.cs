using System.Collections.Generic;
using Escape.Core;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Invisible poltergeist that patrols the house along a waypoint graph and
/// actively hunts the player when it can see them. Vision is honest: a
/// physics raycast blocks sight on walls, furniture and closed doors — a
/// player who locked themselves in a room is invisible to the ghost. Open
/// door panels are looked through. Movement itself is incorporeal (passes
/// through walls by design — the entity is a spirit, but its EYES are not).
/// States: Patrol (waypoint drift) → Chase (player visible) →
/// Search (fly to last seen position, dwell) → Patrol.
/// </summary>
public class GhostPatrol : MonoBehaviour, IPowerConsumer
{
    public enum GhostState { Patrol, Chase, Search, Unpowered }

    [Header("Agent")]

    [Tooltip("Transform that physically moves between waypoints. Waypoints must NOT be children of this object. Leave empty to move this object itself.")]
    [SerializeField] private Transform _agentRoot;

    [Tooltip("Patrol speed in meters per second.")]
    [SerializeField] private float _speed = 1.5f;

    [Tooltip("Distance at which the current waypoint is considered reached.")]
    [SerializeField] private float _arrivalDistance = 0.5f;

    [Tooltip("Seconds to dwell at a waypoint before choosing the next one.")]
    [SerializeField] private Vector2 _dwellTimeRange = new Vector2(60f, 120f);

    [Tooltip("After catching the player the ghost dwells briefly (this min/max range, seconds) and then flees to a far waypoint instead of lingering where the catch happened.")]
    [SerializeField] private Vector2 _postCatchFleeDelayRange = new Vector2(4f, 8f);

    [Tooltip("Max distance between two waypoints to consider them directly connected (auto-linking).")]
    [SerializeField] private float _autoLinkDistance = 15f;

    [Tooltip("Max height difference between the ghost and a prank target (switch, door, throwable). Objects on other floors are never touched, even if within the search radius.")]
    [SerializeField] private float _sameFloorHeightThreshold = 3f;

    [Tooltip("Log state changes and waypoint arrivals to the console (for testing).")]
    [SerializeField] private bool _debugLogging = true;

    [Tooltip("Waypoints to patrol. Auto-populated from children if left empty.")]
    [SerializeField] private List<GhostWaypoint> _waypoints = new List<GhostWaypoint>();

    [Header("Player Hunt")]

    [Tooltip("Max distance at which the ghost can spot the player.")]
    [SerializeField] private float _viewDistance = 15f;

    [Tooltip("Movement speed while chasing the player.")]
    [SerializeField] private float _chaseSpeed = 3f;

    [Tooltip("Seconds of continuous line-of-sight loss before the ghost gives up the chase and searches the last seen position.")]
    [SerializeField] private float _loseSightTime = 4f;

    [Tooltip("Seconds the ghost lingers at the last seen position before resuming the patrol.")]
    [SerializeField] private float _searchDwellTime = 3f;

    [Tooltip("Distance at which the ghost is considered to have reached the player.")]
    [SerializeField] private float _catchDistance = 1f;

    [Tooltip("Fires once when the ghost reaches the player (wire a scare / damage here). Resets when the player escapes the catch distance.")]
    [SerializeField] private UnityEvent _onPlayerReached;

    [Header("Hunt Cooldown")]

    [Tooltip("After reaching the player the catch event fires and the hunt re-arms after a random delay in this min/max range, in seconds. During the cooldown the ghost does NOT despawn — it keeps patrolling and pranking (switches, doors, objects) but cannot start a new chase. Default 360 = one hunt per 6 minutes at most.")]
    [SerializeField] private Vector2 _huntCooldownRange = new Vector2(360f, 360f);

    [Header("Speed Variation")]

    [Tooltip("Min/max multiplier applied to the patrol speed. Re-rolled every _speedChangeIntervalRange seconds.")]
    [SerializeField] private Vector2 _patrolSpeedMultiplierRange = new Vector2(0.6f, 1.6f);

    [Tooltip("Min/max seconds between patrol speed re-rolls.")]
    [SerializeField] private Vector2 _speedChangeIntervalRange = new Vector2(5f, 12f);

    [Tooltip("Seconds to smoothly blend from the current speed to the new one. 0 = instant.")]
    [SerializeField] private float _speedBlendDuration = 2f;

    [Header("Switch Pranks")]

    [Tooltip("Chance (0-1) that the ghost decides to play with a light switch. Evaluated periodically while dwelling at a waypoint. 0 = never, 1 = always.")]
    [SerializeField, Range(0f, 1f)] private float _switchFlipChance = 0.35f;

    [Tooltip("Share (0-1) of prank attempts that prefer turning lights OFF. The rest prefer turning them ON. If the nearest switch is already in the preferred state, the ghost flips it to the opposite anyway.")]
    [SerializeField, Range(0f, 1f)] private float _switchOffPreference = 0.7f;

    [Tooltip("Max distance from the waypoint to search for a LightSwitch (uses its Collider).")]
    [SerializeField] private float _switchInteractRadius = 5f;

    [Tooltip("Global cooldown between switch flips, seconds.")]
    [SerializeField] private float _switchFlipCooldown = 30f;

    [Tooltip("Seconds between prank attempts while dwelling at a waypoint.")]
    [SerializeField] private float _switchPrankInterval = 8f;

    [Header("Poltergeist Throws")]

    [Tooltip("Chance (0-1) that the ghost decides to hurl a nearby draggable object. Evaluated periodically while dwelling at a waypoint. 0 = never.")]
    [SerializeField, Range(0f, 1f)] private float _throwChance = 0.4f;

    [Tooltip("Max distance from the waypoint to search for a PhysicsDraggable to throw (uses its Collider).")]
    [SerializeField] private float _throwSearchRadius = 6f;

    [Tooltip("Horizontal launch speed of a thrown object, m/s.")]
    [SerializeField] private Vector2 _throwSpeedRange = new Vector2(4f, 7f);

    [Tooltip("Upward launch speed of a thrown object, m/s. Adds an arc to the throw.")]
    [SerializeField] private Vector2 _throwUpwardSpeedRange = new Vector2(1.5f, 3.5f);

    [Tooltip("Max mass of an object the ghost is willing to throw. Heavier furniture stays put.")]
    [SerializeField] private float _maxThrowableMass = 15f;

    [Tooltip("Objects closer than this distance to each other count as one pile, meters.")]
    [SerializeField] private float _pileDetectionRadius = 2f;

    [Tooltip("How many objects together count as a pile. A found pile is scattered all at once; fewer objects are thrown one at a time as usual.")]
    [SerializeField] private int _pileMinCount = 3;

    [Tooltip("Global cooldown between poltergeist throws, seconds.")]
    [SerializeField] private float _throwCooldown = 45f;

    [Tooltip("Seconds between throw attempts while dwelling at a waypoint.")]
    [SerializeField] private float _throwAttemptInterval = 10f;

    [Header("Door Pranks")]

    [Tooltip("Chance (0-1) that the ghost decides to play with an unlocked door. Evaluated periodically while dwelling at a waypoint. 0 = never.")]
    [SerializeField, Range(0f, 1f)] private float _doorChance = 0.5f;

    [Tooltip("Max distance from the waypoint to search for a DoorInteraction (uses its Collider).")]
    [SerializeField] private float _doorSearchRadius = 8f;

    [Tooltip("Share (0-1) of door pranks that SLAM the door fully open/shut. The rest give it a short nudge ajar or shut.")]
    [SerializeField, Range(0f, 1f)] private float _doorSlamChance = 0.4f;

    [Tooltip("How far a nudged door swings (share of max angle, 0-1). A nudge is quick, so the creak plays once, clean.")]
    [SerializeField] private Vector2 _nudgeFractionRange = new Vector2(0.25f, 0.5f);

    [Tooltip("Duration of a nudge (partial swing), seconds — roughly the normal door speed.")]
    [SerializeField] private Vector2 _nudgeDurationRange = new Vector2(0.8f, 1.5f);

    [Tooltip("Duration of a full slam, seconds.")]
    [SerializeField] private Vector2 _slamDurationRange = new Vector2(0.6f, 1f);

    [Tooltip("Global cooldown between door pranks, seconds.")]
    [SerializeField] private float _doorCooldown = 60f;

    [Tooltip("Seconds between door attempts while dwelling at a waypoint.")]
    [SerializeField] private float _doorAttemptInterval = 12f;

    [Header("Hunt Unlock")]

    [Tooltip("Puzzle that unlocks the hunt. While unsolved, the ghost only patrols, stomps and flips switches — it never chases the player. Assign the clock puzzle's PuzzleModeController here. Leave empty to hunt from the start.")]
    [SerializeField] private PuzzleModeController _huntUnlockPuzzle;

    [Header("Performance")]

    [Tooltip("Seconds between vision raycasts while patrolling/searching. The chase checks every frame — a hunt must not lose the player to a timing gap. No perceptible difference in behaviour, saves per-frame RaycastAll pressure.")]
    [SerializeField, Range(0.05f, 0.5f)] private float _visionCheckInterval = 0.15f;

    private GhostState _state = GhostState.Patrol;
    private GhostWaypoint _current;
    private GhostWaypoint _target;
    private float _dwellTimer;
    private bool _isPowered;

    // Speed variation state
    private float _currentSpeedMultiplier = 1f;
    private float _targetSpeedMultiplier = 1f;
    private float _speedChangeTimer;
    private float _speedBlendTimer;
    private float _speedBlendStartMultiplier = 1f;

    private FPSController _player;
    private Vector3 _lastSeenPosition;
    private float _lostSightTimer;
    private bool _reachedEventFired;
    private float _switchFlipTimer;
    private float _throwCooldownTimer;
    private float _doorCooldownTimer;
    private float _huntCooldownTimer;
    private float _postCatchFleeTimer;
    private float _switchPrankTimer;
    private float _throwAttemptTimer;
    private float _doorAttemptTimer;

    /// <summary>Recently visited waypoints, newest first — excluded from
    /// <see cref="PickNextWaypoint"/> so the ghost does not ping-pong between
    /// two rooms.</summary>
    private readonly List<GhostWaypoint> _recentWaypoints = new List<GhostWaypoint>();
    private const int RecentWaypointMemory = 2;

    /// <summary>Next Time.time at which the patrol/search vision check runs.</summary>
    private float _nextVisionCheckTime;

    /// <summary>Reusable buffer for the vision raycast (no per-frame allocations).</summary>
    private static readonly RaycastHit[] VisionHits = new RaycastHit[24];

    /// <summary>Waypoint the agent is currently dwelling at (null while moving).</summary>
    public GhostWaypoint CurrentWaypoint => _current;

    /// <summary>True while the agent is flying between waypoints (false while dwelling).</summary>
    public bool IsMoving => _state == GhostState.Chase
        ? _player != null && Vector3.Distance(_agentRoot.position, _player.transform.position) > _catchDistance
        : _target != null;

    /// <summary>True while the ghost is actively chasing a visible player.</summary>
    public bool IsChasing => _state == GhostState.Chase;

    /// <summary>Current behaviour state of the ghost.</summary>
    public GhostState State => _state;

    /// <summary>True while the ghost has just caught the player and its chase is
    /// on cooldown — it keeps patrolling and pranking during this time.</summary>
    public bool IsHuntOnCooldown => _huntCooldownTimer > 0f;

    private void Awake()
    {
        if (_waypoints.Count == 0)
        {
            GetComponentsInChildren(true, _waypoints);
        }
        if (_waypoints.Count == 0)
        {
            Debug.LogWarning("GhostPatrol: no waypoints assigned or found in children.", this);
            enabled = false;
            return;
        }

        AutoLinkWaypoints();
    }

    private void Start()
    {
        if (_agentRoot == null) _agentRoot = transform;

        // Register for power notifications — the current state is delivered
        // immediately, so _isPowered is up to date from this point on.
        if (LightingSystem.Instance != null)
        {
            LightingSystem.Instance.RegisterConsumer(this);
        }
        else
        {
            Debug.LogWarning("GhostPatrol: no LightingSystem found — ghost will be active regardless of power.", this);
            _isPowered = true;
        }

        if (_isPowered)
        {
            SpawnAtRandomWaypoint();
        }
        else
        {
            // Hide the agent until the power is on.
            _state = GhostState.Unpowered;
            _agentRoot.gameObject.SetActive(false);
        }
    }

    /// <summary>Places the agent at a random waypoint and starts dwelling there.</summary>
    private void SpawnAtRandomWaypoint()
    {
        _current = _waypoints[Random.Range(0, _waypoints.Count)];
        _agentRoot.position = _current.transform.position;
        _dwellTimer = Random.Range(_dwellTimeRange.x, _dwellTimeRange.y);

        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] Spawned at '{_current.name}' pos={_agentRoot.position:F1}", this);
        }
    }

    /// <summary>
    /// IPowerConsumer: the ghost only becomes active once the general power
    /// is restored (lights are on). When power goes off, it despawns again.
    /// </summary>
    public void OnPowerStateChanged(bool isPowered)
    {
        _isPowered = isPowered;

        if (isPowered && _state == GhostState.Unpowered)
        {
            // Power restored — spawn at a random waypoint and start hunting.
            _agentRoot.gameObject.SetActive(true);
            SpawnAtRandomWaypoint();
            _state = GhostState.Patrol;

            if (_debugLogging)
            {
                Debug.Log($"[GhostPatrol] Power on — active.", this);
            }
        }
        else if (!isPowered && _state != GhostState.Unpowered)
        {
            // Power cut — the spirit dissolves until the lights come back.
            if (_debugLogging)
            {
                Debug.Log("[GhostPatrol] Power off — despawned.", this);
            }
            _agentRoot.gameObject.SetActive(false);
            _state = GhostState.Unpowered;
        }
    }

    private void Update()
    {
        // Global prank cooldowns tick every frame.
        if (_switchFlipTimer > 0f) _switchFlipTimer -= Time.deltaTime;
        if (_throwCooldownTimer > 0f) _throwCooldownTimer -= Time.deltaTime;
        if (_doorCooldownTimer > 0f) _doorCooldownTimer -= Time.deltaTime;
        if (_huntCooldownTimer > 0f) _huntCooldownTimer -= Time.deltaTime;

        switch (_state)
        {
            case GhostState.Patrol: UpdatePatrol(); break;
            case GhostState.Chase: UpdateChase(); break;
            case GhostState.Search: UpdateSearch(); break;
            case GhostState.Unpowered: break; // Waiting for the lights to come back.
        }
    }

    private void UpdatePatrol()
    {
        if (TrySpotPlayer()) return;

        UpdateSpeedVariation();

        if (_target == null)
        {
            // Dwelling: periodically consider a switch prank or a poltergeist
            // throw while staying.
            _switchPrankTimer -= Time.deltaTime;
            if (_switchPrankTimer <= 0f)
            {
                _switchPrankTimer = _switchPrankInterval;
                TryFlipNearbySwitch();
            }

            _throwAttemptTimer -= Time.deltaTime;
            if (_throwAttemptTimer <= 0f)
            {
                _throwAttemptTimer = _throwAttemptInterval;
                TryThrowNearbyObject();
            }

            _doorAttemptTimer -= Time.deltaTime;
            if (_doorAttemptTimer <= 0f)
            {
                _doorAttemptTimer = _doorAttemptInterval;
                TryToggleNearbyDoor();
            }

            _dwellTimer -= Time.deltaTime;

            // Right after a catch: don't linger where it happened — flee to a
            // far waypoint once the short post-catch delay elapses.
            if (_postCatchFleeTimer > 0f)
            {
                _postCatchFleeTimer -= Time.deltaTime;
                if (_postCatchFleeTimer <= 0f)
                {
                    PickFarWaypoint();
                    return;
                }
            }

            if (_dwellTimer <= 0f)
            {
                PickNextWaypoint();
            }
            return;
        }

        MoveTowards(_target.transform.position, _speed * _currentSpeedMultiplier);

        // In-flight poltergeist: while passing waypoints the ghost may hurl an
        // object from the room it is flying over (cooldown still applies).
        _throwAttemptTimer -= Time.deltaTime;
        if (_throwAttemptTimer <= 0f)
        {
            _throwAttemptTimer = _throwAttemptInterval;
            TryThrowNearbyObject(_target.transform.position);
        }

        // In-flight door pranks: long flights between distant waypoints used to
        // be dead time for door manipulation — use the waypoint being approached.
        _doorAttemptTimer -= Time.deltaTime;
        if (_doorAttemptTimer <= 0f)
        {
            _doorAttemptTimer = _doorAttemptInterval;
            TryToggleNearbyDoor(_target.transform.position);
        }

        if (HasArrived(_target.transform.position))
        {
            _current = _target;
            _target = null;
            _dwellTimer = Random.Range(_dwellTimeRange.x, _dwellTimeRange.y);
            _switchPrankTimer = 0f;

            // Remember where we have just been so PickNextWaypoint skips it.
            _recentWaypoints.Insert(0, _current);
            if (_recentWaypoints.Count > RecentWaypointMemory)
            {
                _recentWaypoints.RemoveAt(_recentWaypoints.Count - 1);
            }

            if (_debugLogging)
            {
                Debug.Log($"[GhostPatrol] Arrived at '{_current.name}' pos={_agentRoot.position:F1} (staying {_dwellTimer:F0}s)", this);
            }
            _current.OnGhostArrived();
        }
    }

    /// <summary>
    /// True when the target sits roughly on the ghost's current floor (within
    /// <see cref="_sameFloorHeightThreshold"/> of the agent). The prank
    /// OverlapSpheres are 3D and can otherwise reach through ceilings — a
    /// waypoint in the pantry would "grab" a chair in the corridor above.
    /// </summary>
    private bool IsOnGhostFloor(Transform target)
    {
        return Mathf.Abs(target.position.y - _agentRoot.position.y) <= _sameFloorHeightThreshold;
    }

    /// <summary>
    /// Prank behaviour: while dwelling at a waypoint the ghost periodically
    /// considers playing with a nearby light switch. It has a preference —
    /// mostly it likes turning lights OFF (spookier), sometimes ON. The
    /// decision is random with a global cooldown so it stays unpredictable.
    /// Uses LightSwitch.Interact() — the physical flip, handle animation and
    /// click sound all happen, exactly like a player's interaction.
    /// </summary>
    private void TryFlipNearbySwitch()
    {
        if (_switchFlipChance <= 0f) return;

        if (_switchFlipTimer > 0f) return;

        if (Random.value > _switchFlipChance) return;

        // Find the closest switch with a collider within range of the waypoint.
        Collider[] hits = Physics.OverlapSphere(
            _current.transform.position, _switchInteractRadius);
        LightSwitch closest = null;
        float closestDistance = float.MaxValue;
        foreach (Collider hit in hits)
        {
            LightSwitch sw = hit.GetComponentInParent<LightSwitch>();
            if (sw == null || !sw.CanInteract()) continue;
            if (!IsOnGhostFloor(sw.transform)) continue;
            float distance = Vector3.Distance(
                _current.transform.position, hit.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = sw;
            }
        }

        if (closest == null)
        {
            // Nothing reachable here — wait for the next attempt anyway.
            _switchPrankTimer = _switchPrankInterval;
            return;
        }

        bool currentState = LightingSystem.Instance != null
            && LightingSystem.Instance.GetZoneSwitchState(closest.GetZoneId());

        // The ghost's decision: mostly it prefers darkness, sometimes light.
        // Either way it flips the switch — the prank is the flip itself.
        bool wantsOff = Random.value < _switchOffPreference;
        closest.Interact();

        _switchFlipTimer = _switchFlipCooldown;
        _switchPrankTimer = _switchPrankInterval;

        bool newState = !currentState;
        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] {(wantsOff ? "Prefers darkness" : "Prefers light")} — flipped a switch (zone now {(newState ? "ON" : "OFF")}) near '{_current.name}'.", this);
        }
    }

    /// <summary>
    /// Poltergeist behaviour: the ghost periodically considers hurling nearby
    /// draggable objects. A clumped pile (see _pileMinCount) is scattered all
    /// at once; loose objects are thrown one at a time — the closest one.
    /// While dwelling, the search centers on the current
    /// waypoint; while flying between waypoints, on the target waypoint (the
    /// room it is passing over). The throw is a
    /// plain physics impulse in a random horizontal direction with a slight
    /// upward arc — the object flies, tumbles and lands on its own; scrape and
    /// impact sounds are produced by <see cref="PhysicsDraggable"/> itself.
    /// Objects the player is currently holding are never touched.
    /// </summary>
    private void TryThrowNearbyObject() => TryThrowNearbyObject(
        _current != null ? _current.transform.position : _agentRoot.position);

    private void TryThrowNearbyObject(Vector3 searchCenter)
    {
        if (_throwChance <= 0f) return;
        if (_throwCooldownTimer > 0f) return;

        if (Random.value > _throwChance) return;

        // Collect every valid throwable within range.
        Collider[] hits = Physics.OverlapSphere(searchCenter, _throwSearchRadius);
        List<PhysicsDraggable> throwables = new List<PhysicsDraggable>();
        foreach (Collider hit in hits)
        {
            PhysicsDraggable draggable = hit.GetComponentInParent<PhysicsDraggable>();
            if (draggable == null) continue;

            Rigidbody body = draggable.Body;
            if (body == null || body.isKinematic) continue;
            if (body.mass > _maxThrowableMass) continue;
            if (!IsOnGhostFloor(draggable.transform)) continue;

            // Never yank an object out of the player's hands.
            if (draggable == PhysicsGrabber.CurrentlyGrabbed) continue;

            // Explicitly protected objects (story props, puzzle pieces) stay put.
            if (!draggable.GhostMayThrow) continue;

            throwables.Add(draggable);
        }

        if (throwables.Count == 0)
        {
            // Nothing throwable here — wait for the next attempt anyway.
            _throwAttemptTimer = _throwAttemptInterval;
            return;
        }

        // A pile the player stacked gets scattered all at once — angrier.
        List<PhysicsDraggable> pile = FindLargestPile(throwables);
        if (pile.Count >= _pileMinCount)
        {
            foreach (PhysicsDraggable draggable in pile) Hurl(draggable);
            _throwCooldownTimer = _throwCooldown;
            _throwAttemptTimer = _throwAttemptInterval;

            if (_debugLogging)
            {
                Debug.Log($"[GhostPatrol] Scattered a pile of {pile.Count} objects near {searchCenter}.", this);
            }
            return;
        }

        // Usual rage: throw the closest object only.
        PhysicsDraggable closest = throwables[0];
        float closestDistance = float.MaxValue;
        foreach (PhysicsDraggable draggable in throwables)
        {
            float distance = Vector3.Distance(searchCenter, draggable.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = draggable;
            }
        }
        Hurl(closest);

        _throwCooldownTimer = _throwCooldown;
        _throwAttemptTimer = _throwAttemptInterval;

        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] Hurled '{closest.name}' (mass {closest.Body.mass:F1} kg) near {searchCenter}.", this);
        }
    }

    /// <summary>
    /// Groups the candidates into clumps of objects lying within
    /// <see cref="_pileDetectionRadius"/> of each other (flood-fill) and
    /// returns the largest clump.
    /// </summary>
    private List<PhysicsDraggable> FindLargestPile(List<PhysicsDraggable> candidates)
    {
        List<PhysicsDraggable> remaining = new List<PhysicsDraggable>(candidates);
        List<PhysicsDraggable> largest = new List<PhysicsDraggable>();

        while (remaining.Count > 0)
        {
            // Grow a clump from the first remaining object.
            List<PhysicsDraggable> clump = new List<PhysicsDraggable> { remaining[0] };
            remaining.RemoveAt(0);

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    foreach (PhysicsDraggable member in clump)
                    {
                        if (Vector3.Distance(member.transform.position, remaining[i].transform.position)
                            <= _pileDetectionRadius)
                        {
                            clump.Add(remaining[i]);
                            remaining.RemoveAt(i);
                            grew = true;
                            break;
                        }
                    }
                }
            }

            if (clump.Count > largest.Count) largest = clump;
        }

        return largest;
    }

    /// <summary>Launches a single draggable in a random direction with a slight upward arc.</summary>
    private void Hurl(PhysicsDraggable draggable)
    {
        Rigidbody body = draggable.Body;
        if (body == null) return;

        // Random horizontal direction (the spirit does not aim — it rages),
        // plus an upward component for a nice tumbling arc.
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        Vector3 velocity = direction * Random.Range(_throwSpeedRange.x, _throwSpeedRange.y)
                         + Vector3.up * Random.Range(_throwUpwardSpeedRange.x, _throwUpwardSpeedRange.y);

        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 5f;
    }

    /// <summary>
    /// Door prank: the ghost periodically considers playing with a nearby
    /// unlocked door. Most of the time it creeps the door open (or shuts it
    /// fully, latch click included) slowly with a long creak; sometimes it
    /// SLAMS it. Locked doors are left alone — the spirit cannot turn a key.
    /// Uses DoorInteraction.GhostMove, so motion loops, the latch click and
    /// occlusion portals all behave exactly like a normal door move.
    /// </summary>
    private void TryToggleNearbyDoor() => TryToggleNearbyDoor(
        _current != null ? _current.transform.position : _agentRoot.position);

    private void TryToggleNearbyDoor(Vector3 searchCenter)
    {
        if (_doorChance <= 0f) return;
        if (_doorCooldownTimer > 0f) return;

        if (Random.value > _doorChance) return;

        // Find the closest unlocked door with a collider within range.
        Collider[] hits = Physics.OverlapSphere(searchCenter, _doorSearchRadius);
        DoorInteraction closest = null;
        float closestDistance = float.MaxValue;
        foreach (Collider hit in hits)
        {
            DoorInteraction door = hit.GetComponentInParent<DoorInteraction>();
            if (door == null || door.IsLocked) continue;
            if (!IsOnGhostFloor(door.transform)) continue;

            float distance = Vector3.Distance(
                searchCenter, hit.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closest = door;
            }
        }

        if (closest == null)
        {
            // No door within reach — wait for the next attempt anyway.
            _doorAttemptTimer = _doorAttemptInterval;
            if (_debugLogging)
            {
                Debug.Log($"[GhostPatrol] Door prank: no unlocked door within {_doorSearchRadius}m of the search point.", this);
            }
            return;
        }

        // Two prank flavors: a quick nudge (door swings part-way ajar or shut
        // at roughly normal door speed — the creak plays once, clean) or a
        // violent full slam.
        bool slam = Random.value < _doorSlamChance;
        float duration;
        bool moved;
        if (slam)
        {
            // Full swing to the opposite extreme.
            float fraction = closest.IsFullyClosed ? 1f : 0f;
            duration = Random.Range(_slamDurationRange.x, _slamDurationRange.y);
            moved = closest.GhostMove(fraction, duration);
        }
        else
        {
            // Partial swing in whichever direction leaves it NOT where it is:
            // a closed door creeps ajar, a half-open door shuts fully so the
            // latch click fires (CheckLatch needs openFraction to reach 0).
            bool ajar = closest.IsFullyClosed;
            float fraction = ajar
                ? Random.Range(_nudgeFractionRange.x, _nudgeFractionRange.y)
                : 0f;
            duration = Random.Range(_nudgeDurationRange.x, _nudgeDurationRange.y);
            moved = closest.GhostMove(fraction, duration);
        }

        if (!moved)
        {
            // Locked or already at the target — try again later.
            _doorAttemptTimer = _doorAttemptInterval;
            return;
        }

        _doorCooldownTimer = _doorCooldown;
        _doorAttemptTimer = _doorAttemptInterval;

        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] {(slam ? "SLAMMED" : "Nudged")} the door '{closest.name}' in {duration:F1}s near ({searchCenter.x:F0},{searchCenter.y:F0},{searchCenter.z:F0}).", this);
        }
    }

    /// <summary>
    /// Re-rolls the patrol speed multiplier on a timer and blends smoothly
    /// toward it — the ghost drifts at a slightly different pace over time.
    /// </summary>
    private void UpdateSpeedVariation()
    {
        // Blend the multiplier toward its current target.
        if (_speedBlendTimer < _speedBlendDuration && _currentSpeedMultiplier != _targetSpeedMultiplier)
        {
            _speedBlendTimer += Time.deltaTime;
            _currentSpeedMultiplier = Mathf.Lerp(
                _speedBlendStartMultiplier, _targetSpeedMultiplier,
                Mathf.Clamp01(_speedBlendTimer / Mathf.Max(0.001f, _speedBlendDuration)));
        }

        // Re-roll the target multiplier on an interval.
        _speedChangeTimer -= Time.deltaTime;
        if (_speedChangeTimer <= 0f)
        {
            _speedChangeTimer = Random.Range(_speedChangeIntervalRange.x, _speedChangeIntervalRange.y);
            _speedBlendStartMultiplier = _currentSpeedMultiplier;
            _targetSpeedMultiplier = Random.Range(_patrolSpeedMultiplierRange.x, _patrolSpeedMultiplierRange.y);
            _speedBlendTimer = 0f;
        }
    }

    private void UpdateChase()
    {
        // Safety net: if the unlock puzzle state was reset, drop the hunt.
        if (!IsHuntUnlocked())
        {
            SetState(GhostState.Patrol);
            return;
        }

        bool visible = CanSeePlayer();
        if (visible)
        {
            _lastSeenPosition = _player.transform.position;
            _lostSightTimer = 0f;
        }
        else
        {
            _lostSightTimer += Time.deltaTime;
            if (_lostSightTimer >= _loseSightTime)
            {
                SetState(GhostState.Search);
                return;
            }
        }

        float distanceToPlayer = _player != null
            ? Vector3.Distance(_agentRoot.position, _player.transform.position)
            : float.MaxValue;

        // Fly to where the player is (visible) or was last seen (hiding).
        Vector3 destination = visible ? _player.transform.position : _lastSeenPosition;

        if (distanceToPlayer > _catchDistance)
        {
            MoveTowards(destination, _chaseSpeed);
            _reachedEventFired = false;
        }
        else if (!_reachedEventFired)
        {
            _reachedEventFired = true;
            if (_debugLogging)
            {
                Debug.Log("[GhostPatrol] Reached the player.", this);
            }
            _onPlayerReached.Invoke();
            BeginHuntCooldown();
        }
    }

    private void UpdateSearch()
    {
        if (TrySpotPlayer()) return;

        if (HasArrived(_lastSeenPosition))
        {
            _dwellTimer += Time.deltaTime;
            if (_dwellTimer >= _searchDwellTime)
            {
                SetState(GhostState.Patrol);
            }
            return;
        }

        MoveTowards(_lastSeenPosition, _speed);
    }

    /// <summary>
    /// Called right after the catch event fires: the ghost does NOT despawn —
    /// it drops back into Patrol and keeps doing its activities (switch flips,
    /// door pranks, throwing objects), but cannot start a new chase until the
    /// hunt cooldown elapses.
    /// </summary>
    private void BeginHuntCooldown()
    {
        _huntCooldownTimer = Random.Range(_huntCooldownRange.x, _huntCooldownRange.y);
        _target = null;
        _reachedEventFired = false;
        // Brief dwell, then flee to a far waypoint instead of lingering
        // where the catch happened.
        _dwellTimer = Random.Range(_postCatchFleeDelayRange.x, _postCatchFleeDelayRange.y);
        _postCatchFleeTimer = _dwellTimer;
        _switchPrankTimer = 0f;
        SetState(GhostState.Patrol);

        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] Caught the player — hunt re-arms in {_huntCooldownTimer:F0}s, fleeing in {_postCatchFleeTimer:F0}s.", this);
        }
    }

    private void SetState(GhostState newState)
    {
        if (_state == newState) return;
        _state = newState;

        if (newState == GhostState.Search)
        {
            _dwellTimer = 0f;
        }

        if (_debugLogging)
        {
            Debug.Log($"[GhostPatrol] State -> {newState}", this);
        }
    }

    /// <summary>
    /// Checks the player and switches to Chase on sight. While patrolling or
    /// searching, the check is throttled to every <see cref="_visionCheckInterval"/>
    /// seconds — the last result is reused between checks, so spotting is
    /// delayed by at most that interval (imperceptible for a hunting ghost).
    /// Returns true when the player was spotted. Suppressed while the hunt is
    /// on cooldown (just caught the player) and until the hunt unlock puzzle
    /// is solved — before that the ghost never chases.
    /// </summary>
    private bool TrySpotPlayer()
    {
        if (_huntCooldownTimer > 0f) return false;
        if (!IsHuntUnlocked()) return false;

        if (_state != GhostState.Chase && Time.time < _nextVisionCheckTime) return false;
        _nextVisionCheckTime = Time.time + _visionCheckInterval;

        if (CanSeePlayer())
        {
            _lastSeenPosition = _player.transform.position;
            _lostSightTimer = 0f;
            SetState(GhostState.Chase);
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when the ghost is allowed to chase: no unlock puzzle assigned, or
    /// the assigned puzzle is solved (live solve or restored from save).
    /// </summary>
    private bool IsHuntUnlocked()
    {
        return _huntUnlockPuzzle == null || _huntUnlockPuzzle.IsSolved;
    }

    /// <summary>
    /// Honest line-of-sight check: a raycast from the agent to the player.
    /// Blocked by walls, furniture and closed doors; open door panels are
    /// looked through. Also fails when the player is beyond _viewDistance.
    /// Uses a reusable buffer (no per-frame allocation) and walks the hits
    /// nearest-first instead of sorting them.
    /// </summary>
    private bool CanSeePlayer()
    {
        if (_player == null)
        {
            _player = FindFirstObjectByType<FPSController>();
            if (_player == null) return false;
        }

        Vector3 origin = _agentRoot.position;
        // Aim at the player's upper body so low furniture gives partial cover.
        Vector3 target = _player.transform.position + Vector3.up * 0.9f;
        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if (distance > _viewDistance) return false;
        if (distance < 0.01f) return true;

        // RaycastNonAlloc with a reusable buffer; then walk the hits
        // nearest-first — no array allocation, no sort. An open door panel
        // is looked through: skip it and consider the next-nearest hit.
        Vector3 normalizedDirection = direction / distance;
        int hitCount = Physics.RaycastNonAlloc(
            origin, normalizedDirection, VisionHits, distance, ~0,
            QueryTriggerInteraction.Ignore);

        bool[] handled = new bool[hitCount]; // tiny, only on the throttled cadence
        while (true)
        {
            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                if (!handled[i] && VisionHits[i].distance < nearestDistance)
                {
                    nearestDistance = VisionHits[i].distance;
                    nearestIndex = i;
                }
            }
            if (nearestIndex < 0) return true; // nothing solid between — clear sight

            handled[nearestIndex] = true;
            Collider hitCollider = VisionHits[nearestIndex].collider;

            // The player's own colliders being first in line means clear sight.
            if (hitCollider.GetComponentInParent<FPSController>() != null) return true;

            // Look through open door panels; anything else blocks vision.
            DoorInteraction door = hitCollider.GetComponentInParent<DoorInteraction>();
            if (door != null && door.IsOpen) continue;

            return false;
        }
    }

    private void MoveTowards(Vector3 destination, float speed)
    {
        Vector3 toDestination = destination - _agentRoot.position;
        _agentRoot.position += toDestination.normalized * (speed * Time.deltaTime);
    }

    private bool HasArrived(Vector3 destination)
    {
        return Vector3.Distance(_agentRoot.position, destination) <= _arrivalDistance;
    }

    private void PickNextWaypoint()
    {
        if (_waypoints.Count < 2) return;

        // Prefer connected waypoints for coherent paths; fall back to any other.
        List<GhostWaypoint> candidates = _current != null && _current.Connected.Count > 0
            ? _current.Connected
            : _waypoints;

        // Never pick the waypoint we are currently at, nor the ones visited
        // recently — prevents visible ping-ponging between two rooms.
        List<GhostWaypoint> options = candidates.FindAll(w => w != _current && !_recentWaypoints.Contains(w));

        if (options.Count == 0)
        {
            options = candidates.FindAll(w => w != _current);
        }
        if (options.Count == 0)
        {
            options = candidates;
        }

        _target = PickWeighted(options);
        _dwellTimer = 0f;
    }

    /// <summary>
    /// Post-catch behaviour: fly to the farthest waypoints so the ghost leaves
    /// the room where the player was just caught instead of hovering nearby.
    /// Candidates are sorted by distance and one of the top three is picked
    /// so the retreat destination still varies.
    /// </summary>
    private void PickFarWaypoint()
    {
        _postCatchFleeTimer = 0f;
        if (_waypoints.Count < 2) return;

        List<GhostWaypoint> options = _current != null
            ? (_current.Connected.Count > 0 ? _current.Connected : _waypoints)
            : _waypoints;

        Vector3 origin = _current != null ? _current.transform.position : _agentRoot.position;

        options.Sort((a, b) => Vector3.SqrMagnitude(b.transform.position - origin)
            .CompareTo(Vector3.SqrMagnitude(a.transform.position - origin)));
        int top = Mathf.Min(3, options.Count);
        _target = options[Random.Range(0, top)];
        _dwellTimer = 0f;

        if (_debugLogging && _target != null)
        {
            Debug.Log($"[GhostPatrol] Post-catch flee -> '{_target.name}'.", this);
        }
    }

    /// <summary>
    /// Weighted random pick: waypoints with a higher <see cref="GhostWaypoint.VisitWeight"/>
    /// are proportionally more likely. This keeps the ghost mostly around heavily
    /// weighted floors (e.g. 2F) while rare waypoints still get visited.
    /// </summary>
    private static GhostWaypoint PickWeighted(List<GhostWaypoint> options)
    {
        if (options.Count == 1) return options[0];

        float totalWeight = 0f;
        foreach (GhostWaypoint option in options)
            totalWeight += Mathf.Max(0.01f, option.VisitWeight);

        float roll = Random.value * totalWeight;
        foreach (GhostWaypoint option in options)
        {
            roll -= Mathf.Max(0.01f, option.VisitWeight);
            if (roll <= 0f) return option;
        }
        return options[options.Count - 1];
    }

    /// <summary>
    /// Links every pair of waypoints closer than <see cref="_autoLinkDistance"/>
    /// unless a manual connection already exists. Runs at Awake; in the editor
    /// the same rule is visualized by <see cref="OnDrawGizmosSelected"/>.
    /// The comparison uses the horizontal (XZ) distance only: the ghost flies
    /// through floors by design.
    /// </summary>
    private void AutoLinkWaypoints()
    {
        float sqrDistance = _autoLinkDistance * _autoLinkDistance;
        for (int i = 0; i < _waypoints.Count; i++)
        {
            for (int j = 0; j < _waypoints.Count; j++)
            {
                if (i == j) continue;
                GhostWaypoint a = _waypoints[i];
                GhostWaypoint b = _waypoints[j];
                if (a.Connected.Contains(b)) continue;

                Vector3 delta = a.transform.position - b.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= sqrDistance)
                {
                    a.Connected.Add(b);
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Fill the list for gizmo drawing when it is only auto-populated at runtime.
        if (_waypoints.Count == 0) GetComponentsInChildren(true, _waypoints);

        Gizmos.color = Color.cyan;
        for (int i = 0; i < _waypoints.Count; i++)
        {
            for (int j = i + 1; j < _waypoints.Count; j++)
            {
                if (_waypoints[i] == null || _waypoints[j] == null) continue;
                Vector3 delta = _waypoints[i].transform.position - _waypoints[j].transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= _autoLinkDistance * _autoLinkDistance)
                {
                    Gizmos.DrawLine(_waypoints[i].transform.position, _waypoints[j].transform.position);
                }
            }
        }

        // Visualization of the honest sight radius.
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Vector3 center = Application.isPlaying && _agentRoot != null ? _agentRoot.position : transform.position;
        Gizmos.DrawWireSphere(center, _viewDistance);
    }
}
