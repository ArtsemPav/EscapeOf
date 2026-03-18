using System.Collections;
using UnityEngine;
using TMPro;

public class TypewriterEffectPro : MonoBehaviour {
    private TMP_Text _textMeshPro;

    [Header("Настройки")]
    [SerializeField] private float typingSpeed = 0.05f;
    [SerializeField] private bool playOnStart = true;

    private string _fullText;
    private int _currentVisibleCharacter;

    void Awake() {
        _textMeshPro = GetComponent<TMP_Text>();
    }

    void Start() {
        if (playOnStart) {
            StartTypewriter();
        }
    }

    public void StartTypewriter() {
        _fullText = _textMeshPro.text;
        StartCoroutine(PlayTypewriter());
    }

    IEnumerator PlayTypewriter() {
        // Сброс счетчика
        _currentVisibleCharacter = 0;
        // Устанавливаем полный текст, но скрываем все символы
        _textMeshPro.text = _fullText;
        _textMeshPro.maxVisibleCharacters = 0;

        // Ждем один кадр, чтобы текстовый движок успел обработать Rich Text теги
        yield return null;

        // Получаем общее количество символов для печати
        // (это количество учитывает символы, а не длину строки с тегами)
        int totalCharacters = _textMeshPro.textInfo.characterCount;

        // Пока не напечатали все символы
        while (_currentVisibleCharacter < totalCharacters) {
            // Увеличиваем количество видимых символов
            _currentVisibleCharacter++;
            _textMeshPro.maxVisibleCharacters = _currentVisibleCharacter;

            // Ждем
            yield return new WaitForSeconds(typingSpeed);
        }
    }

    // Метод для мгновенного отображения всего текста (пропуск анимации)
    public void SkipToEnd() {
        StopAllCoroutines();
        if (_textMeshPro != null) {
            _textMeshPro.maxVisibleCharacters = _textMeshPro.textInfo.characterCount;
        }
    }
}