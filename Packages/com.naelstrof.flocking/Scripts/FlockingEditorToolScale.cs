using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;

[EditorTool("Flocking Add")]
public class FlockingEditorToolScale : FlockingEditorTool {
    [SerializeField, Range(0f,3f)]
    private float offsetVariation = 1f;
    
    private float scaleSum;
    private int pointCount;
    protected override void DrawWindow(int id) {
        base.DrawWindow(id);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(offsetVariation)), true);
        serializedObject.ApplyModifiedProperties();
    }

    protected override void StartOperation() {
        scaleSum = 0f;
        pointCount = 0;
    }

    protected override void PrePointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        if (!data.shiftHeld) {
            return;
        }

        var scale = FlockingData.GetScale(point);
        if (scale != null) {
            scaleSum += scale.Value.x;
            pointCount++;
        }
    }

    protected override void PostPointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        float falloff = 1f - Mathf.Clamp01(Vector3.Distance(point, data.position) / data.radius);
        if (!data.shiftHeld) { // Adjust scale
            var startScale = FlockingData.GetScale(point);
            if (startScale == null && !data.ctrlHeld) {
                Vector3 offset = realSurface.ClosestPointOnPlane(point)-point;
                offset += Vector3.ProjectOnPlane(Random.insideUnitSphere*offsetVariation,realSurface.normal)*FlockingChunk.MAX_TOLERANCE;
                FlockingData.AddPoint(point, normal, Vector3.zero, offset, Random.Range(0f,360f));
            }

            float newScale = (startScale ?? Vector3.zero).x + (data.ctrlHeld ? -data.mouseDelta.magnitude * 0.004f : data.mouseDelta.magnitude * 0.004f) * falloff;
            FlockingData.SetScale(point, Vector3.one*newScale);
            if (newScale <= 0f && data.ctrlHeld) {
                FlockingData.RemovePoint(point);
            }
        } else { // Smooth scale
            var currentScale = FlockingData.GetScale(point);
            if (currentScale == null) {
                Vector3 offset = point - realSurface.ClosestPointOnPlane(point);
                offset += Vector3.ProjectOnPlane(Random.insideUnitSphere*offsetVariation,realSurface.normal)*FlockingChunk.MAX_TOLERANCE;
                FlockingData.AddPoint(point, normal, Vector3.zero, offset, Random.Range(0f,360f));
                return;
            }
            float desiredScale = scaleSum / pointCount;
            FlockingData.SetScale(point, (currentScale.Value)+Vector3.one*(desiredScale-currentScale.Value.x)*data.mouseDelta.magnitude*0.004f*falloff);
        }
    }
}
#endif
