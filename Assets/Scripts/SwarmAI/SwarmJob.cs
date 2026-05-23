using Unity.Burst;
using Unity.Collections;
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
        if (Agents[index].State == SwarmState.Dead) return;

        int3 cell = math.int3(math.floor(transform.position / CellSize));
        int hash = (int)math.hash(cell);
        HashMap.Add(hash, index);
    }
}


// Джоба 4: Основная физика роя — Boids + 3D SDF-коллизии + Anchor navigation
[BurstCompile]
public struct SwarmJob : IJobParallelForTransform
{
    [NativeDisableParallelForRestriction] public NativeArray<AgentData> Agents;
    [ReadOnly] public NativeArray<byte> CostField;
    [ReadOnly] public NativeArray<float3> FlowField;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> HashMap;
    [ReadOnly] public NativeArray<float3> AgentPositions;
    [ReadOnly] public NativeArray<float3> AnchorPositions;

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
    public float AnchorWeight;
    public float WanderWeight;
    public float ObstacleAvoidWeight;

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

    private bool SDF_OutOfBounds(float3 worldPos)
    {
        float3 local = worldPos - SDFOrigin;
        float3 size = new float3(SDFGridSize.x * SDFCellSize, SDFGridSize.y * SDFCellSize, SDFGridSize.z * SDFCellSize);
        return local.x < 0.01f || local.x >= size.x - 0.01f || local.y < 0.01f || local.y >= size.y - 0.01f || local.z < 0.01f || local.z >= size.z - 0.01f;
    }

    private float3 SampleSDF_Gradient(float3 worldPos)
    {
        if (SDF_OutOfBounds(worldPos))
        {
            // Генерируем нормаль, направленную обратно к центру сетки SDF, чтобы вытолкнуть агента!
            float3 size = new float3(SDFGridSize.x * SDFCellSize, SDFGridSize.y * SDFCellSize, SDFGridSize.z * SDFCellSize);
            float3 center = SDFOrigin + size * 0.5f;
            return math.normalizesafe(center - worldPos);
        }
        return SDF_Gradient3D[SDF_GetIndex(worldPos)];
    }

    private float SampleSDF_Distance(float3 worldPos)
    {
        if (SDF_OutOfBounds(worldPos))
        {
            return 0f; // Выход за границы SDF расценивается как сплошная непреодолимая стена!
        }
        return SDF_Distance3D[SDF_GetIndex(worldPos)];
    }

    public void Execute(int index, TransformAccess transform)
    {
        float3 position = transform.position;
        AgentData agent = Agents[index];

        if (agent.State == SwarmState.Dead) return;

        // --- 1. FSM ---
        float distToPlayerSq = math.distancesq(position, PlayerPosition);
        if (agent.State == SwarmState.Swarming)
        {
            if (distToPlayerSq < ChargeRadius * ChargeRadius)
                agent.State = SwarmState.Charging;
        }
        else if (agent.State == SwarmState.Charging)
        {
            if (distToPlayerSq > (ChargeRadius * 1.5f) * (ChargeRadius * 1.5f))
                agent.State = SwarmState.Swarming;
        }

        // --- 2. Boids (Spatial Hashing) ---
        float3 separation = float3.zero;
        float3 alignment  = float3.zero;
        float3 cohesion   = float3.zero;
        int neighborCount = 0;
        int sepCount      = 0;

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
                            // Оставляем pushDir в 3D для трехмерного расталкивания
                            
                            float3 nextSepPos = position + pushDir * penetration * 0.15f;
                            float curSdf = SampleSDF_Distance(position);
                            float nextSdf = SampleSDF_Distance(nextSepPos);
                            if (nextSdf >= WallDistance || nextSdf > curSdf)
                            {
                                position = nextSepPos;
                            }
                        }

                        if (agent.State == SwarmState.Swarming)
                        {
                            float3 otherVel = Agents[otherIndex].Velocity;
                            alignment += otherVel;
                            cohesion  += otherPos;
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
        }
        if (sepCount > 0)
            separation /= sepCount;

        // --- 3. Anchor Navigation ---
        float3 toAnchor = AnchorPositions[agent.AnchorIndex] - position;
        float anchorDist = math.length(toAnchor);
        
        // Эластичная сила: если агент отходит далеко от якоря, сила притяжения прогрессивно возрастает!
        float elasticFactor = 1.0f;
        if (anchorDist > 4.0f)
        {
            elasticFactor += (anchorDist - 4.0f) * 0.75f;
        }
        
