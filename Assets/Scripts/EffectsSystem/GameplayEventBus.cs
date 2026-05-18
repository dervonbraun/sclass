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
    }
}
