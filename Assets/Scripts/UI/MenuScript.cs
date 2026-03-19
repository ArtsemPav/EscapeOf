using UnityEngine;
using UnityEngine.InputSystem;

public class MenuScript : MonoBehaviour
{
    [SerializeField] private GameObject MenuIU;
    private PlayerInputActions _input;
    private bool _isPaused;

    private void Awake()
    {
        _input = new PlayerInputActions();
    }

    private void OnEnable()
    {
        _input.Enable();
    }

    private void OnDisable()
    {
        _input.Disable();
    }

    private void Start()
    {
        _input.Player.Menu.performed += MenuInput;
        _isPaused = true;
        Pause();
    }

    private void OnDestroy()
    {
        _input.Player.Menu.performed -= MenuInput;
    }

    private void MenuInput(InputAction.CallbackContext context)
    {
        // Не обрабатываем ESC если открыта другая панель (инвентарь, превью и т.д.) —
        // ей принадлежит управление курсором, пусть закроется своим способом.
        if (!_isPaused && UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen)
            return;

        _isPaused = !_isPaused;

        if (_isPaused)
            Pause();
        else
            Resume();
    }

    private void Pause()
    {
        Time.timeScale = 0f;
        UIManager.Instance?.OpenPanel(MenuIU);
        AudioManager.Instance.PlayMenuMusic();
    }

    public void Resume()
    {
        Time.timeScale = 1f;
        UIManager.Instance?.ClosePanel(MenuIU);
        AudioManager.Instance.PlayGameMusic();
    }
}
