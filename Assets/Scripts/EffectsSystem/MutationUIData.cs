namespace Sclass.EffectsSystem
{
    /// <summary>
    /// Data structure sent to UI for mutation visualisation.
    /// </summary>
    public struct MutationUIData
    {
        public float KinesiaRatio;   // normalized [0..1]
        public float SmallionRatio;
        public float TransfiniteRatio;
        public UnityEngine.Color KinesiaColor;
        public UnityEngine.Color SmallionColor;
        public UnityEngine.Color TransfiniteColor;
    }
}
