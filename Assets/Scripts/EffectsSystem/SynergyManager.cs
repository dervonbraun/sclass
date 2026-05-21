using System;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Evaluates Class-2 synergy recipes each frame, enforces single-effect exclusivity,
    /// and broadcasts HUD change events.
    ///
    /// Priority order (index 0 = highest priority):
    ///   1. SuperdenseBlackness (S<10, T>60)
    ///   2. KineticTax          (K>60, T<10)
    ///   3. FlickeringWanderer  (K>50, S>50)
    ///
    /// Mathematical analysis: only FlickeringWanderer and KineticTax can theoretically
    /// co-qualify (K>60, S>50, T<10). Priority resolves the tie — KineticTax wins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SynergyManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private ElementalMutationManager _emm;
        [SerializeField] private PlayerMovement           _movement;
        [SerializeField] private PlayerHealth             _health;
        [SerializeField] private SwarmManager             _swarm;
        [SerializeField] private WeaponHolder             _weapons;

        [Header("Combat")]
        [Tooltip("Layer mask for swarm agents (used for kinetic ram and fire trail damage).")]
        [SerializeField] private LayerMask _enemyLayer;

        // ── Public state ─────────────────────────────────────────────────────────
        public SynergyType ActiveSynergy { get; private set; }

        /// <summary>Fires on every synergy change, including to None.</summary>
        public static event Action<SynergyType> OnActiveSynergyChanged;

        // ── Effect registry — order = priority ───────────────────────────────────
        private readonly SynergyEffect[] _effects =
        {
            new SuperdenseBlackness(),
            new KineticTax(),
            new FlickeringWanderer(),
        };

        private SynergyEffect    _active;
        private SynergyDependencies _deps;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void Awake()
        {
            _deps = new SynergyDependencies
            {
                Emm       = _emm,
                Movement  = _movement,
                Health    = _health,
                Swarm     = _swarm,
                Weapons   = _weapons,
                EnemyLayer = _enemyLayer,
            };
        }

        private void Update()
        {
            // 1. Evaluate eligibility for every effect (updates internal hysteresis)
            SynergyEffect candidate = null;
            foreach (var effect in _effects)
            {
                if (effect.EvaluateEligibility(_emm))
                {
                    candidate = effect;
                    break; // first by priority wins
                }
            }

            // 2. Transition if the active effect changed
            if (candidate != _active)
            {
                _active?.OnDeactivate(_deps);
                _active = candidate;
                _active?.OnActivate(_deps);

                ActiveSynergy = _active?.Type ?? SynergyType.None;
                OnActiveSynergyChanged?.Invoke(ActiveSynergy);
            }

            // 3. Tick the active effect
            _active?.OnTick(_deps, Time.unscaledDeltaTime);
        }

        private void OnDisable()
        {
            if (_active == null) return;
            _active.OnDeactivate(_deps);
            foreach (var e in _effects) e.ResetEligibility();
            _active = null;
            ActiveSynergy = SynergyType.None;
            OnActiveSynergyChanged?.Invoke(SynergyType.None);
        }
    }
}
