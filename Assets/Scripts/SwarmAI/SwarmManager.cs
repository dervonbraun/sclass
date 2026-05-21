using UnityEngine;
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
    public float LateralSpreadWeight;
    public float FlowFieldWeight;
}

public class SwarmManager : MonoBehaviour
{
    [Header("Ссылки")]
    public GameObject AgentPrefab;
    public Transform Player;
    public FlowFieldGenerator FlowField;
    public SDFGrid SdfGrid;

    [Header("UI — Счётчик агентов")]
    [Tooltip("TMP_Text для отображения числа живых агентов. Назначить в Inspector.")]
    public TMP_Text AgentCountText;

    [Header("Настройки Роя")]
    public int SwarmSize = 500;

    [Header("Спавн (Появление)")]
    [Tooltip("Радиус появления агентов вокруг менеджера.")]
    public float SpawnRadius = 20f;
    [Tooltip("Слой земли/поверхности. Агенты появятся только если под ними есть поверхность этого слоя.")]
    public LayerMask GroundMask;

    [Header("Boids Weights (Тюнинг сил) — CohesionWeight/SeparationWeight/LateralSpreadWeight/FlowFieldWeight управляются пресетами")]
    [Tooltip("Вес отталкивания от соседей. Управляется пресетом во время игры.")]
    public float SeparationWeight = 1.5f;
    [Tooltip("Вес выравнивания вектора скорости с соседями.")]
    public float AlignmentWeight = 1.0f;
    [Tooltip("Вес стремления к центру группы. Управляется пресетом во время игры.")]
    public float CohesionWeight = 1.0f;
    [Tooltip("Доминирующая сила течения к игроку. Управляется пресетом во время игры.")]
    public float FlowFieldWeight = 3.0f;
    [Tooltip("Сила случайного блуждания.")]
    public float WanderWeight = 1.0f;
    [Tooltip("Сила отталкивания от стен через 3D SDF.")]
    public float ObstacleAvoidWeight = 5.0f;

    [Header("Жидкостная Модель (Fluid)")]
    public float PressureWeight = 2.0f;
    [Tooltip("Боковое растекание. Управляется пресетом во время игры.")]
    public float LateralSpreadWeight = 1.5f;
    public float ViscosityWeight = 0.3f;
    public float TargetDensity = 3.0f;

    [Header("Радиусы Boids")]
    public float SeparationRadius = 1.5f;
    public float NeighborRadius = 4.0f;
    public float ObstacleAvoidRadius = 4.0f;

    [Header("Контакт — Элементарные эффекты")]
    [Tooltip("Радиус контакта. Если игрок внутри, применяются мутации.")]
    public float AgentAttackRadius = 1.2f;
    [Tooltip("Эффекты, накладываемые на игрока при контакте с роем.")]
    public SwarmMutationEffect[] MutationEffects = new SwarmMutationEffect[]
    {
        new SwarmMutationEffect { Type = MutationType.Kinesia,    AmountPerSecond = -5f },
        new SwarmMutationEffect { Type = MutationType.Transfinite, AmountPerSecond = -3f }
    };

    [Header("Движение & FSM")]
    public float MaxSpeed = 5.0f;
    public float ChargeSpeedMult = 2.0f;
    public float ChargeRadius = 10.0f;
    public float AgentRadius = 0.5f;
    public float WallDistance = 0.5f;
    public float PlayerRadius = 0.5f;

    [Header("Динамические Пресеты Роя")]
    [Tooltip("Рой собирается в кучу — высокое сцепление, слабое разделение.")]
    public SwarmPreset Gathered = new SwarmPreset
    {
        CohesionWeight    = 3.0f,
        SeparationWeight  = 0.5f,
        LateralSpreadWeight = 0.3f,
        FlowFieldWeight   = 4.0f
    };
    [Tooltip("Рой рассредотачивается — слабое сцепление, сильное разделение.")]
    public SwarmPreset Scattered = new SwarmPreset
    {
        CohesionWeight    = 0.2f,
        SeparationWeight  = 2.5f,
        LateralSpreadWeight = 2.0f,
        FlowFieldWeight   = 1.5f
    };

    // Native Collections для Джоб
    private NativeArray<AgentData> _agents;
    private NativeArray<float3> _agentPositions;
    private TransformAccessArray _transformAccessArray;
    private NativeParallelMultiHashMap<int, int> _hashMap;
    private JobHandle _swarmHandle;

    // Состояние динамического пресета
    private float _stateTimer;
    private float _stateDuration = 8f;
    private bool _isGathered = true;

    /// <summary>Количество живых агентов (не Dead). Обновляется каждый кадр.</summary>
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
                    Vector3 testPos = hit.point;
                    testPos.y = transform.position.y;

