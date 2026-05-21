using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Class-2 Synergy: "Flickering Wanderer" (Kinesia > 50 AND Smallion > 50).
    ///
    /// While active (always):
    ///   - Ground friction drops to zero — player slides like ice.
    ///
    /// While sprinting:
    ///   - Invisibility: SwarmManager.ChargeRadius set to 0 (enemies stop rushing),
    ///     PlayerHealth.IsInvisible = true (contact damage suppressed).
    ///   - Kinetic ram: deals collision damage to nearby enemies each step.
    ///   - Resource drain: Transfinite -3 per real second.
    ///
    /// Deactivation hysteresis: 5-unit deadband below activation threshold.
    /// </summary>
    public sealed class FlickeringWanderer : SynergyEffect
    {
        public override SynergyType Type => SynergyType.FlickeringWanderer;

        private const float ActivateThresholdK   = 50f;
        private const float ActivateThresholdS   = 50f;
        private const float DeactivateThresholdK = 45f;
        private const float DeactivateThresholdS = 45f;

        private const float TransfiniteDrainPerSec = 3f;
        private const float RamRadius              = 1.5f;
        private const float RamDamagePerStep       = 40f;
        private const float RamStepDistance        = 0.6f; // meters between ram ticks

        private float   _savedChargeRadius;
        private Vector3 _lastRamPosition;

        // ── Condition ────────────────────────────────────────────────────────────
        protected override bool ActivationCondition(ElementalMutationManager emm)
            => emm.Kinesia > ActivateThresholdK && emm.Smallion > ActivateThresholdS;

        protected override bool DeactivationCondition(ElementalMutationManager emm)
            => emm.Kinesia < DeactivateThresholdK || emm.Smallion < DeactivateThresholdS;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public override void OnActivate(SynergyDependencies deps)
        {
            deps.Movement.FrictionMultiplier = 0f;

            if (deps.Swarm != null)
                _savedChargeRadius = deps.Swarm.ChargeRadius;

            _lastRamPosition = deps.Health != null ? deps.Health.transform.position : Vector3.zero;
        }

        public override void OnDeactivate(SynergyDependencies deps)
        {
            deps.Movement.FrictionMultiplier = 1f;
            deps.Health.IsInvisible = false;

            if (deps.Swarm != null)
                deps.Swarm.ChargeRadius = _savedChargeRadius;
        }

        public override void OnTick(SynergyDependencies deps, float dt)
        {
            bool sprinting = deps.Movement.IsSprinting;

            if (sprinting)
            {
                // Drain Transfinite at real-time rate
                deps.Emm.ModifyStat(MutationType.Transfinite, -TransfiniteDrainPerSec * dt);

                // Suppress enemy targeting and contact damage
                deps.Health.IsInvisible = true;
                if (deps.Swarm != null) deps.Swarm.ChargeRadius = 0f;

                // Step-based kinetic ram
                Vector3 currentPos = deps.Health.transform.position;
                if (Vector3.Distance(currentPos, _lastRamPosition) >= RamStepDistance)
                {
                    _lastRamPosition = currentPos;
                    TriggerRam(currentPos, deps.EnemyLayer);
                }
            }
            else
            {
                deps.Health.IsInvisible = false;
                if (deps.Swarm != null) deps.Swarm.ChargeRadius = _savedChargeRadius;
            }
        }

        // ── Internals ────────────────────────────────────────────────────────────
        private static void TriggerRam(Vector3 center, LayerMask enemyLayer)
        {
            Collider[] hits = Physics.OverlapSphere(center, RamRadius, enemyLayer);
            foreach (Collider col in hits)
                col.GetComponentInParent<IDamageable>()?.TakeDamage(RamDamagePerStep);
        }
    }
}
