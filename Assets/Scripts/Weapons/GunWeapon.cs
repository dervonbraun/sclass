using UnityEngine;

/// <summary>
/// Огнестрельное оружие: пистолет / винтовка / дробовик.
/// Логика отдачи полностью вынесена в WeaponBase → CameraRecoil.
/// Здесь только: спавн снаряда + аудио.
/// </summary>
public class GunWeapon : WeaponBase
{
    [Header("Настройки снаряда")]
    public ProjectileSettingsSO projectileSettings;

    [Header("Аудио")]
    public AudioClip shootSound;
    public AudioClip reloadSound;

    // ── Выстрел ────────────────────────────────────────────────

    protected override void Shoot()
    {
        if (projectileSettings == null)
        {
            Debug.LogError($"[GunWeapon] {name}: ProjectileSettingsSO не назначен!");
            return;
        }
        if (settings.projectilePrefab == null)
        {
            Debug.LogError($"[GunWeapon] {name}: projectilePrefab не назначен в WeaponSettingsSO!");
            return;
        }

        Transform origin = muzzlePoint != null ? muzzlePoint : transform;
        Vector3   baseDir = playerCamera != null ? playerCamera.forward : origin.forward;
        Vector3   shootDir = GetSpreadDirection(baseDir, settings.baseSpread * SpreadMultiplier);

        GameObject projGO = Instantiate(
            settings.projectilePrefab,
            origin.position,
            Quaternion.LookRotation(shootDir)
        );

        Projectile proj = projGO.GetComponent<Projectile>();
        if (proj == null)
        {
            Debug.LogError($"[GunWeapon] Prefab '{settings.projectilePrefab.name}' не имеет компонента Projectile!");
            Destroy(projGO);
            return;
        }

        proj.Launch(shootDir * settings.muzzleVelocity, settings, projectileSettings, ownerColliders);
    }

    protected override void OnShotFired()
    {
        if (shootSound != null) audioSource.PlayOneShot(shootSound);
    }

    // ── Перезарядка (звук) ─────────────────────────────────────

    protected override System.Collections.IEnumerator ReloadCoroutine()
    {
        if (reloadSound != null) audioSource.PlayOneShot(reloadSound);
        return base.ReloadCoroutine();
    }
}
