using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ScriptableObject describing a readable in-world document (note, journal, book, etc.).
/// Create instances via Assets > Create > Escape > Document Data.
/// </summary>
[CreateAssetMenu(fileName = "NewDocument", menuName = "Escape/Document Data")]
public class DocumentData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Заголовок документа — отображается крупно вверху панели.")]
    public string title = "Документ";

    [Header("Content")]
    [Tooltip("Каждый элемент списка — отдельная страница. Перелистываются кнопками или стрелками.")]
    [TextArea(4, 12)]
    public List<string> pages = new List<string> { string.Empty };

    [Header("Visual")]
    [Tooltip("3D-префаб документа (книга, журнал, записка). Показывается в изолированной сцене осмотра.")]
    public GameObject documentPrefab;

    [Tooltip("Множитель масштаба модели в превью. 1 = реальный размер, >1 = больше, <1 = меньше.")]
    public float previewScale = 1f;

    [Tooltip("Затемнение 3D-превью документа. 0 = нет затемнения, 1 = полностью чёрный.")]
    [Range(0f, 1f)] public float previewDimAmount = 0.35f;

    [Header("Inspection")]
    [Tooltip("Если включено — переопределяет глобальную начальную ротацию DocumentData своей previewRotation для этого документа.")]
    public bool useCustomPreviewRotation;

    [Tooltip("Эйлеровы углы начальной ротации в превью. Активно только когда useCustomPreviewRotation = true.")]
    public Vector3 previewRotation = new Vector3(15f, -35f, 0f);

    [Header("Typography — Body")]
    [Tooltip("Шрифт основного текста. Если null — используется шрифт по умолчанию компонента.")]
    public TMP_FontAsset font;

    [Tooltip("Размер шрифта основного текста.")]
    public float fontSize = 16f;

    [Tooltip("Цвет основного текста.")]
    public Color fontColor = Color.white;

    [Tooltip("Выравнивание основного текста.")]
    public TextAlignmentOptions textAlignment = TextAlignmentOptions.Left;

    [Header("Typography — Title")]
    [Tooltip("Шрифт заголовка. Если null — используется шрифт по умолчанию компонента.")]
    public TMP_FontAsset titleFont;

    [Tooltip("Размер шрифта заголовка.")]
    public float titleFontSize = 24f;

    [Tooltip("Цвет заголовка.")]
    public Color titleColor = Color.white;

    [Tooltip("Выравнивание заголовка.")]
    public TextAlignmentOptions titleAlignment = TextAlignmentOptions.Center;

    [Header("Audio")]
    [Tooltip("Звук, проигрываемый при открытии документа. Опционально.")]
    public AudioClip openClip;

    [Tooltip("Звук перелистывания страницы. Опционально.")]
    public AudioClip pageTurnClip;

    [Tooltip("Звук, проигрываемый при закрытии документа. Опционально.")]
    public AudioClip closeClip;
}
