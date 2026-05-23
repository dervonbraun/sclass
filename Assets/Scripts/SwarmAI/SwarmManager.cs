using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using TMPro;
using Sclass.EffectsSystem;

[System.Serializable]
public struct SwarmMutationEffect
{
    public MutationType Type;
    [Tooltip("Изменение стата в секунду (отрицательное = убыль).")]
    public float AmountPerSecond;
}

[System.Serializable]
public struct SwarmPreset
{
    public float CohesionWeight;
    public float SeparationWeight;
}

public class SwarmManager : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Префабы агентов по типам: [0] Kinesia, [1] Smallion, [2] Transfinite.")]
    public GameObject[] AgentPrefabs = new GameObject[3];
    public Transform Player;
    public FlowFieldGenerator FlowField;
    public SDFGrid SdfGrid;
    public SwarmAnchorManager AnchorManager;

    [Header("UI — Счётчик агентов")]
    [Tooltip("TMP_Text для отображения числа живых агентов. Назначить в Inspector.")]
    public TMP_Text AgentCountText;

    [Header("Настройки Роя")]
    public int SwarmSize = 500;

    [Header("Спавн")]
    [Tooltip("Радиус появления агентов вокруг менеджера.")]
    public float SpawnRadius = 20f;
    [Tooltip("Слой земли/поверхности.")]
    public LayerMask GroundMask;

    [Header("Boids Weights — CohesionWeight/SeparationWeight управляются пресетами")]
    [Tooltip("Управляется пресетом во время игры.")]
    public float SeparationWeight = 1.5f;
    public float AlignmentWeight = 1.0f;
    [Tooltip("Управляется пресетом во время игры.")]
    public float CohesionWeight = 1.0f;
    public float AnchorWeight = 3.0f;
    public float WanderWeight = 1.0f;
    public float ObstacleAvoidWeight = 5.0f;

    [Header("Радиусы Boids")]
    public float SeparationRadius = 1.5f;
    public float NeighborRadius = 4.0f;
    public float ObstacleAvoidRadius = 4.0f;

    [Header("Контакт — Элементарные эффекты (живые агенты рядом)")]
    [Tooltip("Радиус контакта. Если игрок внутри, применяются мутации.")]
    public float AgentAttackRadius = 1.2f;
    public SwarmMutationEffect[] MutationEffects = new SwarmMutationEffect[]
    {
        new SwarmMutationEffect { Type = MutationType.Kinesia,    AmountPerSecond = -5f },
        new SwarmMutationEffect { Type = MutationType.Transfinite, AmountPerSecond = -3f }
    };

    [Header("Облака смерти")]
    [Tooltip("Префабы облаков по типам: [0] Kinesia, [1] Smallion, [2] Transfinite.")]
    public GameObject[] CloudPrefabs = new GameObject[3];
    [Tooltip("Урон в секунду для каждого типа облака (отрицательный = убыль стата).")]
    public float[] CloudAmountsPerSecond = { -5f, -5f, -3f };
    public float CloudLifetime = 8f;
    public float CloudRadius   = 3f;

    [Header("Движение & FSM")]
    public float MaxSpeed = 5.0f;
    public float ChargeSpeedMult = 2.0f;
    public float ChargeRadius = 10.0f;
    public float AgentRadius = 0.5f;
    public float WallDistance = 0.5f;
    public float PlayerRadius = 0.5f;

    [Header("Динамические Пресеты Роя")]
    [Tooltip("Рой собирается в кучу.")]
    public SwarmPreset Gathered = new SwarmPreset
    {
        CohesionWeight   = 3.0f,
        SeparationWeight = 0.5f
    };
    [Tooltip("Рой рассредотачивается.")]
    public SwarmPreset Scattered = new SwarmPreset
    {
        CohesionWeight   = 0.2f,
        SeparationWeight = 2.5f
    };

    [Header("Трейл облаков (пока рой летит)")]
    [Tooltip("Как часто каждый тип оставляет облако на позиции своего якоря (секунды).")]
    public float CloudSpawnInterval = 3f;
    [Tooltip("Максимум облаков в сцене одновременно.")]
    public int MaxCloudsInScene = 30;

    private float _cloudSpawnTimer;
    private int _currentCloudCount;

    private NativeArray<AgentData> _agents;
    private NativeArray<float3> _agentPositions;
    private TransformAccessArray _transformAccessArray;
    private NativeParallelMultiHashMap<int, int> _hashMap;
    private JobHandle _swarmHandle;

    private float _stateTimer;
    private float _stateDuration = 8f;
    private bool _isGathered = true;

    public int AliveCount { get; private set; }

    private void Start()
    {
        _agents = new NativeArray<AgentData>(SwarmSize, Allocator.Persistent);
        _agentPositions = new NativeArray<float3>(SwarmSize, Allocator.Persistent);
        _transformAccessArray = new TransformAccessArray(SwarmSize);
        _hashMap = new NativeParallelMultiHashMap<int, int>(SwarmSize, Allocator.Persistent);

        for (int i = 0; i < SwarmSize; i++)
        {
            Vector3 spawnPos = transform.position;
            bool pointFound = false;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * SpawnRadius;
                Vector3 rayStart = transform.position + new Vector3(randomCircle.x, 50f, randomCircle.y);

                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, GroundMask))
                {
                    Vector3 testPos = hit.point + Vector3.up * 0.1f;

                    if (!Physics.CheckSphere(testPos, AgentRadius, FlowField.UnwalkableMask)
                        && NavMesh.SamplePosition(testPos, out NavMeshHit navHit, 2f, NavMesh.AllAreas))
                    {
                        spawnPos = navHit.position;
                        pointFound = true;
                        break;
                    }
                }
            }

            if (!pointFound)
                Debug.LogWarning($"[Swarm] Не смог найти точку для спавна агента. Спавним в центре.");

            int agentType = i % 3;
            GameObject prefab = null;
            for (int t = agentType; t >= 0; t--)
            {
                if (AgentPrefabs != null && t < AgentPrefabs.Length && AgentPrefabs[t] != null)
                {
                    prefab = AgentPrefabs[t];
                    break;
                }
            }

            if (prefab == null)
            {
                // Нет префаба — добавляем пустой GO чтобы не сломать индексы TransformAccessArray
                GameObject dummy = new GameObject($"SwarmAgent_Dummy_{i}");
                dummy.transform.position = new Vector3(0f, -1000f, 0f);
                _agents[i] = new AgentData { State = SwarmState.Dead };
                _transformAccessArray.Add(dummy.transform);
                Debug.LogError($"[SwarmManager] AgentPrefabs не назначены! Агент {i} — заглушка.");
                continue;
            }

            GameObject inst = Instantiate(prefab, spawnPos, Quaternion.identity);

            uint seed = (uint)((i + 1) * 2654435761);

            _agents[i] = new AgentData
            {
                Velocity       = float3.zero,
                State          = SwarmState.Swarming,
                StateTimer     = 0,
                WanderAngle    = UnityEngine.Random.Range(0f, math.PI * 2f),
                LateralOffset  = UnityEngine.Random.value * 2f - 1f,
                VerticalOffset = UnityEngine.Random.value,
                RandomSeed     = seed,
                Type        = (AgentType)agentType,
                AnchorIndex = agentType   // индекс якоря == индекс типа, всегда
            };

            _transformAccessArray.Add(inst.transform);

            AgentHealth health = inst.GetComponentInChildren<AgentHealth>();
            if (health != null) health.Init(i, this);
            else Debug.LogWarning($"[SwarmManager] AgentPrefab не имеет AgentHealth! Агент {i} не получит урон.");
        }
        AliveCount = SwarmSize;
    }

    public void KillAgent(int index, Vector3 deathPosition)
    {
        if (!_agents.IsCreated || index < 0 || index >= _agents.Length) return;

        AgentData data = _agents[index];
        if (data.State == SwarmState.Dead) return;

        data.State = SwarmState.Dead;
        _agents[index] = data;
        AliveCount = Mathf.Max(0, AliveCount - 1);
    }

    private void SpawnCloud(int typeIndex, Vector3 position)
    {
        if (CloudPrefabs == null || typeIndex >= CloudPrefabs.Length) return;
        GameObject cloudPrefab = CloudPrefabs[typeIndex];
        if (cloudPrefab == null) return;

        GameObject cloudGO = Instantiate(cloudPrefab, position, Quaternion.identity);
        AgentCloud cloud = cloudGO.GetComponent<AgentCloud>();
        if (cloud == null) return;

        float amount = (CloudAmountsPerSecond != null && typeIndex < CloudAmountsPerSecond.Length)
            ? CloudAmountsPerSecond[typeIndex]
            : -5f;

        cloud.Radius   = CloudRadius;
        cloud.Lifetime = CloudLifetime;

        MutationType mutationType = typeIndex switch
        {
            0 => MutationType.Kinesia,
            1 => MutationType.Smallion,
            2 => MutationType.Transfinite,
            _ => MutationType.Kinesia
        };

        _currentCloudCount++;
        cloud.Init(Player, mutationType, amount, () => _currentCloudCount--);
    }

    public bool IsAgentNearPlayer(Vector3 center, float radius)
    {
        if (!_agentPositions.IsCreated) return false;
        float radiusSq = radius * radius;
        float3 c = new float3(center.x, 0, center.z);
        for (int i = 0; i < _agentPositions.Length; i++)
        {
            if (_agents[i].State == SwarmState.Dead) continue;
            float3 p = new float3(_agentPositions[i].x, 0, _agentPositions[i].z);
            if (math.distancesq(c, p) < radiusSq)
                return true;
        }
        return false;
    }

    private void OnGUI()
    {
#if UNITY_EDITOR
        if (AgentCountText == null)
        {
            GUI.contentColor = Color.yellow;
            GUI.Label(new Rect(20, 55, 300, 30), $"Агентов: {AliveCount} / {SwarmSize}");
        }
#endif
    }

    private void OnDestroy()
    {
        _swarmHandle.Complete();

        if (_agents.IsCreated) _agents.Dispose();
        if (_agentPositions.IsCreated) _agentPositions.Dispose();
        if (_hashMap.IsCreated) _hashMap.Dispose();
        if (_transformAccessArray.isCreated) _transformAccessArray.Dispose();
    }

    private void Update()
    {
        if (FlowField == null || !FlowField.FlowField.IsCreated || Player == null
            || SdfGrid == null || !SdfGrid.Gradient3D.IsCreated
            || AnchorManager == null || !AnchorManager.AnchorPositions.IsCreated)
            return;

        // Динамическая интерполяция пресетов
        _stateTimer += Time.deltaTime;
        if (_stateTimer > _stateDuration)
        {
            _stateTimer = 0f;
            _isGathered = !_isGathered;
            _stateDuration = UnityEngine.Random.Range(8f, 20f);
        }
        float presetT = _stateTimer / _stateDuration;
        SwarmPreset fromPreset = _isGathered ? Gathered : Scattered;
        SwarmPreset toPreset   = _isGathered ? Scattered : Gathered;
        CohesionWeight   = Mathf.Lerp(fromPreset.CohesionWeight,   toPreset.CohesionWeight,   presetT);
        SeparationWeight = Mathf.Lerp(fromPreset.SeparationWeight, toPreset.SeparationWeight, presetT);

        _hashMap.Clear();

        // 1. Копируем позиции
        var copyPositionsJob = new CopyPositionsJob { Positions = _agentPositions };
        JobHandle copyHandle = copyPositionsJob.Schedule(_transformAccessArray, default);

        // 2. Хешируем
        var hashJob = new HashPositionsJob
        {
            HashMap  = _hashMap.AsParallelWriter(),
            Agents   = _agents,
            CellSize = NeighborRadius
        };
        JobHandle hashHandle = hashJob.Schedule(_transformAccessArray, copyHandle);

        // 4. Основная джоба
        var swarmJob = new SwarmJob
        {
            Agents          = _agents,
            CostField       = FlowField.CostField,
            FlowField       = FlowField.FlowField,
            HashMap         = _hashMap,
            AgentPositions  = _agentPositions,
            AnchorPositions = AnchorManager.AnchorPositions,

            SDF_Gradient3D = SdfGrid.Gradient3D,
            SDF_Distance3D = SdfGrid.Distance3D,
            SDFOrigin      = SdfGrid.GridOrigin,
            SDFGridSize    = SdfGrid.GridSize,
            SDFCellSize    = SdfGrid.CellSize,

            GridSize          = FlowField.GridSize,
            FlowFieldCellSize = FlowField.CellSize,
            FlowFieldOrigin   = FlowField.WorldBottomLeft,
            PlayerPosition    = Player.position,
            DeltaTime         = math.min(Time.deltaTime, 0.033f), // ЗАЩИТА ОТ ПРОЛЁТОВ (макс шаг для 30 FPS)
            Time              = Time.time,

            SeparationWeight    = SeparationWeight,
            AlignmentWeight     = AlignmentWeight,
            CohesionWeight      = CohesionWeight,
            AnchorWeight        = AnchorWeight,
            WanderWeight        = WanderWeight,
            ObstacleAvoidWeight = ObstacleAvoidWeight,

            SeparationRadius    = SeparationRadius,
            NeighborRadius      = NeighborRadius,
            ObstacleAvoidRadius = ObstacleAvoidRadius,

            MaxSpeed        = MaxSpeed,
            ChargeSpeedMult = ChargeSpeedMult,
            ChargeRadius    = ChargeRadius,
            PlayerRadius    = PlayerRadius,

            HashCellSize = NeighborRadius,
            AgentRadius  = AgentRadius,
            WallDistance = WallDistance
        };

        _swarmHandle = swarmJob.Schedule(_transformAccessArray, hashHandle);
        _swarmHandle.Complete(); // Ждем только здесь!

        TickCloudTrail(Time.deltaTime);

        if (AgentCountText != null)
            AgentCountText.text = AliveCount.ToString();
    }

    private void TickCloudTrail(float dt)
    {
        _cloudSpawnTimer += dt;
        if (_cloudSpawnTimer < CloudSpawnInterval) return;
        _cloudSpawnTimer = 0f;

        if (_currentCloudCount >= MaxCloudsInScene) return;

        // Один облако на тип, на позиции якоря — не стакуется в смертельную кучу
        NativeArray<float3> anchors = AnchorManager.AnchorPositions;
        for (int typeIndex = 0; typeIndex < 3; typeIndex++)
        {
            if (_currentCloudCount >= MaxCloudsInScene) break;
            if (typeIndex >= anchors.Length) break;

            float3 ap = anchors[typeIndex];
            SpawnCloud(typeIndex, new Vector3(ap.x, ap.y, ap.z));
        }
    }
}
