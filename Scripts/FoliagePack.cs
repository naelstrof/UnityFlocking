using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = System.Object;

[CreateAssetMenu(fileName = "New Foliage Pack", menuName = "Data/Foliage Pack", order = 0)]
public class FoliagePack : ScriptableObject {
    [SerializeField] private List<Foliage> foliages;
    [SerializeField] private Material material;
    
    [NonSerialized] private List<Foliage> cachedFoliages;
    
    [NonSerialized] private GraphicsBuffer meshTriangles;
    [NonSerialized] private GraphicsBuffer meshPositions;
    [NonSerialized] private GraphicsBuffer meshNormals;
    [NonSerialized] private GraphicsBuffer meshUVs;

    public Material GetMaterial() => material;
    public IReadOnlyCollection<Foliage> GetFoliages() => foliages.AsReadOnly();

    public int GetFoliageIndexOf(Foliage foliage) {
        return foliages.IndexOf(foliage);
    }
    public int GetFoliageCount() => foliages.Count;

    public int GetFoliageTriangleIndexCount() {
        if (foliages == null) {
            return 0;
        }

        if (foliages.Count <= 0) {
            return 0;
        }

        if (foliages[0].mesh == null) {
            return 0;
        }
        
        return (int)foliages[0].mesh.GetIndexCount(0);
    }

    private void OnDestroy() {
        meshTriangles?.Release();
        meshPositions?.Release();
        meshNormals?.Release();
        meshUVs?.Release();
    }

    private bool GetNeedsRegeneration() {
        if (foliages == null) {
            return false;
        }
        
        if (cachedFoliages == null) {
            return true;
        }
        if (cachedFoliages.Count != foliages.Count) {
            return true;
        }

        for (int i = 0; i < cachedFoliages.Count; i++) {
            if (!foliages[i].Equals(cachedFoliages[i])) {
                return true;
            }
        }

        return false;
    }

    private void RegenerateIfNeeded() {
        if (!GetNeedsRegeneration()) {
            return;
        }
        int? vertexCount = null;
        foreach (var foliage in foliages) {
            var mesh = foliage.mesh;
            vertexCount ??= (int)mesh.GetIndexCount(0);
            if (vertexCount.Value != (int)mesh.GetIndexCount(0)) {
                throw new UnityException("Cannot use meshes with differing triangle counts.");
            }
        }

        List<int> triangles = new List<int>();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();

        List<int> cachedTriangles = new List<int>();
        List<Vector3> cachedVertices = new List<Vector3>();
        List<Vector3> cachedNormals = new List<Vector3>();
        List<Vector2> cachedUvs = new List<Vector2>();
        foreach (var foliage in foliages) {
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
        
        cachedFoliages = new List<Foliage>(foliages);
    }

    public void SetBuffers(MaterialPropertyBlock block) {
        RegenerateIfNeeded();
        block.SetBuffer("_GrassTriangles", meshTriangles);
        block.SetBuffer("_GrassPositions", meshPositions);
        block.SetBuffer("_GrassNormals", meshNormals);
        block.SetBuffer("_GrassUVs", meshUVs);
        block.SetInt("_GrassIndexCount", (int)foliages[0].mesh.GetIndexCount(0));
    }
}
