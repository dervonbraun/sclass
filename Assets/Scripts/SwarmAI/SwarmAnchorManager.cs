using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Три якоря — по одному на каждый тип агентов: [0] Kinesia, [1] Smallion, [2] Transfinite.
/// Агенты с AnchorIndex = (int)Type всегда следуют за соответствующим якорем.
/// </summary>
public class SwarmAnchorManager : MonoBehaviour
{
    public NavMeshAgent AnchorPrefab;
    public Transform Player;

    [Header("Поведение якорей")]
    public float ScatterRadius  = 40f;
    public float GatherRadius   = 2f;
    public float ScatterDuration = 15f;
    public float GatherDuration  = 10f;

    /// <summary>Всегда ровно 3 — по одному на тип.</summary>
    public const int AnchorCount = 3;

    public NativeArray<float3> AnchorPositions;

    public enum SwarmMode { Gathering, Scattered }
    public SwarmMode CurrentMode { get; private set; } = SwarmMode.Gathering;

    private NavMeshAgent[] _anchors;
    private float _modeTimer;
    private float _currentDuration;

    private void Awake()
    {
        _anchors = new NavMeshAgent[AnchorCount];
        AnchorPositions = new NativeArray<float3>(AnchorCount, Allocator.Persistent);
        _currentDuration = GatherDuration;

        Vector3 origin = Player != null ? Player.position : transform.position;
        if (NavMesh.SamplePosition(origin, out NavMeshHit spawnHit, 10f, NavMesh.AllAreas))
            origin = spawnHit.position;

        string[] typeNames = { "Kinesia", "Smallion", "Transfinite" };
        for (int i = 0; i < AnchorCount; i++)
        {
            GameObject go = Instantiate(AnchorPrefab.gameObject, origin, Quaternion.identity);
            go.name = $"SwarmAnchor_{typeNames[i]}";
            NavMeshAgent agent = go.GetComponent<NavMeshAgent>();
            agent.Warp(origin);
            _anchors[i] = agent;
            AnchorPositions[i] = origin;
        }
    }

    private void Start()
    {
        for (int i = 0; i < AnchorCount; i++)
        {
            Vector3 offset = UnityEngine.Random.insideUnitSphere * GatherRadius;
            offset.y = 0;
            if (_anchors[i].isOnNavMesh)
                _anchors[i].SetDestination(Player.position + offset);
        }
    }

    private void Update()
    {
        _modeTimer += Time.deltaTime;
        if (_modeTimer > _currentDuration)
        {
            _modeTimer = 0f;
            SwitchMode();
        }

        for (int i = 0; i < AnchorCount; i++)
        {
            if (_anchors[i].isOnNavMesh && !_anchors[i].hasPath && _anchors[i].remainingDistance < 0.01f)
            {
                Vector3 offset = UnityEngine.Random.insideUnitSphere * GatherRadius;
                offset.y = 0;
                _anchors[i].SetDestination(Player.position + offset);
            }

            AnchorPositions[i] = _anchors[i].transform.position;
        }
    }

    private void SwitchMode()
    {
        if (CurrentMode == SwarmMode.Gathering)
        {
            CurrentMode = SwarmMode.Scattered;
            _currentDuration = ScatterDuration + UnityEngine.Random.Range(-3f, 3f);
            for (int i = 0; i < AnchorCount; i++)
            {
                Vector3 randomDir = UnityEngine.Random.insideUnitSphere * ScatterRadius;
                randomDir.y = 0;
                Vector3 target = Player.position + randomDir;
                if (_anchors[i].isOnNavMesh && NavMesh.SamplePosition(target, out NavMeshHit hit, ScatterRadius, NavMesh.AllAreas))
                    _anchors[i].SetDestination(hit.position);
            }
        }
        else
        {
            CurrentMode = SwarmMode.Gathering;
            _currentDuration = GatherDuration + UnityEngine.Random.Range(-3f, 3f);
            for (int i = 0; i < AnchorCount; i++)
            {
                Vector3 offset = UnityEngine.Random.insideUnitSphere * GatherRadius;
                offset.y = 0;
                if (_anchors[i].isOnNavMesh)
                    _anchors[i].SetDestination(Player.position + offset);
            }
        }
    }

    private void OnDestroy()
    {
        if (AnchorPositions.IsCreated) AnchorPositions.Dispose();
    }
}
