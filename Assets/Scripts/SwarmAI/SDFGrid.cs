using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System.Collections.Generic;

public class SDFGrid : MonoBehaviour
{
    [Header("3D SDF Grid")]
    public float3 GridOrigin;
    public int3 GridSize = new int3(100, 20, 100);
    public float CellSize = 0.5f;
    public LayerMask UnwalkableMask;

    public NativeArray<float3> Gradient3D;
    public NativeArray<float> Distance3D;

    private static readonly int[] s_dx = { 1, -1, 0, 0, 0, 0 };
    private static readonly int[] s_dy = { 0, 0, 1, -1, 0, 0 };
    private static readonly int[] s_dz = { 0, 0, 0, 0, 1, -1 };

    private void Awake()
    {
        FlowFieldGenerator flowField = FindAnyObjectByType<FlowFieldGenerator>();
        if (flowField != null)
        {
            GridOrigin = flowField.WorldBottomLeft;
            GridSize.x = Mathf.RoundToInt(flowField.GridWorldSize.x / CellSize);
            GridSize.y = Mathf.RoundToInt(20.0f / CellSize); // Покрываем высоту до 20 метров гарантированно!
            GridSize.z = Mathf.RoundToInt(flowField.GridWorldSize.y / CellSize);
        }
        BakeGrid();
    }

