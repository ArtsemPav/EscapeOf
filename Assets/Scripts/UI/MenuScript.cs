using UnityEngine;
using UnityEngine.InputSystem;

public class MenuScript : MonoBehaviour
{
    [SerializeField] private GameObject MenuIU;
    private PlayerInputActions _input;
    private bool _isPaused;

    private void Awake() {
        _input = new PlayerInputActions();
    }

    private void OnEnable() {
        _input.Enable();
    }

    private void OnDisable() {
        _input.Disable();
    }

    private void Start() {
        _input.Player.Menu.performed += MenuInput;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
    private void OnDestroy() {
        _input.Player.Menu.performed -= MenuInput;
    }

    private void MenuInput(InputAction.CallbackContext context) {
        _isPaused = !_isPaused;
        if (_isPaused) {
            Pause();
        } else {
            Resume();
        }
            
    }
    private void Pause() {
        MenuIU.SetActive(true);
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;
    }

    private void Resume() {
        MenuIU.SetActive(false);
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
