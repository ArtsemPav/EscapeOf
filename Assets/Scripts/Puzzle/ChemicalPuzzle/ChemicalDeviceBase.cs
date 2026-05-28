using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Abstract base class for chemical synthesis devices (centrifuge, burner, mixer).
/// Manages the busy state, result event, and provides helpers for animation coroutines.
/// </summary>
public abstract class ChemicalDeviceBase : MonoBehaviour
{
    /// <summary>Fired when the device finishes processing and produces an item.</summary>
    public event Action<ItemData> OnProcessComplete;

    /// <summary>True while the device is running a process cycle.</summary>
    public bool IsBusy { get; protected set; }

    /// <summary>Loads an item into the device. Called by ChemicalSynthesisController on drop.</summary>
    public abstract void LoadFlask(ItemData input);

    /// <summary>Starts processing the loaded item. Called by ChemicalSynthesisController after drop.</summary>
    public abstract void ProcessLoadedFlask();

    /// <summary>Fires OnProcessComplete and clears the busy flag.</summary>
    protected void CompleteWithResult(ItemData result)
    {
        IsBusy = false;
        OnProcessComplete?.Invoke(result);
    }

    /// <summary>
    /// Fires OnProcessComplete for each result in the list and clears the busy flag once.
    /// Use this when a single processing cycle produces multiple results (e.g. centrifuge with 3 slots).
    /// </summary>
    protected void CompleteWithResults(IReadOnlyList<ItemData> results)
    {
        IsBusy = false;
        foreach (var result in results)
            OnProcessComplete?.Invoke(result);
    }

    /// <summary>Waits for the given duration, then fires CompleteWithResult.</summary>
    protected IEnumerator AnimateAndComplete(float duration, ItemData result)
    {
        yield return new WaitForSeconds(duration);
        CompleteWithResult(result);
    }
}
