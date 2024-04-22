using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.EditorTools;
[EditorTool("Flocking Delete Projection")]
public class FlockingEditorDeleteTool : EditorTool {
    private int controlID;

    private void OnEnable() {
        controlID = GUIUtility.GetControlID(FocusType.Passive);
    }

    public override void OnActivated() {
        SceneView.duringSceneGui += OnSceneGUI;
        SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Entering Flocking Tool"), .1f);
    }

    public override void OnWillBeDeactivated() {
        SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Exiting Flocking Tool"), .1f);
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void CylinderDelete(Event currentEvent) {
        var ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
        Bounds bounds = new Bounds(ray.origin, Vector3.one);
        bounds.Encapsulate(ray.origin + ray.direction * FlockingData.CHUNK_SIZE);
        
        
        Vector3Int min = Vector3Int.FloorToInt((bounds.center - bounds.extents) * FlockingChunk.MAX_DIVISOR)-Vector3Int.one*4;
        Vector3Int max = Vector3Int.FloorToInt((bounds.center + bounds.extents) * FlockingChunk.MAX_DIVISOR)+Vector3Int.one*4;
        for (int x = min.x; x < max.x; x++) {
            for (int y = min.y; y < max.y; y++) {
                for (int z = min.z; z < max.z; z++) {
                    Vector3 testPosition = new Vector3(x, y, z) / FlockingChunk.MAX_DIVISOR;
                    if (Vector3.ProjectOnPlane(testPosition - ray.origin, ray.direction).magnitude < 1f) {
                        FlockingData.RemovePoint(testPosition);
                    }
                }
            }
        }
    }

    private void OnSceneGUI(SceneView obj) {
        if (ToolManager.activeToolType != GetType()) {
            return;
        }

        switch (Event.current.type) {
            case EventType.MouseDown:
                if (Event.current.button == 0 && GUIUtility.hotControl != controlID) {
                    GUIUtility.hotControl = controlID;
                    FlockingData.StartChange();
                    Event.current.Use();
                    CylinderDelete(Event.current);
                }
                break;
            case EventType.MouseUp:
                if (Event.current.button == 0 && GUIUtility.hotControl == controlID) {
                    GUIUtility.hotControl = 0;
                    FlockingData.EndChange();
                    Event.current.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != controlID) {
                    break;
                }
                CylinderDelete(Event.current);
                break;
            case EventType.Repaint:
                //Handles.color = Color.white;
                //var ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
                //Handles.DrawWireDisc(ray.origin+obj.camera.transform.forward*0.01f, obj.camera.transform.forward, 0.05f);
                break;
        }
    }
}
#endif
