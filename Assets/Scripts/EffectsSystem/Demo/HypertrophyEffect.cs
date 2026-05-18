using UnityEngine;

namespace Sclass.EffectsSystem.Demo
{
    public class HypertrophyEffect : BaseEffect
    {
        private StatModifier _hpModifier;
        private StatModifier _hitboxModifier;

        protected override void OnApply(GameObject target)
        {
            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health != null)
            {
                // Увеличиваем Макс. HP на 50%
                _hpModifier = new StatModifier(0.5f, StatModifierType.PercentAdd, this);
                health.MaxHealth.AddModifier(_hpModifier);
                
                // Увеличиваем радиус получения урона (HitboxRadiusMultiplier) на 30%
                _hitboxModifier = new StatModifier(0.3f, StatModifierType.PercentAdd, this);
                health.HitboxRadiusMultiplier.AddModifier(_hitboxModifier);
                
                // Подлечиваем игрока на ту же сумму (опционально)
                health.Heal(health.MaxHealth.GetValue() * 0.5f);
            }
        }

        protected override void OnRemove(GameObject target)
        {
            PlayerHealth health = target.GetComponent<PlayerHealth>();
            if (health != null)
            {
                if (_hpModifier != null)
                {
                    health.MaxHealth.RemoveModifier(_hpModifier);
                    // Корректируем текущее здоровье, если оно вышло за пределы нового максимума
                    if (health.CurrentHealth > health.MaxHealth.GetValue())
                    {
                        health.TakeDamage(health.CurrentHealth - health.MaxHealth.GetValue(), null);
                    }
                }
                
                if (_hitboxModifier != null)
                {
                    health.HitboxRadiusMultiplier.RemoveModifier(_hitboxModifier);
                }
            }
        }
    }
}
