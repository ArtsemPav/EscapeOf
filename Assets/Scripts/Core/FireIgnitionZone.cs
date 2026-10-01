using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Ignition zone trigger: when the player enters the collider, activates
/// groups of objects (e.g. torch pairs or fire wall segments) one by one
/// with a delay between groups. Used for staged fire ignition.
/// </summary>
public class FireIgnitionZone : MonoBehaviour
{
    /// <summary>A set of objects activated simultaneously (e.g. a pair of torches).</summary>
    [Serializable]
    public class IgnitionGroup
    {
        public GameObject[] objects;
    }

    [Header("Ignition")]
    [Tooltip("Groups of objects activated one by one when the player enters the zone.")]
    [SerializeField] private IgnitionGroup[] _groups;

    [Tooltip("Pause between groups, in seconds.")]
    [SerializeField] private float _groupInterval = 0.35f;

    private bool _triggered;

    private void Start()
    {
        if (_groups == null) return;
        foreach (IgnitionGroup group in _groups)
        {
            if (group?.objects == null) continue;
            foreach (GameObject obj in group.objects)
                if (obj != null) obj.SetActive(false);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered || other.GetComponentInParent<FPSController>() == null) return;
        _triggered = true;
        StartCoroutine(IgniteSequence());
    }

    private IEnumerator IgniteSequence()
    {
        if (_groups == null) yield break;

        foreach (IgnitionGroup group in _groups)
        {
            if (group?.objects == null) continue;

            foreach (GameObject obj in group.objects)
                if (obj != null) obj.SetActive(true);

            if (_groupInterval > 0f)
                yield return new WaitForSeconds(_groupInterval);
        }
    }
}