    private void BakeGrid()
    {
        int total = GridSize.x * GridSize.y * GridSize.z;
        Gradient3D = new NativeArray<float3>(total, Allocator.Persistent);
        Distance3D = new NativeArray<float>(total, Allocator.Persistent);

        var occupied = new NativeArray<bool>(total, Allocator.TempJob);
        for (int x = 0; x < GridSize.x; x++)
        for (int y = 0; y < GridSize.y; y++)
        for (int z = 0; z < GridSize.z; z++)
        {
            Vector3 center = (Vector3)(GridOrigin + new float3(
                (x + 0.5f) * CellSize,
                (y + 0.5f) * CellSize,
                (z + 0.5f) * CellSize));
            // Используем размер 0.75f для перекрытия зон, гарантируя улавливание бесконечно тонких стен Plane!
            occupied[GetIndex(x, y, z)] = Physics.CheckBox(center, new Vector3(CellSize * 0.75f, CellSize * 0.75f, CellSize * 0.75f), Quaternion.identity, UnwalkableMask);
        }

        int sliceSize = GridSize.x * GridSize.y;
        var dist = new NativeArray<float>(total, Allocator.TempJob);
        var nearestWallX = new NativeArray<int>(total, Allocator.TempJob);
        var nearestWallY = new NativeArray<int>(total, Allocator.TempJob);
        var nearestWallZ = new NativeArray<int>(total, Allocator.TempJob);
        for (int i = 0; i < total; i++)
        {
            dist[i] = float.MaxValue;
            nearestWallX[i] = -1;
        }

        // BFS outward from all wall cells (uniform edge weight = CellSize)
        var queue = new Queue<int>(total / 4);
        for (int x = 0; x < GridSize.x; x++)
        for (int y = 0; y < GridSize.y; y++)
        for (int z = 0; z < GridSize.z; z++)
        {
            int idx = GetIndex(x, y, z);
            if (!occupied[idx]) continue;
            dist[idx] = 0f;
            nearestWallX[idx] = x;
            nearestWallY[idx] = y;
            nearestWallZ[idx] = z;
            queue.Enqueue(idx);
        }

        while (queue.Count > 0)
        {
            int curIdx = queue.Dequeue();
            float curDist = dist[curIdx];
            int wx = nearestWallX[curIdx];
            int wy = nearestWallY[curIdx];
            int wz = nearestWallZ[curIdx];

            int cz = curIdx / sliceSize;
            int rem = curIdx % sliceSize;
            int cy = rem / GridSize.x;
            int cx = rem % GridSize.x;

            for (int d = 0; d < 6; d++)
            {
                int nx = cx + s_dx[d];
                int ny = cy + s_dy[d];
                int nz = cz + s_dz[d];
                if (nx < 0 || nx >= GridSize.x || ny < 0 || ny >= GridSize.y || nz < 0 || nz >= GridSize.z)
                    continue;

                int nbIdx = GetIndex(nx, ny, nz);
                float newDist = curDist + CellSize;
                if (newDist >= dist[nbIdx]) continue;

                dist[nbIdx] = newDist;
                nearestWallX[nbIdx] = wx;
                nearestWallY[nbIdx] = wy;
                nearestWallZ[nbIdx] = wz;
                queue.Enqueue(nbIdx);
            }
        }

        // BFS inward from all empty cells into occupied wall cells (uniform edge weight = CellSize)
        var distInside = new NativeArray<float>(total, Allocator.TempJob);
        var nearestEmptyX = new NativeArray<int>(total, Allocator.TempJob);
        var nearestEmptyY = new NativeArray<int>(total, Allocator.TempJob);
        var nearestEmptyZ = new NativeArray<int>(total, Allocator.TempJob);
        for (int i = 0; i < total; i++)
        {
            distInside[i] = float.MaxValue;
            nearestEmptyX[i] = -1;
        }

        var queueInside = new Queue<int>(total / 4);
        for (int x = 0; x < GridSize.x; x++)
        for (int y = 0; y < GridSize.y; y++)
        for (int z = 0; z < GridSize.z; z++)
        {
            int idx = GetIndex(x, y, z);
            if (occupied[idx]) continue;
            distInside[idx] = 0f;
            nearestEmptyX[idx] = x;
            nearestEmptyY[idx] = y;
            nearestEmptyZ[idx] = z;
            queueInside.Enqueue(idx);
        }

        while (queueInside.Count > 0)
        {
            int curIdx = queueInside.Dequeue();
            float curDist = distInside[curIdx];
            int ex = nearestEmptyX[curIdx];
            int ey = nearestEmptyY[curIdx];
            int ez = nearestEmptyZ[curIdx];

            int cz = curIdx / sliceSize;
            int rem = curIdx % sliceSize;
            int cy = rem / GridSize.x;
            int cx = rem % GridSize.x;

            for (int d = 0; d < 6; d++)
            {
                int nx = cx + s_dx[d];
                int ny = cy + s_dy[d];
                int nz = cz + s_dz[d];
                if (nx < 0 || nx >= GridSize.x || ny < 0 || ny >= GridSize.y || nz < 0 || nz >= GridSize.z)
                    continue;

                int nbIdx = GetIndex(nx, ny, nz);
                if (!occupied[nbIdx]) continue;

                float newDist = curDist + CellSize;
                if (newDist >= distInside[nbIdx]) continue;

                distInside[nbIdx] = newDist;
                nearestEmptyX[nbIdx] = ex;
                nearestEmptyY[nbIdx] = ey;
                nearestEmptyZ[nbIdx] = ez;
                queueInside.Enqueue(nbIdx);
            }
        }

        for (int x = 0; x < GridSize.x; x++)
        for (int y = 0; y < GridSize.y; y++)
        for (int z = 0; z < GridSize.z; z++)
        {
            int idx = GetIndex(x, y, z);

            if (occupied[idx])
            {
                Distance3D[idx] = -distInside[idx];

                if (nearestEmptyX[idx] >= 0)
                {
                    float3 cellPos = GridOrigin + new float3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, (z + 0.5f) * CellSize);
                    float3 emptyPos = GridOrigin + new float3((nearestEmptyX[idx] + 0.5f) * CellSize, (nearestEmptyY[idx] + 0.5f) * CellSize, (nearestEmptyZ[idx] + 0.5f) * CellSize);
                    Gradient3D[idx] = math.normalizesafe(emptyPos - cellPos);
                }
                else
                {
                    Gradient3D[idx] = new float3(0, 0, 0);
                }
            }
            else
            {
                Distance3D[idx] = dist[idx];

                if (dist[idx] > 0f && nearestWallX[idx] >= 0)
                {
                    float3 cellPos = GridOrigin + new float3((x + 0.5f) * CellSize, (y + 0.5f) * CellSize, (z + 0.5f) * CellSize);
                    float3 wallPos = GridOrigin + new float3((nearestWallX[idx] + 0.5f) * CellSize, (nearestWallY[idx] + 0.5f) * CellSize, (nearestWallZ[idx] + 0.5f) * CellSize);
                    Gradient3D[idx] = math.normalizesafe(cellPos - wallPos);
                }
                else
                {
                    Gradient3D[idx] = new float3(0, 0, 0);
                }
            }
        }

        occupied.Dispose();
        dist.Dispose();
        nearestWallX.Dispose();
        nearestWallY.Dispose();
        nearestWallZ.Dispose();

        distInside.Dispose();
        nearestEmptyX.Dispose();
        nearestEmptyY.Dispose();
        nearestEmptyZ.Dispose();
    }

    public int GetIndex(int x, int y, int z) => x + y * GridSize.x + z * GridSize.x * GridSize.y;

    private void OnDestroy()
    {
        if (Gradient3D.IsCreated) Gradient3D.Dispose();
        if (Distance3D.IsCreated) Distance3D.Dispose();
    }
}
