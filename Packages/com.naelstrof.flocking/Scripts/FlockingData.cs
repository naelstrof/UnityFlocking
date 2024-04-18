using System;
using System.Collections.Generic;
using NetStack.Quantization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class FlockingData : MonoBehaviour {
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
        public float rotation;
    }

    private Dictionary<QuantizedVector3, GrassData> quantizedPoints;
    private BoundedRange[] boundedRanges;
    
    private static FlockingData instance;
    private static List<GrassData> cachedPoints;
    private const int MAX_SUBDIV = 3;
    private const float MAX_TOLERANCE = 1f/(1<<MAX_SUBDIV);
    
    private Mesh currentMesh;
    private GraphicsBuffer modelMatricies;
    private RenderParams renderParams;
    
    private void OnEnable() {
        if (instance == null || instance == this) {
            instance = this;
            boundedRanges = GetBoundedRanges(serializedData?.grassDatas ?? new List<GrassData>());
            quantizedPoints ??= new Dictionary<QuantizedVector3, GrassData>();
            foreach (var data in serializedData?.grassDatas ?? new List<GrassData>()) {
                quantizedPoints[BoundedRange.Quantize(data.position, boundedRanges)] = data;
            }
            RegenerateMatricies();
        } else {
            if (Application.isPlaying) {
                Destroy(this);
            } else {
                DestroyImmediate(this);
            }
        }
    }

    private void OnDisable() {
        modelMatricies?.Release();
        modelMatricies = null;
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
        if (serializedData.grassDatas.Count == 0) {
            return;
        }
        if (renderParams.matProps == null) {
            return;
        }
        renderParams.matProps.SetBuffer("_GrassMat", modelMatricies);
        Graphics.RenderMeshPrimitives(renderParams, grassMesh, 0, serializedData.grassDatas.Count);
    }

    public static void AddPoint(Vector3 point, Vector3 normal, float rotation) {
        if (instance == null) {
            instance = new GameObject("Flocking Data", typeof(FlockingData)).GetComponent<FlockingData>();
        }
        if (point.x <= instance.boundedRanges[0].GetMinValue() ||
            point.y <= instance.boundedRanges[1].GetMinValue() ||
            point.z <= instance.boundedRanges[2].GetMinValue() ||
            point.x >= instance.boundedRanges[0].GetMaxValue() ||
            point.y >= instance.boundedRanges[1].GetMaxValue() ||
            point.z >= instance.boundedRanges[2].GetMaxValue()) {
            var newRanges = GetBoundedRanges(instance.serializedData.grassDatas, point);
            instance.SetBoundRanges(newRanges);
        }
        var quantizedPoint = BoundedRange.Quantize(point, instance.boundedRanges);
        instance.quantizedPoints[quantizedPoint] = new GrassData {
            position = point,
            normal = normal,
            rotation = rotation,
        };
        instance.RegenerateMatricies();
    }

    public static void StartChange() {
        if (instance == null) {
            instance = new GameObject("Flocking Data", typeof(FlockingData)).GetComponent<FlockingData>();
        }
        Undo.RecordObject(instance, "Flocking change");
    }
    public static void EndChange() {
        instance.BeforeSerialize();
        EditorUtility.SetDirty(instance);
    }
    public static void RemovePoint(Vector3 point) {
        var quantizedPoint = BoundedRange.Quantize(point, instance.boundedRanges);
        if (!instance.quantizedPoints.ContainsKey(quantizedPoint)) {
            return;
        }
        instance.quantizedPoints.Remove(quantizedPoint);
        instance.RegenerateMatricies();
    }

    private void RegenerateMatricies() {
        if (serializedData.grassDatas.Count == 0) {
            return;
        }
        if (modelMatricies != null && modelMatricies.count != serializedData.grassDatas.Count) {
            modelMatricies.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, serializedData.grassDatas.Count, sizeof(float) * 16);
        } else if (modelMatricies == null || !modelMatricies.IsValid()) {
            modelMatricies?.Release();
            modelMatricies = new GraphicsBuffer(GraphicsBuffer.Target.Structured, serializedData.grassDatas.Count, sizeof(float) * 16);
            boundedRanges = GetBoundedRanges(serializedData.grassDatas);
        }

        Vector3 minBounds = new Vector3(boundedRanges[0].GetMinValue(), boundedRanges[1].GetMinValue(), boundedRanges[2].GetMinValue());
        Vector3 maxBounds = new Vector3(boundedRanges[0].GetMaxValue(), boundedRanges[1].GetMaxValue(), boundedRanges[2].GetMaxValue());

        Vector3 center = Vector3.Lerp(maxBounds,minBounds, 0.5f);
        Matrix4x4[] matricies = new Matrix4x4[serializedData.grassDatas.Count];
        int i = 0;
        foreach (var data in serializedData.grassDatas) {
            matricies[i] = Matrix4x4.TRS(data.position-center, Quaternion.AngleAxis(data.rotation,Vector3.up)*Quaternion.FromToRotation(Vector3.forward, data.normal.normalized), Vector3.one);
            i++;
        }
        modelMatricies.SetData(matricies);
        if (!TryGetComponent(out LightProbeProxyVolume proxy)) {
            proxy = gameObject.AddComponent<LightProbeProxyVolume>();
        }
        proxy.originCustom = center;
        proxy.sizeCustom = maxBounds - minBounds;
        renderParams = new RenderParams(grassMaterial) {
            worldBounds = new Bounds(center, Vector3.one+(maxBounds-minBounds)),
            material = grassMaterial,
            matProps = new MaterialPropertyBlock(),
            lightProbeUsage = LightProbeUsage.UseProxyVolume,
            reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
            lightProbeProxyVolume = proxy,
        };
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

    //private void OnDrawGizmos() {
        //foreach (var pair in quantizedPoints) {
            //Gizmos.DrawWireCube(pair.Value.position, Vector3.one * 0.1f);
        //}
    //}

    private void BeforeSerialize() {
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
}
