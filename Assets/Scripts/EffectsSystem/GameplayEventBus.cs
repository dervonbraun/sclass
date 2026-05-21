using System;
using UnityEngine;

namespace Sclass.EffectsSystem
{
    public class DamageContext
    {
        public GameObject Sender;
        public GameObject Target;
        public float RawDamage;
        public float FinalDamage;
        public bool IsCancelled;
        public int Flags;
    }

    /// <summary>
    /// Mutable context passed to OnProjectileHit subscribers.
    /// Set IsReflected = true to reverse the projectile instead of destroying it.
    /// </summary>
    public class ProjectileHitContext
    {
        public GameObject Target;
        public float      Damage;
        public bool       IsReflected;
        public float      ReflectDamageMultiplier;
    }

    public static class GameplayEventBus
    {
        // В Noita-like играх бывает полезно иметь как глобальную шину, 
        // так и локальную (на конкретном энтити). Здесь представлена глобальная 
        // для простоты подписки из любой точки.
        
        public static event Action<DamageContext> OnDamageProcessing;

        /// <summary>
        /// Вызывает цепочку обработки урона. Эффекты могут изменять FinalDamage или отменять урон (IsCancelled).
        /// </summary>
        public static void ProcessDamage(DamageContext context)
        {
            OnDamageProcessing?.Invoke(context);
        }

        /// <summary>
        /// Fired by Projectile when it is about to hit the player.
        /// SuperdenseBlackness subscribes and may set IsReflected = true.
        /// </summary>
        public static event Action<ProjectileHitContext> OnProjectileHit;

        public static void ProcessProjectileHit(ProjectileHitContext context)
        {
            OnProjectileHit?.Invoke(context);
        }
    }
}
