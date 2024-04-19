using System;
using System.Collections.Generic;
using NetStack.Quantization;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FlockingData : MonoBehaviour, ISerializationCallbackReceiver {
    [Serializable]
    private class SerializedFlockingData {
        [SerializeField] public List<GrassData> grassDatas;
    }

    [SerializeField]
    private Mesh grassMesh;
    [SerializeField]
    private Material grassMaterial;
    
    [SerializeField] private SerializedFlockingData serializedData;

    [Serializable]
    private struct GrassData {
        public Vector3 position;
        public Vector3 normal;
        public float scale;
        public float rotation;
    }

    private Dictionary<QuantizedVector3, GrassData> quantizedPoints;
    private BoundedRange[] boundedRanges;
    private Matrix4x4[] cachedMatricies;
    
    private static FlockingData instance;
    private static List<GrassData> cachedPoints;
    private const int MAX_SUBDIV = 3;
    private const float MAX_TOLERANCE = 1f/(1<<MAX_SUBDIV);
    
    private Mesh currentMesh;
    private GraphicsBuffer modelMatricies;
    private RenderParams renderParams;
    private static int undoGroup = -1;
    private MaterialPropertyBlock materialProperties;
    
    private void OnEnable() {
        if (instance == null || instance == this) {
            instance = this;
            OnAfterDeserialize();
            RegenerateMatricies();
        } else {
            if (Application.isPlaying) {
                Destroy(this);
            } else {
                DestroyImmediate(this);
            }
        }
#if UNITY_EDITOR
        Undo.undoRedoEvent += OnUndoRedoEvent;
#endif
    }

#if UNITY_EDITOR
    private void OnUndoRedoEvent(in UndoRedoInfo undo) {
        if (undo.undoName.Contains(nameof(FlockingData))) {
            OnAfterDeserialize();
        }
    }
#endif

    private void OnDisable() {
        modelMatricies?.Release();
        modelMatricies = null;
#if UNITY_EDITOR
        Undo.undoRedoEvent -= OnUndoRedoEvent;
#endif
    }

    private void SetBoundRanges(BoundedRange[] newRanges) {
        cachedPoints ??= new List<GrassData>();
        cachedPoints.Clear();
        cachedPoints.AddRange(serializedData.grassDatas);
        quantizedPoints ??= new Dictionary<QuantizedVector3, GrassData>();
        quantizedPoints.Clear();
        foreach (var data in cachedPoints) {
            quantizedPoints[BoundedRange.Quantize(data.position, newRanges)] = data;
        }
        boundedRanges = newRanges;
    }

    private void Update() {
        Render();
    }

    public static void ScalePoint(Vector3 point, float scaleMultiplier) {
        var quantizedPoint = BoundedRange.Quantize(point, instance.boundedRanges);
        if (!instance.quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        var data = instance.quantizedPoints[quantizedPoint];
        data.scale *= scaleMultiplier;
        instance.quantizedPoints[quantizedPoint] = data;
    }

    public static void AddPoint(Vector3 point, Vector3 normal, Vector3 scale, float rotation) {
        if (point.x <= instance.boundedRanges[0].GetMinValue() ||
            point.y <= instance.boundedRanges[1].GetMinValue() ||
            point.z <= instance.boundedRanges[2].GetMinValue() ||
            point.x >= instance.boundedRanges[0].GetMaxValue() ||
            point.y >= instance.boundedRanges[1].GetMaxValue() ||
            point.z >= instance.boundedRanges[2].GetMaxValue()) {
            var newRanges = GetBoundedRanges(new List<GrassData>(instance.quantizedPoints.Values), point);
            instance.SetBoundRanges(newRanges);
        }
        var quantizedPoint = BoundedRange.Quantize(point, instance.boundedRanges);
        instance.quantizedPoints[quantizedPoint] = new GrassData {
            position = BoundedRange.Dequantize(quantizedPoint, instance.boundedRanges),
            scale = scale.magnitude,
            normal = normal,
            rotation = rotation,
        };
    }

    public static void Regenerate() {
        instance.RegenerateMatricies();
    }

    private void Render() {
        if (serializedData.grassDatas.Count == 0) {
            return;
        }
        if (renderParams.matProps == null) {
            return;
        }
        Graphics.RenderMeshPrimitives(renderParams, grassMesh, 0, quantizedPoints.Count);
    }

    #if UNITY_EDITOR
    public static void RenderIfNeeded() {
        if (instance == null) return;
        if (instance.grassMesh == null) {
            return;
        }
        instance.Render();
    }
    public static void StartChange() {
        if (instance == null) {
            instance = new GameObject("Flocking Data", typeof(FlockingData)).GetComponent<FlockingData>();
        }
        if (undoGroup == -1) {
            Undo.SetCurrentGroupName($"Change {nameof(FlockingData)}");
            undoGroup = Undo.GetCurrentGroup();
        } else {
            throw new Exception("Tried to start change twice! This shouldn't happen.");
        }
    }
    public static void EndChange() {
        if (undoGroup == -1) {
            throw new Exception("Tried to end change without starting it! This shouldn't happen.");
        }
        Undo.RecordObject(instance, "Flocking change");
        instance.ManualSerialization();
        EditorUtility.SetDirty(instance);
        Undo.CollapseUndoOperations(undoGroup);
        undoGroup = -1;
        RenderIfNeeded();
    }
    #endif
    public static void RemovePoint(Vector3 point) {
        var quantizedPoint = BoundedRange.Quantize(point, instance.boundedRanges);
        if (!instance.quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        instance.quantizedPoints.Remove(quantizedPoint);
    }

    private void RegenerateMatricies() {
        if (instance.quantizedPoints.Count == 0) {
            return;
        }
        if (modelMatricies != null && modelMatricies.count < instance.quantizedPoints.Count) {
            modelMatricies.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instance.quantizedPoints.Count*2, sizeof(float) * 16);
            cachedMatricies = new Matrix4x4[instance.quantizedPoints.Count * 2];
        } else if (modelMatricies == null || !modelMatricies.IsValid()) {
            modelMatricies?.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instance.quantizedPoints.Count, sizeof(float) * 16);
            boundedRanges = GetBoundedRanges(new List<GrassData>(instance.quantizedPoints.Values));
            cachedMatricies = new Matrix4x4[instance.quantizedPoints.Count];
        }

        Vector3 minBounds = new Vector3(boundedRanges[0].GetMinValue(), boundedRanges[1].GetMinValue(), boundedRanges[2].GetMinValue());
        Vector3 maxBounds = new Vector3(boundedRanges[0].GetMaxValue(), boundedRanges[1].GetMaxValue(), boundedRanges[2].GetMaxValue());

        Vector3 center = Vector3.Lerp(maxBounds,minBounds, 0.5f);
        int i = 0;
        foreach (var data in instance.quantizedPoints) {
            cachedMatricies[i] = Matrix4x4.TRS(data.Value.position-center, Quaternion.FromToRotation(Vector3.forward, data.Value.normal.normalized)*Quaternion.AngleAxis(data.Value.rotation,Vector3.forward), data.Value.scale == 0f ? Vector3.one : Vector3.one*data.Value.scale);
            i++;
        }
        modelMatricies.SetData(cachedMatricies);
        if (!TryGetComponent(out LightProbeProxyVolume proxy)) {
            proxy = gameObject.AddComponent<LightProbeProxyVolume>();
        }
        proxy.originCustom = center;
        proxy.sizeCustom = maxBounds - minBounds;
        materialProperties ??= new MaterialPropertyBlock();
        renderParams = new RenderParams(grassMaterial) {
            worldBounds = new Bounds(center, Vector3.one+(maxBounds-minBounds)),
            material = grassMaterial,
            matProps = materialProperties,
            lightProbeUsage = LightProbeUsage.UseProxyVolume,
            reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
            lightProbeProxyVolume = proxy,
        };
        // TODO: Sending waaaay too much to the GPU, probably should chunk up the grid
        renderParams.matProps.SetBuffer("_GrassMat", modelMatricies);
    }

    private static BoundedRange[] GetBoundedRanges(IList<GrassData> points, Vector3? extraPoint = null) {
        if (points == null || points.Count == 0) {
            return new BoundedRange[] {
                new(0f, 1f, MAX_TOLERANCE),
                new(0f, 1f, MAX_TOLERANCE),
                new(0f, 1f, MAX_TOLERANCE),
            };
        }

        Vector3 min = points[0].position;
        Vector3 max = points[0].position;
        foreach (var point in points) {
            min = new Vector3(
                Mathf.Min(point.position.x, min.x),
                Mathf.Min(point.position.y, min.y),
                Mathf.Min(point.position.z, min.z)
            );
            max = new Vector3(
                Mathf.Max(point.position.x, max.x),
                Mathf.Max(point.position.y, max.y),
                Mathf.Max(point.position.z, max.z)
            );
        }

        if (extraPoint != null) {
            min = new Vector3(
                Mathf.Min(extraPoint.Value.x, min.x),
                Mathf.Min(extraPoint.Value.y, min.y),
                Mathf.Min(extraPoint.Value.z, min.z)
            );
            max = new Vector3(
                Mathf.Max(extraPoint.Value.x, max.x),
                Mathf.Max(extraPoint.Value.y, max.y),
                Mathf.Max(extraPoint.Value.z, max.z)
            );
        }

        Vector3 extents = (max - min)*0.5f;
        return new BoundedRange[] {
            new(Mathf.Floor(min.x-MAX_TOLERANCE-extents.x), Mathf.Ceil(max.x+MAX_TOLERANCE+extents.x), MAX_TOLERANCE),
            new(Mathf.Floor(min.y-MAX_TOLERANCE-extents.y), Mathf.Ceil(max.y+MAX_TOLERANCE+extents.y), MAX_TOLERANCE),
            new(Mathf.Floor(min.z-MAX_TOLERANCE-extents.z), Mathf.Ceil(max.z+MAX_TOLERANCE+extents.z), MAX_TOLERANCE),
        };
    }

    private void OnDrawGizmos() {
        foreach (var pair in quantizedPoints) {
            Gizmos.DrawWireCube(pair.Value.position, Vector3.one * 0.1f);
        }
    }

    private void ManualSerialization() {
        if (quantizedPoints == null || quantizedPoints.Count == 0) {
            return;
        }
        if (serializedData == null) {
            serializedData = new SerializedFlockingData {
                grassDatas = new List<GrassData>(quantizedPoints.Values)
            };
        } else {
            serializedData.grassDatas.Clear();
            serializedData.grassDatas.AddRange(quantizedPoints.Values);
        }
    }

    public void OnBeforeSerialize() {
    }

    public void OnAfterDeserialize() {
        boundedRanges = GetBoundedRanges(serializedData.grassDatas);
        quantizedPoints ??= new Dictionary<QuantizedVector3, GrassData>();
        quantizedPoints.Clear();
        foreach (var data in serializedData.grassDatas) {
            quantizedPoints[BoundedRange.Quantize(data.position, boundedRanges)] = data;
        }
    }
}
