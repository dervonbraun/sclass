using System;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    [DisallowMultipleComponent]
    public sealed class ElementalMutationManager : MonoBehaviour
    {
        // ── Elemental Scales ────────────────────────────────────────────────────
        [Header("Mutation Stats")]
        [SerializeField] private float _kinesia     = 50f;
        [SerializeField] private float _smallion    = 50f;
        [SerializeField] private float _transfinite = 50f;

        private const float MinStat         = 0f;
        private const float MaxStat         = 100f;
        private const float Neutral         = 50f;
        private const float BurnCoefficient = 0.1f;

        // ── Dependencies ────────────────────────────────────────────────────────
        [Header("Dependencies")]
        [SerializeField] private PlayerHealth   _playerHealth;
        [SerializeField] private PlayerMovement _playerMovement;
        [SerializeField] private WeaponHolder   _weaponHolder;

        private bool _isDead;

        // ── UI Event ────────────────────────────────────────────────────────────
        public static readonly Color KinesiaColor     = new Color(0.20f, 0.90f, 0.20f);
        public static readonly Color SmallionColor    = new Color(0.20f, 0.50f, 1.00f);
        public static readonly Color TransfiniteColor = new Color(1.00f, 0.20f, 0.20f);

        public static event Action<MutationUIData> OnUIUpdate;

        // ── Read-only accessors ──────────────────────────────────────────────────
        public float Kinesia     => _kinesia;
        public float Smallion    => _smallion;
        public float Transfinite => _transfinite;

        // Smallion: BaseRate * (Smallion / 50)
        public float SmallionAbsorptionMultiplier => _smallion / Neutral;

        // Kinesia: BaseAccuracy * (50 / Kinesia) — high Kinesia = lower spread
        public float KinesiaSpreadMultiplier => _kinesia > 0f ? Neutral / _kinesia : float.MaxValue;

        // Transfinite economy
        public float TransfiniteIncomeMultiplier   => _transfinite > 0f ? Neutral / _transfinite : float.MaxValue;
        public float TransfiniteEffectMultiplier   => _transfinite / Neutral;
        public float TransfiniteDurationMultiplier => _transfinite > 0f ? Neutral / _transfinite : float.MaxValue;

        // ── Lifecycle ───────────────────────────────────────────────────────────
        private void Awake()
        {
            _kinesia = _smallion = _transfinite = Neutral;
        }

        private void Update()
        {
            if (_isDead) return;

            ApplyKinesiaEffects();
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

        // ── Kinesia → Speed & Spread ─────────────────────────────────────────────
        private void ApplyKinesiaEffects()
        {
            if (_playerMovement != null)
                _playerMovement.SpeedMultiplier = _kinesia / Neutral;

            if (_weaponHolder != null && _weaponHolder.ActiveWeapon != null)
                _weaponHolder.ActiveWeapon.SpreadMultiplier = KinesiaSpreadMultiplier;
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

        // ── Burn: scales above 50 drain the other two ────────────────────────────
        private void ApplyBurn(float dt)
        {
            if (_kinesia > Neutral)
            {
                float burn = (_kinesia - Neutral) * BurnCoefficient * dt;
                _smallion    = Mathf.Max(MinStat, _smallion    - burn);
                _transfinite = Mathf.Max(MinStat, _transfinite - burn);
            }
            if (_smallion > Neutral)
            {
                float burn = (_smallion - Neutral) * BurnCoefficient * dt;
                _kinesia     = Mathf.Max(MinStat, _kinesia     - burn);
                _transfinite = Mathf.Max(MinStat, _transfinite - burn);
            }
            if (_transfinite > Neutral)
            {
                float burn = (_transfinite - Neutral) * BurnCoefficient * dt;
                _kinesia  = Mathf.Max(MinStat, _kinesia  - burn);
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
                KinesiaRatio     = _kinesia     / total,
                SmallionRatio    = _smallion    / total,
                TransfiniteRatio = _transfinite / total,
                KinesiaColor     = KinesiaColor,
                SmallionColor    = SmallionColor,
                TransfiniteColor = TransfiniteColor,
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
