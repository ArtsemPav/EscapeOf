using UnityEngine;

public class ContinuousRotation : MonoBehaviour
{
    [Header("Настройки вращения")]
    [Tooltip("Скорость вращения по осям X, Y, Z в градусах в секунду")]
    public Vector3 rotationSpeed = new Vector3(0, 0, 90); // По умолчанию 90 град/сек по оси Z (удобно для 2D)

    void Update()
    {
        // Вращаем объект относительно локальных осей с учетом времени между кадрами
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);
    }
}