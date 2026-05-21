namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Base class for a Class-2 synergy effect.
    /// Subclasses define activation/deactivation conditions (with built-in hysteresis)
    /// and the three lifecycle callbacks.
    ///
    /// Hysteresis pattern:
    ///   - While NOT eligible: check ActivationCondition  (strict threshold)
    ///   - While eligible:     check DeactivationCondition (relaxed threshold = deadband)
    ///
    /// SynergyManager calls EvaluateEligibility every frame, then picks the highest-priority
    /// eligible effect. Only one Class-2 effect can be active at a time.
    /// </summary>
    public abstract class SynergyEffect
    {
        public abstract SynergyType Type { get; }

        // Internal hysteresis state — tracks eligibility independently of activation
        private bool _wasEligible;

        /// <summary>
        /// Updates eligibility state with hysteresis. Call once per frame before reading.
        /// </summary>
        public bool EvaluateEligibility(ElementalMutationManager emm)
        {
            if (!_wasEligible)
                _wasEligible = ActivationCondition(emm);
            else
                _wasEligible = !DeactivationCondition(emm);

            return _wasEligible;
        }

        /// <summary>Reset hysteresis (e.g. on scene reset). Call from SynergyManager.OnDisable.</summary>
        public void ResetEligibility() => _wasEligible = false;

        // ── Condition overrides ──────────────────────────────────────────────────

        /// <summary>Strict condition: all thresholds must be satisfied to start the effect.</summary>
        protected abstract bool ActivationCondition(ElementalMutationManager emm);

        /// <summary>
        /// Lenient condition: returns true when the effect SHOULD STOP.
        /// Should use a 5-unit deadband (e.g. activate at K>50, deactivate at K<45).
        /// </summary>
        protected abstract bool DeactivationCondition(ElementalMutationManager emm);

        // ── Lifecycle callbacks ──────────────────────────────────────────────────

        public abstract void OnActivate(SynergyDependencies deps);
        public abstract void OnDeactivate(SynergyDependencies deps);

        /// <param name="deps">All player references.</param>
        /// <param name="dt">Time.unscaledDeltaTime — effects use this so resource drain
        ///                  is not affected by SingularityController's timeScale change.</param>
        public abstract void OnTick(SynergyDependencies deps, float dt);
    }
}
