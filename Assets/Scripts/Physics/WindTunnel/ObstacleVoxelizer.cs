using System.Collections.Generic;
using UnityEngine;

static class ObstacleVoxelizer
{
    private static readonly List<MeshFilter> MeshFilterCache = new List<MeshFilter>(128);
    private static readonly List<SkinnedMeshRenderer> SkinnedRendererCache = new List<SkinnedMeshRenderer>(32);
    private static readonly Queue<int> FloodQueue = new Queue<int>(4096);

    public static bool BuildMask(
        Transform obstacleRoot,
        Bounds domainBounds,
        int sizeX,
        int sizeY,
        int sizeZ,
        int[] mask,
        Bounds fallbackBounds,
        out int solidCellCount)
    {
        solidCellCount = 0;
        if (mask == null || mask.Length != sizeX * sizeY * sizeZ || sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
        {
            return false;
        }

        System.Array.Clear(mask, 0, mask.Length);

        if (domainBounds.size.x <= 1e-5f || domainBounds.size.y <= 1e-5f || domainBounds.size.z <= 1e-5f)
        {
            return false;
        }

        Vector3 cellSize = new Vector3(
            domainBounds.size.x / Mathf.Max(sizeX, 1),
            domainBounds.size.y / Mathf.Max(sizeY, 1),
            domainBounds.size.z / Mathf.Max(sizeZ, 1));
        float shellRadius = Mathf.Max(cellSize.magnitude * 0.30f, 0.008f);
        float shellRadiusSq = shellRadius * shellRadius;

        bool wroteShell = false;
        if (obstacleRoot != null)
        {
            wroteShell = RasterizeMeshShells(obstacleRoot, domainBounds, sizeX, sizeY, sizeZ, shellRadiusSq, mask);
        }

        if (!wroteShell && fallbackBounds.size.sqrMagnitude > 1e-6f)
        {
            MarkBoundsSolid(fallbackBounds, domainBounds, sizeX, sizeY, sizeZ, mask);
        }
        else if (wroteShell)
        {
            // Count shell cells before flood fill
            int shellCount = 0;
            for (int i = 0; i < mask.Length; i++)
                shellCount += mask[i] != 0 ? 1 : 0;

            FillInterior(mask, sizeX, sizeY, sizeZ);
            Dilate(mask, sizeX, sizeY, sizeZ);

            // Safety: if flood fill added almost nothing (leak), fall back to bounding box
            int afterFill = 0;
            for (int i = 0; i < mask.Length; i++)
                afterFill += mask[i] != 0 ? 1 : 0;

            if (afterFill < shellCount * 2 && fallbackBounds.size.sqrMagnitude > 1e-6f)
            {
                // Flood fill leaked — use bounding box as a solid block instead
                System.Array.Clear(mask, 0, mask.Length);
                MarkBoundsSolid(fallbackBounds, domainBounds, sizeX, sizeY, sizeZ, mask);
            }
        }

        for (int i = 0; i < mask.Length; i++)
        {
            solidCellCount += mask[i] != 0 ? 1 : 0;
        }

        return solidCellCount > 0;
    }

    /// <summary>
    /// Builds an inverted mask for internal flow: cells OUTSIDE the mesh = wall (1),
    /// cells INSIDE the mesh = fluid (0). Pipe openings (interior cells touching
    /// domain boundaries) are detected and returned via openingMask.
    /// openingMask values: 0 = not opening, positive = opening group ID.
    /// </summary>
    public static bool BuildInvertedMask(
        Transform obstacleRoot,
        Bounds domainBounds,
        int sizeX,
        int sizeY,
        int sizeZ,
        int[] mask,
        int[] openingMask,
        List<AeroFlow.Sim3D.PipeFlow.DetectedOpening> openings,
        out int fluidCellCount,
        out int openingCount)
    {
        fluidCellCount = 0;
        openingCount = 0;
        if (mask == null || mask.Length != sizeX * sizeY * sizeZ || sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
            return false;

        System.Array.Clear(mask, 0, mask.Length);
        if (openingMask != null && openingMask.Length == mask.Length)
            System.Array.Clear(openingMask, 0, openingMask.Length);

        if (domainBounds.size.x <= 1e-5f || domainBounds.size.y <= 1e-5f || domainBounds.size.z <= 1e-5f)
            return false;

        Vector3 cellSize = new Vector3(
            domainBounds.size.x / Mathf.Max(sizeX, 1),
            domainBounds.size.y / Mathf.Max(sizeY, 1),
            domainBounds.size.z / Mathf.Max(sizeZ, 1));
        float shellRadius = Mathf.Max(cellSize.magnitude * 0.30f, 0.008f);
        float shellRadiusSq = shellRadius * shellRadius;

        // Step 1: Rasterize the mesh surface as shell (1)
        bool wroteShell = false;
        if (obstacleRoot != null)
        {
            wroteShell = RasterizeMeshShells(obstacleRoot, domainBounds, sizeX, sizeY, sizeZ, shellRadiusSq, mask);
        }
        if (!wroteShell) return false;

        // Step 2: Seed-based flood-fill to find Interior fluid cells
        // We use the openings as seeds, projected slightly inside the domain
        bool[] inside = new bool[mask.Length];
        FloodQueue.Clear();

        void EnqueueIfFluid(int x, int y, int z)
        {
            if (x < 0 || x >= sizeX || y < 0 || y >= sizeY || z < 0 || z >= sizeZ) return;
            int idx = Flatten(x, y, z, sizeX, sizeY);
            if (mask[idx] != 0 || inside[idx]) return;
            inside[idx] = true;
            FloodQueue.Enqueue(idx);
        }

        if (openings != null && openings.Count > 0)
        {
            foreach (var op in openings)
            {
                // Move centroid slightly 'inside' along the inverse normal
                Vector3 seedWorld = op.centroid - op.normal * cellSize.magnitude * 1.1f;
                int sx = WorldToGrid(seedWorld.x, domainBounds.min.x, domainBounds.size.x, sizeX);
                int sy = WorldToGrid(seedWorld.y, domainBounds.min.y, domainBounds.size.y, sizeY);
                int sz = WorldToGrid(seedWorld.z, domainBounds.min.z, domainBounds.size.z, sizeZ);
                EnqueueIfFluid(sx, sy, sz);
            }
        }
        else
        {
            // Fallback: seed from domain center
            EnqueueIfFluid(sizeX / 2, sizeY / 2, sizeZ / 2);
        }

        while (FloodQueue.Count > 0)
        {
            int idx = FloodQueue.Dequeue();
            int x = idx % sizeX;
            int yz = idx / sizeX;
            int y = yz % sizeY;
            int z = yz / sizeY;

            EnqueueIfFluid(x - 1, y, z);
            EnqueueIfFluid(x + 1, y, z);
            EnqueueIfFluid(x, y - 1, z);
            EnqueueIfFluid(x, y + 1, z);
            EnqueueIfFluid(x, y, z - 1);
            EnqueueIfFluid(x, y, z + 1);
        }

        // Step 3: Finalize mask — everything NOT 'inside' is wall (1)
        for (int i = 0; i < mask.Length; i++)
        {
            bool isFluid = inside[i];
            mask[i] = isFluid ? 0 : 1;
            if (isFluid) fluidCellCount++;
        }

        // Step 4: Detect openings — interior cells on domain boundary faces
        if (openingMask != null && openingMask.Length == mask.Length)
        {
            int currentOpeningId = 0;
            bool[] visited = new bool[mask.Length];

            for (int i = 0; i < mask.Length; i++)
            {
                if (mask[i] != 0 || visited[i]) continue;
                int x = i % sizeX;
                int yz = i / sizeX;
                int y = yz % sizeY;
                int z = yz / sizeY;

                bool onBound = (x <= 1 || x >= sizeX - 2 || y <= 1 || y >= sizeY - 2 || z <= 1 || z >= sizeZ - 2);
                if (!onBound) continue;

                // Start a new opening group via flood fill on boundary-connected fluid cells
                currentOpeningId++;
                FloodQueue.Clear();
                visited[i] = true;
                FloodQueue.Enqueue(i);
                openingMask[i] = currentOpeningId;

                while (FloodQueue.Count > 0)
                {
                    int ci = FloodQueue.Dequeue();
                    int cx = ci % sizeX;
                    int cyz = ci / sizeX;
                    int cy = cyz % sizeY;
                    int cz = cyz / sizeY;

                    int[] dx = { -1, 1, 0, 0, 0, 0 };
                    int[] dy = { 0, 0, -1, 1, 0, 0 };
                    int[] dz = { 0, 0, 0, 0, -1, 1 };
                    for (int d = 0; d < 6; d++)
                    {
                        int nx = cx + dx[d], ny = cy + dy[d], nz = cz + dz[d];
                        if (nx < 0 || nx >= sizeX || ny < 0 || ny >= sizeY || nz < 0 || nz >= sizeZ) continue;
                        int ni = Flatten(nx, ny, nz, sizeX, sizeY);
                        if (visited[ni] || mask[ni] != 0) continue;
                        bool nOnBound = (nx <= 1 || nx >= sizeX - 2 || ny <= 1 || ny >= sizeY - 2 || nz <= 1 || nz >= sizeZ - 2);
                        if (!nOnBound) continue;
                        visited[ni] = true;
                        openingMask[ni] = currentOpeningId;
                        FloodQueue.Enqueue(ni);
                    }
                }
            }
            openingCount = currentOpeningId;
        }

        return fluidCellCount > 0;
    }

    private static bool RasterizeMeshShells(
        Transform obstacleRoot,
        Bounds domainBounds,
        int sizeX,
        int sizeY,
        int sizeZ,
        float shellRadiusSq,
        int[] mask)
    {
        bool wroteShell = false;

        MeshFilterCache.Clear();
        obstacleRoot.GetComponentsInChildren(true, MeshFilterCache);
        for (int i = 0; i < MeshFilterCache.Count; i++)
        {
            MeshFilter filter = MeshFilterCache[i];
            if (filter == null || filter.sharedMesh == null) continue;

            wroteShell |= RasterizeMesh(
                filter.sharedMesh,
                filter.transform.localToWorldMatrix,
                domainBounds,
                sizeX,
                sizeY,
                sizeZ,
                shellRadiusSq,
                mask);
        }

        SkinnedRendererCache.Clear();
        obstacleRoot.GetComponentsInChildren(true, SkinnedRendererCache);
        for (int i = 0; i < SkinnedRendererCache.Count; i++)
        {
            SkinnedMeshRenderer skinned = SkinnedRendererCache[i];
            if (skinned == null || skinned.sharedMesh == null) continue;

            Mesh bakedMesh = new Mesh();
            skinned.BakeMesh(bakedMesh, true);
            wroteShell |= RasterizeMesh(
                bakedMesh,
                skinned.transform.localToWorldMatrix,
                domainBounds,
                sizeX,
                sizeY,
                sizeZ,
                shellRadiusSq,
                mask);
            Object.Destroy(bakedMesh);
        }

        return wroteShell;
    }

    private static bool RasterizeMesh(
        Mesh mesh,
        Matrix4x4 localToWorld,
        Bounds domainBounds,
        int sizeX,
        int sizeY,
        int sizeZ,
        float shellRadiusSq,
        int[] mask)
    {
        Vector3[] vertices = mesh.vertices;
        if (vertices == null || vertices.Length == 0)
        {
            return false;
        }

        Vector3 minDomain = domainBounds.min;
        Vector3 maxDomain = domainBounds.max;
        Vector3 cellSize = new Vector3(
            domainBounds.size.x / Mathf.Max(sizeX, 1),
            domainBounds.size.y / Mathf.Max(sizeY, 1),
            domainBounds.size.z / Mathf.Max(sizeZ, 1));
        Vector3 expand = cellSize * 1.25f;
        bool wrote = false;

        int triangleCount = CountTriangleCount(mesh);
        if (triangleCount <= 0)
        {
            return false;
        }

        int stride = Mathf.Max(1, triangleCount / 50000);
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
            {
                continue;
            }

            int[] triangles = mesh.GetIndices(subMesh);
            if (triangles == null || triangles.Length < 3)
            {
                continue;
            }

            for (int tri = 0; tri < triangles.Length; tri += 3 * stride)
            {
                int ia = triangles[tri];
                int ib = triangles[tri + 1];
                int ic = triangles[tri + 2];
                if (ia < 0 || ib < 0 || ic < 0 || ia >= vertices.Length || ib >= vertices.Length || ic >= vertices.Length)
                {
                    continue;
                }

                Vector3 a = localToWorld.MultiplyPoint3x4(vertices[ia]);
                Vector3 b = localToWorld.MultiplyPoint3x4(vertices[ib]);
                Vector3 c = localToWorld.MultiplyPoint3x4(vertices[ic]);

                Bounds triBounds = new Bounds(a, Vector3.zero);
                triBounds.Encapsulate(b);
                triBounds.Encapsulate(c);
                triBounds.Expand(expand);

                if (!triBounds.Intersects(domainBounds))
                {
                    continue;
                }

                Vector3 triMin = Vector3.Max(triBounds.min, minDomain);
                Vector3 triMax = Vector3.Min(triBounds.max, maxDomain);
                int minX = WorldToGrid(triMin.x, minDomain.x, domainBounds.size.x, sizeX);
                int maxX = WorldToGrid(triMax.x, minDomain.x, domainBounds.size.x, sizeX);
                int minY = WorldToGrid(triMin.y, minDomain.y, domainBounds.size.y, sizeY);
                int maxY = WorldToGrid(triMax.y, minDomain.y, domainBounds.size.y, sizeY);
                int minZ = WorldToGrid(triMin.z, minDomain.z, domainBounds.size.z, sizeZ);
                int maxZ = WorldToGrid(triMax.z, minDomain.z, domainBounds.size.z, sizeZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    float worldZ = CellCenter(z, minDomain.z, domainBounds.size.z, sizeZ);
                    for (int y = minY; y <= maxY; y++)
                    {
                        float worldY = CellCenter(y, minDomain.y, domainBounds.size.y, sizeY);
                        for (int x = minX; x <= maxX; x++)
                        {
                            int idx = Flatten(x, y, z, sizeX, sizeY);
                            if (mask[idx] != 0) continue;

                            Vector3 point = new Vector3(
                                CellCenter(x, minDomain.x, domainBounds.size.x, sizeX),
                                worldY,
                                worldZ);

                            if (SqDistPointTriangle(point, a, b, c) <= shellRadiusSq)
                            {
                                mask[idx] = 1;
                                wrote = true;
                            }
                        }
                    }
                }
            }
        }

        return wrote;
    }

    private static int CountTriangleCount(Mesh mesh)
    {
        if (mesh == null) return 0;

        int total = 0;
        for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
            total += (int)mesh.GetIndexCount(subMesh) / 3;
        }

        return total;
    }

