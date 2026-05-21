using Unity.Mathematics;

public enum AgentType : byte
{
    Kinesia,
    Smallion,
    Transfinite
}

public struct AgentData
{
    public float3 Velocity;
    public SwarmState State;
    public float StateTimer;
    public float WanderAngle;
    public float LateralOffset;
    public float VerticalOffset; // личная фаза синусоиды вертикального блуждания [0..1]
    public uint RandomSeed;
    public AgentType Type;
}
