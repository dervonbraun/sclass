using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine.Jobs;

// Джоба 1: Копирование позиций Transform'ов в NativeArray для безопасного чтения другими джобами
[BurstCompile]
public struct CopyPositionsJob : IJobParallelForTransform
{
    [WriteOnly] public NativeArray<float3> Positions;
    public void Execute(int index, TransformAccess transform)
    {
        Positions[index] = transform.position;
    }
}

// Джоба 2: Заполнение Spatial Hash Grid (NativeParallelMultiHashMap)
[BurstCompile]
public struct HashPositionsJob : IJobParallelForTransform
{
    [WriteOnly] public NativeParallelMultiHashMap<int, int>.ParallelWriter HashMap;
    [ReadOnly]  public NativeArray<AgentData> Agents;
    public float CellSize;

    public void Execute(int index, TransformAccess transform)
    {
        // Мёртвые агенты не добавляем в хэш — живые не будут видеть их и реагировать
        if (Agents[index].State == SwarmState.Dead) return;

        int3 cell = math.int3(math.floor(transform.position / CellSize));
        int hash = (int)math.hash(cell);
        HashMap.Add(hash, index);
    }
}

// Джоба 3: Основная физика роя — модель «жидкости» (SPH-inspired) + 3D SDF-коллизии
[BurstCompile]
public struct SwarmJob : IJobParallelForTransform
{
    [NativeDisableParallelForRestriction] public NativeArray<AgentData> Agents;
    [ReadOnly] public NativeArray<float3> FlowField;
    [ReadOnly] public NativeArray<byte> CostField;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> HashMap;
    [ReadOnly] public NativeArray<float3> AgentPositions;

    // 3D SDF Grid
    [ReadOnly] public NativeArray<float3> SDF_Gradient3D;
    [ReadOnly] public NativeArray<float> SDF_Distance3D;
    public float3 SDFOrigin;
    public int3 SDFGridSize;
    public float SDFCellSize;

    public int2 GridSize;
    public float FlowFieldCellSize;
    public float3 FlowFieldOrigin;

    public float3 PlayerPosition;
    public float DeltaTime;
    public float Time;

    // Boids Weights
    public float SeparationWeight;
    public float AlignmentWeight;
    public float CohesionWeight;
    public float FlowFieldWeight;
    public float WanderWeight;
    public float ObstacleAvoidWeight;

    // --- Fluid-specific weights ---
    public float PressureWeight;
    public float LateralSpreadWeight;
    public float ViscosityWeight;

    // Radii
    public float SeparationRadius;
    public float NeighborRadius;
    public float ObstacleAvoidRadius;

    // Movement params
    public float MaxSpeed;
    public float ChargeSpeedMult;
    public float ChargeRadius;
    public float PlayerRadius;

    public float HashCellSize;
    public float TargetDensity;
    public float AgentRadius;
    public float WallDistance;

    private int SDF_GetIndex(float3 worldPos)
    {
        float3 local = worldPos - SDFOrigin;
        int x = math.clamp((int)math.floor(local.x / SDFCellSize), 0, SDFGridSize.x - 1);
        int y = math.clamp((int)math.floor(local.y / SDFCellSize), 0, SDFGridSize.y - 1);
        int z = math.clamp((int)math.floor(local.z / SDFCellSize), 0, SDFGridSize.z - 1);
        return x + y * SDFGridSize.x + z * SDFGridSize.x * SDFGridSize.y;
    }

    private float3 SampleSDF_Gradient(float3 worldPos) => SDF_Gradient3D[SDF_GetIndex(worldPos)];
    private float SampleSDF_Distance(float3 worldPos) => SDF_Distance3D[SDF_GetIndex(worldPos)];

