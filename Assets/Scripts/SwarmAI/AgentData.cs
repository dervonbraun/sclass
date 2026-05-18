using Unity.Mathematics;

public struct AgentData
{
    public float3 Velocity;
    public SwarmState State;
    public float StateTimer; // Можно использовать для кулдаунов атак или задержек
    public float WanderAngle; // Угол блуждания для разбиения идеальных линий
    public float LateralOffset; // Предпочтительное боковое смещение агента (от -1 до 1) — имитация "слоя" жидкости
    public uint RandomSeed; // Персональный сид для стабильного псевдо-RNG внутри Job'ы
}
