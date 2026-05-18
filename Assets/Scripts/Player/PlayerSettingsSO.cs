using UnityEngine;

[CreateAssetMenu(fileName = "PlayerSettings", menuName = "Settings/PlayerSettings")]
public class PlayerSettingsSO : ScriptableObject
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float sprintSpamAdd = 1.5f;
    public float sprintSpamDecay = 2.5f;
    public float acceleration = 10f;
    public float deceleration = 10f;
    public float gravity = -9.81f;
    public float jumpHeight = 2f;

    [Header("Camera")]
    public float mouseSensitivity = 2f;
    public float cameraSmoothTime = 0.05f;

    [Header("Headbob — Idle (дыхание)")]
    [Tooltip("Частота дыхания в покое")]
    public float idleBobFrequency = 0.8f;
    [Tooltip("Вертикальная амплитуда дыхания")]
    public float idleBobAmplitudeY = 0.004f;
    [Tooltip("Горизонтальная амплитуда дыхания")]
    public float idleBobAmplitudeX = 0.001f;

    [Header("Headbob — Walk")]
    [Tooltip("Частота шагов при ходьбе")]
    public float walkBobFrequency = 2.2f;
    [Tooltip("Вертикальная амплитуда при ходьбе")]
    public float walkBobAmplitudeY = 0.008f;
    [Tooltip("Горизонтальная амплитуда при ходьбе (качание в стороны)")]
    public float walkBobAmplitudeX = 0.005f;
    [Tooltip("Наклон камеры при ходьбе (градусы)")]
    public float walkBobTilt = 0.4f;

    [Header("Headbob — Sprint")]
    [Tooltip("Частота шагов при спринте")]
    public float sprintBobFrequency = 3.2f;
    [Tooltip("Вертикальная амплитуда при спринте")]
    public float sprintBobAmplitudeY = 0.014f;
    [Tooltip("Горизонтальная амплитуда при спринте")]
    public float sprintBobAmplitudeX = 0.009f;
    [Tooltip("Наклон камеры при спринте (градусы)")]
    public float sprintBobTilt = 0.8f;

    [Header("Headbob — Smoothing")]
    [Tooltip("Скорость плавного перехода между состояниями headbob")]
    public float headbobTransitionSpeed = 6f;
    [Tooltip("Скорость возврата камеры в центр при остановке")]
    public float headbobResetSpeed = 8f;
}
