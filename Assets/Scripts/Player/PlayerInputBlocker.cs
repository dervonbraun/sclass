using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputBlocker : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private string actionMapName = "Player";

    public bool IsInputBlocked { get; private set; }
    public event System.Action<bool> OnInputBlockStateChanged;

    public void SetInputBlocked(bool blocked)
    {
        if (IsInputBlocked == blocked) return;

        IsInputBlocked = blocked;

        var map = inputActions != null ? inputActions.FindActionMap(actionMapName) : null;

        if (blocked)
        {
            map?.Disable();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        else
        {
            map?.Enable();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        OnInputBlockStateChanged?.Invoke(IsInputBlocked);
    }
}