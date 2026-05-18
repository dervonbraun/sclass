using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Менеджер оружия игрока.
/// Хранит массив из 2 слотов, переключение через клавиши 1/2 (или Q).
/// Каждый слот — prefab; при переключении текущее оружие уничтожается,
/// новое спавнится и получает все зависимости через Initialize().
/// </summary>
public class WeaponHolder : MonoBehaviour
{
    [Header("Ссылки сцены")]
    [Tooltip("Пустой GameObject у рук / перед камерой — сюда спавнится оружие")]
    public Transform weaponSlot;

    [Tooltip("Transform камеры (Camera или её дочерний объект)")]
    public Transform playerCamera;

    [Tooltip("Компонент отдачи (на дочернем объекте камеры — RecoilPivot)")]
    public CameraRecoil cameraRecoil;

    [Header("Слоты оружия (2 штуки)")]
    [Tooltip("Prefab оружия для слота 1 и 2")]
    public GameObject[] weaponLoadout = new GameObject[2];

    [Header("Input — Стрельба / Перезарядка / Прицел")]
    public InputActionReference fireAction;
    public InputActionReference reloadAction;
    public InputActionReference aimAction;

    [Header("Input — Переключение слотов")]
    [Tooltip("Клавиша слота 1 (например, «1» или «Alpha1»)")]
    public InputActionReference slot1Action;
    [Tooltip("Клавиша слота 2 (например, «2» или «Alpha2»)")]
    public InputActionReference slot2Action;
    [Tooltip("Переключатель (предыдущий/следующий, например Q)")]
    public InputActionReference cycleAction;

    // ── Runtime ────────────────────────────────────────────────

    private WeaponBase _activeWeapon;
    private int        _activeSlot = -1;     // -1 = ничего не экипировано

    public WeaponBase ActiveWeapon  => _activeWeapon;
    public int        ActiveSlotIndex => _activeSlot;

    // ── Жизненный цикл ─────────────────────────────────────────

    private void Start()
    {
        // Экипируем первый непустой слот
        for (int i = 0; i < weaponLoadout.Length; i++)
        {
            if (weaponLoadout[i] != null) { SwitchToSlot(i); break; }
        }
    }

    private void OnEnable()
    {
        BindAction(fireAction,   started: OnFireStarted,   canceled: OnFireCanceled);
        BindAction(reloadAction, started: OnReloadStarted);
        BindAction(aimAction,    started: OnAimStarted,    canceled: OnAimCanceled);
        BindAction(slot1Action,  started: OnSlot1Started);
        BindAction(slot2Action,  started: OnSlot2Started);
        BindAction(cycleAction,  started: OnCycleWeaponStarted);
    }

    private void OnDisable()
    {
        UnbindAction(fireAction,   started: OnFireStarted,   canceled: OnFireCanceled);
        UnbindAction(reloadAction, started: OnReloadStarted);
        UnbindAction(aimAction,    started: OnAimStarted,    canceled: OnAimCanceled);
        UnbindAction(slot1Action,  started: OnSlot1Started);
        UnbindAction(slot2Action,  started: OnSlot2Started);
        UnbindAction(cycleAction,  started: OnCycleWeaponStarted);
    }

    // ── Переключение слотов ────────────────────────────────────

    /// <summary>
    /// Переключает на слот index. Если уже активен — ничего не делает.
    /// Можно вызывать из UI, триггеров, анимаций и т.д.
    /// </summary>
    public void SwitchToSlot(int index)
    {
        if (index < 0 || index >= weaponLoadout.Length) return;
        if (index == _activeSlot) return;

        _activeSlot = index;
        EquipWeapon(weaponLoadout[index]);
    }

    /// <summary>Циклично переключает слоты (Q).</summary>
    public void CycleWeapon()
    {
        if (weaponLoadout.Length == 0) return;
        int next = (_activeSlot + 1) % weaponLoadout.Length;
        SwitchToSlot(next);
    }

    /// <summary>
    /// Назначает новый prefab в указанный слот.
    /// Если слот активен — немедленно переэкипирует.
    /// </summary>
    public void SetSlot(int index, GameObject prefab)
    {
        if (index < 0 || index >= weaponLoadout.Length) return;
        weaponLoadout[index] = prefab;

        if (index == _activeSlot)
        {
            _activeSlot = -1; // сбрасываем, чтобы SwitchToSlot не засабился
            SwitchToSlot(index);
        }
    }

    // ── Спавн / уничтожение оружия ─────────────────────────────

    private void EquipWeapon(GameObject prefab)
    {
        if (_activeWeapon != null)
        {
            _activeWeapon.ReleaseTriger();
            Destroy(_activeWeapon.gameObject);
            _activeWeapon = null;
        }

        if (prefab == null)
        {
            Debug.Log("[WeaponHolder] Слот пуст.");
            return;
        }

        Transform parent = weaponSlot != null ? weaponSlot : transform;
        GameObject go    = Instantiate(prefab, parent.position, parent.rotation, parent);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        _activeWeapon = go.GetComponent<WeaponBase>();
        if (_activeWeapon == null)
        {
            Debug.LogError($"[WeaponHolder] Prefab '{prefab.name}' не содержит WeaponBase!");
            Destroy(go);
            return;
        }

        _activeWeapon.Initialize(playerCamera, cameraRecoil);
        Debug.Log($"[WeaponHolder] Слот {_activeSlot}: экипировано «{_activeWeapon.settings?.weaponName ?? prefab.name}»");
    }

    // ── Input callbacks ────────────────────────────────────────

    private void OnFireStarted(InputAction.CallbackContext ctx)   => _activeWeapon?.PressTriger();
    private void OnFireCanceled(InputAction.CallbackContext ctx)  => _activeWeapon?.ReleaseTriger();
    private void OnReloadStarted(InputAction.CallbackContext ctx) => _activeWeapon?.RequestReload();
    private void OnAimStarted(InputAction.CallbackContext ctx)    => _activeWeapon?.SetAiming(true);
    private void OnAimCanceled(InputAction.CallbackContext ctx)   => _activeWeapon?.SetAiming(false);
    private void OnSlot1Started(InputAction.CallbackContext ctx)  => SwitchToSlot(0);
    private void OnSlot2Started(InputAction.CallbackContext ctx)  => SwitchToSlot(1);
    private void OnCycleWeaponStarted(InputAction.CallbackContext ctx) => CycleWeapon();

    // ── Утилиты бинда ──────────────────────────────────────────

    private void BindAction(InputActionReference r,
                            System.Action<InputAction.CallbackContext> started  = null,
                            System.Action<InputAction.CallbackContext> canceled = null)
    {
        if (r == null) return;
        r.action.Enable();
        if (started  != null) r.action.started  += started;
        if (canceled != null) r.action.canceled += canceled;
    }

    private void UnbindAction(InputActionReference r,
                              System.Action<InputAction.CallbackContext> started  = null,
                              System.Action<InputAction.CallbackContext> canceled = null)
    {
        if (r == null) return;
        if (started  != null) r.action.started  -= started;
        if (canceled != null) r.action.canceled -= canceled;
        r.action.Disable();
    }
}
