using TMPro;
using UnityEngine;

/// <summary>
/// Reads the active code from a CodeLock and writes it to a World Space TMP text.
/// Attach this script to the note/display GameObject in the scene.
/// Wire CodeLock and CodeText in the Inspector.
/// </summary>
public class CodeHintDisplay : MonoBehaviour
{
    [SerializeField] private CodeLock _codeLock;
    [SerializeField] private TextMeshProUGUI _codeText;

    private void Start()
    {
        if (_codeLock != null && _codeText != null)
            _codeText.text = _codeLock.GetCode();
    }
}
