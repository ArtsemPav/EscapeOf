using UnityEngine;

/// <summary>
/// Вешается на звено падающей цепи. Отмечает первое касание «пола» —
/// любой поверхности с нормалью, смотрящей вверх (пол, порог, лежащий предмет).
/// Используется <see cref="ChainCuttable"/> для определения момента,
/// когда вся цепь улеглась и её можно заморозить.
/// </summary>
public class ChainLinkFloorProbe : MonoBehaviour
{
    // Порог вертикальности нормали контакта: поверхность считается полом,
    // если она достаточно горизонтальна (не стена и не бок ручки).
    private const float FloorNormalThreshold = 0.6f;

    /// <summary>Звено уже коснулось пола?</summary>
    public bool TouchedFloor { get; private set; }

    /// <summary>Отмечает касание пола при первом контакте с горизонтальной поверхностью.</summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (TouchedFloor)
            return;

        foreach (ContactPoint contact in collision.contacts)
        {
            // Нормаль смотрит вверх относительно мира — это пол или горизонтальная опора.
            if (contact.normal.y >= FloorNormalThreshold)
            {
                TouchedFloor = true;
                return;
            }
        }
    }
}
