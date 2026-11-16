using UnityEngine;

/// <summary>
/// Activates the duck horror event when the player delivers a draggable duck
/// into the toilet. Flow: the draggable duck (Rigidbody) is dropped into this
/// trigger zone (placed underwater in the toilet bowl) → the pantry duck is
/// consumed and the event duck surfaces in its place.
/// Fires once per play session; the delivered state persists via ISaveable.
///
/// Attach to the DuckTrigger GameObject (BoxCollider with Is Trigger enabled).
/// Serialized references are resolved at runtime — same pattern as
/// CanTriggerZone — so the trigger survives prefab reverts and room re-placements.
/// </summary>
public class DuckToiletTrigger : MonoBehaviour, ISaveable
{
    [Tooltip("The draggable duck's Rigidbody the player brings from the pantry.")]
    [SerializeField] private Rigidbody _pantryDuckBody;

    [Tooltip("Seconds between the duck entering the zone and the delivery — protects " +
             "against the duck being saved inside the zone and instantly re-delivering.")]
    [SerializeField, Min(0f)] private float _triggerDelay = 0f;

    [Tooltip("Ignore trigger events for this many seconds after scene load.")]
    [SerializeField, Min(0f)] private float _startGracePeriod = 2f;

    [Tooltip("The event duck to surface when the draggable duck is delivered.")]
    [SerializeField] private DuckHorrorInteractable _eventDuck;

    private bool _hasFired;
    private float _elapsed;

    public string SaveId => "duck_toilet_trigger";

    private void Awake()
    {
        // The serialized references live in the room prefab and can only point at
        // scene objects via fragile prefab-instance overrides — resolve them at
        // runtime so the trigger survives prefab reverts and room re-placements.
        if (_eventDuck == null)
            _eventDuck = FindFirstObjectByType<DuckHorrorInteractable>();
        if (_eventDuck == null)
        {
            Debug.LogWarning($"[{name}] No DuckHorrorInteractable found in the scene — the trigger can never fire.", this);
            return;
        }

        if (_pantryDuckBody == null && _eventDuck.PantryDuck != null)
            _pantryDuckBody = _eventDuck.PantryDuck.GetComponentInParent<Rigidbody>();

        if (_pantryDuckBody == null)
            Debug.LogWarning($"[{name}] Pantry duck Rigidbody not resolved — the trigger can never fire.", this);

        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    private void Update() => _elapsed += Time.deltaTime;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasFired) return;
        if (_elapsed < _startGracePeriod) return;
        if (_eventDuck == null || _eventDuck.HasBeenDelivered) return;
        if (_pantryDuckBody == null || other.attachedRigidbody != _pantryDuckBody) return;

        _hasFired = true;
        Debug.Log($"[DuckToiletTrigger] Duck delivered: {_pantryDuckBody.name}; Position={_pantryDuckBody.position}; ZonePosition={transform.position}", this);

        if (_triggerDelay > 0f)
            StartCoroutine(DeliverAfterDelay());
        else
            _eventDuck.DeliverFromToilet();
    }

    private System.Collections.IEnumerator DeliverAfterDelay()
    {
        yield return new WaitForSeconds(_triggerDelay);
        _eventDuck.DeliverFromToilet();
    }

    // ── ISaveable ──────────────────────────────────────────────────────────────

    public string GetSaveData() => JsonUtility.ToJson(new SaveData { hasFired = _hasFired });

    public void LoadSaveData(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        _hasFired = JsonUtility.FromJson<SaveData>(json).hasFired;

        if (_hasFired)
        {
            // Safety: the pantry duck must never reappear on load, even if the
            // duck's own save record raced behind the trigger's.
            if (_pantryDuckBody != null)
                _pantryDuckBody.gameObject.SetActive(false);
            if (_eventDuck != null && !_eventDuck.HasBeenDelivered)
                _eventDuck.RestoreDelivered();
            gameObject.SetActive(false);
        }
    }

    [System.Serializable]
    private class SaveData
    {
        public bool hasFired;
    }
}
