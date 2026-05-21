namespace Sclass.EffectsSystem
{
    public enum SynergyType
    {
        None                = 0,
        FlickeringWanderer  = 1,   // Kinesia > 50  && Smallion > 50
        SuperdenseBlackness = 2,   // Smallion < 10 && Transfinite > 60
        KineticTax          = 3,   // Kinesia > 60  && Transfinite < 10
    }
}
