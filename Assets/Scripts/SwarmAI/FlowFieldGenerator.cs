using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;

public class FlowFieldGenerator : MonoBehaviour
{
    [Header("Настройки Сетки (Grid)")]
    public float2 GridWorldSize = new float2(100, 100);
    public float CellRadius = 1f;
    public LayerMask UnwalkableMask;

    public Transform Target; // Игрок

    private float _cellDiameter;
    private int2 _gridSize;
    public int2 GridSize => _gridSize;
    public float CellSize => _cellDiameter;

    // Векторное поле для использования внутри Job'ы
    public NativeArray<float3> FlowField;
    
    // Карта препятствий (255 = стена) для навигации (BFS)
    public NativeArray<byte> CostField => _costField;

    private NativeArray<byte> _costField;
    private NativeArray<int> _integrationField;

    // Очередь для BFS. Переиспользуется, чтобы не было GC.Alloc.
    private Queue<int2> _cellsToCheck;

    public float3 WorldBottomLeft => (float3)transform.position - new float3(GridWorldSize.x / 2, 0, GridWorldSize.y / 2);

    private void Awake()
    {
        _cellDiameter = CellRadius * 2;
        _gridSize.x = Mathf.RoundToInt(GridWorldSize.x / _cellDiameter);
        _gridSize.y = Mathf.RoundToInt(GridWorldSize.y / _cellDiameter);

        int totalCells = _gridSize.x * _gridSize.y;
        FlowField = new NativeArray<float3>(totalCells, Allocator.Persistent);
        _costField = new NativeArray<byte>(totalCells, Allocator.Persistent);
        _integrationField = new NativeArray<int>(totalCells, Allocator.Persistent);

        _cellsToCheck = new Queue<int2>(totalCells);

        GenerateCostField();
    }

    private void OnDestroy()
    {
        if (FlowField.IsCreated) FlowField.Dispose();
        if (_costField.IsCreated) _costField.Dispose();
        if (_integrationField.IsCreated) _integrationField.Dispose();
    }

