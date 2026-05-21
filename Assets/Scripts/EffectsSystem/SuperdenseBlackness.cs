using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Class-2 Synergy: "Superdense Blackness" (Smallion &lt; 10 AND Transfinite > 60).
    ///
    /// Buffs:
    ///   - 50% chance to reflect incoming projectiles at 2× damage.
    ///   - GravityWell child object pulls any Rigidbody tagged "Pickup" within 15 m.
    ///
    /// Debuffs:
    ///   - Movement speed reduced by 60% (SynergySpeedMultiplier = 0.4).
    ///   - Attack hitbox doubled (HitboxRadiusMultiplier +1.0 = ×2 radius).
    ///
    /// Resource drain:
    ///   - Each reflected projectile costs 2 Kinesia instantly.
    ///
    /// Projectile reflection requires ranged enemies whose projectiles fire the
    /// GameplayEventBus.ProcessProjectileHit event (see Projectile.cs integration).
    /// The GravityWell pulls Unity Rigidbodies tagged "Pickup" — tag pickup prefabs accordingly.
    /// </summary>
    public sealed class SuperdenseBlackness : SynergyEffect
    {
        public override SynergyType Type => SynergyType.SuperdenseBlackness;

        private const float ActivateS    = 10f;
        private const float ActivateT    = 60f;
        private const float DeactivateS  = 12f; // hysteresis: stop when S rises above 12
        private const float DeactivateT  = 57f; // hysteresis: stop when T drops below 57

        private const float SpeedReduction       = 0.4f;
        private const float HitboxBonus          = 1f;   // +100% = double radius
        private const float ReflectChance        = 0.5f;
        private const float ReflectDamageMultiplier = 2f;
        private const float KinesiaPerReflect    = 2f;
        private const float GravityWellRadius    = 15f;
        private const float GravityPullForce     = 8f;

        private ElementalMutationManager _emm;
        private StatModifier             _hitboxMod;
        private GravityWell              _gravityWell;

        // ── Conditions ───────────────────────────────────────────────────────────
        protected override bool ActivationCondition(ElementalMutationManager emm)
            => emm.Smallion < ActivateS && emm.Transfinite > ActivateT;

        protected override bool DeactivationCondition(ElementalMutationManager emm)
            => emm.Smallion >= DeactivateS || emm.Transfinite <= DeactivateT;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public override void OnActivate(SynergyDependencies deps)
        {
            _emm = deps.Emm;

            // Slow movement
            deps.Movement.SynergySpeedMultiplier = SpeedReduction;

            // Double hitbox radius via stat modifier
            _hitboxMod = new StatModifier(HitboxBonus, StatModifierType.PercentAdd, this);
            deps.Health.HitboxRadiusMultiplier.AddModifier(_hitboxMod);

            // Subscribe to projectile-hit event for reflection
            GameplayEventBus.OnProjectileHit += HandleProjectileHit;

            // Spawn gravity well as child of player
            var go = new UnityEngine.GameObject("_GravityWell");
            go.transform.SetParent(deps.Health.transform);
            go.transform.localPosition = UnityEngine.Vector3.zero;
            _gravityWell = go.AddComponent<GravityWell>();
            _gravityWell.Initialize(GravityWellRadius, GravityPullForce);
        }

        public override void OnDeactivate(SynergyDependencies deps)
        {
            deps.Movement.SynergySpeedMultiplier = 1f;
            deps.Health.HitboxRadiusMultiplier.RemoveAllModifiersFromSource(this);

            GameplayEventBus.OnProjectileHit -= HandleProjectileHit;

            if (_gravityWell != null)
                UnityEngine.Object.Destroy(_gravityWell.gameObject);

            _gravityWell = null;
            _emm = null;
        }

        public override void OnTick(SynergyDependencies deps, float dt) { }

        // ── Projectile reflection ────────────────────────────────────────────────
        private void HandleProjectileHit(ProjectileHitContext ctx)
        {
            if (UnityEngine.Random.value > ReflectChance) return;

            ctx.IsReflected             = true;
            ctx.ReflectDamageMultiplier = ReflectDamageMultiplier;

            _emm?.ModifyStat(MutationType.Kinesia, -KinesiaPerReflect);
        }
    }
}
