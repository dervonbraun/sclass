using UnityEngine;

/// <summary>
/// Абстрактный базовый класс для всего оружия.
/// Наследуй и реализуй Shoot().
/// Зависит только от CameraRecoil и Transform камеры — не от CameraController.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public abstract class WeaponBase : MonoBehaviour
{
    [Header("Настройки оружия")]
    public WeaponSettingsSO settings;

    [Header("Точки")]
    [Tooltip("Дульный срез — откуда вылетает снаряд")]
    public Transform muzzlePoint;

    // ── Инъекции (устанавливает WeaponHolder) ──────────────────
    protected Transform     playerCamera;   // направление стрельбы
    protected CameraRecoil  cameraRecoil;   // компонент отдачи
    protected bool          isAiming;       // текущий режим прицела

    // Коллайдеры владельца: снаряд будет игнорировать их через Physics.IgnoreCollision
    protected Collider[] ownerColliders = System.Array.Empty<Collider>();

    // ── Состояние ──────────────────────────────────────────────
    public int  CurrentAmmo   { get; protected set; }
    public bool IsReloading   { get; protected set; }
    public bool IsTriggerHeld { get; protected set; }

    protected float       nextFireTime;
    protected AudioSource audioSource;

    // ── События ────────────────────────────────────────────────
    public event System.Action<int, int> OnAmmoChanged;   // (current, max)
    public event System.Action           OnReloadStart;
    public event System.Action           OnReloadEnd;
    public event System.Action           OnShot;

    // ── Инициализация ──────────────────────────────────────────

    protected virtual void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    protected virtual void Start()
    {
        if (settings == null)
        {
            Debug.LogError($"[WeaponBase] {name}: WeaponSettingsSO не назначен!");
            enabled = false;
            return;
        }
        CurrentAmmo = settings.magazineSize;
    }

    /// <summary>
    /// Вызывается WeaponHolder после спавна оружия.
    /// Передаёт все внешние зависимости — оружие не ищет их само.
    /// </summary>
    public virtual void Initialize(Transform camera, CameraRecoil recoil)
    {
        playerCamera = camera;
        cameraRecoil = recoil;

        // Собираем все коллайдеры игрока (корень иерархии = родитель playerCamera)
        // Снаряд будет игнорировать столкновения со всеми этими коллайдерами
        if (camera != null)
        {
            GameObject playerRoot = camera.root.gameObject;
            ownerColliders = playerRoot.GetComponentsInChildren<Collider>(includeInactive: true);
        }

        // Сообщаем компоненту отдачи о новом профиле
        cameraRecoil?.SetProfile(settings);
    }

    // ── Публичный API ──────────────────────────────────────────

    public void PressTriger()
    {
        IsTriggerHeld = true;
        if (!settings.isAutomatic) TryShoot();
    }

    public void ReleaseTriger()
    {
        IsTriggerHeld = false;
    }

    public void RequestReload()
    {
        if (!IsReloading && CurrentAmmo < settings.magazineSize)
            StartCoroutine(ReloadCoroutine());
    }

    public void SetAiming(bool aiming)
    {
        isAiming = aiming;
    }

    // ── Внутренняя логика ──────────────────────────────────────

    protected virtual void Update()
    {
        if (settings != null && settings.isAutomatic && IsTriggerHeld)
            TryShoot();
    }

    protected void TryShoot()
    {
        if (IsReloading || CurrentAmmo <= 0 || Time.time < nextFireTime) return;

        nextFireTime = Time.time + 60f / settings.fireRate;

        for (int i = 0; i < settings.pelletsPerShot; i++)
            Shoot();

        // Звук и события — один раз на выстрел, не на каждый снаряд
        OnShotFired();

        CurrentAmmo--;
        OnShot?.Invoke();
        OnAmmoChanged?.Invoke(CurrentAmmo, settings.magazineSize);

        // Отдача через CameraRecoil
        if (isAiming)
            cameraRecoil?.AimFire();
        else
            cameraRecoil?.Fire();

        if (CurrentAmmo <= 0)
            StartCoroutine(ReloadCoroutine());
    }

    /// <summary>Реализуй логику выстрела: спавн снаряда, звук и т.д.</summary>
    protected abstract void Shoot();

    /// <summary>
    /// Вызывается ровно один раз за выстрел — после цикла всех снарядов/дробин.
    /// Переопредели в наследнике для воспроизведения звука, вспышки и пр.
    /// </summary>
    protected virtual void OnShotFired() { }

    // ── Перезарядка ────────────────────────────────────────────

    protected virtual System.Collections.IEnumerator ReloadCoroutine()
    {
        IsReloading = true;
        OnReloadStart?.Invoke();
        yield return new WaitForSeconds(settings.reloadTime);
        CurrentAmmo = settings.magazineSize;
        IsReloading = false;
        OnReloadEnd?.Invoke();
        OnAmmoChanged?.Invoke(CurrentAmmo, settings.magazineSize);
    }

    // ── Хелперы ────────────────────────────────────────────────

    protected Vector3 GetSpreadDirection(Vector3 baseDirection, float spreadDegrees)
    {
        if (spreadDegrees <= 0f) return baseDirection;
        return Quaternion.Euler(
            Random.Range(-spreadDegrees, spreadDegrees),
            Random.Range(-spreadDegrees, spreadDegrees),
            0f
        ) * baseDirection;
    }
}
