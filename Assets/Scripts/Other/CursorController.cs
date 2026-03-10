using UnityEngine;

public class CursorController : MonoBehaviour
{
    private void Awake() {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