    public void Execute(int index, TransformAccess transform)
    {
        float3 position = transform.position;
        AgentData agent = Agents[index];

        if (agent.State == SwarmState.Dead) return;

        // --- 1. FSM (Конечный автомат) ---
        float distToPlayerSq = math.distancesq(position, PlayerPosition);
        if (agent.State == SwarmState.Swarming)
        {
            if (distToPlayerSq < ChargeRadius * ChargeRadius)
            {
                agent.State = SwarmState.Charging;
            }
        }
        else if (agent.State == SwarmState.Charging)
        {
            if (distToPlayerSq > (ChargeRadius * 1.5f) * (ChargeRadius * 1.5f))
            {
                agent.State = SwarmState.Swarming;
            }
        }

        // --- 2. Подсчёт локальной плотности и сил Boids (Spatial Hashing) ---
        float3 separation = float3.zero;
        float3 alignment = float3.zero;
        float3 cohesion = float3.zero;
        float3 viscosity = float3.zero;
        float3 densityGradient = float3.zero;
        int neighborCount = 0;
        int sepCount = 0;
        float localDensity = 0f;

        int3 cell = math.int3(math.floor(position / HashCellSize));

        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
        {
            int3 neighborCell = cell + new int3(x, y, z);
            int hash = (int)math.hash(neighborCell);

            if (HashMap.TryGetFirstValue(hash, out int otherIndex, out NativeParallelMultiHashMapIterator<int> it))
            {
                do
                {
                    if (index == otherIndex) continue;
                    if (Agents[otherIndex].State == SwarmState.Dead) continue;

                    float3 otherPos = AgentPositions[otherIndex];
                    float distSq = math.distancesq(position, otherPos);

                    if (distSq < NeighborRadius * NeighborRadius)
                    {
                        float dist = math.sqrt(distSq);

                        float q = dist / NeighborRadius;
                        float kernel = math.max(0f, 1f - q);
                        localDensity += kernel * kernel;

                        if (dist > 0.001f)
                            densityGradient += (position - otherPos) / dist * kernel;

                        if (dist < SeparationRadius && dist > 0.001f)
                        {
                            separation += (position - otherPos) / distSq;
                            sepCount++;
                        }

                        float agentDiameter = AgentRadius * 2f;
                        if (distSq < agentDiameter * agentDiameter && distSq > 0.00001f)
                        {
                            float penetration = (agentDiameter - dist) * 0.5f;
                            float3 pushDir = (position - otherPos) / dist;
                            pushDir.y = 0;
                            position += pushDir * penetration * 0.5f;
                        }

                        if (agent.State == SwarmState.Swarming)
                        {
                            float3 otherVel = Agents[otherIndex].Velocity;
                            alignment += otherVel;
                            cohesion += otherPos;
                            viscosity += otherVel;
                            neighborCount++;
                        }
                    }

                } while (HashMap.TryGetNextValue(out otherIndex, ref it));
            }
        }

        if (neighborCount > 0)
        {
            alignment /= neighborCount;
            cohesion = (cohesion / neighborCount) - position;
            viscosity /= neighborCount;
        }
        if (sepCount > 0)
        {
            separation /= sepCount;
        }

        // --- 3. Flow Field Navigation ---
        float3 flowDir = float3.zero;

        float3 localPos = position - FlowFieldOrigin;
        int2 ffCell = new int2(
            math.clamp((int)math.floor(localPos.x / FlowFieldCellSize), 0, GridSize.x - 1),
            math.clamp((int)math.floor(localPos.z / FlowFieldCellSize), 0, GridSize.y - 1)
        );
        int ffIndex = ffCell.x + ffCell.y * GridSize.x;

        if (ffIndex >= 0 && ffIndex < FlowField.Length)
        {
            flowDir = FlowField[ffIndex];
        }

        // --- 4. Давление (Pressure) ---
        float pressureError = localDensity - TargetDensity;
        float3 pressureForce = float3.zero;

        if (math.abs(pressureError) > 0.01f)
        {
            float3 pressureDir = sepCount > 0
                ? math.normalizesafe(separation)
                : math.normalizesafe(densityGradient);
            if (math.lengthsq(pressureDir) > 0.001f)
                pressureForce = pressureDir * pressureError;
        }

        // --- 5. Боковое растекание (Lateral Spread) ---
        float sdfHere = SampleSDF_Distance(position);
        float3 wallGradHere = SampleSDF_Gradient(position);

        float3 lateralForce = float3.zero;
        if (math.lengthsq(flowDir) > 0.01f)
        {
            float3 perpendicular = new float3(-flowDir.z, 0, flowDir.x);

            float phase = (agent.RandomSeed % 1000) * 0.00628f;
            float lateralTarget = math.sin(Time * 0.7f + phase);
            agent.LateralOffset = math.lerp(agent.LateralOffset, lateralTarget, DeltaTime * 0.5f);

            lateralForce = perpendicular * agent.LateralOffset;

            if (pressureError > 0.5f)
            {
                lateralForce *= (1f + pressureError);
            }

            float corridorFactor = 1f - math.saturate(sdfHere / (ObstacleAvoidRadius * 2f));
            lateralForce *= (1f + corridorFactor * 3f);
        }

        // --- 6. Wander ---
        agent.RandomSeed = agent.RandomSeed * 1664525 + 1013904223;
        float randomWanderChange = ((float)(agent.RandomSeed & 0xFFFF) / 65535f) * 2f - 1f;

        agent.WanderAngle += randomWanderChange * 8f * DeltaTime;
        float3 wanderDir = new float3(math.cos(agent.WanderAngle), 0, math.sin(agent.WanderAngle));

        // --- 6b. Трёхосевое отталкивание от геометрии (3D SDF) ---
        float3 obstacleForce = sdfHere < ObstacleAvoidRadius
            ? wallGradHere * (1f - sdfHere / ObstacleAvoidRadius)
            : float3.zero;

        // --- 6c. Вертикальное блуждание ---
        float verticalWander = math.sin(Time * 0.8f + agent.VerticalOffset * math.PI * 2f) * 0.3f;
        float3 verticalForce = new float3(0, verticalWander, 0);

        // --- 6d. Глубинный слой по типу агента ---
        float preferredDist = agent.Type switch {
            AgentType.Kinesia     => ChargeRadius * 0.3f,
            AgentType.Smallion    => ChargeRadius * 0.6f,
            AgentType.Transfinite => ChargeRadius * 1.0f,
            _                     => ChargeRadius * 0.6f
        };
        float3 depthDir = PlayerPosition - position;
        float depthCurrentDist = math.length(depthDir);
        float3 depthForce = math.normalizesafe(depthDir) * (depthCurrentDist - preferredDist) * 0.5f;

        // --- 6e. Отталкивание от игрока (рой обтекает, не давит) ---
        float3 playerRepel = float3.zero;
        float3 toPlayer = position - PlayerPosition;
        toPlayer.y = 0;
        float distToPlayerXZ = math.length(toPlayer);
        float playerHardDist = PlayerRadius + AgentRadius;
        float playerSoftDist = playerHardDist + AgentRadius * 4f;

        if (distToPlayerXZ < playerSoftDist && distToPlayerXZ > 0.001f)
        {
            float3 pushDir = toPlayer / distToPlayerXZ;

            if (distToPlayerXZ < playerHardDist)
            {
                position += pushDir * (playerHardDist - distToPlayerXZ);

                float3 towardPlayer = -pushDir;
                float velTowardPlayer = math.dot(agent.Velocity, towardPlayer);
                if (velTowardPlayer > 0) agent.Velocity -= towardPlayer * velTowardPlayer;
            }

            float t = 1f - math.saturate(distToPlayerXZ / playerSoftDist);
            float3 repelForce = pushDir * t * t * 10f;

            float3 tangent = new float3(-pushDir.z, 0, pushDir.x);
            float sideDir = math.sign(agent.LateralOffset);
            if (sideDir == 0) sideDir = 1f;
            float3 flowAroundForce = tangent * sideDir * t * 8f;

            playerRepel = repelForce + flowAroundForce;
        }

        // --- 7. Вязкость (Viscosity) ---
        float3 viscosityForce = float3.zero;
        if (neighborCount > 0)
        {
            viscosityForce = viscosity - agent.Velocity;
        }

        // --- 8. Расчёт итогового вектора скорости ---
        float3 targetVelocity = float3.zero;

        if (agent.State == SwarmState.Swarming)
        {
            targetVelocity = separation * SeparationWeight +
                             math.normalizesafe(alignment) * AlignmentWeight +
                             math.normalizesafe(cohesion) * CohesionWeight +
                             flowDir * FlowFieldWeight +
                             wanderDir * WanderWeight +
                             pressureForce * PressureWeight +
                             lateralForce * LateralSpreadWeight +
                             viscosityForce * ViscosityWeight +
                             obstacleForce * ObstacleAvoidWeight +
                             verticalForce;

            if (math.lengthsq(targetVelocity) > 0.001f)
                targetVelocity = math.normalizesafe(targetVelocity) * MaxSpeed;

            targetVelocity += playerRepel;
            targetVelocity += depthForce;
        }
        else if (agent.State == SwarmState.Charging)
        {
            targetVelocity = flowDir * (FlowFieldWeight * ChargeSpeedMult) +
                             separation * SeparationWeight +
                             wanderDir * (WanderWeight * 0.3f) +
                             pressureForce * (PressureWeight * 0.5f) +
                             lateralForce * (LateralSpreadWeight * 0.3f) +
                             obstacleForce * ObstacleAvoidWeight +
                             verticalForce;

            if (math.lengthsq(targetVelocity) > 0.001f)
                targetVelocity = math.normalizesafe(targetVelocity) * (MaxSpeed * ChargeSpeedMult);

            targetVelocity += playerRepel;
            targetVelocity += depthForce;
        }

        // Интерполяция скорости (инерция роя). Сглаживаем у стен, чтобы не дёргалось.
        float wallProximity = 1f - math.saturate(sdfHere / ObstacleAvoidRadius);
        float lerpSpeed = math.lerp(5f, 2f, wallProximity);
        agent.Velocity = math.lerp(agent.Velocity, targetVelocity, DeltaTime * lerpSpeed);

        // Применяем движение (Swept Collision — защита от пролёта сквозь стены на высокой скорости)
        float3 moveVec = agent.Velocity * DeltaTime;
        float moveDist = math.length(moveVec);
        if (moveDist > 0.001f)
        {
            float3 moveDir = moveVec / moveDist;
            int steps = math.max(2, (int)math.ceil(moveDist / (FlowFieldCellSize * 0.5f)));
            for (int s = 1; s <= steps; s++)
            {
                float3 testPos = position + moveDir * (moveDist * s / steps);

                float3 localTest = testPos - FlowFieldOrigin;
                int2 tc = new int2(
                    math.clamp((int)math.floor(localTest.x / FlowFieldCellSize), 0, GridSize.x - 1),
                    math.clamp((int)math.floor(localTest.z / FlowFieldCellSize), 0, GridSize.y - 1));

                if (CostField[tc.x + tc.y * GridSize.x] == 255)
                {
                    float3 testWallN = SampleSDF_Gradient(testPos);
                    float dot = math.dot(agent.Velocity, testWallN);
                    if (dot < 0) agent.Velocity -= testWallN * dot;
                    moveVec = agent.Velocity * DeltaTime;
                    break;
                }
            }
        }
        position += moveVec;

        // --- 9. ЖЁСТКАЯ SDF-КОЛЛИЗИЯ И СТРОГОЕ ПРАВИЛО UNWALKABLE ЗОНЫ ---

        // Шаг А: Страховочная коллизия после движения.
        float sdfDist = SampleSDF_Distance(position);
        float3 wallNormal = SampleSDF_Gradient(position);
        if (sdfDist < WallDistance)
        {
            float penetration = WallDistance - sdfDist;

            if (math.lengthsq(wallNormal) < 0.01f)
            {
                float3 localP = position - FlowFieldOrigin;
                int2 fc = new int2(
                    math.clamp((int)math.floor(localP.x / FlowFieldCellSize), 0, GridSize.x - 1),
                    math.clamp((int)math.floor(localP.z / FlowFieldCellSize), 0, GridSize.y - 1));
                float bestDistSq = float.MaxValue;
                float3 bestEscape = new float3(1, 0, 0);

                for (int r = 1; r <= 3; r++)
                {
                    for (int wx = -r; wx <= r; wx++)
                    for (int wz = -r; wz <= r; wz++)
                    {
                        int2 wc = fc + new int2(wx, wz);
                        if (wc.x < 0 || wc.x >= GridSize.x || wc.y < 0 || wc.y >= GridSize.y) continue;
                        if (CostField[wc.x + wc.y * GridSize.x] == 255) continue;

                        float3 emptyCenter = FlowFieldOrigin + new float3(
                            wc.x * FlowFieldCellSize + FlowFieldCellSize * 0.5f, position.y,
                            wc.y * FlowFieldCellSize + FlowFieldCellSize * 0.5f);
                        float dSq = math.distancesq(position, emptyCenter);
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestEscape = math.normalizesafe(new float3(emptyCenter.x - position.x, 0, emptyCenter.z - position.z));
                        }
                    }
                    if (bestDistSq < float.MaxValue) break;
                }
                wallNormal = bestEscape;
            }

            position += wallNormal * penetration;
            float wallDot = math.dot(agent.Velocity, wallNormal);
            if (wallDot < 0) agent.Velocity -= wallNormal * wallDot;
        }

