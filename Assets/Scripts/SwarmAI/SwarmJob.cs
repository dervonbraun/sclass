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

// Джоба 3: Основная физика роя — модель «жидкости» (SPH-inspired) + SDF-коллизии
[BurstCompile]
public struct SwarmJob : IJobParallelForTransform
{
    [NativeDisableParallelForRestriction] public NativeArray<AgentData> Agents;
    [ReadOnly] public NativeArray<float3> FlowField;
    [ReadOnly] public NativeArray<byte> CostField;
    [ReadOnly] public NativeArray<float> WallSDF;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> HashMap;
    [ReadOnly] public NativeArray<float3> AgentPositions;

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

    // Билинейная интерполяция WallSDF — даёт точное расстояние для любой точки между центрами ячеек
    private float SampleSDF(float3 worldPos)
    {
        float3 local = worldPos - FlowFieldOrigin;
        float fx = local.x / FlowFieldCellSize - 0.5f;
        float fz = local.z / FlowFieldCellSize - 0.5f;

        int x0 = math.clamp((int)math.floor(fx),     0, GridSize.x - 1);
        int x1 = math.clamp(x0 + 1,                  0, GridSize.x - 1);
        int z0 = math.clamp((int)math.floor(fz),     0, GridSize.y - 1);
        int z1 = math.clamp(z0 + 1,                  0, GridSize.y - 1);

        float tx = math.saturate(fx - math.floor(fx));
        float tz = math.saturate(fz - math.floor(fz));

        float v00 = WallSDF[x0 + z0 * GridSize.x];
        float v10 = WallSDF[x1 + z0 * GridSize.x];
        float v01 = WallSDF[x0 + z1 * GridSize.x];
        float v11 = WallSDF[x1 + z1 * GridSize.x];

        return math.lerp(math.lerp(v00, v10, tx), math.lerp(v01, v11, tx), tz);
    }

    // Градиент SDF — направление от стены (увеличение SDF → дальше от стены)
    private float3 SampleSDFGradient(float3 worldPos)
    {
        float eps = FlowFieldCellSize * 0.3f;
        float dx = SampleSDF(worldPos + new float3(eps, 0, 0)) - SampleSDF(worldPos - new float3(eps, 0, 0));
        float dz = SampleSDF(worldPos + new float3(0, 0, eps)) - SampleSDF(worldPos - new float3(0, 0, eps));
        return math.normalizesafe(new float3(dx, 0, dz));
    }

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
        int neighborCount = 0;
        int sepCount = 0;
        float localDensity = 0f;

        int3 cell = math.int3(math.floor(position / HashCellSize));

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                int3 neighborCell = cell + new int3(x, 0, z);
                int hash = (int)math.hash(neighborCell);

