using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// A short-lived world-space hazard that ticks damage to enemies in a sphere.
    /// Spawned by KineticTax on each player footstep.
    /// Self-destructs after <see cref="Initialize"/>'s lifetime parameter.
    /// </summary>
    public sealed class DamageDecal : MonoBehaviour
    {
        private float     _radius;
        private float     _damagePerTick;
        private LayerMask _enemyLayer;
        private float     _tickInterval;
        private float     _nextTickTime;

        public void Initialize(float radius, float damagePerTick, float lifetime,
                               float tickInterval, LayerMask enemyLayer)
        {
            _radius        = radius;
            _damagePerTick = damagePerTick;
            _tickInterval  = tickInterval;
            _enemyLayer    = enemyLayer;
            _nextTickTime  = Time.time + _tickInterval;

            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            if (Time.time < _nextTickTime) return;
            _nextTickTime = Time.time + _tickInterval;

            Collider[] hits = Physics.OverlapSphere(transform.position, _radius, _enemyLayer);
            foreach (Collider col in hits)
                col.GetComponentInParent<IDamageable>()?.TakeDamage(_damagePerTick);
        }
    }
}