        // Коэффициент следования: если якорь слишком далеко (>15м), рой плавно перестает за ним следовать и рассеивается
        float followFactor = 1.0f - math.smoothstep(10.0f, 15.0f, anchorDist);
        float anchorMag = math.min(anchorDist * 0.4f, AnchorWeight * elasticFactor) * followFactor;

        float3 localPAnchor = position - FlowFieldOrigin;
        int2 fcAnchor = new int2(
            math.clamp((int)math.floor(localPAnchor.x / FlowFieldCellSize), 0, GridSize.x - 1),
            math.clamp((int)math.floor(localPAnchor.z / FlowFieldCellSize), 0, GridSize.y - 1));
        
        float3 flowDir = FlowField[fcAnchor.x + fcAnchor.y * GridSize.x];
        float3 navigateDir = math.normalizesafe(toAnchor);
        
        bool inWallCell = SampleSDF_Distance(position) < 0f;
        if (math.lengthsq(flowDir) > 0.01f)
        {
            // Увеличиваем приоритет векторного поля (вес 2.5), чтобы агенты надежно огибали стены!
            navigateDir = math.normalizesafe(navigateDir * 0.3f + flowDir * 2.5f);
        }
        else if (inWallCell)
        {
            // Если мы глубоко в стене и векторного поля нет, принудительно рулим к выходу по запеченному градиенту SDF!
            float3 escapeGrad = SampleSDF_Gradient(position);
            // Разрешаем вертикальный уход от пола/потолка
            if (math.lengthsq(escapeGrad) > 0.01f)
            {
                navigateDir = math.normalizesafe(escapeGrad);
            }
            else
            {
                navigateDir = math.normalizesafe(toAnchor);
            }
        }
        float3 anchorForce = navigateDir * anchorMag;

        // --- 4. Wander ---
        agent.RandomSeed = agent.RandomSeed * 1664525 + 1013904223;
        float randomWanderChange = ((float)(agent.RandomSeed & 0xFFFF) / 65535f) * 2f - 1f;
        agent.WanderAngle += randomWanderChange * 8f * DeltaTime;

        // --- 5. Отталкивание от геометрии (3D SDF) ---
        float sdfHere = SampleSDF_Distance(position);
        float3 wallGradHere = SampleSDF_Gradient(position);
        
        if (sdfHere < ObstacleAvoidRadius)
        {
            if (math.lengthsq(wallGradHere) > 0.01f)
            {
                wallGradHere = math.normalizesafe(wallGradHere);
            }
            else
            {
                // Безопасный дефолт, хотя запеченный градиент гарантирует валидное значение
                wallGradHere = new float3(1, 0, 0);
            }
        }
        
        // Делаем силу уклонения нелинейной (кубической) и увеличиваем её вес на близких дистанциях (3x), чтобы она была жесткой!
        float tObstacle = 1f - math.saturate(sdfHere / ObstacleAvoidRadius);
        float forceCurve = tObstacle * tObstacle * tObstacle;
        float3 obstacleForce = sdfHere < ObstacleAvoidRadius
            ? wallGradHere * forceCurve * ObstacleAvoidWeight * 3.0f
            : float3.zero;

        // --- 6. Вертикальное блуждание — заполняет объём коридора ---
        float verticalWander = math.sin(Time * 0.8f + agent.VerticalOffset * math.PI * 2f) * 1.5f;
        float3 verticalForce = new float3(0, verticalWander, 0);
        
        // --- 7. Глубинный слой по типу агента ---
        float3 toPlayer = PlayerPosition - position;
        float playerDist = math.length(toPlayer);
        float preferredDist = agent.Type switch {
            AgentType.Kinesia     => 2.0f,
            AgentType.Smallion    => 5.0f,
            AgentType.Transfinite => 9.0f,
            _                     => 5.0f
        };
        
        float3 playerNavigateDir = math.normalizesafe(toPlayer);
        if (math.lengthsq(flowDir) > 0.01f)
        {
            // Используем направление векторного поля для движения к игроку, чтобы огибать стены!
            playerNavigateDir = flowDir;
        }
        else if (inWallCell)
        {
            // Если мы в стене и пути по FlowField нет — полностью отключаем силу притяжения к игроку, чтобы она не мешала выталкиванию!
            playerNavigateDir = float3.zero;
        }
        // Также плавно гасим притяжение к игроку, если якорь улетел слишком далеко, чтобы рой рассеивался локально
        float3 depthForce = playerNavigateDir * (playerDist - preferredDist) * 0.3f * followFactor;

