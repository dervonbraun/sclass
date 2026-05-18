using UnityEngine;

namespace Sclass.EffectsSystem
{
    public abstract class BaseEffect
    {
        public bool IsActive { get; private set; }
        public GameObject Target { get; private set; }

        // --- UI Properties ---
        public Color EffectColor { get; protected set; } = Color.white;
        public float HudWeight { get; set; } = 1f;

        public void Apply(GameObject target)
        {
            if (IsActive) return;
            Target = target;
            IsActive = true;
            OnApply(target);
        }

        public void Remove()
        {
            if (!IsActive) return;
            OnRemove(Target);
            IsActive = false;
            Target = null;
        }

        public void Tick(float deltaTime)
        {
            if (!IsActive) return;
            OnTick(deltaTime);
        }

        /// <summary>
        /// Вызывается при наложении эффекта. 
        /// Идеальное место для добавления StatModifier и подписки на события.
        /// </summary>
        protected abstract void OnApply(GameObject target);

        /// <summary>
        /// Вызывается при снятии эффекта.
        /// ОБЯЗАТЕЛЬНО отписываться от событий и удалять свои модификаторы!
        /// </summary>
        protected abstract void OnRemove(GameObject target);

        /// <summary>
        /// Опциональный метод для периодической логики эффекта.
        /// </summary>
        protected virtual void OnTick(float deltaTime) { }
    }
}
