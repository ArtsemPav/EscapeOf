using System.Collections;
using UnityEngine;

/// <summary>
/// Writes a save once physics props around a restored/solved transition have come
/// to rest. Wire it into the same delayed sequence as the transition (e.g. after
/// DelayedActionSequence removes a wall and drops the props resting on it), so the
/// post-fall poses of RigidbodyStateSaver props land in the save and replaying the
/// fall (and its impact sounds) on every load is avoided.
/// </summary>
public class DelayedSaveAfterSettle : MonoBehaviour
{
    [Tooltip("Seconds to wait after Run() before writing the save. Should exceed the time the heaviest prop needs to fall and settle.")]
    [SerializeField, Min(0f)] private float _settleDelay = 4f;

    private Coroutine _routine;

    /// <summary>
    /// Starts (or restarts) the delayed save. Safe to wire directly into a UnityEvent.
    /// </summary>
    public void Run()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
        }
        _routine = StartCoroutine(SaveAfterDelay());
    }

    /// <summary>
    /// Writes the save immediately, skipping the settle delay. Used when restoring a
    /// solved state on load, where nothing needs to settle.
    /// </summary>
    public void RunInstant()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
        SaveManager.Instance?.Save();
    }

    private IEnumerator SaveAfterDelay()
    {
        yield return new WaitForSeconds(_settleDelay);
        _routine = null;
        SaveManager.Instance?.Save();
    }
}
