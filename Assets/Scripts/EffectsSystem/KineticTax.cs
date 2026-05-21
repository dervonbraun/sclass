using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Class-2 Synergy: "Kinetic Tax" (Kinesia > 60 AND Transfinite &lt; 10).
    ///
    /// Buffs:
    ///   - Fire rate ×3, reload time ÷3 (200% increase as per spec).
    ///   - Fire trail: spawns DamageDecal on each footstep.
    ///
    /// Debuffs:
    ///   - All incoming damage to player is doubled.
    ///
    /// Resource drain:
    ///   - Each reload (manual or auto) costs 5 Smallion instantly.
    ///
    /// Automatically re-applies weapon buffs when the active weapon changes.
    /// </summary>
    public sealed class KineticTax : SynergyEffect
    {
        public override SynergyType Type => SynergyType.KineticTax;

        private const float ActivateK    = 60f;
        private const float ActivateT    = 10f;
        private const float DeactivateK  = 55f; // hysteresis
        private const float DeactivateT  = 13f;

        private const float FireRateMult    = 3f;
        private const float ReloadTimeMult  = 1f / 3f;
        private const float SmallionPerReload = 5f;

        private const float TrailStepDistance = 1.5f;
        private const float TrailRadius        = 1.2f;
        private const float TrailDamagePerTick = 12f;
        private const float TrailLifetime      = 3f;
        private const float TrailTickInterval  = 0.4f;

        private ElementalMutationManager _emm;
        private WeaponHolder             _weapons;
        private WeaponBase               _subscribedWeapon;
        private Vector3                  _lastTrailPos;

        // ── Conditions ───────────────────────────────────────────────────────────
        protected override bool ActivationCondition(ElementalMutationManager emm)
            => emm.Kinesia > ActivateK && emm.Transfinite < ActivateT;

        protected override bool DeactivationCondition(ElementalMutationManager emm)
            => emm.Kinesia < DeactivateK || emm.Transfinite >= DeactivateT;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        public override void OnActivate(SynergyDependencies deps)
        {
            _emm     = deps.Emm;
            _weapons = deps.Weapons;
            _lastTrailPos = deps.Movement != null ? deps.Movement.transform.position : Vector3.zero;

            GameplayEventBus.OnDamageProcessing += HandleDamage;

            if (_weapons != null)
                ApplyWeaponBuffs(_weapons.ActiveWeapon);
        }

        public override void OnDeactivate(SynergyDependencies deps)
        {
            GameplayEventBus.OnDamageProcessing -= HandleDamage;

            RemoveWeaponBuffs(_subscribedWeapon);
            _subscribedWeapon = null;
            _emm     = null;
            _weapons = null;
        }

        public override void OnTick(SynergyDependencies deps, float dt)
        {
            // Re-subscribe if weapon changed (SwitchToSlot destroys old weapon and spawns new)
            if (_weapons != null && _weapons.ActiveWeapon != _subscribedWeapon)
            {
                RemoveWeaponBuffs(_subscribedWeapon);
                ApplyWeaponBuffs(_weapons.ActiveWeapon);
            }

            // Fire trail — one decal per step
            if (deps.Movement != null)
            {
                Vector3 pos = deps.Movement.transform.position;
                if (Vector3.Distance(pos, _lastTrailPos) >= TrailStepDistance)
                {
                    _lastTrailPos = pos;
                    SpawnDecal(pos, deps.EnemyLayer);
                }
            }
        }

        // ── Weapon buff management ────────────────────────────────────────────────
        private void ApplyWeaponBuffs(WeaponBase weapon)
        {
            if (weapon == null) return;
            weapon.FireRateMultiplier   = FireRateMult;
            weapon.ReloadTimeMultiplier = ReloadTimeMult;
            weapon.OnReloadStart       += HandleReload;
            _subscribedWeapon           = weapon;
        }

        private void RemoveWeaponBuffs(WeaponBase weapon)
        {
            if (weapon == null) return; // Unity-safe: null check covers destroyed objects
            weapon.FireRateMultiplier   = 1f;
            weapon.ReloadTimeMultiplier = 1f;
            weapon.OnReloadStart       -= HandleReload;
        }

        // ── Callbacks ────────────────────────────────────────────────────────────
        private void HandleReload()
        {
            _emm?.ModifyStat(MutationType.Smallion, -SmallionPerReload);
        }

        private void HandleDamage(DamageContext ctx)
        {
            // Only double damage aimed at the player
            if (ctx.Target != null && ctx.Target.TryGetComponent<PlayerHealth>(out _))
                ctx.FinalDamage *= 2f;
        }

        // ── Fire trail ────────────────────────────────────────────────────────────
        private static void SpawnDecal(Vector3 pos, LayerMask enemyLayer)
        {
            var go = new GameObject("_DamageDecal");
            go.transform.position = pos;
            go.AddComponent<DamageDecal>().Initialize(
                TrailRadius, TrailDamagePerTick, TrailLifetime, TrailTickInterval, enemyLayer);
        }
    }
}
