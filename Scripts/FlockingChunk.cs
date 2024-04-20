using System;
using System.Collections.Generic;
using NetStack.Quantization;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class FlockingChunk {
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;
    [SerializeField] private List<GrassData> data;
    
    [SerializeField] private Vector3 minBoundedRange;
    [SerializeField] private Vector3 maxBoundedRange;

    [Serializable]
    private struct GrassData {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 offset;
        public float scale;
    }

    private Dictionary<QuantizedVector3, GrassData> quantizedPoints;
    private BoundedRange[] boundedRanges;
    private Matrix4x4[] cachedMatricies;
    
    private static List<GrassData> cachedPoints;
    public const int MAX_SUBDIV = 3;
    public const float MAX_DIVISOR = (1<<MAX_SUBDIV);
    public const float MAX_TOLERANCE = 1f/MAX_DIVISOR;
    
    private GraphicsBuffer modelMatricies;
    private RenderParams renderParams;
    private MaterialPropertyBlock materialProperties;
    private LightProbeProxyVolume lightProbeVolume;
    
    public void OnEnable() {
        boundedRanges = new[] {
            new BoundedRange(minBoundedRange.x, maxBoundedRange.x, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.y, maxBoundedRange.y, MAX_TOLERANCE),
            new BoundedRange(minBoundedRange.z, maxBoundedRange.z, MAX_TOLERANCE)
        };
        OnAfterDeserialize();
        RegenerateMatricies();
    }

    public void OnDisable() {
        modelMatricies?.Release();
        modelMatricies = null;
    }

    public void SetMesh(Mesh newMesh) {
        grassMesh = newMesh;
    }
    public void SetMaterial(Material newMaterial) {
        grassMaterial = newMaterial;
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
        cachedPoints ??= new List<GrassData>();
        cachedPoints.Clear();
        if (data != null) {
            cachedPoints.AddRange(data);
        }
        quantizedPoints ??= new Dictionary<QuantizedVector3, GrassData>();
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
        quantizedPoints[quantizedPoint] = new GrassData {
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
        Graphics.RenderMeshPrimitives(renderParams, grassMesh, 0, quantizedPoints.Count);
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
        if (modelMatricies != null && modelMatricies.count < quantizedPoints.Count) {
            modelMatricies.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, quantizedPoints.Count*2, sizeof(float) * 16);
            cachedMatricies = new Matrix4x4[quantizedPoints.Count * 2];
        } else if (modelMatricies == null || !modelMatricies.IsValid()) {
            modelMatricies?.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, quantizedPoints.Count, sizeof(float) * 16);
            cachedMatricies = new Matrix4x4[quantizedPoints.Count];
        }
        Vector3 center = Vector3.Lerp(minBoundedRange,maxBoundedRange, 0.5f);
        int i = 0;
        foreach (var pair in quantizedPoints) {
            Vector3 position = pair.Value.position - center + pair.Value.offset;
            cachedMatricies[i] = Matrix4x4.TRS(position, pair.Value.rotation, Vector3.one*pair.Value.scale);
            i++;
        }
        modelMatricies.SetData(cachedMatricies);
        if (lightProbeVolume == null) {
            lightProbeVolume = new GameObject("FlockingLightProbeVolume", typeof(LightProbeProxyVolume)).GetComponent<LightProbeProxyVolume>();
        }

        //lightProbeVolume.originCustom = center;
        //lightProbeVolume.sizeCustom = maxBoundedRange - minBoundedRange;
        lightProbeVolume.transform.position = center;
        lightProbeVolume.transform.localScale = maxBoundedRange - minBoundedRange;
        materialProperties ??= new MaterialPropertyBlock();
        renderParams = new RenderParams(grassMaterial) {
            worldBounds = new Bounds(center, Vector3.one+(maxBoundedRange-minBoundedRange)),
            material = grassMaterial,
            matProps = materialProperties,
            lightProbeUsage = LightProbeUsage.UseProxyVolume,
            reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
            lightProbeProxyVolume = lightProbeVolume,
        };
        renderParams.matProps.SetBuffer("_GrassMat", modelMatricies);
    }

    private void OnDrawGizmos() {
        foreach (var pair in quantizedPoints) {
            Gizmos.DrawWireCube(pair.Value.position, Vector3.one * 0.1f);
        }
    }

    public void OnBeforeSerialize() {
        if (quantizedPoints == null || quantizedPoints.Count == 0) {
            return;
        }
        if (data == null) {
            data = new List<GrassData>(quantizedPoints.Values);
        } else {
            data.Clear();
            data.AddRange(quantizedPoints.Values);
        }
    }

    public void OnAfterDeserialize() {
        quantizedPoints ??= new Dictionary<QuantizedVector3, GrassData>();
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
        if (!quantizedPoints.TryGetValue(quantizedPoint, out GrassData data)) {
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
}
