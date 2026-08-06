using UnityEngine;

/// <summary>
/// Rotates child rings to visually represent the code from a DigitalLockSystem.
/// Each digit of the code maps to a ring: digit 0 = Ring 1, digit 1 = Ring 2, etc.
/// Rotation is calculated as digit * DEGREES_PER_DIGIT on the local Z axis.
/// </summary>
public class RingCodeIndicator : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Degrees of rotation per code digit value (e.g. 30 means digit 1 = 30, digit 2 = 60).")]
    [SerializeField] private float _degreesPerDigit = 30f;

    [Tooltip("Smooth rotation speed in degrees per second. 0 = instant.")]
    [SerializeField] private float _rotationSpeed = 0f;

    [Header("References")]
    [Tooltip("The digital lock system to read the code from.")]
    [SerializeField] private DigitalLockSystem _digitalLockSystem;

    [Tooltip("Rings ordered from first digit to last. If empty, auto-collects child Transforms named 'Ring'.")]
    [SerializeField] private Transform[] _rings;

    private Quaternion[] _targetRotations;

    private void Awake()
    {
        if (_rings == null || _rings.Length == 0)
        {
            CollectRings();
        }
    }

    private void Start()
    {
        ApplyCode();
    }

    /// <summary>
    /// Reads the code from DigitalLockSystem and sets target rotations for each ring.
    /// Call this after the code has been (re)generated.
    /// </summary>
    public void ApplyCode()
    {
        if (_digitalLockSystem == null || _rings == null || _rings.Length == 0) return;

        string code = _digitalLockSystem.ActiveCode;
        if (string.IsNullOrEmpty(code)) return;

        _targetRotations = new Quaternion[_rings.Length];

        for (int i = 0; i < _rings.Length; i++)
        {
            float angle = 0f;

            if (i < code.Length && char.IsDigit(code[i]))
            {
                int digit = code[i] - '0';
                angle = digit * _degreesPerDigit;
            }

            _targetRotations[i] = Quaternion.Euler(0f, 0f, angle);

            if (_rotationSpeed <= 0f)
            {
                _rings[i].localRotation = _targetRotations[i];
            }
        }
    }

    private void Update()
    {
        if (_rotationSpeed <= 0f || _targetRotations == null) return;

        for (int i = 0; i < _rings.Length; i++)
        {
            if (i >= _targetRotations.Length) break;
            _rings[i].localRotation = Quaternion.RotateTowards(
                _rings[i].localRotation,
                _targetRotations[i],
                _rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Auto-collects child Transforms whose name contains "Ring".
    /// </summary>
    private void CollectRings()
    {
        var found = new System.Collections.Generic.List<Transform>();
        foreach (Transform child in transform)
        {
            if (child.name.Contains("Ring"))
            {
                found.Add(child);
            }
        }
        _rings = found.ToArray();
    }
}
