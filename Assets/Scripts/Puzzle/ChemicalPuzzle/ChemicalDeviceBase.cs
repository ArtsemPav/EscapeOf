using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Abstract base class for chemical synthesis devices (centrifuge, burner, mixer).
/// Manages the busy state, result event, and provides helpers for animation coroutines.
///
/// Thread-safety / re-entrancy guarantees:
/// - OnProcessComplete fires at most once per processing cycle.
/// - IsBusy stays true until all results have been fired, preventing re-entry.
/// - _hasFiredResult guards against duplicate event invocations from coroutine
///   restarts or double-completion paths.
/// </summary>
public abstract class ChemicalDeviceBase : MonoBehaviour
{
    /// <summary>Fired when the device finishes processing and produces an item.</summary>
    public event Action<ItemData> OnProcessComplete;

    /// <summary>True while the device is running a process cycle.</summary>
    public bool IsBusy { get; protected set; }

    // Guard flag: prevents OnProcessComplete from firing more than once per cycle.
    private bool _hasFiredResult;

    /// <summary>Loads an item into the device. Called by ChemicalSynthesisController on drop.</summary>
    public abstract void LoadFlask(ItemData input);

    /// <summary>Starts processing the loaded item. Called by ChemicalSynthesisController after drop.</summary>
    public abstract void ProcessLoadedFlask();

    /// <summary>
    /// Marks the start of a processing cycle. Sets IsBusy and resets the
    /// duplicate-fire guard. Call this at the beginning of every processing
    /// coroutine before any yield.
    /// </summary>
    protected void BeginProcess()
    {
        IsBusy = true;
        _hasFiredResult = false;
    }

    /// <summary>
    /// Fires OnProcessComplete for a single result and clears the busy flag.
    /// Guarded against duplicate invocations — if called more than once per
    /// cycle, subsequent calls are silently ignored.
    /// </summary>
    protected void CompleteWithResult(ItemData result)
    {
        if (_hasFiredResult) return;
        _hasFiredResult = true;
        IsBusy = false;
        OnProcessComplete?.Invoke(result);
    }

    /// <summary>
    /// Fires OnProcessComplete for each result in the list.
    /// IsBusy stays true until all results have been fired, then is cleared once.
    /// Guarded against duplicate invocations — if called more than once per
    /// cycle, subsequent calls are silently ignored.
    /// </summary>
    protected void CompleteWithResults(IReadOnlyList<ItemData> results)
    {
        if (_hasFiredResult) return;
        _hasFiredResult = true;

        foreach (var result in results)
            OnProcessComplete?.Invoke(result);

        IsBusy = false;
    }

    /// <summary>Waits for the given duration, then fires CompleteWithResult.</summary>
    protected IEnumerator AnimateAndComplete(float duration, ItemData result)
    {
        yield return new WaitForSeconds(duration);
        CompleteWithResult(result);
    }
}
