using UnityEngine;
using UnityEngine.InputSystem;

public class MenuScript : MonoBehaviour
{
    [SerializeField] private GameObject MenuIU;
  

    private void Start()
    {
        // Начальное состояние паузы при старте (если нужно)
        GameManager.Instance.SetPause(true);
        Pause();
    }
    private void OnEnable() {
        if (InputManager.Instance != null) {
            InputManager.Instance.OnMenuPerformed += OnToggleMenu;
        }
    }

    private void OnDisable() {
        if (InputManager.Instance != null) {
            InputManager.Instance.OnMenuPerformed -= OnToggleMenu;
        }
    }

    private void OnToggleMenu()
    {
        // Не обрабатываем ESC если открыта другая панель (инвентарь, превью и т.д.)
        if (!GameManager.Instance.IsPaused && UIManager.Instance != null && UIManager.Instance.IsAnyPanelOpen)
            return;

        GameManager.Instance.TogglePause();

        if (GameManager.Instance.IsPaused)
            Pause();
        else
            Resume();
    }

    private void Pause()
    {
        UIManager.Instance?.OpenPanel(MenuIU);
        AudioManager.Instance.PlayMenuMusic();
    }

    public void Resume()
    {
        UIManager.Instance?.ClosePanel(MenuIU);
        AudioManager.Instance.PlayGameMusic();
    }
}
