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
        public static event Action<DamageContext> OnDamageProcessing;

        public static void ProcessDamage(DamageContext context)
        {
            OnDamageProcessing?.Invoke(context);
        }
    }
}
