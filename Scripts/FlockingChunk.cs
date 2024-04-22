using System;
using System.Collections.Generic;
using NetStack.Quantization;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class FlockingChunk {
    [SerializeField]
    private FoliagePack foliagePack;
    
    [SerializeField] private List<CPUFoliage> data;
    
    [SerializeField] private Vector3 minBoundedRange;
    [SerializeField] private Vector3 maxBoundedRange;

    [Serializable]
    private struct CPUFoliage {
        public int index;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 offset;
        public float scale;
    }

    private struct GPUFoliage {
        public Matrix4x4 matrix;
        public int meshIndex;
    }

    private Dictionary<QuantizedVector3, CPUFoliage> quantizedPoints;
    private BoundedRange[] boundedRanges;
    private GPUFoliage[] cachedMatricies;
    
    private static List<CPUFoliage> cachedPoints;
    public const int MAX_SUBDIV = 3;
    public const float MAX_DIVISOR = (1<<MAX_SUBDIV);
    public const float MAX_TOLERANCE = 1f/MAX_DIVISOR;
    
    private GraphicsBuffer meshTriangles;
    private GraphicsBuffer meshPositions;
    private GraphicsBuffer meshNormals;
    private GraphicsBuffer meshUVs;
    
    private GraphicsBuffer foliageChunks;
    private MaterialPropertyBlock materialPropertyBlock;
    
    private RenderParams renderParams;
    private LightProbeProxyVolume lightProbeVolume;
    
    public void OnEnable() {
        boundedRanges = new[] {
            new BoundedRange(minBoundedRange.x, maxBoundedRange.x, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.y, maxBoundedRange.y, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.z, maxBoundedRange.z, MAX_TOLERANCE)
        };
        OnAfterDeserialize();
        SetFoliagePack(foliagePack);
        RegenerateMatricies();
    }

    public void OnDisable() {
        foliageChunks?.Release();
        foliageChunks = null;
    }

    public void SetFoliagePack(FoliagePack pack) {
        int? vertexCount = null;
        foreach (var foliage in pack.foliages) {
            var mesh = foliage.mesh;
            vertexCount ??= (int)mesh.GetIndexCount(0);
            if (vertexCount.Value != (int)mesh.GetIndexCount(0)) {
                throw new UnityException("Cannot use meshes with differing triangle counts.");
            }
        }

        foliagePack = pack;
        
        List<int> triangles = new List<int>();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        List<int> cachedTriangles = new List<int>();
        List<Vector3> cachedVertices = new List<Vector3>();
        List<Vector3> cachedNormals = new List<Vector3>();
        List<Vector2> cachedUvs = new List<Vector2>();
        foreach (var foliage in pack.foliages) {
            var mesh = foliage.mesh;
            
            cachedTriangles.Clear();
            cachedVertices.Clear();
            cachedNormals.Clear();
            cachedUvs.Clear();
            
            mesh.GetTriangles(cachedTriangles,0);
            mesh.GetVertices(cachedVertices);
            mesh.GetNormals(cachedNormals);
            mesh.GetUVs(0,cachedUvs);
            int offset = vertices.Count;
            for (int i = 0; i < cachedTriangles.Count; i++) {
                triangles.Add(cachedTriangles[i] + offset);
            }
            vertices.AddRange(cachedVertices);
            uvs.AddRange(cachedUvs);
            normals.AddRange(cachedNormals);
        }

        if (triangles.Count == 0) {
            throw new UnityException("Can't make grass without meshes...");
        }

        meshTriangles?.Release();
        meshPositions?.Release();
        meshNormals?.Release();
        meshUVs?.Release();
        
        meshTriangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangles.Count, sizeof(int));
        meshPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Count, sizeof(float)*3);
        meshNormals = new GraphicsBuffer(GraphicsBuffer.Target.Structured, normals.Count, sizeof(float)*3);
        meshUVs = new GraphicsBuffer(GraphicsBuffer.Target.Structured, uvs.Count, sizeof(float)*2);
        
        meshTriangles.SetData(triangles);
        meshPositions.SetData(vertices);
        meshNormals.SetData(normals);
        meshUVs.SetData(uvs);

        materialPropertyBlock ??= new MaterialPropertyBlock();
        materialPropertyBlock.SetBuffer("_GrassTriangles", meshTriangles);
        materialPropertyBlock.SetBuffer("_GrassPositions", meshPositions);
        materialPropertyBlock.SetBuffer("_GrassNormals", meshNormals);
        materialPropertyBlock.SetBuffer("_GrassUVs", meshUVs);
        materialPropertyBlock.SetInt("_GrassIndexCount", (int)foliagePack.foliages[0].mesh.GetIndexCount(0));
    }
    public bool ContainsPoint(Vector3 check) {
        return check.x >= minBoundedRange.x && check.x <= maxBoundedRange.x &&
               check.y >= minBoundedRange.y && check.y <= maxBoundedRange.y &&
               check.z >= minBoundedRange.z && check.z <= maxBoundedRange.z;
    }

    public void SetBoundRanges(Vector3 newMinBoundedRange, Vector3 newMaxBoundedRange) {
        minBoundedRange = newMinBoundedRange;
        maxBoundedRange = newMaxBoundedRange;
        boundedRanges = new[] {
            new BoundedRange(minBoundedRange.x, maxBoundedRange.x, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.y, maxBoundedRange.y, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.z, maxBoundedRange.z, MAX_TOLERANCE)
        };
        cachedPoints ??= new List<CPUFoliage>();
        cachedPoints.Clear();
        if (data != null) {
            cachedPoints.AddRange(data);
        }
        quantizedPoints ??= new Dictionary<QuantizedVector3, CPUFoliage>();
        quantizedPoints.Clear();
        foreach (var d in cachedPoints) {
            quantizedPoints[BoundedRange.Quantize(d.position, boundedRanges)] = d;
        }
    }
    public void SetScale(Vector3 point, Vector3 newScale) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        if (!quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        var d= quantizedPoints[quantizedPoint];
        d.scale = Mathf.Max(newScale.x,0f);
        quantizedPoints[quantizedPoint] = d;
    }

    public void AddPoint(Vector3 point, Vector3 normal, Vector3 scale, Vector3 offset, float rotation) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        bool shouldRender = !quantizedPoints.ContainsKey(quantizedPoint);
        quantizedPoints[quantizedPoint] = new CPUFoliage {
            position = BoundedRange.Dequantize(quantizedPoint, boundedRanges),
            rotation = Quaternion.FromToRotation(Vector3.forward, normal) * Quaternion.AngleAxis(rotation, Vector3.forward),
            scale = scale.magnitude,
            offset = offset,
        };
        if (shouldRender) {
            RegenerateMatricies();
            Render();
        }
    }

    public void Render() {
        if ((data?.Count ?? 0) == 0) {
            return;
        }
        if (renderParams.matProps == null) {
            return;
        }

        int indexCount = (int)foliagePack.foliages[0].mesh.GetIndexCount(0);
        Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, indexCount, quantizedPoints.Count);
    }

    public void RemovePoint(Vector3 point) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        if (!quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        quantizedPoints.Remove(quantizedPoint);
        RegenerateMatricies();
        Render();
    }

    public void RegenerateMatricies() {
        if (quantizedPoints.Count == 0) {
            return;
        }
        if (foliageChunks != null && foliageChunks.count < quantizedPoints.Count) {
            foliageChunks.Release();
            foliageChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, quantizedPoints.Count*2, sizeof(float) * 16 + sizeof(int));
            cachedMatricies = new GPUFoliage[quantizedPoints.Count * 2];
        } else if (foliageChunks == null || !foliageChunks.IsValid()) {
            foliageChunks?.Release();
            foliageChunks = new GraphicsBuffer(GraphicsBuffer.Target.Structured, quantizedPoints.Count, sizeof(float) * 16 + sizeof(int));
            cachedMatricies = new GPUFoliage[quantizedPoints.Count];
        }
        Vector3 center = Vector3.Lerp(minBoundedRange,maxBoundedRange, 0.5f);
        int i = 0;
        foreach (var pair in quantizedPoints) {
            Vector3 position = pair.Value.position + pair.Value.offset;
            cachedMatricies[i] = new GPUFoliage() {
                matrix = Matrix4x4.TRS(position, pair.Value.rotation, Vector3.one*pair.Value.scale),
                meshIndex = pair.Value.index,
            };
            i++;
        }
        foliageChunks.SetData(cachedMatricies);
        if (lightProbeVolume == null) {
            lightProbeVolume = new GameObject("FlockingLightProbeVolume", typeof(LightProbeProxyVolume)).GetComponent<LightProbeProxyVolume>();
        }

        //lightProbeVolume.originCustom = center;
        //lightProbeVolume.sizeCustom = maxBoundedRange - minBoundedRange;
        lightProbeVolume.transform.position = center;
        lightProbeVolume.transform.localScale = maxBoundedRange - minBoundedRange;
        materialPropertyBlock ??= new MaterialPropertyBlock();
        materialPropertyBlock.SetBuffer("_GrassMat", foliageChunks);
        renderParams = new RenderParams(foliagePack.material) {
            worldBounds = new Bounds(center, Vector3.one+(maxBoundedRange-minBoundedRange)),
            material = foliagePack.material,
            matProps = materialPropertyBlock,
            lightProbeUsage = LightProbeUsage.UseProxyVolume,
            reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
            lightProbeProxyVolume = lightProbeVolume,
        };
    }

    public void OnDrawGizmos() {
        foreach (var pair in quantizedPoints) {
            Gizmos.DrawWireCube(pair.Value.position+pair.Value.offset, Vector3.one * 0.1f);
        }
    }

    public void OnBeforeSerialize() {
        if (quantizedPoints == null || quantizedPoints.Count == 0) {
            return;
        }
        if (data == null) {
            data = new List<CPUFoliage>(quantizedPoints.Values);
        } else {
            data.Clear();
            data.AddRange(quantizedPoints.Values);
        }
    }

    public void OnAfterDeserialize() {
        quantizedPoints ??= new Dictionary<QuantizedVector3, CPUFoliage>();
        quantizedPoints.Clear();
        boundedRanges = new[] {
            new BoundedRange(minBoundedRange.x, maxBoundedRange.x, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.y, maxBoundedRange.y, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.z, maxBoundedRange.z, MAX_TOLERANCE)
        };
        foreach (var d in data) {
            quantizedPoints[BoundedRange.Quantize(d.position, boundedRanges)] = d;
        }
    }

    public Vector3? GetScale(Vector3 point) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        if (!quantizedPoints.TryGetValue(quantizedPoint, out CPUFoliage data)) {
            return null;
        }
        return Vector3.one*data.scale;
    }

    public void SetPointRotation(Vector3 point, Vector3 up, Vector3 forward, float influence) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        if (!quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        Quaternion look = Quaternion.LookRotation(up, forward);
        var d= quantizedPoints[quantizedPoint];
        d.rotation = Quaternion.Lerp(d.rotation,look, influence);
        quantizedPoints[quantizedPoint] = d;
    }

    public void SetPointIndex(Vector3 point, int index) {
        var quantizedPoint = BoundedRange.Quantize(point, boundedRanges);
        if (!quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        var d= quantizedPoints[quantizedPoint];
        d.index = index;
        quantizedPoints[quantizedPoint] = d;
    }
}
