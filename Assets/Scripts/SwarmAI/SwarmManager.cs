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

public class SwarmManager : MonoBehaviour
{
    [Header("Ссылки")]
    public GameObject AgentPrefab;
    public Transform Player;
    public FlowFieldGenerator FlowField;

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

    [Header("Boids Weights (Тюнинг сил)")]
    [Tooltip("Вес отталкивания от соседей. Чем больше, тем реже агенты слипаются.")]
    public float SeparationWeight = 1.5f;
    [Tooltip("Вес выравнивания вектора скорости с соседями. Создает эффект косяка рыб/птиц.")]
    public float AlignmentWeight = 1.0f;
    [Tooltip("Вес стремления к центру локальной группы. Не дает рою распадаться.")]
    public float CohesionWeight = 1.0f;
    [Tooltip("Доминирующая сила течения (Flow Field) к игроку. Должна быть самой сильной.")]
    public float FlowFieldWeight = 3.0f;
    [Tooltip("Сила случайного копошения/блуждания. Разрушает идеальные линии в коридорах.")]
    public float WanderWeight = 1.0f;
    [Tooltip("Сила отталкивания от стен. Должна быть высокой, чтобы агенты не терлись о стены.")]
    public float ObstacleAvoidWeight = 5.0f;

    [Header("Жидкостная Модель (Fluid)")]
    [Tooltip("Давление: сила расталкивания при высокой локальной плотности. Ключевая сила для 'жидкостного' поведения.")]
    public float PressureWeight = 2.0f;
    [Tooltip("Боковое растекание: сила, заставляющая агентов заполнять ширину коридора перпендикулярно потоку.")]
    public float LateralSpreadWeight = 1.5f;
    [Tooltip("Вязкость: сглаживание скорости агента со скоростью соседей. Создает 'течение' вместо хаоса.")]
    public float ViscosityWeight = 0.3f;
    [Tooltip("Целевая плотность: сколько соседей считается комфортным. Меньше = рой более рыхлый и растекается сильнее.")]
    public float TargetDensity = 3.0f;

    [Header("Радиусы Boids")]
    [Tooltip("Радиус внутри которого агенты отталкиваются.")]
    public float SeparationRadius = 1.5f;
    [Tooltip("Радиус для поиска соседей для Spatial Hashing (используется для Alignment, Cohesion).")]
    public float NeighborRadius = 4.0f;
    [Tooltip("Дистанция до стены, на которой начинается отталкивание.")]
    public float ObstacleAvoidRadius = 4.0f;

    [Header("Контакт — Элементарные эффекты")]
    [Tooltip("Радиус контакта. Если игрок внутри, применяются мутации.")]
    public float AgentAttackRadius = 1.2f;
    [Tooltip("Эффекты, накладываемые на игрока при контакте с роем. Можно добавить несколько разных типов.")]
    public SwarmMutationEffect[] MutationEffects = new SwarmMutationEffect[]
    {
        new SwarmMutationEffect { Type = MutationType.Kinesia,    AmountPerSecond = -5f },
        new SwarmMutationEffect { Type = MutationType.Transfinite, AmountPerSecond = -3f }
    };

    [Header("Движение & FSM")]
    [Tooltip("Максимальная скорость в состоянии Swarming.")]
    public float MaxSpeed = 5.0f;
    [Tooltip("Множитель скорости при переходе в состояние атаки (Charging).")]
    public float ChargeSpeedMult = 2.0f;
    [Tooltip("Дистанция до игрока для перехода в агрессивный рывок (Charging).")]
    public float ChargeRadius = 10.0f;
    [Tooltip("Физический радиус сферы агента. Используется для столкновений между агентами.")]
    public float AgentRadius = 0.5f;

    [Tooltip("Насколько далеко агенты должны держаться от стен (коллизия со стенами).")]
    public float WallDistance = 0.5f;

    [Tooltip("Радиус CharacterController игрока. Агенты не заходят внутрь этого радиуса.")]
    public float PlayerRadius = 0.5f;

    // Native Collections для Джоб
    private NativeArray<AgentData> _agents;
    private NativeArray<float3> _agentPositions;
    private TransformAccessArray _transformAccessArray;
    private NativeParallelMultiHashMap<int, int> _hashMap;
    private JobHandle _swarmHandle;

    /// <summary>Количество живых агентов (не Dead). Обновляется каждый кадр.</summary>
    public int AliveCount { get; private set; }