                    if (!Physics.CheckSphere(testPos, WallDistance, FlowField.UnwalkableMask))
                    {
                        spawnPos = testPos;
                        pointFound = true;
                        break;
                    }
                }
            }

            if (!pointFound)
            {
                Debug.LogWarning($"[Swarm] Не смог найти точку для спавна агента на слое GroundMask. Спавним в центре.");
            }

            GameObject inst = Instantiate(AgentPrefab, spawnPos, Quaternion.identity);

            uint seed = (uint)((i + 1) * 2654435761);
            float lateralOffset = (UnityEngine.Random.value * 2f - 1f);

            _agents[i] = new AgentData
            {
                Velocity      = float3.zero,
                State         = SwarmState.Swarming,
                StateTimer    = 0,
                WanderAngle   = UnityEngine.Random.Range(0f, math.PI * 2f),
                LateralOffset = lateralOffset,
                VerticalOffset = UnityEngine.Random.value,
                RandomSeed    = seed,
                Type          = (AgentType)(i % 3)
            };

            _transformAccessArray.Add(inst.transform);

            AgentHealth health = inst.GetComponentInChildren<AgentHealth>();
            if (health != null) health.Init(i, this);
            else Debug.LogWarning($"[SwarmManager] AgentPrefab не имеет AgentHealth! Агент {i} не получит урон.");
        }
        AliveCount = SwarmSize;
    }

    public void KillAgent(int index)
    {
        if (!_agents.IsCreated || index < 0 || index >= _agents.Length) return;

        AgentData data = _agents[index];
        if (data.State == SwarmState.Dead) return;

        data.State = SwarmState.Dead;
        _agents[index] = data;
        AliveCount = Mathf.Max(0, AliveCount - 1);
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
        if (AgentCountText == null)
        {
            GUI.contentColor = Color.yellow;
            GUI.Label(new Rect(20, 55, 300, 30), $"Агентов: {AliveCount} / {SwarmSize}");
        }
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
            || SdfGrid == null || !SdfGrid.Gradient3D.IsCreated)
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
        CohesionWeight      = Mathf.Lerp(fromPreset.CohesionWeight,      toPreset.CohesionWeight,      presetT);
        SeparationWeight    = Mathf.Lerp(fromPreset.SeparationWeight,    toPreset.SeparationWeight,    presetT);
        LateralSpreadWeight = Mathf.Lerp(fromPreset.LateralSpreadWeight, toPreset.LateralSpreadWeight, presetT);
        FlowFieldWeight     = Mathf.Lerp(fromPreset.FlowFieldWeight,     toPreset.FlowFieldWeight,     presetT);

        // 1. Очищаем хэш-сетку на каждый кадр
        _hashMap.Clear();

        // 2. Джоба копирования позиций
        var copyPositionsJob = new CopyPositionsJob
        {
            Positions = _agentPositions
        };
        JobHandle copyHandle = copyPositionsJob.Schedule(_transformAccessArray);

        // 3. Джоба заполнения Spatial Hash Grid
        var hashJob = new HashPositionsJob
        {
            HashMap  = _hashMap.AsParallelWriter(),
            Agents   = _agents,
            CellSize = NeighborRadius
        };
        JobHandle hashHandle = hashJob.Schedule(_transformAccessArray, copyHandle);

        // 4. Основная джоба физики роя и FSM
        var swarmJob = new SwarmJob
        {
            Agents = _agents,
            FlowField = FlowField.FlowField,
            CostField = FlowField.CostField,
            HashMap = _hashMap,
            AgentPositions = _agentPositions,

            SDF_Gradient3D = SdfGrid.Gradient3D,
            SDF_Distance3D = SdfGrid.Distance3D,
            SDFOrigin      = SdfGrid.GridOrigin,
            SDFGridSize    = SdfGrid.GridSize,
            SDFCellSize    = SdfGrid.CellSize,

            GridSize = FlowField.GridSize,
            FlowFieldCellSize = FlowField.CellSize,
            FlowFieldOrigin = FlowField.WorldBottomLeft,
            PlayerPosition = Player.position,
            DeltaTime = Time.deltaTime,
            Time = Time.time,

            SeparationWeight = SeparationWeight,
            AlignmentWeight = AlignmentWeight,
            CohesionWeight = CohesionWeight,
            FlowFieldWeight = FlowFieldWeight,
            WanderWeight = WanderWeight,
            ObstacleAvoidWeight = ObstacleAvoidWeight,

            PressureWeight = PressureWeight,
            LateralSpreadWeight = LateralSpreadWeight,
            ViscosityWeight = ViscosityWeight,

            SeparationRadius = SeparationRadius,
            NeighborRadius = NeighborRadius,
            ObstacleAvoidRadius = ObstacleAvoidRadius,

            MaxSpeed = MaxSpeed,
            ChargeSpeedMult = ChargeSpeedMult,
            ChargeRadius = ChargeRadius,
            PlayerRadius = PlayerRadius,

            HashCellSize = NeighborRadius,
            TargetDensity = TargetDensity,
            AgentRadius = AgentRadius,
            WallDistance = WallDistance
        };

        _swarmHandle = swarmJob.Schedule(_transformAccessArray, hashHandle);
        _swarmHandle.Complete();

        int alive = 0;
        for (int i = 0; i < _agents.Length; i++)
            if (_agents[i].State != SwarmState.Dead) alive++;
        AliveCount = alive;

        if (AgentCountText != null)
            AgentCountText.text = AliveCount.ToString();
    }
}