        // Шаг Б: ЖЁСТКОЕ ПРАВИЛО. Если агент внутри ячейки-стены — перемещаем в ближайшую свободную зону.
        float3 localPFinal = position - FlowFieldOrigin;
        int2 fcFinal = new int2(
            math.clamp((int)math.floor(localPFinal.x / FlowFieldCellSize), 0, GridSize.x - 1),
            math.clamp((int)math.floor(localPFinal.z / FlowFieldCellSize), 0, GridSize.y - 1));

        if (CostField[fcFinal.x + fcFinal.y * GridSize.x] == 255)
        {
            float bestDistSq = float.MaxValue;
            float3 safePos = position;

            for (int r = 1; r <= 4; r++)
            {
                for (int wx = -r; wx <= r; wx++)
                for (int wz = -r; wz <= r; wz++)
                {
                    int2 wc = fcFinal + new int2(wx, wz);
                    if (wc.x < 0 || wc.x >= GridSize.x || wc.y < 0 || wc.y >= GridSize.y) continue;
                    if (CostField[wc.x + wc.y * GridSize.x] == 255) continue;

                    float3 emptyCenter = FlowFieldOrigin + new float3(
                        wc.x * FlowFieldCellSize + FlowFieldCellSize * 0.5f, position.y,
                        wc.y * FlowFieldCellSize + FlowFieldCellSize * 0.5f);

                    float dSq = math.distancesq(position, emptyCenter);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        safePos = emptyCenter;
                    }
                }
                if (bestDistSq < float.MaxValue) break;
            }

