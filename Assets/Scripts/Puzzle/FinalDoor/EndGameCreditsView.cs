using System.Collections;
using UnityEngine;

/// <summary>
/// Full-screen credits overlay for the end of the game.
/// A Canvas with a black background and a centered TextMeshPro label.
/// The whole canvas stays active all game with alpha 0; Show() fades it in
/// and keeps it visible (input is already disabled by EndGameSequence).
/// </summary>
public class EndGameCreditsView : MonoBehaviour
{
    public static EndGameCreditsView Instance { get; private set; }

    [Tooltip("CanvasGroup on this canvas used for the fade-in.")]
    [SerializeField] private CanvasGroup _canvasGroup;

    [Tooltip("Fade-in duration of the credits.")]
    [SerializeField, Min(0.1f)] private float _fadeInDuration = 2f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Fades the credits in over the configured duration.
    /// The view stays visible afterwards.
    /// </summary>
    public Coroutine Show()
    {
        return StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        if (_canvasGroup == null) yield break;

        float elapsed = 0f;
        while (elapsed < _fadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            _canvasGroup.alpha = Mathf.Clamp01(elapsed / _fadeInDuration);
            yield return null;
        }

        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;
    }
}
