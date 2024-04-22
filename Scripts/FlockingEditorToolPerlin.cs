using System.Collections;
using System.Collections.Generic;
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
    protected override void DrawWindow(int id) {
        base.DrawWindow(id);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(biomeScale)), true);
        serializedObject.ApplyModifiedProperties();
    }

    protected override void StartOperation() {
        simplex = new OpenSimplexNoise(0);
    }

    protected override void PostPointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        int totalIndices = FlockingData.GetIndexCount();
        int biomeCount = 3;
        
        var biomeNoise = simplex.Evaluate(point.x*(biomeScale*biomeScale), point.y*(biomeScale*biomeScale), point.z*(biomeScale*biomeScale));

        float individualScale = 1f / 4f;
        var noise = simplex.Evaluate(point.x*individualScale, point.y*individualScale, point.z*individualScale);

        int selection = Mathf.RoundToInt(Mathf.Repeat((float)biomeNoise * (float)(totalIndices)/(float)biomeCount + (float)noise * (float)biomeCount, totalIndices - 1));
        
        int desiredIndex = Mathf.Clamp(selection, 0, totalIndices-1);
        FlockingData.SetPointIndex(point, desiredIndex);
        
    }
}
#endif
