using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;

[EditorTool("Flocking Comb")]
public class FlockingEditorToolComb : FlockingEditorTool {
    protected override void DrawWindow(int id) {
        base.DrawWindow(id);
        serializedObject.ApplyModifiedProperties();
    }

    protected override void PostPointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        if (data.mouseDelta.magnitude == 0) {
            return;
        }

        float falloff = 1f - Vector3.Distance(point, data.position) / data.radius;
        if (data.shiftHeld) {
            Vector3 mouseForward = data.camera.cameraToWorldMatrix.MultiplyVector(new Vector3(data.mouseDelta.x, -data.mouseDelta.y)).normalized;
            Vector3 forward = (point - data.position).normalized;
            if (forward == Vector3.zero || mouseForward == Vector3.zero) {
                return;
            }

            float dirFalloff = 1f - Mathf.Clamp01(Vector3.Dot(mouseForward, forward));
            FlockingData.SetPointRotation(point, normal, forward, falloff * data.mouseDelta.magnitude * 0.01f * dirFalloff);
        } else {
            if (!data.ctrlHeld) {
                Vector3 forward = data.camera.cameraToWorldMatrix.MultiplyVector(new Vector3(data.mouseDelta.x, -data.mouseDelta.y)).normalized;
                FlockingData.SetPointRotation(point, normal, forward, falloff * data.mouseDelta.magnitude * 0.01f);
            } else {
                Vector3 forward = Random.insideUnitSphere.normalized;
                if (forward == Vector3.zero) {
                    return;
                }

                FlockingData.SetPointRotation(point, normal, forward,
                    falloff * data.mouseDelta.magnitude * 0.01f);
            }
        }
    }
}
#endif
