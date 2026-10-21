using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A single delayed action: waits Delay seconds, then invokes the UnityEvent.
/// </summary>
[System.Serializable]
public class DelayedAction
{
    [SerializeField, Min(0f)] private float _delay = 0f;
    [SerializeField] private UnityEvent _action = new UnityEvent();

    public float Delay => _delay;
    public UnityEvent Action => _action;
}

/// <summary>
/// Runs a configurable list of UnityEvents, each with its own delay.
/// Wire it into any puzzle's "On Solved" event by calling <see cref="Run"/>.
/// </summary>
public class DelayedActionSequence : MonoBehaviour
{
    [SerializeField] private DelayedAction[] _actions;

    [Tooltip("If true, all actions count their delays from the moment Run() is called. If false, each action starts after the previous one finishes (delays act as gaps between actions).")]
    [SerializeField] private bool _runInParallel = true;

    private Coroutine _sequenceRoutine;

    /// <summary>
    /// Starts executing the configured action list. A repeated call restarts the sequence from scratch.
    /// </summary>
    public void Run()
    {
        if (_sequenceRoutine != null)
        {
            StopCoroutine(_sequenceRoutine);
        }
        _sequenceRoutine = StartCoroutine(RunSequenceRoutine());
    }

    /// <summary>
    /// Invokes every action immediately, ignoring delays. Use for restoring a solved
    /// state on save load, where delayed transitions and physics side effects are unwanted.
    /// </summary>
    public void RunInstant()
    {
        if (_sequenceRoutine != null)
        {
            StopCoroutine(_sequenceRoutine);
        }
        _sequenceRoutine = null;

        if (_actions == null)
        {
            return;
        }

        for (int i = 0; i < _actions.Length; i++)
        {
            _actions[i].Action?.Invoke();
        }
    }

    private IEnumerator RunSequenceRoutine()
    {
        if (_runInParallel)
        {
            for (int i = 0; i < _actions.Length; i++)
            {
                StartCoroutine(RunSingleActionRoutine(_actions[i]));
            }
            yield break;
        }

        for (int i = 0; i < _actions.Length; i++)
        {
            yield return RunSingleActionRoutine(_actions[i]);
        }
    }

    private IEnumerator RunSingleActionRoutine(DelayedAction delayedAction)
    {
        if (delayedAction.Delay > 0f)
        {
            yield return new WaitForSeconds(delayedAction.Delay);
        }
        delayedAction.Action?.Invoke();
    }
}