    private void GenerateCostField()
    {
        float3 bottomLeft = WorldBottomLeft;
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                float3 worldPoint = bottomLeft + new float3(x * _cellDiameter + CellRadius, 0, y * _cellDiameter + CellRadius);
                
                // Маленький бокс — помечаем как стену ТОЛЬКО ячейки, центр которых реально внутри стены.
                // Физическая коллизия теперь через SDF, а не через AABB ячеек.
                Vector3 boxCenter = (Vector3)worldPoint + new Vector3(0, 1.5f, 0);
                Vector3 halfExtents = new Vector3(CellRadius * 0.5f, 2.0f, CellRadius * 0.5f);
                
                bool isObstacle = Physics.CheckBox(boxCenter, halfExtents, Quaternion.identity, UnwalkableMask);
                
                _costField[GetIndex(x, y)] = isObstacle ? (byte)255 : (byte)1;
            }
        }
    }

    private void Update()
    {
        if (Target != null)
        {
            GenerateIntegrationField();
            GenerateFlowField();
        }
    }

    private static readonly int[] s_dx = { -1, 1, 0, 0 };
    private static readonly int[] s_dy = { 0, 0, -1, 1 };

    private void GenerateIntegrationField()
    {
        // Сброс Integration Field
        for (int i = 0; i < _integrationField.Length; i++)
        {
            _integrationField[i] = int.MaxValue;
        }

        int2 targetCell = WorldToCell(Target.position);
        if (!IsValid(targetCell)) return;
        // Если игрок стоит в ячейке-стене — навигация невозможна
        if (_costField[GetIndex(targetCell.x, targetCell.y)] == 255) return;

        int targetIndex = GetIndex(targetCell.x, targetCell.y);
        _integrationField[targetIndex] = 0;

        _cellsToCheck.Clear();
        _cellsToCheck.Enqueue(targetCell);

        // BFS для расчета расстояний (Dijkstra/BFS)
        while (_cellsToCheck.Count > 0)
        {
            int2 current = _cellsToCheck.Dequeue();
            int currentIndex = GetIndex(current.x, current.y);
            int currentCost = _integrationField[currentIndex];

            for (int i = 0; i < 4; i++)
            {
                int2 neighbor = new int2(current.x + s_dx[i], current.y + s_dy[i]);
                if (IsValid(neighbor))
                {
                    int neighborIndex = GetIndex(neighbor.x, neighbor.y);
                    byte cost = _costField[neighborIndex];
                    
                    if (cost == 255) continue; // Непроходимая ячейка

                    int newCost = currentCost + cost;
                    if (newCost < _integrationField[neighborIndex])
                    {
                        _integrationField[neighborIndex] = newCost;
                        _cellsToCheck.Enqueue(neighbor);
                    }
                }
            }
        }
    }

    private void GenerateFlowField()
    {
        // Вычисление векторов направления (течения) к игроку
        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                int index = GetIndex(x, y);
                if (_costField[index] == 255)
                {
                    FlowField[index] = float3.zero;
                    continue;
                }

                int bestCost = _integrationField[index];
                int2 bestNeighbor = new int2(x, y);

                // Ищем соседа с наименьшим Integration Cost
                for (int nx = -1; nx <= 1; nx++)
                {
                    for (int ny = -1; ny <= 1; ny++)
                    {
                        if (nx == 0 && ny == 0) continue;
                        int2 neighbor = new int2(x + nx, y + ny);
                        if (!IsValid(neighbor)) continue;
                        if (_costField[GetIndex(neighbor.x, neighbor.y)] == 255) continue;

                        // ВОТ ГЛАВНЫЙ ФИКС:
                        // Если шаг диагональный — проверяем оба "срезаемых" угла.
                        // Если хотя бы один из них стена — эта диагональ запрещена.
                        if (nx != 0 && ny != 0)
                        {
                            bool cornerA = _costField[GetIndex(x + nx, y)] == 255;
                            bool cornerB = _costField[GetIndex(x, y + ny)] == 255;
                            if (cornerA || cornerB) continue; // срезать угол нельзя
                        }

                        int neighborIndex = GetIndex(neighbor.x, neighbor.y);
                        if (_integrationField[neighborIndex] < bestCost)
                        {
                            bestCost = _integrationField[neighborIndex];
                            bestNeighbor = neighbor;
                        }
                    }
                }

                // Вектор от текущей клетки к лучшему соседу
                float3 dir = new float3(bestNeighbor.x - x, 0, bestNeighbor.y - y);
                FlowField[index] = math.normalizesafe(dir);
            }
        }
    }

    public int2 WorldToCell(float3 worldPos)
    {
        float3 localPos = worldPos - WorldBottomLeft;
        return new int2(
            Mathf.Clamp(Mathf.FloorToInt(localPos.x / _cellDiameter), 0, _gridSize.x - 1),
            Mathf.Clamp(Mathf.FloorToInt(localPos.z / _cellDiameter), 0, _gridSize.y - 1)
        );
    }

    public int GetIndex(int x, int y) => x + y * _gridSize.x;
    public bool IsValid(int2 cell) => cell.x >= 0 && cell.x < _gridSize.x && cell.y >= 0 && cell.y < _gridSize.y;

    // Всегда рисуем границы сетки (даже без выделения объекта)
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position, new Vector3(GridWorldSize.x, 1f, GridWorldSize.y));
    }

    // Визуализация векторного поля в окне Scene только при выделении
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || !FlowField.IsCreated || !Target) return;

        Gizmos.color = Color.cyan;
        float3 bottomLeft = WorldBottomLeft;

        for (int x = 0; x < _gridSize.x; x++)
        {
            for (int y = 0; y < _gridSize.y; y++)
            {
                int index = GetIndex(x, y);
                float3 dir = FlowField[index];
                
                if (math.lengthsq(dir) > 0.01f)
                {
                    Vector3 cellCenter = bottomLeft + new float3(x * _cellDiameter + CellRadius, 0.5f, y * _cellDiameter + CellRadius);
                    Gizmos.DrawRay(cellCenter, (Vector3)dir * (CellRadius * 0.8f));
                    
                    // Рисуем стрелочку
                    Gizmos.DrawSphere(cellCenter + (Vector3)dir * (CellRadius * 0.8f), 0.1f);
                }
            }
        }
    }
}
