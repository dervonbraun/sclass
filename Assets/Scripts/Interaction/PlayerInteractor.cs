using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using Sclass.UI;

/// <summary>
/// Raycast-взаимодействие игрока.
/// Наведение на IInteractable → вызывает OnHoverEnter/Exit автоматически.
/// E — подтверждение/покупка. Стрелки вверх/вниз — навигация по терминалу.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("Raycast")]
    public Transform CameraTransform;
    public float InteractRange = 3f;
    public LayerMask InteractMask = -1;

    [Header("Input")]
    public InputActionReference InteractAction;
    [Tooltip("Vector2 — вертикальная ось (стрелки вверх/вниз).")]
    public InputActionReference NavigateAction;

    [Header("Prompt UI (Screen Space)")]
    public GameObject PromptPanel;
    public TMP_Text   PromptLabel;

    [Header("Player References")]
    public PlayerWallet             Wallet;
    public Sclass.EffectsSystem.ElementalMutationManager Mutations;
    public WeaponHolder             WeaponHolder;

    [Header("Debug")]
    public bool DebugMode = false;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private IInteractable _hovered;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (InteractAction != null) { InteractAction.action.Enable();  InteractAction.action.started   += OnInteractStarted; }
        if (NavigateAction != null) { NavigateAction.action.Enable();  NavigateAction.action.performed += OnNavigatePerformed; }
    }

    private void OnDisable()
    {
        if (InteractAction != null) { InteractAction.action.started   -= OnInteractStarted;  InteractAction.action.Disable(); }
        if (NavigateAction != null) { NavigateAction.action.performed -= OnNavigatePerformed; NavigateAction.action.Disable(); }
    }

    private void Update()
    {
        UpdateHover();
    }

    // ── Hover ─────────────────────────────────────────────────────────────────

    private void UpdateHover()
    {
        Transform origin = CameraTransform != null ? CameraTransform : transform;
        IInteractable hit = null;

        if (Physics.Raycast(origin.position, origin.forward, out RaycastHit info, InteractRange, InteractMask))
        {
            hit = info.collider.GetComponentInParent<IInteractable>();
            if (DebugMode && hit == null)
                Debug.Log($"[Interactor] Hit '{info.collider.name}' but no IInteractable found.");
        }

        if (hit == _hovered) return;

        // Уходим со старого
        if (_hovered != null)
        {
            _hovered.OnHoverExit(this);
            if (DebugMode) Debug.Log($"[Interactor] HoverExit: {(_hovered as MonoBehaviour)?.name}");
        }

        _hovered = hit;

        // Входим на новый
        if (_hovered != null)
        {
            _hovered.OnHoverEnter(this);
            if (DebugMode) Debug.Log($"[Interactor] HoverEnter: {(_hovered as MonoBehaviour)?.name}");
        }

        UpdatePrompt();
    }

    private void UpdatePrompt()
    {
        if (_hovered == null) { HidePrompt(); return; }

        if (_hovered is TerminalShopUI)
            SetPrompt("[↑↓] Выбрать   [E] Купить");
        else
            SetPrompt($"[E] {_hovered.GetPrompt()}");
    }

    // ── Input callbacks ───────────────────────────────────────────────────────

    private void OnInteractStarted(InputAction.CallbackContext ctx)
    {
        if (DebugMode) Debug.Log($"[Interactor] E pressed. Hovered={_hovered != null}");
        _hovered?.Interact(this);
    }

    private void OnNavigatePerformed(InputAction.CallbackContext ctx)
    {
        if (_hovered is not TerminalShopUI terminal) return;

        float y = ctx.ReadValue<Vector2>().y;
        if      (y >  0.1f) terminal.Navigate(-1);
        else if (y < -0.1f) terminal.Navigate(1);
    }

    // ── Prompt helpers ────────────────────────────────────────────────────────

    private void SetPrompt(string text)
    {
        if (PromptPanel != null) PromptPanel.SetActive(true);
        if (PromptLabel != null) PromptLabel.text = text;
    }

    private void HidePrompt()
    {
        if (PromptPanel != null) PromptPanel.SetActive(false);
    }

    private void OnDrawGizmosSelected()
    {
        Transform origin = CameraTransform != null ? CameraTransform : transform;
        Gizmos.color = _hovered != null ? Color.green : Color.cyan;
        Gizmos.DrawRay(origin.position, origin.forward * InteractRange);
    }
}
