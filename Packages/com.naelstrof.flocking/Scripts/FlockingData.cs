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
    [SerializeField] List<FlockingChunk> chunks;
    [SerializeField] private FoliagePack foliagePack;

    private static FlockingData instance;
    private static int undoGroup = -1;
    public const int CHUNK_SIZE = 16;
    
    private void OnEnable() {
        if (instance == null || instance == this) {
            instance = this;
            foreach (var chunk in chunks) {
                chunk.OnEnable();
            }
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
        foreach (var chunk in chunks) {
            chunk.OnDisable();
        }
#if UNITY_EDITOR
        Undo.undoRedoEvent -= OnUndoRedoEvent;
#endif
    }
    private void Update() {
        foreach (var chunk in chunks) {
            chunk.Render();
        }
    }

    private FlockingChunk GetChunk(Vector3 point) {
        foreach (var chunk in chunks) {
            if (chunk.ContainsPoint(point)) {
                return chunk;
            }
        }
        FlockingChunk newChunk = new FlockingChunk();
        Vector3Int flooredChunk = Vector3Int.FloorToInt(point/CHUNK_SIZE);
        Vector3Int nextChunk = flooredChunk+Vector3Int.one;

        newChunk.SetBoundRanges(flooredChunk*CHUNK_SIZE, nextChunk*CHUNK_SIZE);
        newChunk.SetFoliagePack(foliagePack);
        chunks.Add(newChunk);
        return newChunk;
    }

    public static void SetScale(Vector3 point, Vector3 newScale) {
        instance.GetChunk(point).SetScale(point, newScale);
    }

    public static void AddPoint(Vector3 point, Vector3 normal, Vector3 scale, Vector3 offset, float rotation, int index) {
        instance.GetChunk(point).AddPoint(point, normal, scale, offset, rotation, index);
    }

    public static void SetPointRotation(Vector3 point, Vector3 up, Vector3 forward, float influence) {
        instance.GetChunk(point).SetPointRotation(point, up, forward, influence);
    }

    public static Vector3? GetScale(Vector3 point) {
        return instance.GetChunk(point).GetScale(point);
    }
    public static void Regenerate() {
        if (instance == null) {
            return;
        }

        if (instance.chunks == null) {
            return;
        }
        
        foreach (var chunk in instance.chunks) {
            chunk.RegenerateMatricies();
        }
    }

    #if UNITY_EDITOR
    public static void StartChange() {
        if (instance == null) {
            instance = new GameObject("Flocking Data", typeof(FlockingData)).GetComponent<FlockingData>();
            instance.foliagePack = AssetDatabase.LoadAssetAtPath<FoliagePack>( AssetDatabase.GUIDToAssetPath("48d57ab6cc0493f448d4a6ff0674ee3c"));
        }
        if (undoGroup == -1) {
            Undo.SetCurrentGroupName($"Change {nameof(FlockingData)}");
            undoGroup = Undo.GetCurrentGroup();
        } else {
            //throw new Exception("Tried to start change twice! This shouldn't happen.");
        }
    }
    public static void EndChange() {
        if (undoGroup == -1) {
            throw new Exception("Tried to end change without starting it! This shouldn't happen.");
        }
        Undo.RecordObject(instance, "Flocking change");
        foreach (var chunk in instance.chunks) {
            chunk.OnBeforeSerialize();
        }
        EditorUtility.SetDirty(instance);
        Undo.CollapseUndoOperations(undoGroup);
        undoGroup = -1;
        //instance.Update();
    }
    #endif
    public static FoliagePack GetFoliagePack() => instance.foliagePack;
    public static void RemovePoint(Vector3 point) {
        instance.GetChunk(point).RemovePoint(point);
    }
    public static void SetPointIndex(Vector3 point, int index) {
        instance.GetChunk(point).SetPointIndex(point, index);
    }
    private void OnDrawGizmos() {
        if (chunks == null) {
            return;
        }

        //foreach (var chunk in chunks) {
            //chunk.OnDrawGizmos();
        //}
    }

    public void OnBeforeSerialize() {
    }
    public void OnAfterDeserialize() {
        foreach (var chunk in chunks) {
            chunk.OnAfterDeserialize();
        }
    }
}
