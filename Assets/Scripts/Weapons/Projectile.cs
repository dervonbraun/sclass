using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    private ProjectileSettingsSO projectileSettings;
    private WeaponSettingsSO     weaponSettings;
    private float                travelDistance;
    private Vector3              spawnPosition;
    private bool                 isDying;

    private Rigidbody            rb;
    private Renderer             projectileRenderer;
    private MaterialPropertyBlock mpb;

    // Стартовые цвета, читаются один раз при Launch
    private Color _startEmission;
    private Color _startBaseColor;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    // Названия свойств HDRP Lit
    private static readonly int PropEmissive = Shader.PropertyToID("_EmissiveColor");
    private static readonly int PropBaseColor = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        projectileRenderer = GetComponentInChildren<Renderer>();
        if (projectileRenderer != null)
            mpb = new MaterialPropertyBlock();
    }

    public void Launch(Vector3 velocity, WeaponSettingsSO wSettings, ProjectileSettingsSO pSettings,
                       Collider[] ownerColliders = null)
    {
        weaponSettings     = wSettings;
        projectileSettings = pSettings;
        spawnPosition      = transform.position;

        if (ownerColliders != null)
        {
            Collider myCollider = GetComponent<Collider>();
            if (myCollider != null)
                foreach (Collider c in ownerColliders)
                    if (c != null)
                        Physics.IgnoreCollision(myCollider, c, true);
        }

        rb.useGravity     = false;
        rb.linearDamping  = 0f;
        rb.mass           = pSettings.mass;
        rb.linearVelocity = velocity;

        // Читаем стартовые цвета из материала ОДИН РАЗ здесь,
        // пока MPB ещё пустой — sharedMaterial даёт правильные значения
        if (projectileRenderer != null)
        {
            Material mat = projectileRenderer.sharedMaterial;
            _startEmission  = mat.HasProperty(PropEmissive)  ? mat.GetColor(PropEmissive)  : Color.black;
            _startBaseColor = mat.HasProperty(PropBaseColor) ? mat.GetColor(PropBaseColor) : Color.white;

            // Сразу пишем в MPB, чтобы дальше работать только с ним
            projectileRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(PropEmissive,  _startEmission);
            mpb.SetColor(PropBaseColor, _startBaseColor);
            projectileRenderer.SetPropertyBlock(mpb);
        }

        if (pSettings.tracerEffectPrefab != null)
            Instantiate(pSettings.tracerEffectPrefab, transform.position, Quaternion.identity, transform);

        float fadeStart = Mathf.Max(0f, pSettings.lifetime - pSettings.fadeOutDuration);
        Invoke(nameof(BeginFadeOut), fadeStart);
    }

    // ── Физика ────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        if (projectileSettings == null) return;

        Vector3 velocity = rb.linearVelocity;

        rb.AddForce(Physics.gravity * projectileSettings.gravityMultiplier, ForceMode.Acceleration);

        if (projectileSettings.drag > 0f)
            rb.AddForce(-projectileSettings.drag * velocity, ForceMode.Acceleration);

        if (velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(rb.linearVelocity);

        travelDistance = Vector3.Distance(spawnPosition, transform.position);
        if (travelDistance >= projectileSettings.maxRange && !isDying)
            BeginFadeOut();
    }

    // ── Угасание ──────────────────────────────────────────────────────────

    private void BeginFadeOut()
    {
        if (isDying) return;
        isDying = true;
        CancelInvoke(nameof(BeginFadeOut));
        StartCoroutine(FadeOutRoutine(projectileSettings.fadeOutDuration));
    }

    private IEnumerator FadeOutRoutine(float duration)
    {
        if (projectileRenderer == null) { Destroy(gameObject); yield break; }

        Vector3 startScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Меш сжимается в точку — Base Color больше не виден
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);

            // Emission: жёлтый → красный → чёрный
            Color emissionNow;
            if (t < 0.5f)
            {
                Color midColor = new Color(_startEmission.r, 0f, 0f, _startEmission.a);
                emissionNow = Color.Lerp(_startEmission, midColor, t * 2f);
            }
            else
            {
                Color midColor = new Color(_startEmission.r, 0f, 0f, _startEmission.a);
                emissionNow = Color.Lerp(midColor, Color.black, (t - 0.5f) * 2f);
            }

            projectileRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(PropEmissive, emissionNow);
            projectileRenderer.SetPropertyBlock(mpb);

            yield return null;
        }

        Destroy(gameObject);
    }

    // ── Столкновение ──────────────────────────────────────────────────────

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

        float distMult    = weaponSettings.GetDamageMultiplier(travelDistance);
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
                Debug.Log($"<color=cyan>[Projectile]</color> Hit <b>{target.name}</b> | dmg={totalDamage:F1} | dist={travelDistance:F1}m", target);
                Debug.DrawRay(hitPoint, hitNormal, Color.red, 3f);
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