using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using NoiseTest;
using UnityEditor;
using UnityEditor.EditorTools;

[EditorTool("Flocking Perlin")]
public class FlockingEditorToolPerlin : FlockingEditorTool {
    private OpenSimplexNoise simplex;
    [SerializeField, Range(0f,1f)]
    private float biomeScale = 0.5f;
    [SerializeField]
    private int biomeSeed = 0;
    private class Biome {
        public Foliage filler;
        public Foliage spiller;
        public Foliage thriller;
    }

    private List<Biome> biomes = new List<Biome>();

    protected override void DrawWindow(int id) {
        base.DrawWindow(id);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(biomeScale)), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(biomeSeed)), true);
        serializedObject.ApplyModifiedProperties();
    }

    protected override void StartOperation() {
        simplex = new OpenSimplexNoise(0);
        List<Foliage> fillers = new List<Foliage>(FlockingData.GetFoliagePack().foliages.Where((f) => f.foliageType == Foliage.FoliageType.Filler));
        List<Foliage> spillers = new List<Foliage>(FlockingData.GetFoliagePack().foliages.Where((f) => f.foliageType == Foliage.FoliageType.Spiller));
        List<Foliage> thrillers = new List<Foliage>(FlockingData.GetFoliagePack().foliages.Where((f) => f.foliageType == Foliage.FoliageType.Thriller));
        biomes.Clear();
        for (int i = 0; i < Mathf.Max(fillers.Count, spillers.Count, thrillers.Count); i++) {
            biomes.Add(new Biome() {
                filler = fillers[(i+73*biomeSeed) % fillers.Count],
                spiller = spillers[(i+37*biomeSeed) % spillers.Count],
                thriller = thrillers[(i+11*biomeSeed) % thrillers.Count],
            });
        }
    }

    protected override void PostPointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        var biomeNoise = simplex.Evaluate(point.x*(biomeScale*biomeScale), point.y*(biomeScale*biomeScale), point.z*(biomeScale*biomeScale));
        int biomeSelection = Mathf.Clamp(Mathf.FloorToInt((float)biomeNoise * (float)(biomes.Count - 1)), 0, biomes.Count);
        
        float individualScale = 1f / 4f;
        int desiredIndex;
        var scale = FlockingData.GetScale(point)?.x ?? 1f;
        var noise = simplex.Evaluate(point.x*individualScale, point.y*individualScale, point.z*individualScale);
        if (noise*scale*Random.Range(0.25f,4f) < 0.33f) {
            desiredIndex = FlockingData.GetFoliagePack().foliages.IndexOf(biomes[biomeSelection].spiller);
            if (Random.Range(0f, 1f) < 0.02f) {
                desiredIndex = FlockingData.GetFoliagePack().foliages.IndexOf(biomes[biomeSelection].thriller);
            }
        } else {
            desiredIndex = FlockingData.GetFoliagePack().foliages.IndexOf(biomes[biomeSelection].filler);
            if (Random.Range(0f, 1f) < 0.08f) {
                desiredIndex = FlockingData.GetFoliagePack().foliages.IndexOf(biomes[biomeSelection].thriller);
            }
        }
        
        FlockingData.SetPointIndex(point, desiredIndex);
    }
}
#endif
