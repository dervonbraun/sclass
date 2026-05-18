using System.Collections.Generic;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Компонент, управляющий активными эффектами на сущности.
    /// Вешается на игрока или врагов.
    /// </summary>
    public class EffectManager : MonoBehaviour
    {
        private readonly List<BaseEffect> _activeEffects = new List<BaseEffect>();
        
        public event System.Action<BaseEffect> OnEffectAdded;
        public event System.Action<BaseEffect> OnEffectRemoved;

        public IReadOnlyList<BaseEffect> ActiveEffects => _activeEffects;

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                _activeEffects[i].Tick(dt);
            }
        }

        public void AddEffect(BaseEffect effect)
        {
            if (!_activeEffects.Contains(effect))
            {
                _activeEffects.Add(effect);
                effect.Apply(gameObject);
                OnEffectAdded?.Invoke(effect);
            }
        }

        public void RemoveEffect(BaseEffect effect)
        {
            if (_activeEffects.Contains(effect))
            {
                effect.Remove();
                _activeEffects.Remove(effect);
                OnEffectRemoved?.Invoke(effect);
            }
        }

        private void OnDestroy()
        {
            // Защита от утечек памяти: снимаем все эффекты при уничтожении объекта
            for (int i = _activeEffects.Count - 1; i >= 0; i--)
            {
                _activeEffects[i].Remove();
            }
            _activeEffects.Clear();
        }
    }
}
