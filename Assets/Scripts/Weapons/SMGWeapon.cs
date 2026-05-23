using UnityEngine;

/// <summary>
/// Пистолет-пулемёт (SMG).
///
/// Особенности:
///   • Высокая скорострельность (задаётся в SO: fireRate ~900–1200)
///   • Система нагрева (heat): чем дольше стреляешь — тем больше разброс
///   • Разброс при нагреве добавляется поверх baseSpread из SO
///   • При отпускании курка разброс плавно остывает
///   • Урон падает с расстоянием (useDamageFalloff = true в SO)
/// </summary>
public class SMGWeapon : WeaponBase
{
    [Header("Настройки снаряда")]
    public ProjectileSettingsSO projectileSettings;

    [Header("Аудио")]
    public AudioClip shootSound;
    public AudioClip reloadSound;

    [Header("Система нагрева (heat bloom)")]
    [Tooltip("Разброс прибавляется за каждый выстрел (градусы)")]
    public float heatPerShot    = 0.3f;
    [Tooltip("Максимальный добавочный разброс от нагрева (градусы)")]
    public float maxHeatSpread  = 4f;
    [Tooltip("Скорость остывания (градусы в секунду)")]
    public float cooldownRate   = 6f;
    [Tooltip("Задержка до начала остывания после прекращения стрельбы (сек)")]
    public float cooldownDelay  = 0.25f;

    // ── Runtime состояние нагрева ──────────────────────────────
    private float _currentHeat     = 0f;
    private float _lastShotTime    = 0f;

    // ── Update: остывание ──────────────────────────────────────

    protected override void Update()
    {
        base.Update(); // автоматическая стрельба

        // Остывание с задержкой после последнего выстрела
        if (!IsTriggerHeld && Time.time > _lastShotTime + cooldownDelay)
        {
            _currentHeat = Mathf.Max(0f, _currentHeat - cooldownRate * Time.deltaTime);
        }
    }

    // ── Выстрел ────────────────────────────────────────────────

    protected override void Shoot()
    {
        if (projectileSettings == null || settings.projectilePrefab == null)
        {
            Debug.LogError($"[SMGWeapon] {name}: не назначены настройки снаряда или prefab!");
            return;
        }

        Transform origin  = muzzlePoint != null ? muzzlePoint : transform;
        Vector3   baseDir = playerCamera != null ? playerCamera.forward : origin.forward;

        // Суммарный разброс = базовый + нагрев
        float totalSpread = (settings.baseSpread + _currentHeat) * SpreadMultiplier;
        Vector3 shootDir  = GetSpreadDirection(baseDir, totalSpread);

        GameObject go = Instantiate(
            settings.projectilePrefab,
            origin.position,
            Quaternion.LookRotation(shootDir)
        );

        Projectile proj = go.GetComponent<Projectile>();
        if (proj == null) { Destroy(go); return; }

        proj.Launch(shootDir * settings.muzzleVelocity, settings, projectileSettings, ownerColliders);

        // Нагрев
        _currentHeat   = Mathf.Min(_currentHeat + heatPerShot, maxHeatSpread);
        _lastShotTime  = Time.time;
    }

    protected override void OnShotFired()
    {
        if (shootSound != null) audioSource.PlayOneShot(shootSound);
    }

    // ── Перезарядка ────────────────────────────────────────────

    protected override System.Collections.IEnumerator ReloadCoroutine()
    {
        if (reloadSound != null) audioSource.PlayOneShot(reloadSound);
        return base.ReloadCoroutine();
    }

    // ── Геттер для UI (опционально) ───────────────────────────

    /// <summary>Текущий уровень нагрева (0–1) для UI индикатора.</summary>
    public float HeatNormalized => maxHeatSpread > 0f ? _currentHeat / maxHeatSpread : 0f;
}
