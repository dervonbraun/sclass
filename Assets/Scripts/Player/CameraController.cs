using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public PlayerSettingsSO settings;
    public Transform playerBody;
    public PlayerMovement playerMovement;

    [Header("Input Actions")]
    public InputActionReference lookAction;

    // --- Rotation ---
    private float xRotation = 0f;
    private float yRotation = 0f;
    private float currentXRotation = 0f;
    private float currentYRotation = 0f;
    private float xRotVelocity = 0f;
    private float yRotVelocity = 0f;

    // --- Headbob ---
    private float bobTimer = 0f;

    // Текущие сглаженные параметры
    private float currentFrequency = 0f;
    private float currentAmplitudeY = 0f;
    private float currentAmplitudeX = 0f;
    private float currentTilt = 0f;

    // Текущее смещение позиции и наклон камеры
    private Vector3 currentBobOffset = Vector3.zero;
    private float currentBobTilt = 0f;
    private float bobTiltVelocity = 0f;

    private Vector3 defaultLocalPos;

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        defaultLocalPos = transform.localPosition;

        if (playerBody != null)
        {
            yRotation = playerBody.localEulerAngles.y;
            currentYRotation = yRotation;
        }

        if (lookAction == null) Debug.LogWarning("CameraController: Не назначен lookAction!");
        
        if (playerMovement == null)
        {
            playerMovement = GetComponentInParent<PlayerMovement>();
            if (playerMovement == null)
            {
                Debug.LogWarning("CameraController: Не назначен и не найден PlayerMovement!");
            }
        }
    }

    private void Update()
    {
        if (settings == null || playerBody == null) return;

        HandleRotation();
        HandleHeadbob();
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void HandleRotation()
    {
        Vector2 lookInput = lookAction != null ? lookAction.action.ReadValue<Vector2>() : Vector2.zero;

        float mouseX = lookInput.x * settings.mouseSensitivity * 0.1f;
        float mouseY = lookInput.y * settings.mouseSensitivity * 0.1f;

        yRotation += mouseX;
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        if (settings.cameraSmoothTime > 0)
        {
            currentXRotation = Mathf.SmoothDamp(currentXRotation, xRotation, ref xRotVelocity, settings.cameraSmoothTime);
            currentYRotation = Mathf.SmoothDamp(currentYRotation, yRotation, ref yRotVelocity, settings.cameraSmoothTime);
        }
        else
        {
            currentXRotation = xRotation;
            currentYRotation = yRotation;
        }

        // Вертикаль — камера, горизонталь — тело игрока
        transform.localRotation = Quaternion.Euler(currentXRotation, 0f, 0f);
        playerBody.localRotation = Quaternion.Euler(0f, currentYRotation, 0f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void HandleHeadbob()
    {
        if (playerMovement == null) return;

        float speed = playerMovement.GetCurrentVelocity().magnitude;
        bool isGrounded = playerMovement.IsGrounded();

        // --- Определяем целевые параметры ---
        float targetFrequency;
        float targetAmplY;
        float targetAmplX;
        float targetTilt;

        if (!isGrounded)
        {
            // В воздухе — гасим headbob
            targetFrequency = 0f;
            targetAmplY = 0f;
            targetAmplX = 0f;
            targetTilt = 0f;
        }
        else if (speed > 0.1f)
        {
            // Вычисляем коэффициент бега на основе текущей скорости
            float walkSpeed = settings.walkSpeed;
            float sprintSpeed = settings.sprintSpeed;
            float sprintFactor = 0f;

            if (sprintSpeed > walkSpeed)
            {
                // 0 = ходьба, 1 = максимальный бег
                sprintFactor = Mathf.Clamp01((speed - walkSpeed) / (sprintSpeed - walkSpeed));
            }

            // Плавно смешиваем параметры ходьбы и бега в зависимости от реальной скорости
            targetFrequency = Mathf.Lerp(settings.walkBobFrequency, settings.sprintBobFrequency, sprintFactor);
            targetAmplY = Mathf.Lerp(settings.walkBobAmplitudeY, settings.sprintBobAmplitudeY, sprintFactor);
            targetAmplX = Mathf.Lerp(settings.walkBobAmplitudeX, settings.sprintBobAmplitudeX, sprintFactor);
            targetTilt = Mathf.Lerp(settings.walkBobTilt, settings.sprintBobTilt, sprintFactor);
        }
        else
        {
            // Idle — дыхание
            targetFrequency = settings.idleBobFrequency;
            targetAmplY = settings.idleBobAmplitudeY;
            targetAmplX = settings.idleBobAmplitudeX;
            targetTilt = 0f;
        }

        // --- Плавно меняем параметры ---
        float t = settings.headbobTransitionSpeed * Time.deltaTime;
        currentFrequency = Mathf.Lerp(currentFrequency, targetFrequency, t);
        currentAmplitudeY = Mathf.Lerp(currentAmplitudeY, targetAmplY, t);
        currentAmplitudeX = Mathf.Lerp(currentAmplitudeX, targetAmplX, t);
        currentTilt = Mathf.Lerp(currentTilt, targetTilt, t);

        // --- Считаем таймер ---
        bobTimer += Time.deltaTime * currentFrequency;

        // --- Lissajous-кривая: "восьмёрка" ---
        // X: sin(t)        — качание влево/вправо (1 полный цикл на шаг)
        // Y: sin(2*t)      — прыжок вверх на каждый шаг (2 цикла = форма восьмёрки)
        float sinX = Mathf.Sin(bobTimer);
        float sinY = Mathf.Sin(bobTimer * 2f);

        float offsetX = sinX * currentAmplitudeX;
        float offsetY = sinY * currentAmplitudeY;

        // --- Наклон камеры по Z (в такт горизонтальному качанию) ---
        float targetBobTilt = -sinX * currentTilt;
        currentBobTilt = Mathf.SmoothDamp(currentBobTilt, targetBobTilt, ref bobTiltVelocity, 0.05f);

        // --- Целевое смещение позиции ---
        Vector3 targetBobOffset = new Vector3(offsetX, offsetY, 0f);

        // Плавно интерполируем смещение (важно при резкой остановке — камера не прыгает)
        float resetSpeed = (speed < 0.1f && isGrounded) ? settings.headbobResetSpeed : settings.headbobTransitionSpeed * 2f;
        currentBobOffset = Vector3.Lerp(currentBobOffset, targetBobOffset, resetSpeed * Time.deltaTime);

        // --- Применяем ---
        transform.localPosition = defaultLocalPos + currentBobOffset;

        // Z-наклон добавляем к текущему вращению камеры (не перезаписываем xRotation)
        transform.localRotation = Quaternion.Euler(currentXRotation, 0f, currentBobTilt);
    }

    private void OnEnable()
    {
        if (lookAction != null) lookAction.action.Enable();
    }

    private void OnDisable()
    {
        if (lookAction != null) lookAction.action.Disable();
    }
}
