using UnityEngine;

/// <summary>
/// ScriptableObject describing a readable in-world document (note, journal, book, etc.).
/// Text content is baked into the prefab as 3D TextMeshPro components.
/// Create instances via Assets > Create > Escape > Document Data.
/// </summary>
[CreateAssetMenu(fileName = "NewDocument", menuName = "Escape/Document Data")]
public class DocumentData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Если включено — это короткая записка: только 1 страница, без стрелок навигации и анимации переворота.")]
    public bool isNote;

    [Tooltip("Префикс локализации страниц: ключи 'note.<prefix>.page<n>' (n с 0, по порядку сортировки Pages). " +
             "Когда ключ есть в таблице строк, переведённый текст заменяет запечённый TMP-текст страницы; " +
             "пока ключа нет — показывается оригинал. Пусто — документ не локализуется.")]
    public string localizationPrefix;

    [Header("Visual")]
    [Tooltip("3D-префаб документа (книга, журнал, записка). Текст запечён внутри как дочерние TextMeshPro (3D) объекты.")]
    public GameObject documentPrefab;

    [Tooltip("Множитель масштаба модели в превью. 1 = реальный размер, >1 = больше, <1 = меньше.")]
    public float previewScale = 1f;

    [Tooltip("Затемнение 3D-превью документа. 0 = нет затемнения, 1 = полностью чёрный.")]
    [Range(0f, 1f)] public float previewDimAmount = 0.35f;

    [Header("Inspection")]
    [Tooltip("Если включено — переопределяет глобальную начальную ротацию своей previewRotation для этого документа.")]
    public bool useCustomPreviewRotation;

    [Tooltip("Эйлеровы углы начальной ротации в превью. Активно только когда useCustomPreviewRotation = true.")]
    public Vector3 previewRotation = new Vector3(15f, -35f, 0f);

    [Header("Audio")]
    [Tooltip("Звук, проигрываемый при открытии документа. Опционально.")]
    public AudioClip openClip;

    [Tooltip("Звук перелистывания страницы. Опционально.")]
    public AudioClip pageTurnClip;

    [Tooltip("Звук, проигрываемый при закрытии документа. Опционально.")]
    public AudioClip closeClip;

    /// <summary>
    /// Initial preview rotation for this document: per-document previewRotation when
    /// useCustomPreviewRotation is set, identity otherwise.
    /// </summary>
    public Quaternion GetPreviewRotation()
    {
        return useCustomPreviewRotation ? Quaternion.Euler(previewRotation) : Quaternion.identity;
    }
}
