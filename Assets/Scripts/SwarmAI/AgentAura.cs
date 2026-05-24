using UnityEngine;

/// <summary>
/// Вешать на дочерний GameObject агента. Содержит SphereCollider (isTrigger=true).
/// Когда снаряд (или любой объект на ProjectileLayer) входит в ауру —
/// родительский AgentHealth получает урон.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class AgentAura : MonoBehaviour
{
    [Tooltip("Радиус ауры. Должен быть больше основного коллайдера агента.")]
    public float AuraRadius = 1.5f;

    [Tooltip("Урон, наносимый агенту при попадании снаряда в ауру.")]
    public float DamageOnHit = 15f;

    [Tooltip("Слой снарядов игрока.")]
    public LayerMask ProjectileLayer;

    private AgentHealth _health;
    private SphereCollider _collider;

    private void Awake()
    {
        _health = GetComponentInParent<AgentHealth>();

        _collider = GetComponent<SphereCollider>();
        _collider.isTrigger = true;
        _collider.radius    = AuraRadius;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & ProjectileLayer.value) == 0) return;

        _health?.TakeDamage(DamageOnHit);

        // Уничтожаем снаряд — он попал в ауру
        Destroy(other.gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.2f);
        Gizmos.DrawSphere(transform.position, AuraRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, AuraRadius);
    }
#endif
}
