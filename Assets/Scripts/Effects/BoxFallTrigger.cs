using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Drops the target box when the player enters this trigger zone: releases the
/// box's Rigidbody (isKinematic off) so it falls and lands with an impact sound
/// played by PhysicsDraggable. Fires once. Implements ISaveable: the fired flag
/// and the box's landed pose (via RigidbodyStateSaver on the box) survive save/load,
/// so a fallen box stays on the floor after loading instead of replaying the fall.
/// </summary>
public class BoxFallTrigger : MonoBehaviour, ISaveable
{
    private const float DefaultStartGracePeriod = 2f;
    private const float MaxLandingWaitSeconds = 5f;

    [Tooltip("Rigidbody of the box that falls when the player enters the zone.")]
    [SerializeField] private Rigidbody _boxRigidbody;

    [Tooltip("Ignore trigger events for this many seconds after scene load — protects " +
             "against the player spawning inside the zone and instantly firing it.")]
    [SerializeField, Min(0f)] private float _startGracePeriod = DefaultStartGracePeriod;

    [Header("Save Settings")]
    [SerializeField] private string _saveId = "box_fall_unique_id";

    private bool _hasFired;
    private float _elapsed;

    // ── ISaveable ────────────────────────────────────────────────────────

    /// <summary>Unique, never-changing identifier used by the save system.</summary>
    public string SaveId => _saveId;

    /// <summary>Returns a JSON snapshot of the fired flag.</summary>
    public string GetSaveData()
    {
        return JsonUtility.ToJson(new SaveData { hasFired = _hasFired });
    }

    /// <summary>Restores the fired flag; a fired trigger stays silent after loading.</summary>
    public void LoadSaveData(string json)
    {
        _hasFired = JsonUtility.FromJson<SaveData>(json).hasFired;
        if (_hasFired)
            _elapsed = _startGracePeriod;
    }

    [Serializable]
    private struct SaveData
    {
        public bool hasFired;
    }

    // ── Registration ─────────────────────────────────────────────────────

    private void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    private void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ── Trigger logic ────────────────────────────────────────────────────

    private void Update() => _elapsed += Time.deltaTime;

    private void OnTriggerEnter(Collider other)
    {
        if (_hasFired || _elapsed < _startGracePeriod) return;
        if (_boxRigidbody == null || !other.CompareTag("Player")) return;

        _hasFired = true;
        _boxRigidbody.isKinematic = false;
        StartCoroutine(SaveAfterLanding());
    }

    /// <summary>Waits until the box settles (or a timeout), then snapshots the save so
    /// RigidbodyStateSaver on the box captures its landed pose, not a mid-air one.</summary>
    private IEnumerator SaveAfterLanding()
    {
        float remaining = MaxLandingWaitSeconds;
        while (remaining > 0f && !_boxRigidbody.IsSleeping())
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        SaveManager.Instance?.Save();
    }
}
