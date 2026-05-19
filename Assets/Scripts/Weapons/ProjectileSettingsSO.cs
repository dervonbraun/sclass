using UnityEngine;

[CreateAssetMenu(fileName = "NewProjectileSettings", menuName = "Weapons/ProjectileSettings")]
public class ProjectileSettingsSO : ScriptableObject
{
    [Header("Физика")]
    [Tooltip("Масса снаряда (кг). Влияет на импульс при столкновении")]
    public float mass = 0.004f;
    [Tooltip("Аэродинамическое сопротивление воздуха (0 = нет торможения)")]
    public float drag = 0.1f;
    [Tooltip("Множитель гравитации (1 = стандартная, 0 = нет падения)")]
    public float gravityMultiplier = 1f;

    [Header("Баллистика")]
    [Tooltip("Максимальная дальность (м). Снаряд уничтожается за этим порогом")]
    public float maxRange = 500f;
    [Tooltip("Lifetime снаряда (сек) — fallback, если он пролетает дальше maxRange")]
    public float lifetime = 10f;

    [Header("Визуал")]
    [Tooltip("Время (сек) плавного угасания эмиссии перед уничтожением")]
    public float fadeOutDuration = 0.15f;

    [Header("Попадание")]
    [Tooltip("Слои, с которыми снаряд взаимодействует")]
    public LayerMask hitLayers = ~0; // все слои по умолчанию
    [Tooltip("Создать дырку/декал при попадании (если назначен)")]
    public GameObject impactEffectPrefab;
    [Tooltip("Эффект трассера (trail renderer prefab / particle)")]
    public GameObject tracerEffectPrefab;
}
