using UnityEngine;

/// <summary>
/// Дробовик.
///
/// Особенности:
///   • Конусный разлёт дроби (равномерное распределение по кругу + случайность)
///   • Жёсткая отдача через SO (сильный recoilCamRotation)
///   • Тяжёлый pump-action: fireRate в SO задаёт минимальный интервал
///   • Урон падает с расстоянием через WeaponSettingsSO.useDamageFalloff
/// </summary>
public class ShotgunWeapon : WeaponBase
{
    [Header("Настройки снаряда")]
    public ProjectileSettingsSO projectileSettings;

    [Header("Аудио")]
    public AudioClip shootSound;
    public AudioClip reloadSound;

    [Header("Разлёт дроби")]
    [Tooltip("Угол конуса разлёта (полуугол, градусы). Не зависит от baseSpread в SO.")]
    [Min(0f)]
    public float coneHalfAngle = 8f;

    [Tooltip("Добавить случайное смещение внутри конуса (true) или равномерно распределить по кольцу (false)")]
    public bool randomPattern = true;

    // ── Выстрел ────────────────────────────────────────────────

    protected override void Shoot()
    {
        if (projectileSettings == null || settings.projectilePrefab == null)
        {
            Debug.LogError($"[ShotgunWeapon] {name}: не назначены настройки снаряда или prefab!");
            return;
        }

        Transform origin  = muzzlePoint != null ? muzzlePoint : transform;
        Vector3   baseDir = playerCamera != null ? playerCamera.forward : origin.forward;

        int pellets = Mathf.Max(1, settings.pelletsPerShot);

        for (int i = 0; i < pellets; i++)
        {
            Vector3 shotDir = randomPattern
                ? RandomConeDirection(baseDir, coneHalfAngle)
                : UniformConeDirection(baseDir, coneHalfAngle, i, pellets);

            SpawnPellet(origin.position, shotDir);
        }
    }

    protected override void OnShotFired()
    {
        if (shootSound != null) audioSource.PlayOneShot(shootSound);
    }

    // ── Разлёт ─────────────────────────────────────────────────

    /// <summary>Случайное направление внутри конуса (стандартный дробовик).</summary>
    private Vector3 RandomConeDirection(Vector3 forward, float halfAngleDeg)
    {
        float angle   = Random.Range(0f, halfAngleDeg);
        float azimuth = Random.Range(0f, 360f);

        Quaternion rot = Quaternion.AngleAxis(azimuth, forward)
                       * Quaternion.AngleAxis(angle, GetPerpendicularAxis(forward));

        return rot * forward;
    }

    /// <summary>
    /// Равномерно распределяет пули по кольцу внутри конуса.
    /// Идеально для малого числа пуль (3, 5, 7...).
    /// </summary>
    private Vector3 UniformConeDirection(Vector3 forward, float halfAngleDeg, int index, int total)
    {
        float azimuth = (360f / total) * index;
        float angle   = halfAngleDeg * (total > 1 ? (float)index / (total - 1) : 0f);

        Quaternion rot = Quaternion.AngleAxis(azimuth, forward)
                       * Quaternion.AngleAxis(angle, GetPerpendicularAxis(forward));

        return rot * forward;
    }

    private Vector3 GetPerpendicularAxis(Vector3 forward)
    {
        // Находим ось, перпендикулярную вектору вперёд
        return Mathf.Abs(forward.y) < 0.9f
            ? Vector3.Cross(forward, Vector3.up).normalized
            : Vector3.Cross(forward, Vector3.right).normalized;
    }

    // ── Спавн одной дробины ────────────────────────────────────

    private void SpawnPellet(Vector3 origin, Vector3 direction)
    {
        GameObject go = Instantiate(
            settings.projectilePrefab,
            origin,
            Quaternion.LookRotation(direction)
        );

        Projectile proj = go.GetComponent<Projectile>();
        if (proj == null) { Destroy(go); return; }

        proj.Launch(direction * settings.muzzleVelocity, settings, projectileSettings, ownerColliders);
    }

    // ── Перезарядка ────────────────────────────────────────────

    protected override System.Collections.IEnumerator ReloadCoroutine()
    {
        if (reloadSound != null) audioSource.PlayOneShot(reloadSound);
        return base.ReloadCoroutine();
    }
}
