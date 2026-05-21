using UnityEngine;

namespace Sclass.EffectsSystem
{
    /// <summary>
    /// All player-side references needed by Class-2 synergy effects.
    /// Populated once in SynergyManager.Awake and passed by value to each effect call.
    /// </summary>
    public struct SynergyDependencies
    {
        public ElementalMutationManager Emm;
        public PlayerMovement           Movement;
        public PlayerHealth             Health;
        public SwarmManager             Swarm;
        public WeaponHolder             Weapons;
        public LayerMask                EnemyLayer;
    }
}
