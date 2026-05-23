using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

public class SwarmAnchorManager : MonoBehaviour
{
    public NavMeshAgent AnchorPrefab;
    public Transform Player;

    public float ScatterRadius   = 40f;
    public float GatherRadius    = 2f;
    public float ScatterDuration = 15f;
    public float GatherDuration  = 10f;

    public NativeArray<float3> AnchorPositions;

    public enum SwarmMode { Gathering, Scattered }
    public SwarmMode CurrentMode { get; private set; } = SwarmMode.Gathering;

    private NavMeshAgent _anchor;
    private float _modeTimer;
    private float _currentDuration;

    private void Awake()
    {
        AnchorPositions  = new NativeArray<float3>(1, Allocator.Persistent);
        _currentDuration = GatherDuration;

        Vector3 origin = Player != null ? Player.position : transform.position;
        if (NavMesh.SamplePosition(origin, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            origin = hit.position;

        GameObject go = Instantiate(AnchorPrefab.gameObject, origin, Quaternion.identity);
        go.name = "SwarmAnchor";
        _anchor = go.GetComponent<NavMeshAgent>();
        _anchor.Warp(origin);
        AnchorPositions[0] = origin;
    }

    private void Start()
    {
        if (_anchor.isOnNavMesh)
            _anchor.SetDestination(Player.position);
    }

    private void Update()
    {
        _modeTimer += Time.deltaTime;
        if (_modeTimer > _currentDuration)
        {
            _modeTimer = 0f;
            SwitchMode();
        }

        if (_anchor.isOnNavMesh && !_anchor.hasPath && _anchor.remainingDistance < 0.01f)
        {
            Vector3 offset = UnityEngine.Random.insideUnitSphere * GatherRadius;
            offset.y = 0;
            _anchor.SetDestination(Player.position + offset);
        }

        AnchorPositions[0] = _anchor.transform.position;
    }

    private void SwitchMode()
    {
        if (CurrentMode == SwarmMode.Gathering)
        {
            CurrentMode      = SwarmMode.Scattered;
            _currentDuration = ScatterDuration + UnityEngine.Random.Range(-3f, 3f);

            Vector3 randomDir = UnityEngine.Random.insideUnitSphere * ScatterRadius;
            randomDir.y = 0;
            Vector3 target = Player.position + randomDir;
            if (_anchor.isOnNavMesh && NavMesh.SamplePosition(target, out NavMeshHit hit, ScatterRadius, NavMesh.AllAreas))
                _anchor.SetDestination(hit.position);
        }
        else
        {
            CurrentMode      = SwarmMode.Gathering;
            _currentDuration = GatherDuration + UnityEngine.Random.Range(-3f, 3f);

            Vector3 offset = UnityEngine.Random.insideUnitSphere * GatherRadius;
            offset.y = 0;
            if (_anchor.isOnNavMesh)
                _anchor.SetDestination(Player.position + offset);
        }
    }

    private void OnDestroy()
    {
        if (AnchorPositions.IsCreated) AnchorPositions.Dispose();
    }
}
