using System;
using UnityEngine;

/// <summary>
/// Wire simulation and rendering settings shared across all wires in the puzzle.
/// Configure on <see cref="ElectricPuzzleController"/> — passed to each wire on Init.
/// </summary>
[Serializable]
public class ElectricWireSettings
{
    [Tooltip("Number of Verlet simulation points. Higher = smoother curve, slightly more expensive.")]
    public int segmentCount = 20;

    [Range(1f, 2f)]
    [Tooltip("Wire rest length as a multiple of the straight-line distance. 1.0 = taut, higher = more sag.")]
    public float slackFactor = 1.08f;

    [Tooltip("Minimum total wire length so the wire stays visible even at very short distances.")]
    public float minWireLength = 0.08f;

    [Tooltip("Gravity applied to interior wire points (negative = downward, world units / s²).")]
    public float gravity = -9.81f;

    [Range(0f, 1f)]
    [Tooltip("Velocity damping per frame. Higher = wire settles faster with less bounce. 0.12 decays oscillations in ~0.3s at 60fps.")]
    public float damping = 0.12f;

    [Tooltip("Wire renderer thickness at the colored (start) terminal end.")]
    public float wireWidthStart = 0.007f;

    [Tooltip("Wire renderer thickness at the neutral (end) terminal end.")]
    public float wireWidthEnd = 0.007f;

    [Tooltip("Maximum drag radius from the starting terminal (world units). Prevents overstretching.")]
    public float maxDragDistance = 0.3f;

    [Tooltip("Minimum distance enforced between different wires to prevent them from passing through each other.")]
    public float repulsionRadius = 0.025f;
}
