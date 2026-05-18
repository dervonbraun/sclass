using UnityEngine;

namespace Sclass.EffectsSystem
{
    public enum StatModifierType
    {
        Flat = 100,
        PercentAdd = 200,
        PercentMult = 300,
    }

    public class StatModifier
    {
        public readonly float Value;
        public readonly StatModifierType Type;
        public readonly object Source;

        public StatModifier(float value, StatModifierType type, object source = null)
        {
            Value = value;
            Type = type;
            Source = source;
        }
    }
}