            position = safePos;
            agent.Velocity = float3.zero;
        }

        // Ограничиваем агентов рамками карты (XZ — flow field, Y — SDF grid)
        float3 minMap = FlowFieldOrigin;
        float3 maxMap = FlowFieldOrigin + new float3(GridSize.x * FlowFieldCellSize, 0, GridSize.y * FlowFieldCellSize);
        position.x = math.clamp(position.x, minMap.x + WallDistance, maxMap.x - WallDistance);
        position.z = math.clamp(position.z, minMap.z + WallDistance, maxMap.z - WallDistance);
        position.y = math.clamp(position.y, SDFOrigin.y + WallDistance, SDFOrigin.y + SDFGridSize.y * SDFCellSize - WallDistance);

        // --- ФИНАЛЬНАЯ ЖЁСТКАЯ ЗОНА: агент НИКОГДА не касается физического коллайдера игрока ---
        float3 toPlayerFinal = position - PlayerPosition;
        toPlayerFinal.y = 0;
        float distToPlayerFinal = math.length(toPlayerFinal);
        float playerExclusionDist = PlayerRadius + AgentRadius + 0.15f;
        if (distToPlayerFinal < playerExclusionDist && distToPlayerFinal > 0.001f)
        {
            float3 pushAwayDir = toPlayerFinal / distToPlayerFinal;
            position = new float3(PlayerPosition.x, position.y, PlayerPosition.z) + pushAwayDir * playerExclusionDist;

            float velTowardPlayer = math.dot(agent.Velocity, -pushAwayDir);
            if (velTowardPlayer > 0)
                agent.Velocity -= -pushAwayDir * velTowardPlayer;
        }

        // Поворот агента по вектору движения
        if (math.lengthsq(agent.Velocity) > 0.01f)
        {
            quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(agent.Velocity), math.up());
            transform.rotation = math.slerp(transform.rotation, targetRot, DeltaTime * 10f);
        }

        transform.position = position;
        Agents[index] = agent;
    }
}
