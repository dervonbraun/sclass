using UnityEngine;

[CreateAssetMenu(fileName = "NewWeaponSettings", menuName = "Weapons/WeaponSettings")]
public class WeaponSettingsSO : ScriptableObject
{
    [Header("Общие")]
    public string weaponName = "Weapon";

    [Header("Урон")]
    [Tooltip("Урон при прямом попадании (на минимальной дистанции falloff)")]
    public float damage = 25f;
    [Tooltip("Радиус взрыва (0 = нет AoE)")]
    public float splashRadius = 0f;

    [Header("Падение урона с расстоянием")]
    [Tooltip("Включить падение урона")]
    public bool useDamageFalloff = false;
    [Tooltip("Дистанция, до которой урон максимальный (м)")]
    public float falloffStartDistance = 15f;
    [Tooltip("Дистанция полного ослабления урона (м)")]
    public float falloffEndDistance   = 50f;
    [Tooltip("Минимальный множитель урона на предельной дистанции (0–1)")]
    [Range(0f, 1f)]
    public float minDamageMultiplier  = 0.2f;

    [Header("Огонь")]
    [Tooltip("Выстрелов в минуту")]
    public float fireRate = 600f;
    [Tooltip("Полуавтомат (false) / Автомат (true)")]
    public bool isAutomatic = true;
    [Tooltip("Количество снарядов за выстрел (дробовик)")]
    public int pelletsPerShot = 1;

    [Header("Разброс")]
    [Tooltip("Базовый разброс в градусах")]
    public float baseSpread = 0.5f;
    [Tooltip("Разброс в движении (прибавляется)")]
    public float movementSpread = 1.5f;

    [Header("Отдача камеры — Hip")]
    [Tooltip("X = вверх, Y = влево/вправо, Z = наклон")]
    public Vector3 recoilCamRotation = new Vector3(2f, 0.4f, 0.1f);
    [Tooltip("Скорость применения отдачи (lag-lerp)")]
    public float recoilRotationSpeed = 6f;
    [Tooltip("Скорость возврата к нулю")]
    public float recoilReturnSpeed = 3f;

    [Header("Отдача камеры — ADS")]
    public Vector3 recoilCamRotationADS = new Vector3(1f, 0.15f, 0.05f);

    [Header("Патроны")]
    public int magazineSize = 30;
    public float reloadTime = 2f;

    [Header("Снаряд")]
    [Tooltip("Prefab снаряда")]
    public GameObject projectilePrefab;
    [Tooltip("Начальная скорость снаряда (м/с)")]
    public float muzzleVelocity = 800f;

    [Header("ADS")]
    public float adsInDuration  = 0.15f;
    public float adsOutDuration = 0.12f;

    // ── Хелпер: вычислить множитель урона по дистанции ────────────
    public float GetDamageMultiplier(float distance)
    {
        if (!useDamageFalloff) return 1f;
        if (distance <= falloffStartDistance) return 1f;
        float t = Mathf.InverseLerp(falloffStartDistance, falloffEndDistance, distance);
        return Mathf.Lerp(1f, minDamageMultiplier, t);
    }
}