        // --- 8. Отталкивание от игрока (рой обтекает, не давит) ---
        float3 playerRepel = float3.zero;
        float3 toPlayerXZ = position - PlayerPosition;
        toPlayerXZ.y = 0;
        float distToPlayerXZ = math.length(toPlayerXZ);
        float playerHardDist = PlayerRadius + AgentRadius;
        float playerSoftDist = playerHardDist + AgentRadius * 4f;

        if (distToPlayerXZ < playerSoftDist && distToPlayerXZ > 0.001f)
        {
            float3 pushDir = toPlayerXZ / distToPlayerXZ;

            if (distToPlayerXZ < playerHardDist)
            {
                float3 nextRepelPos = position + pushDir * (playerHardDist - distToPlayerXZ);
                float curSdf = SampleSDF_Distance(position);
                float nextSdf = SampleSDF_Distance(nextRepelPos);
                if (nextSdf >= WallDistance || nextSdf > curSdf)
                {
                    position = nextRepelPos;
                }
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

        // --- 9. Итоговая скорость ---
        float3 targetVelocity = float3.zero;

        if (agent.State == SwarmState.Swarming)
        {
            targetVelocity = anchorForce
                + separation  * SeparationWeight
                + math.normalizesafe(alignment) * AlignmentWeight
                + math.normalizesafe(cohesion) * CohesionWeight
                + verticalForce * WanderWeight;

            // Слайдинг рулевой силы вдоль стен, чтобы избежать «бодания» с геометрией
            if (sdfHere < ObstacleAvoidRadius && math.lengthsq(wallGradHere) > 0.01f)
            {
                float dot = math.dot(targetVelocity, wallGradHere);
                if (dot < 0f)
                {
                    targetVelocity -= wallGradHere * dot;
                }
            }

            // Сила уклонения от стен влияет на направление до нормализации
            targetVelocity += obstacleForce * ObstacleAvoidWeight;

            if (math.lengthsq(targetVelocity) > 0.001f)
            {
                float speed = MaxSpeed;
                if (anchorDist < 2.0f)
                {
                    speed = math.lerp(0.1f * MaxSpeed, MaxSpeed, anchorDist / 2.0f);
                }
                targetVelocity = math.normalizesafe(targetVelocity) * speed;
            }

            // Сила притяжения к игроку и расталкивания добавляются после нормализации для высокой реактивности
            targetVelocity += playerRepel;
            targetVelocity += depthForce;
        }
        else if (agent.State == SwarmState.Charging)
        {
            targetVelocity = anchorForce * ChargeSpeedMult
                + separation * SeparationWeight
                + verticalForce;

            // Слайдинг рулевой силы вдоль стен
            if (sdfHere < ObstacleAvoidRadius && math.lengthsq(wallGradHere) > 0.01f)
            {
                float dot = math.dot(targetVelocity, wallGradHere);
                if (dot < 0f)
                {
                    targetVelocity -= wallGradHere * dot;
                }
            }

            targetVelocity += obstacleForce * ObstacleAvoidWeight;

            if (math.lengthsq(targetVelocity) > 0.001f)
            {
                float speed = MaxSpeed * ChargeSpeedMult;
                if (anchorDist < 2.0f)
                {
                    speed = math.lerp(0.1f * (MaxSpeed * ChargeSpeedMult), MaxSpeed * ChargeSpeedMult, anchorDist / 2.0f);
                }
                targetVelocity = math.normalizesafe(targetVelocity) * speed;
            }

            targetVelocity += playerRepel;
            targetVelocity += depthForce;
        }

        float wallProximity = 1f - math.saturate(sdfHere / ObstacleAvoidRadius);
        // Сглаживаем реакцию до максимум 10f во избежание резкого дребезга скорости у препятствий
        float lerpSpeed = math.lerp(5f, 10f, wallProximity);
        agent.Velocity = math.lerp(agent.Velocity, targetVelocity, DeltaTime * lerpSpeed);

        // --- CCD SUBSTEPPING (Непрерывный тест коллизий против туннелирования на больших скоростях) ---
        float3 moveVec = agent.Velocity * DeltaTime;
        float moveDist = math.length(moveVec);
        // Используем половину ячейки SDF в качестве максимального шага
        int substeps = math.max(1, (int)math.ceil(moveDist / (SDFCellSize * 0.5f))); 
        float3 stepVec = moveVec / substeps;

        for (int step = 0; step < substeps; step++)
        {
            float3 nextPos = position + stepVec;
            bool nextRealWall = SampleSDF_Distance(nextPos) < WallDistance;

            if (!nextRealWall)
            {
                position = nextPos;
            }
            else
            {
                // Sliding: пробуем X и Z независимо
                float3 posX = position + new float3(stepVec.x, stepVec.y, 0);
                float3 posZ = position + new float3(0, stepVec.y, stepVec.z);

                bool xWall = SampleSDF_Distance(posX) < WallDistance;
                bool zWall = SampleSDF_Distance(posZ) < WallDistance;

                if (!xWall)
                {
                    position = posX;
                    float3 vel = agent.Velocity; vel.z = 0; agent.Velocity = vel;
                }
                else if (!zWall)
                {
                    position = posZ;
                    float3 vel = agent.Velocity; vel.x = 0; agent.Velocity = vel;
                }
                else
                {
                    // Полностью заблокированы — стоим, прерываем CCD цикл в этом кадре
                    agent.Velocity = float3.zero;
                    break;
                }
            }
        }

        // --- 10. Жёсткая SDF-коллизия ---

        // Шаг А: Страховочная коллизия после движения.
        float sdfDist = SampleSDF_Distance(position);
        float3 wallNormal = SampleSDF_Gradient(position);

        if (sdfDist < WallDistance)
        {
            float penetration = WallDistance - sdfDist;

            if (math.lengthsq(wallNormal) > 0.01f)
            {
                wallNormal = math.normalizesafe(wallNormal);
            }
            else
            {
                // Безопасный дефолт, если градиент по какой-то причине нулевой
                wallNormal = new float3(1, 0, 0);
            }

            position += wallNormal * penetration;
            float wallDot = math.dot(agent.Velocity, wallNormal);
            if (wallDot < 0) agent.Velocity -= wallNormal * wallDot;
        }

        // Ограничиваем агентов рамками карты (XZ — flow field, Y — SDF grid)
        float3 minMap = FlowFieldOrigin;
        float3 maxMap = FlowFieldOrigin + new float3(GridSize.x * FlowFieldCellSize, 0, GridSize.y * FlowFieldCellSize);
        position.x = math.clamp(position.x, minMap.x + WallDistance, maxMap.x - WallDistance);
        position.z = math.clamp(position.z, minMap.z + WallDistance, maxMap.z - WallDistance);
        position.y = math.clamp(position.y, SDFOrigin.y + WallDistance, SDFOrigin.y + SDFGridSize.y * SDFCellSize - WallDistance);

        // Финальная жёсткая зона: агент не касается коллайдера игрока
        float3 toPlayerFinal = position - PlayerPosition;
        toPlayerFinal.y = 0;
        float distToPlayerFinal = math.length(toPlayerFinal);
        float playerExclusionDist = PlayerRadius + AgentRadius + 0.15f;
        if (distToPlayerFinal < playerExclusionDist && distToPlayerFinal > 0.001f)
        {
            float3 pushAwayDir = toPlayerFinal / distToPlayerFinal;
            float3 nextExclusionPos = new float3(PlayerPosition.x, position.y, PlayerPosition.z) + pushAwayDir * playerExclusionDist;
            
            float curSdf = SampleSDF_Distance(position);
            float nextSdf = SampleSDF_Distance(nextExclusionPos);
            if (nextSdf >= WallDistance || nextSdf > curSdf)
            {
                position = nextExclusionPos;
            }

            float velTowardPlayer = math.dot(agent.Velocity, -pushAwayDir);
            if (velTowardPlayer > 0)
                agent.Velocity -= -pushAwayDir * velTowardPlayer;
        }

        if (math.lengthsq(agent.Velocity) > 0.01f)
        {
            quaternion targetRot = quaternion.LookRotationSafe(math.normalizesafe(agent.Velocity), math.up());
            transform.rotation = math.slerp(transform.rotation, targetRot, DeltaTime * 10f);
        }

        transform.position = position;
        Agents[index] = agent;
    }
}
