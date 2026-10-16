using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen spider web overlay shown when the player touches a spider web.
/// Each new touch while a web is already visible spawns an additional,
/// randomly rotated web on top; every web fades out independently.
/// </summary>
public class SpiderWebOverlay : MonoBehaviour
{
    public static SpiderWebOverlay Instance { get; private set; }

    [Header("Overlay Settings")]
    [Tooltip("Full-screen Image with the spider web texture.")]
    [SerializeField] private Image webImage;

    [Tooltip("Fade out duration (seconds) after the main display time ends.")]
    [SerializeField] private float fadeOutDuration = 1.5f;

    [Tooltip("Начальный масштаб спрайта при появлении (1 = размер оверлея).")]
    [SerializeField, Range(0.1f, 1f)] private float startScale = 0.3f;

    [Tooltip("Сколько секунд длится «набрасывание» паутины на экран.")]
    [SerializeField] private float scaleInDuration = 0.4f;

    [Tooltip("Запас масштаба повёрнутых паутин, чтобы закрыть углы экрана.")]
    [SerializeField, Range(1f, 1.6f)] private float rotationCoverPadding = 1.05f;

    private sealed class ActiveWeb
    {
        public Image Image;
        public bool IsClone;
        public Coroutine Fade;
        public float CoverScale = 1f;
    }

    private readonly List<ActiveWeb> _activeWebs = new List<ActiveWeb>();

    private void Awake()
    {
        if (Instance != null && Instance != this) return; // клон: не перехватываем синглтон
        Instance = this;
        if (webImage != null) HideWeb(webImage);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Shows the overlay. If a web is already visible, spawns an additional
    /// randomly rotated web on top; each web fades out independently.
    /// </summary>
    public void Show(float duration)
    {
        if (webImage == null) return;

        bool alreadyHasWeb = _activeWebs.Count > 0;
        Image image = alreadyHasWeb ? CreateStackedWeb() : webImage;

        ActiveWeb web = new ActiveWeb { Image = image, IsClone = alreadyHasWeb };
        ApplyRandomRotation(web);
        _activeWebs.Add(web);

        image.enabled = true;
        image.color = Color.white;

        web.Fade = StartCoroutine(FadeOutAndHide(web, duration, scaleInDuration));
        StartCoroutine(ScaleIn(web));
    }

    /// <summary>
    /// Creates a copy of the base web image as a sibling so it stacks on top.
    /// </summary>
    private Image CreateStackedWeb()
    {
        Image clone = Instantiate(webImage, webImage.transform.parent);
        clone.name = "SpiderWebOverlay_Clone";
        clone.rectTransform.localRotation = Quaternion.identity;
        clone.rectTransform.localScale = Vector3.one;

        SpiderWebOverlay cloneController = clone.GetComponent<SpiderWebOverlay>();
        if (cloneController != null) Destroy(cloneController);

        return clone;
    }

    /// <summary>
    /// Rotates the web by a random angle and scales it so the rotated
    /// rectangle still covers the whole screen (no empty corners).
    /// </summary>
    private void ApplyRandomRotation(ActiveWeb web)
    {
        RectTransform rect = web.Image.rectTransform;
        float angle = Random.Range(0f, 360f);
        rect.localEulerAngles = new Vector3(0f, 0f, angle);

        Vector2 size = rect.rect.size;
        float rad = angle * Mathf.Deg2Rad;
        float cos = Mathf.Abs(Mathf.Cos(rad));
        float sin = Mathf.Abs(Mathf.Sin(rad));

        float coverX = (size.x * cos + size.y * sin) / Mathf.Max(size.x, 1f);
        float coverY = (size.y * cos + size.x * sin) / Mathf.Max(size.y, 1f);
        web.CoverScale = Mathf.Max(coverX, coverY) * rotationCoverPadding;
    }

    private void HideWeb(Image image)
    {
        image.color = new Color(1f, 1f, 1f, 0f);
        image.enabled = false;
        image.rectTransform.localScale = Vector3.one;
    }

    private IEnumerator ScaleIn(ActiveWeb web)
    {
        RectTransform rect = web.Image.rectTransform;
        float t = 0f;
        while (t < scaleInDuration)
        {
            t += Time.deltaTime;
            // EaseOutCubic — быстрый старт, плавное прибытие
            float progress = 1f - Mathf.Pow(1f - t / scaleInDuration, 3f);
            float s = Mathf.Lerp(startScale, web.CoverScale, progress);
            rect.localScale = new Vector3(s, s, s);
            yield return null;
        }
        rect.localScale = new Vector3(web.CoverScale, web.CoverScale, web.CoverScale);
    }

    private IEnumerator FadeOutAndHide(ActiveWeb web, float duration, float scaleInTime)
    {
        yield return new WaitForSeconds(duration + scaleInTime);

        float t = 0f;
        while (t < fadeOutDuration)
        {
            t += Time.deltaTime;
            float a = 1f - t / fadeOutDuration;
            web.Image.color = new Color(1f, 1f, 1f, a);
            yield return null;
        }

        if (web.IsClone) Destroy(web.Image.gameObject);
        else HideWeb(web.Image);

        _activeWebs.Remove(web);
        web.Fade = null;
    }
}