    private static void MarkBoundsSolid(Bounds obstacleBounds, Bounds domainBounds, int sizeX, int sizeY, int sizeZ, int[] mask)
    {
        Bounds clipped = obstacleBounds;
        clipped.SetMinMax(Vector3.Max(obstacleBounds.min, domainBounds.min), Vector3.Min(obstacleBounds.max, domainBounds.max));
        if (clipped.size.x <= 1e-5f || clipped.size.y <= 1e-5f || clipped.size.z <= 1e-5f)
        {
            return;
        }

        int minX = WorldToGrid(clipped.min.x, domainBounds.min.x, domainBounds.size.x, sizeX);
        int maxX = WorldToGrid(clipped.max.x, domainBounds.min.x, domainBounds.size.x, sizeX);
        int minY = WorldToGrid(clipped.min.y, domainBounds.min.y, domainBounds.size.y, sizeY);
        int maxY = WorldToGrid(clipped.max.y, domainBounds.min.y, domainBounds.size.y, sizeY);
        int minZ = WorldToGrid(clipped.min.z, domainBounds.min.z, domainBounds.size.z, sizeZ);
        int maxZ = WorldToGrid(clipped.max.z, domainBounds.min.z, domainBounds.size.z, sizeZ);

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    mask[Flatten(x, y, z, sizeX, sizeY)] = 1;
                }
            }
        }
    }

    private static void FillInterior(int[] mask, int sizeX, int sizeY, int sizeZ)
    {
        bool[] outside = new bool[mask.Length];
        FloodQueue.Clear();

        void EnqueueIfOpen(int x, int y, int z)
        {
            int idx = Flatten(x, y, z, sizeX, sizeY);
            if (mask[idx] != 0 || outside[idx]) return;
            outside[idx] = true;
            FloodQueue.Enqueue(idx);
        }

        for (int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                EnqueueIfOpen(x, y, 0);
                EnqueueIfOpen(x, y, sizeZ - 1);
            }
        }
        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                EnqueueIfOpen(x, 0, z);
                EnqueueIfOpen(x, sizeY - 1, z);
            }
        }
        for (int y = 0; y < sizeY; y++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                EnqueueIfOpen(0, y, z);
                EnqueueIfOpen(sizeX - 1, y, z);
            }
        }

        while (FloodQueue.Count > 0)
        {
            int idx = FloodQueue.Dequeue();
            int x = idx % sizeX;
            int yz = idx / sizeX;
            int y = yz % sizeY;
            int z = yz / sizeY;

            if (x > 0) EnqueueIfOpen(x - 1, y, z);
            if (x + 1 < sizeX) EnqueueIfOpen(x + 1, y, z);
            if (y > 0) EnqueueIfOpen(x, y - 1, z);
            if (y + 1 < sizeY) EnqueueIfOpen(x, y + 1, z);
            if (z > 0) EnqueueIfOpen(x, y, z - 1);
            if (z + 1 < sizeZ) EnqueueIfOpen(x, y, z + 1);
        }

        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0 && !outside[i])
            {
                mask[i] = 1;
            }
        }
    }

    private static void Dilate(int[] mask, int sizeX, int sizeY, int sizeZ)
    {
        int[] copy = new int[mask.Length];
        System.Array.Copy(mask, copy, mask.Length);

        for (int z = 0; z < sizeZ; z++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int idx = Flatten(x, y, z, sizeX, sizeY);
                    if (copy[idx] == 0) continue;
                    mask[idx] = 1;
                    if (x > 0) mask[Flatten(x - 1, y, z, sizeX, sizeY)] = 1;
                    if (x + 1 < sizeX) mask[Flatten(x + 1, y, z, sizeX, sizeY)] = 1;
                    if (y > 0) mask[Flatten(x, y - 1, z, sizeX, sizeY)] = 1;
                    if (y + 1 < sizeY) mask[Flatten(x, y + 1, z, sizeX, sizeY)] = 1;
                    if (z > 0) mask[Flatten(x, y, z - 1, sizeX, sizeY)] = 1;
                    if (z + 1 < sizeZ) mask[Flatten(x, y, z + 1, sizeX, sizeY)] = 1;
                }
            }
        }
    }

    private static int WorldToGrid(float coord, float min, float size, int resolution)
    {
        float denom = Mathf.Max(size, 1e-5f);
        float uv = Mathf.Clamp01((coord - min) / denom);
        int cell = Mathf.FloorToInt(uv * Mathf.Max(resolution - 1, 0));
        return Mathf.Clamp(cell, 0, Mathf.Max(resolution - 1, 0));
    }

    private static float CellCenter(int index, float min, float size, int resolution)
    {
        float cellSize = size / Mathf.Max(resolution, 1);
        return min + (index + 0.5f) * cellSize;
    }

    private static int Flatten(int x, int y, int z, int sizeX, int sizeY)
    {
        return x + sizeX * (y + sizeY * z);
    }

    private static float SqDistPointTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        Vector3 ap = point - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return (point - a).sqrMagnitude;

        Vector3 bp = point - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return (point - b).sqrMagnitude;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / Mathf.Max(d1 - d3, 1e-6f);
            Vector3 projection = a + v * ab;
            return (point - projection).sqrMagnitude;
        }

        Vector3 cp = point - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return (point - c).sqrMagnitude;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / Mathf.Max(d2 - d6, 1e-6f);
            Vector3 projection = a + w * ac;
            return (point - projection).sqrMagnitude;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            Vector3 bc = c - b;
            float w = (d4 - d3) / Mathf.Max((d4 - d3) + (d5 - d6), 1e-6f);
            Vector3 projection = b + w * bc;
            return (point - projection).sqrMagnitude;
        }

        Vector3 normal = Vector3.Cross(ab, ac);
        float normalMag = normal.magnitude;
        if (normalMag <= 1e-6f)
        {
            return (point - a).sqrMagnitude;
        }

        normal /= normalMag;
        float distance = Mathf.Abs(Vector3.Dot(point - a, normal));
        return distance * distance;
    }
    public static bool BuildVelocityField(
        AeroFlow.Core.PartRegistry registry,
        Bounds domainBounds,
        int sizeX,
        int sizeY,
        int sizeZ,
        int[] mask,
        Vector3[] velocityField)
    {
        if (mask == null || velocityField == null || mask.Length != velocityField.Length) return false;
        System.Array.Clear(velocityField, 0, velocityField.Length);

        if (registry == null) return false;

        Vector3 minDomain = domainBounds.min;
        Vector3 cellSize = new Vector3(
            domainBounds.size.x / Mathf.Max(sizeX, 1),
            domainBounds.size.y / Mathf.Max(sizeY, 1),
            domainBounds.size.z / Mathf.Max(sizeZ, 1));

        var parts = registry.GetParts();
        foreach (var part in parts)
        {
            if (part.motionSettings == null || part.motionSettings.motionType == AeroFlow.Core.PartMotionType.Static)
                continue;

            // For each cell, if it's marked as solid in the mask, check if it's within this part's bounds.
            // This is a simpler heuristic than re-rasterizing everything.
            // A more accurate way would be to check the Mesh of the part.
            
            // Collect all renderers for this part
            MeshFilterCache.Clear();
            part.partTransform.GetComponentsInChildren(true, MeshFilterCache);

            foreach (var filter in MeshFilterCache)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                
                // Get world bounds of this mesh to prune search
                Bounds meshBounds = filter.sharedMesh.bounds;
                meshBounds = TransformBounds(meshBounds, filter.transform.localToWorldMatrix);
                
                if (!meshBounds.Intersects(domainBounds)) continue;

                Bounds clipped = meshBounds;
                clipped.SetMinMax(Vector3.Max(meshBounds.min, domainBounds.min), Vector3.Min(meshBounds.max, domainBounds.max));

                int minX = WorldToGrid(clipped.min.x, minDomain.x, domainBounds.size.x, sizeX);
                int maxX = WorldToGrid(clipped.max.x, minDomain.x, domainBounds.size.x, sizeX);
                int minY = WorldToGrid(clipped.min.y, minDomain.y, domainBounds.size.y, sizeY);
                int maxY = WorldToGrid(clipped.max.y, minDomain.y, domainBounds.size.y, sizeY);
                int minZ = WorldToGrid(clipped.min.z, minDomain.z, domainBounds.size.z, sizeZ);
                int maxZ = WorldToGrid(clipped.max.z, minDomain.z, domainBounds.size.z, sizeZ);

                for (int z = minZ; z <= maxZ; z++)
                {
                    float worldZ = CellCenter(z, minDomain.z, domainBounds.size.z, sizeZ);
                    for (int y = minY; y <= maxY; y++)
                    {
                        float worldY = CellCenter(y, minDomain.y, domainBounds.size.y, sizeY);
                        for (int x = minX; x <= maxX; x++)
                        {
                            int idx = Flatten(x, y, z, sizeX, sizeY);
                            if (mask[idx] == 0) continue;

                            Vector3 point = new Vector3(
                                CellCenter(x, minDomain.x, domainBounds.size.x, sizeX),
                                worldY,
                                worldZ);

                            // We assign velocity if the point is inside the mask.
                            // To be perfect, we should check if 'point' is inside THIS specific part's mesh.
                            // But since the mask is already built, and parts usually don't overlap much,
                            // we'll use a simple bounds check + mask check.
                            if (meshBounds.Contains(point))
                            {
                                velocityField[idx] = part.motionSettings.GetWorldVelocityAtPoint(point);
                            }
                        }
                    }
                }
            }
        }

        return true;
    }

    private static Bounds TransformBounds(Bounds localBounds, Matrix4x4 l2w)
    {
        Vector3 center = l2w.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = l2w.MultiplyVector(new Vector3(extents.x, 0, 0));
        Vector3 axisY = l2w.MultiplyVector(new Vector3(0, extents.y, 0));
        Vector3 axisZ = l2w.MultiplyVector(new Vector3(0, 0, extents.z));
        extents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        return new Bounds(center, extents * 2f);
    }
}
