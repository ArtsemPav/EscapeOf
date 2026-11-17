using TMPro;
using UnityEngine;

/// <summary>
/// Writes the build version (Project Settings -> Player -> Version) into a
/// TMP label. Attach to any splash/loading/menu screen and assign the label.
/// </summary>
public class BuildVersionLabel : MonoBehaviour
{
    [Tooltip("Label that displays the build version, e.g. 'v1.2.3'.")]
    [SerializeField] private TextMeshProUGUI _versionLabel;

    [Tooltip("Optional prefix, e.g. 'v' or 'Version '.")]
    [SerializeField] private string _prefix = "v";

    private void Awake()
    {
        if (_versionLabel != null)
        {
            _versionLabel.text = $"{_prefix}{Application.version}";
        }
    }
}
