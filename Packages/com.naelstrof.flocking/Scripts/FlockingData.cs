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
    [SerializeField] private Mesh grassMesh;
    [SerializeField] private Material grassMaterial;

    private static FlockingData instance;
    private static int undoGroup = -1;
    private const int CHUNK_SIZE = 32;
    
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
        newChunk.SetMaterial(grassMaterial);
        newChunk.SetMesh(grassMesh);
        chunks.Add(newChunk);
        return newChunk;
    }

    public static void ScalePoint(Vector3 point, float addScale) {
        instance.GetChunk(point).ScalePoint(point, addScale);
    }

    public static void AddPoint(Vector3 point, Vector3 normal, Vector3 scale, float rotation) {
        instance.GetChunk(point).AddPoint(point, normal, scale, rotation);
    }
    public static void Regenerate() {
        foreach (var chunk in instance.chunks) {
            chunk.RegenerateMatricies();
        }
    }

    #if UNITY_EDITOR
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
        foreach (var chunk in instance.chunks) {
            chunk.OnBeforeSerialize();
        }
        EditorUtility.SetDirty(instance);
        Undo.CollapseUndoOperations(undoGroup);
        undoGroup = -1;
        //instance.Update();
    }
    #endif
    public static void RemovePoint(Vector3 point) {
        instance.GetChunk(point).RemovePoint(point);
    }
    //private void OnDrawGizmos() {
        //foreach (var pair in quantizedPoints) {
            //Gizmos.DrawWireCube(pair.Value.position, Vector3.one * 0.1f);
        //}
    //}

    public void OnBeforeSerialize() {
    }
    public void OnAfterDeserialize() {
        foreach (var chunk in chunks) {
            chunk.OnAfterDeserialize();
        }
    }
}