    private void Start()
    {
        _agents = new NativeArray<AgentData>(SwarmSize, Allocator.Persistent);
        _agentPositions = new NativeArray<float3>(SwarmSize, Allocator.Persistent);
        _transformAccessArray = new TransformAccessArray(SwarmSize);
        // Выделяем память с запасом под хэш-мапу. Ключ - хэш ячейки, значение - индекс агента.
        _hashMap = new NativeParallelMultiHashMap<int, int>(SwarmSize, Allocator.Persistent);

        // Инициализация пула агентов
        for (int i = 0; i < SwarmSize; i++)
        {
            Vector3 spawnPos = transform.position;
            bool pointFound = false;

            // Пытаемся найти валидную точку на земле (до 30 попыток на агента)
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * SpawnRadius;
                // Бросаем луч сверху вниз
                Vector3 rayStart = transform.position + new Vector3(randomCircle.x, 50f, randomCircle.y);

                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, GroundMask))
                {
                    Vector3 testPos = hit.point;
                    // Игнорируем высоту неровностей/других объектов и жестко фиксируем по высоте Менеджера
                    testPos.y = transform.position.y; 
                    
                    // Проверяем, не застрянет ли агент в стене при спавне
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

            // Каждый агент получает уникальный RNG-сид и рандомный боковой offset
            uint seed = (uint)((i + 1) * 2654435761); // Knuth's multiplicative hash
            float lateralOffset = (UnityEngine.Random.value * 2f - 1f); // от -1 до +1

            _agents[i] = new AgentData
            {
                Velocity = float3.zero,
                State = SwarmState.Swarming,
                StateTimer = 0,
                WanderAngle = UnityEngine.Random.Range(0f, math.PI * 2f),
                LateralOffset = lateralOffset,
                RandomSeed = seed
            };

            _transformAccessArray.Add(inst.transform);

            // Регистрируем AgentHealth: передаём индекс и ссылку на себя
            AgentHealth health = inst.GetComponentInChildren<AgentHealth>();
            if (health != null) health.Init(i, this);
            else Debug.LogWarning($"[SwarmManager] AgentPrefab не имеет AgentHealth! Агент {i} не получит урон.");
        }
        AliveCount = SwarmSize;
    }

    /// <summary>
    /// Вызывается AgentHealth при смерти агента.
    /// Безопасно: SwarmHandle.Complete() уже вызван до конца Update(), 
    /// поэтому NativeArray не занят джобой.
    /// </summary>
    public void KillAgent(int index)
    {
        if (!_agents.IsCreated || index < 0 || index >= _agents.Length) return;

        AgentData data = _agents[index];
        if (data.State == SwarmState.Dead) return; // уже мёртв — не декрементируем дважды

        data.State = SwarmState.Dead;
        _agents[index] = data;
        AliveCount = Mathf.Max(0, AliveCount - 1);
    }

    /// <summary>
    /// Проверяет, есть ли живой агент в радиусе от точки (XZ-плоскость).
    /// Используется PlayerHealth вместо Physics.OverlapSphere — не зависит от коллайдеров и слоёв.
    /// </summary>
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
        // Завершаем все активные джобы перед освобождением памяти
        _swarmHandle.Complete();

        if (_agents.IsCreated) _agents.Dispose();
        if (_agentPositions.IsCreated) _agentPositions.Dispose();
        if (_hashMap.IsCreated) _hashMap.Dispose();
        if (_transformAccessArray.isCreated) _transformAccessArray.Dispose();
    }

    private void Update()
    {
        if (FlowField == null || !FlowField.FlowField.IsCreated || Player == null)
            return;

        // 1. Очищаем хэш-сетку на каждый кадр
        _hashMap.Clear();

        // 2. Джоба копирования позиций в NativeArray для безопасного чтения
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
            CellSize = NeighborRadius // Размер ячейки хеширования = радиусу видимости
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
            GridSize = FlowField.GridSize,
            FlowFieldCellSize = FlowField.CellSize,
            FlowFieldOrigin = FlowField.WorldBottomLeft,
            PlayerPosition = Player.position,
            DeltaTime = UnityEngine.Time.deltaTime,
            Time = UnityEngine.Time.time,
            
            SeparationWeight = SeparationWeight,
            AlignmentWeight = AlignmentWeight,
            CohesionWeight = CohesionWeight,
            FlowFieldWeight = FlowFieldWeight,
            WanderWeight = WanderWeight,
            ObstacleAvoidWeight = ObstacleAvoidWeight,

            // Fluid forces
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

        // Запуск цепочки Джоб и ожидание их выполнения.
        // P.S: Для оптимизации .Complete() можно перенести в LateUpdate.
        _swarmHandle = swarmJob.Schedule(_transformAccessArray, hashHandle);
        _swarmHandle.Complete();

        // Пересчитываем живых агентов напрямую из NativeArray — надёжнее любого декремента
        int alive = 0;
        for (int i = 0; i < _agents.Length; i++)
            if (_agents[i].State != SwarmState.Dead) alive++;
        AliveCount = alive;

        if (AgentCountText != null)
            AgentCountText.text = AliveCount.ToString();
    }
}
