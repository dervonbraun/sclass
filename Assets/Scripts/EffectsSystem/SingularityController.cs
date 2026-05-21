using System;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// God Singularity — Class-3 synergy effect.
    /// Activates when Kinesia AND Smallion AND Transfinite all exceed the threshold (default 50).
    /// Attach to the same GameObject as ElementalMutationManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SingularityController : MonoBehaviour
    {
        // ── Activation ──────────────────────────────────────────────────────────
        [Header("Activation")]
        [SerializeField] private float _activationThreshold = 50f;

        // ── Time Distortion ─────────────────────────────────────────────────────
        [Header("Time Distortion")]
        [Tooltip("Global Time.timeScale while active. Player is compensated to feel 1×.")]
        [SerializeField] private float _worldTimeScale = 0.2f;

        // ── Kinetic Annihilation ─────────────────────────────────────────────────
        [Header("Kinetic Annihilation")]
        [Tooltip("Radius of the shockwave sphere around the player.")]
        [SerializeField] private float _annihilationRadius = 5f;
        [Tooltip("Flat damage dealt to every enemy inside the sphere. Effectively instakills.")]
        [SerializeField] private float _annihilationDamage = 99999f;
        [Tooltip("Meters the player must travel before the next shockwave fires.")]
        [SerializeField] private float _stepDistance = 1.5f;
        [Tooltip("Layer mask for enemies (AgentHealth objects).")]
        [SerializeField] private LayerMask _enemyLayer;

        // ── Doomsday Burn ────────────────────────────────────────────────────────
        [Header("Doomsday Burn")]
        [Tooltip("All three scales drain at this rate per real second while active.")]
        [SerializeField] private float _burnPerSecond = 5f;

        // ── Kill Restoration ─────────────────────────────────────────────────────
        [Header("Kill Restoration")]
        [Tooltip("Each kill restores this amount to all three scales.")]
        [SerializeField] private float _restorePerKill = 10f;

        // ── Dependencies ─────────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private ElementalMutationManager _emm;
        [SerializeField] private PlayerMovement           _movement;
        [SerializeField] private Animator                 _animator; // optional

        // ── Public state ─────────────────────────────────────────────────────────
        public bool IsActive { get; private set; }

        /// <summary>Fires true on enter, false on exit. EffectsHydraBar subscribes for visual feedback.</summary>
        public static event Action<bool> OnSingularityChanged;

        // ── Private state ────────────────────────────────────────────────────────
        private float   _savedTimeScale;
        private float   _savedFixedDeltaTime;
        private Vector3 _lastShockwavePos;

        // ── Lifecycle ────────────────────────────────────────────────────────────
        private void Awake()
        {
            if (_emm     == null) _emm      = GetComponent<ElementalMutationManager>();
            if (_movement == null) _movement = GetComponent<PlayerMovement>();
        }

        private void OnEnable()
        {
            AgentHealth.OnAgentKilled += HandleKill;
        }

        private void OnDisable()
        {
            AgentHealth.OnAgentKilled -= HandleKill;
            ExitSingularity(); // safe even if already inactive
        }

        private void Update()
        {
            bool shouldBeActive = _emm.Kinesia     > _activationThreshold
                               && _emm.Smallion    > _activationThreshold
                               && _emm.Transfinite > _activationThreshold;

            if (shouldBeActive && !IsActive)
                EnterSingularity();
            else if (!shouldBeActive && IsActive)
                ExitSingularity();

            if (!IsActive) return;

            BurnResources();
            CheckAnnihilationStep();
        }

        // ── Enter / Exit ─────────────────────────────────────────────────────────
        private void EnterSingularity()
        {
            IsActive = true;

            _savedTimeScale       = Time.timeScale;
            _savedFixedDeltaTime  = Time.fixedDeltaTime;
            _lastShockwavePos     = transform.position;

            Time.timeScale       = _worldTimeScale;
            Time.fixedDeltaTime  = 0.02f * _worldTimeScale;

            float compensation = 1f / _worldTimeScale; // 5× at timeScale 0.2

            // SingularityActive bypasses EMM's Kinesia multiplier so they don't compound
            _movement.SingularityActive          = true;
            _movement.TimeCompensationMultiplier = compensation;
            _movement.GravityMultiplier          = compensation;

            if (_animator != null) _animator.speed = compensation;

            OnSingularityChanged?.Invoke(true);
        }

        private void ExitSingularity()
        {
            if (!IsActive) return;
            IsActive = false;

            Time.timeScale      = _savedTimeScale;
            Time.fixedDeltaTime = _savedFixedDeltaTime;

            _movement.SingularityActive          = false;
            _movement.TimeCompensationMultiplier = 1f;
            _movement.GravityMultiplier          = 1f;

            if (_animator != null) _animator.speed = 1f;

            OnSingularityChanged?.Invoke(false);
        }

        // ── Doomsday Burn ─────────────────────────────────────────────────────────
        // Uses unscaledDeltaTime so burn rate is always 5/s regardless of timeScale
        private void BurnResources()
        {
            float burn = _burnPerSecond * Time.unscaledDeltaTime;
            _emm.ModifyStat(MutationType.Kinesia,     -burn);
            _emm.ModifyStat(MutationType.Smallion,    -burn);
            _emm.ModifyStat(MutationType.Transfinite, -burn);
        }

        // ── Kinetic Annihilation ──────────────────────────────────────────────────
        private void CheckAnnihilationStep()
        {
            if (Vector3.Distance(transform.position, _lastShockwavePos) < _stepDistance) return;

            _lastShockwavePos = transform.position;
            TriggerAnnihilation();
        }

        private void TriggerAnnihilation()
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, _annihilationRadius, _enemyLayer);
            foreach (Collider col in hits)
            {
                IDamageable target = col.GetComponentInParent<IDamageable>();
                target?.TakeDamage(_annihilationDamage);
            }
        }

        // ── Kill Restoration ──────────────────────────────────────────────────────
        private void HandleKill()
        {
            if (!IsActive) return;
            _emm.ModifyStat(MutationType.Kinesia,     _restorePerKill);
            _emm.ModifyStat(MutationType.Smallion,    _restorePerKill);
            _emm.ModifyStat(MutationType.Transfinite, _restorePerKill);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!IsActive) return;
            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _annihilationRadius);
        }
#endif
    }
}
