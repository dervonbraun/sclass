using Unity.Mathematics;

public struct AgentData
{
    public float3 Velocity;
    public SwarmState State;
    public float StateTimer;
    public float WanderAngle;
    public float LateralOffset;
    public float VerticalOffset;
    public uint RandomSeed;
}
