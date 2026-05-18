using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public PlayerSettingsSO settings;

    [Header("Input Actions (Обязательно назначьте!)")]
    public InputActionReference moveAction;
    public InputActionReference jumpAction;
    public InputActionReference sprintAction;

    private CharacterController controller;
    private Vector2 currentInput;
    private Vector3 currentVelocity;
    private float verticalVelocity;
    private float currentSprintOffset;
    
    private void Start()
    {
        controller = GetComponent<CharacterController>();
        if (moveAction == null) Debug.LogWarning("PlayerMovement: Не назначен moveAction!");
    }

    private void Update()
    {
        if (settings == null) return;

        Move();
    }

    private void Move()
    {
        Vector2 targetInput = moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        float accelRate = targetInput.sqrMagnitude > 0 ? settings.acceleration : settings.deceleration;
        if (accelRate > 0)
            currentInput = Vector2.Lerp(currentInput, targetInput, accelRate * Time.deltaTime);
        else
            currentInput = targetInput; // На случай если в настройках ускорение равно 0

        Vector3 moveDirection = transform.right * currentInput.x + transform.forward * currentInput.y;
        
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        if (sprintAction != null && sprintAction.action.WasPressedThisFrame())
        {
            currentSprintOffset += settings.sprintSpamAdd;
        }

        currentSprintOffset -= settings.sprintSpamDecay * Time.deltaTime;
        currentSprintOffset = Mathf.Clamp(currentSprintOffset, 0f, settings.sprintSpeed - settings.walkSpeed);

        float targetSpeed = settings.walkSpeed + currentSprintOffset;

        currentVelocity = moveDirection * targetSpeed;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = -2f; 
            }

            if (jumpAction != null && jumpAction.action.WasPressedThisFrame())
            {
                verticalVelocity = Mathf.Sqrt(settings.jumpHeight * -2f * settings.gravity);
            }
        }
        else
        {
            verticalVelocity += settings.gravity * Time.deltaTime;
        }

        Vector3 finalMove = currentVelocity + Vector3.up * verticalVelocity;
        controller.Move(finalMove * Time.deltaTime);
    }
    
    public Vector3 GetCurrentVelocity()
    {
        return currentVelocity;
    }
    
    public bool IsGrounded()
    {
        return controller != null && controller.isGrounded;
    }

    private void OnEnable()
    {
        if (moveAction != null) moveAction.action.Enable();
        if (jumpAction != null) jumpAction.action.Enable();
        if (sprintAction != null) sprintAction.action.Enable();
    }

    private void OnDisable()
    {
        if (moveAction != null) moveAction.action.Disable();
        if (jumpAction != null) jumpAction.action.Disable();
        if (sprintAction != null) sprintAction.action.Disable();
    }
}
