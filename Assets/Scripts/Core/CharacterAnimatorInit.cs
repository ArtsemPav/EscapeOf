using UnityEngine;

/// <summary>
/// Sets the Animator integer parameter 'State' to a configured value on Awake,
/// so the character starts in the correct pose without waiting for any other system.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimatorInit : MonoBehaviour
{
    private const string StateParam = "State";

    [Tooltip("Animator 'State' integer value to apply on Awake.\n" +
             "0 = LowCrawl | 1 = ActionPose | 2 = ActionPose2 | 3 = Sitting | 4 = Run")]
    [SerializeField] private int _initialState = 3;

    private void Awake()
    {
        GetComponent<Animator>().SetInteger(StateParam, _initialState);
    }
}
