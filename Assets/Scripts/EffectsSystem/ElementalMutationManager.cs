using System;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    [DisallowMultipleComponent]
    public sealed class ElementalMutationManager : MonoBehaviour
    {
        // ── Elemental Scales ────────────────────────────────────────────────────
        [Header("Mutation Stats")]
        [SerializeField] private float _kinesia     = 10f;
        [SerializeField] private float _smallion    = 10f;
        [SerializeField] private float _transfinite = 10f;

        private const float MinStat              = 0f;
        private const float MaxStat              = 100f;
        private const float NormBase             = 10f;
        private const float KinesiaSeizureThreshold = 5f;
        private const float BurnCoefficient      = 0.1f;

        // ── Dependencies ────────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private PlayerHealth   _playerHealth;
        [SerializeField] private PlayerMovement _playerMovement;
        [SerializeField] private Camera         _camera;

        [Header("Smallion Lerp Speeds")]
        [SerializeField] private float _scaleLerpSpeed = 5f;
        [SerializeField] private float _fovLerpSpeed   = 5f;

        // Captured at Awake — ground truth at Smallion == 10
        private Vector3 _baseScale;
        private float   _baseFov;
        private bool    _isDead;

        // ── UI Event ────────────────────────────────────────────────────────────
        public static readonly Color KinesiaColor    = new Color(0.20f, 0.90f, 0.20f);
        public static readonly Color SmallionColor   = new Color(0.20f, 0.50f, 1.00f);
        public static readonly Color TransfiniteColor = new Color(1.00f, 0.20f, 0.20f);

        public static event Action<MutationUIData> OnUIUpdate;

        // ── Read-only accessors for zones / UI ──────────────────────────────────
        public float Kinesia     => _kinesia;
        public float Smallion    => _smallion;
        public float Transfinite => _transfinite;

        // ── Lifecycle ───────────────────────────────────────────────────────────
        private void Awake()
        {
            _baseScale = transform.localScale;

            if (_camera == null)
            {
                _camera = GetComponentInChildren<Camera>(true);
                if (_camera == null) _camera = Camera.main;
            }

            if (_camera != null)
                _baseFov = _camera.fieldOfView;
        }

        private void OnEnable()
        {
            GameplayEventBus.OnDamageProcessing += ApplyTransfiniteModifier;
        }

        private void OnDisable()
        {
            GameplayEventBus.OnDamageProcessing -= ApplyTransfiniteModifier;
        }

        private void Update()
        {
            if (_isDead) return;

            ApplyKinesiaEffects();
            ApplySmallionEffects();
            ApplyBurn(Time.deltaTime);
            CheckForDeath();
            BroadcastUIData();
        }

        // ── Public API ──────────────────────────────────────────────────────────
        public void ModifyStat(MutationType type, float amount)
        {
            switch (type)
            {
                case MutationType.Kinesia:
                    _kinesia = Mathf.Clamp(_kinesia + amount, MinStat, MaxStat);
                    break;
                case MutationType.Smallion:
                    _smallion = Mathf.Clamp(_smallion + amount, MinStat, MaxStat);
                    break;
                case MutationType.Transfinite:
                    _transfinite = Mathf.Clamp(_transfinite + amount, MinStat, MaxStat);
                    break;
            }
        }

        // ── Kinesia → Speed & Seizure ───────────────────────────────────────────
        private void ApplyKinesiaEffects()
        {
            if (_playerMovement == null) return;

            _playerMovement.SpeedMultiplier = _kinesia / NormBase;

            bool seized = _kinesia < KinesiaSeizureThreshold;
            _playerMovement.JumpBlocked   = seized;
            _playerMovement.SprintBlocked = seized;
        }

        // ── Smallion → Scale & FOV ──────────────────────────────────────────────
        private void ApplySmallionEffects()
        {
            // Guard: avoid div-by-zero; death will fire anyway if smallion == 0
            if (_smallion <= 0f) return;

            float t = Time.deltaTime;

            // Bigger Smallion → smaller physical presence
            Vector3 targetScale = _baseScale * (NormBase / _smallion);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, _scaleLerpSpeed * t);

            if (_camera != null)
            {
                float targetFov = _baseFov * (_smallion / NormBase);
                _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, targetFov, _fovLerpSpeed * t);
            }
        }

        // ── Transfinite → Damage Resistance (via GameplayEventBus) ──────────────
        private void ApplyTransfiniteModifier(DamageContext context)
        {
            if (_transfinite <= 0f) return;
            // At 10 → multiplier 1×, at 50 → 0.2×, at 2 → 5×
            context.FinalDamage = context.RawDamage * (NormBase / _transfinite);
        }

        // ── Death Check ─────────────────────────────────────────────────────────
        private void CheckForDeath()
        {
            if (_kinesia <= 0f || _smallion <= 0f || _transfinite <= 0f)
            {
                _isDead = true;
                _playerHealth?.Die();
            }
        }

        // Burn mechanic: scales above 50 reduce other two scales proportionally
        private void ApplyBurn(float dt)
        {
            // Compute burn for each scale if above neutral (50)
            if (_kinesia > 50f)
            {
                float burn = (_kinesia - 50f) * BurnCoefficient * dt;
                _smallion = Mathf.Max(MinStat, _smallion - burn);
                _transfinite = Mathf.Max(MinStat, _transfinite - burn);
            }
            if (_smallion > 50f)
            {
                float burn = (_smallion - 50f) * BurnCoefficient * dt;
                _kinesia = Mathf.Max(MinStat, _kinesia - burn);
                _transfinite = Mathf.Max(MinStat, _transfinite - burn);
            }
            if (_transfinite > 50f)
            {
                float burn = (_transfinite - 50f) * BurnCoefficient * dt;
                _kinesia = Mathf.Max(MinStat, _kinesia - burn);
                _smallion = Mathf.Max(MinStat, _smallion - burn);
            }
        }
        // ── UI Broadcast ────────────────────────────────────────────────────────
        private void BroadcastUIData()
        {
            float total = _kinesia + _smallion + _transfinite;
            if (total <= 0f) return;

            OnUIUpdate?.Invoke(new MutationUIData
            {
                KinesiaRatio      = _kinesia     / total,
                SmallionRatio     = _smallion    / total,
                TransfiniteRatio  = _transfinite / total,
                KinesiaColor      = KinesiaColor,
                SmallionColor     = SmallionColor,
                TransfiniteColor  = TransfiniteColor,
            });
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            UnityEditor.Handles.color = KinesiaColor;
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f,
                $"K:{_kinesia:F1}  S:{_smallion:F1}  T:{_transfinite:F1}");
        }
#endif
    }
}