                if (HashMap.TryGetFirstValue(hash, out int otherIndex, out NativeParallelMultiHashMapIterator<int> it))
                {
                    do
                    {
                        if (index == otherIndex) continue;
                        // Пропускаем мёртвых соседей — они не должны влиять на живых
                        if (Agents[otherIndex].State == SwarmState.Dead) continue;

                        float3 otherPos = AgentPositions[otherIndex];
                        float distSq = math.distancesq(position, otherPos);

                        if (distSq < NeighborRadius * NeighborRadius)
                        {
                            float dist = math.sqrt(distSq);
                            
                            // SPH Kernel
                            float q = dist / NeighborRadius;
                            float kernel = math.max(0f, 1f - q);
                            localDensity += kernel * kernel;
                            
                            // Separation
                            if (dist < SeparationRadius && dist > 0.001f)
                            {
                                separation += (position - otherPos) / distSq;
                                sepCount++;
                            }

                            // Столкновение агент-агент
                            float agentDiameter = AgentRadius * 2f;
                            if (distSq < agentDiameter * agentDiameter && distSq > 0.00001f)
                            {
                                float penetration = (agentDiameter - dist) * 0.5f;
                                float3 pushDir = (position - otherPos) / dist;
                                pushDir.y = 0;
                                position += pushDir * penetration;
                            }

                            // Alignment, Cohesion & Viscosity
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

        if (sepCount > 0 && math.abs(pressureError) > 0.01f)
        {
            float3 pressureDir = math.normalizesafe(separation);
            pressureForce = pressureDir * pressureError;
        }

        // --- 5. Боковое растекание (Lateral Spread) ---
        float sdfHere = SampleSDF(position); // Вычисляем заранее для использования в других силах
        float3 lateralForce = float3.zero;
        if (math.lengthsq(flowDir) > 0.01f)
        {
            float3 perpendicular = new float3(-flowDir.z, 0, flowDir.x);

            float phase = (agent.RandomSeed % 1000) * 0.00628f; // уникальная фаза [0..6.28]
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

        // --- 6b. Превентивное отталкивание от стен (SDF) ---
        float3 obstacleForce = float3.zero;
        if (sdfHere < ObstacleAvoidRadius)
        {
            float3 wallGrad = SampleSDFGradient(position);
            // Квадратичная кривая: резкий удар у стены, слабый вдали
            float t = math.saturate(sdfHere / ObstacleAvoidRadius);
            float strength = (1f - t) * (1f - t);
            obstacleForce = wallGrad * strength;
        }

        // --- 6c. Отталкивание от игрока (чтобы рой обтекал, а не блокировал) ---
        float3 playerRepel = float3.zero;
        float3 toPlayer = position - PlayerPosition;
        toPlayer.y = 0;
        float distToPlayerXZ = math.length(toPlayer);
        float playerAvoidDist = PlayerRadius + AgentRadius;
        
        if (distToPlayerXZ < playerAvoidDist && distToPlayerXZ > 0.001f)
        {
            float3 pushDir = toPlayer / distToPlayerXZ;
            float penetration = playerAvoidDist - distToPlayerXZ;
            
            // Чтобы рой плавно обтекал игрока, мы убираем 100% жёсткое телепортирование
            // (оставляем только 20%, чтобы они не проваливались прямо в камеру мгновенно)
            position += pushDir * penetration * 0.2f; 
            
            // Сила отталкивания от игрока (мягкая)
            float3 repelForce = pushDir * 5f;
            
            // Сила обтекания (в стороны по касательной). 
            // Используем agent.LateralOffset (от -1 до 1), чтобы агенты расходились влево и вправо
            float3 tangent = new float3(-pushDir.z, 0, pushDir.x);
            float sideDir = math.sign(agent.LateralOffset);
            if (sideDir == 0) sideDir = 1f;
            
            float3 flowAroundForce = tangent * sideDir * 15f;
            
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
                             playerRepel;

            if (math.lengthsq(targetVelocity) > 0.001f)
                targetVelocity = math.normalizesafe(targetVelocity) * MaxSpeed;
        }
        else if (agent.State == SwarmState.Charging)
        {
            // flowDir уже вычислен выше — используем его для навигации с учётом стен
            targetVelocity = flowDir * (FlowFieldWeight * ChargeSpeedMult) +
                             separation * SeparationWeight +
                             wanderDir * (WanderWeight * 0.3f) +
                             pressureForce * (PressureWeight * 0.5f) +
                             lateralForce * (LateralSpreadWeight * 0.3f) +
                             obstacleForce * ObstacleAvoidWeight +
                             playerRepel;
            
            if (math.lengthsq(targetVelocity) > 0.001f)
                targetVelocity = math.normalizesafe(targetVelocity) * (MaxSpeed * ChargeSpeedMult);
        }

        // Интерполяция скорости (инерция роя)
        agent.Velocity = math.lerp(agent.Velocity, targetVelocity, DeltaTime * 5f);
        agent.Velocity.y = 0;

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
                float sdf = SampleSDF(testPos);
                if (sdf < WallDistance) 
                {
                    float3 wallN = SampleSDFGradient(testPos);
                    float dot = math.dot(agent.Velocity, wallN);
                    if (dot < 0) agent.Velocity -= wallN * dot;
                    moveVec = agent.Velocity * DeltaTime; 
                    break;
                }
            }
        }
        position += moveVec;

        // --- 9. ЖЁСТКАЯ SDF-КОЛЛИЗИЯ И СТРОГОЕ ПРАВИЛО UNWALKABLE ЗОНЫ ---
        
        // Шаг А: Итеративный солвер SDF (до 3 итераций). 
        // Помогает, если за один кадр агента вдавило сразу несколькими силами.
        for (int iter = 0; iter < 3; iter++)
        {
            float sdfDist = SampleSDF(position);
            if (sdfDist >= WallDistance) break;

            float penetration = WallDistance - sdfDist;
            float3 wallNormal = SampleSDFGradient(position);

            // Фоллбэк: если градиент ~0 (агент глубоко в стене) — находим ближайшую СВОБОДНУЮ ячейку
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
                            wc.x * FlowFieldCellSize + FlowFieldCellSize * 0.5f, 0,
                            wc.y * FlowFieldCellSize + FlowFieldCellSize * 0.5f);
                        float dSq = math.distancesq(position, emptyCenter);
                        if (dSq < bestDistSq) 
                        { 
                            bestDistSq = dSq; 
                            bestEscape = math.normalizesafe(emptyCenter - position);
                        }
                    }
                    if (bestDistSq < float.MaxValue) break;
                }
                wallNormal = bestEscape;
            }

            position += wallNormal * penetration;
            float dot = math.dot(agent.Velocity, wallNormal);
            if (dot < 0) agent.Velocity -= wallNormal * dot;
        }

        // Шаг Б: ЖЁСТКОЕ ПРАВИЛО. Если после всех сил и выталкиваний агент всё равно 
        // оказался внутри ячейки, помеченной как стена (Unwalkable Layer, CostField == 255),
        // мы безапелляционно перемещаем его в ближайшую свободную зону.
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
                    if (CostField[wc.x + wc.y * GridSize.x] == 255) continue; // Нашли свободную!
                    
                    float3 emptyCenter = FlowFieldOrigin + new float3(
                        wc.x * FlowFieldCellSize + FlowFieldCellSize * 0.5f, 0,
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
            agent.Velocity = float3.zero; // Сбрасываем скорость, чтобы не пролетел дальше
        }

        // Ограничиваем агентов рамками карты
        float3 minMap = FlowFieldOrigin;
        float3 maxMap = FlowFieldOrigin + new float3(GridSize.x * FlowFieldCellSize, 0, GridSize.y * FlowFieldCellSize);
        position.x = math.clamp(position.x, minMap.x + WallDistance, maxMap.x - WallDistance);
        position.z = math.clamp(position.z, minMap.z + WallDistance, maxMap.z - WallDistance);

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
