using UnityEngine;

/// <summary>
/// Снаряд с реалистичной баллистикой.
/// Движется через Rigidbody + кастомный drag + гравитация.
/// Никакого Raycast — только физика.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    private ProjectileSettingsSO projectileSettings;
    private WeaponSettingsSO     weaponSettings;
    private float                travelDistance;
    private Vector3              spawnPosition;



    private Rigidbody rb;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Вызывается сразу после Instantiate.
    /// ownerColliders — коллайдеры игрока (чтобы пуля не стреляла в себя).
    /// </summary>
    public void Launch(Vector3 velocity, WeaponSettingsSO wSettings, ProjectileSettingsSO pSettings,
                       Collider[] ownerColliders = null)
    {
        weaponSettings     = wSettings;
        projectileSettings = pSettings;
        spawnPosition      = transform.position;



        // Игнорируем столкновения со всеми коллайдерами игрока
        if (ownerColliders != null)
        {
            Collider myCollider = GetComponent<Collider>();
            if (myCollider != null)
            {
                foreach (Collider c in ownerColliders)
                {
                    if (c != null)
                        Physics.IgnoreCollision(myCollider, c, true);
                }
            }
        }

        rb.useGravity    = false;
        rb.linearDamping = 0f;
        rb.mass          = pSettings.mass;
        rb.linearVelocity = velocity;

        if (pSettings.tracerEffectPrefab != null)
            Instantiate(pSettings.tracerEffectPrefab, transform.position, Quaternion.identity, transform);

        Destroy(gameObject, pSettings.lifetime);
    }

    // ── Физика ─────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (projectileSettings == null) return;

        Vector3 velocity = rb.linearVelocity;

        // Кастомная гравитация
        rb.AddForce(Physics.gravity * projectileSettings.gravityMultiplier, ForceMode.Acceleration);

        // Аэродинамическое сопротивление
        if (projectileSettings.drag > 0f)
            rb.AddForce(-projectileSettings.drag * velocity, ForceMode.Acceleration);

        // Поворот по вектору скорости
        if (velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(rb.linearVelocity);

        // Проверка дальности
        travelDistance = Vector3.Distance(spawnPosition, transform.position);
        if (travelDistance >= projectileSettings.maxRange)
            Destroy(gameObject);
    }

    // ── Столкновение ───────────────────────────────────────────

    private void OnCollisionEnter(Collision collision)
    {
        if (projectileSettings == null) return;

        if ((projectileSettings.hitLayers.value & (1 << collision.gameObject.layer)) == 0) return;

        ContactPoint contact = collision.GetContact(0);
        HandleHit(collision.gameObject, contact.point, contact.normal);
    }

    private void HandleHit(GameObject target, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (weaponSettings == null) return;

        // Множитель урона по дистанции
        float distMult = weaponSettings.GetDamageMultiplier(travelDistance);
        float totalDamage = 0f;

        if (weaponSettings.splashRadius > 0f)
        {
            Collider[] cols = Physics.OverlapSphere(hitPoint, weaponSettings.splashRadius, projectileSettings.hitLayers);
            foreach (var col in cols)
            {
                float dist        = Vector3.Distance(hitPoint, col.transform.position);
                float splashFactor = 1f - Mathf.Clamp01(dist / weaponSettings.splashRadius);
                float dmg = weaponSettings.damage * splashFactor * distMult;
                ApplyDamage(col.gameObject, dmg);
                totalDamage += dmg;
            }
        }
        else
        {
            totalDamage = weaponSettings.damage * distMult;
            ApplyDamage(target, totalDamage);
        }

        // ── Визуальный дебаг (видно в Game View) ───────────────────────
        if (showDebug)
        {
            if (WeaponDebugger.Instance != null)
            {
                WeaponDebugger.Instance.RegisterHit(
                    fromPos:      spawnPosition,
                    hitPoint:     hitPoint,
                    hitNormal:    hitNormal,
                    targetName:   target.name,
                    damage:       totalDamage,
                    splashRadius: weaponSettings.splashRadius
                );
            }
            else
            {
                // Фоллбэк: только Scene View (старое поведение)
                Debug.Log($"<color=cyan>[Projectile]</color> Hit <b>{target.name}</b> | dmg={totalDamage:F1} | dist={travelDistance:F1}m | layer={LayerMask.LayerToName(target.layer)}", target);
                Debug.DrawRay(hitPoint, hitNormal, Color.red, 3f);
                Debug.DrawLine(hitPoint - Vector3.right * 0.15f, hitPoint + Vector3.right * 0.15f, Color.red, 3f);
                Debug.DrawLine(hitPoint - Vector3.up    * 0.15f, hitPoint + Vector3.up    * 0.15f, Color.red, 3f);
                Debug.DrawLine(spawnPosition, hitPoint, Color.yellow, 2f);
            }
        }

        if (projectileSettings.impactEffectPrefab != null)
            Instantiate(projectileSettings.impactEffectPrefab, hitPoint, Quaternion.LookRotation(hitNormal));

        Destroy(gameObject);
    }

    private void ApplyDamage(GameObject target, float amount)
    {
        target.GetComponentInParent<IDamageable>()?.TakeDamage(amount);
    }
}
