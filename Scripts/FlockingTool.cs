using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.ShortcutManagement;

// The second argument in the EditorToolAttribute flags this as a Component tool. That means that it will be instantiated
// and destroyed along with the selection. EditorTool.targets will contain the selected objects matching the type.
[EditorTool("Flocking Tool")]
class FlockingEditorTool : EditorTool {
    private Rect windowRect = new Rect(20, 20, 180, 50);
    [SerializeField, Range(0f,10f)]
    private float toolRadius = 0.5f;
    [SerializeField, Range(0,16)]
    private int toolSubdivAmount = 1;
    [SerializeField]
    private float toolFoliageStartScale = 1f;

    private int controlID;
    private RaycastHit[] cachedHits;
    
    private SerializedObject serializedObject;
    private FlockData? flockData;
    private List<int> cachedTriangles;
    private List<Vector3> cachedVertices;

    private class FlockOperation {
    }
    private class FlockAddOperation : FlockOperation{
        public float startScale;
    }
    private class FlockRemoveOperation : FlockOperation {
    }
    private class FlockScaleOperation : FlockOperation {
        public float scaleMultiplier;
    }

    private struct FlockData {
        public Vector3 position;
        public Vector3 normal;
        public Collider hitCollider;
        public float radius;
        public int divisor;
        public bool backfaceCulling;
        public FlockOperation operation;
    }
    
    void OnEnable() {
        serializedObject = new SerializedObject(this);
        cachedHits = new RaycastHit[32];
        controlID = GUIUtility.GetControlID(FocusType.Passive);
    }

    void OnDisable() {
        cachedHits = null;
    }

    // The second "context" argument accepts an EditorWindow type.
    [Shortcut("Activate Flocking Tool", typeof(SceneView), KeyCode.P)]
    static void FlockingToolShortcut() {
        ToolManager.SetActiveTool<FlockingEditorTool>();
    }

    public override void OnActivated() {
        SceneView.duringSceneGui += OnSceneGUI;
        SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Entering Flocking Tool"), .1f);
    }

    public override void OnWillBeDeactivated() {
        SceneView.lastActiveSceneView.ShowNotification(new GUIContent("Exiting Flocking Tool"), .1f);
        SceneView.duringSceneGui -= OnSceneGUI;
    }
    
    // Equivalent to Editor.OnSceneGUI.
    public override void OnToolGUI(EditorWindow window) {
        CustomGUI(window);
    }
    
    private void CustomGUI(EditorWindow window) {
        if (window is not SceneView sceneView) {
            return;
        }
        sceneView.sceneViewState.alwaysRefresh = true;
        if (!sceneView.sceneViewState.fxEnabled) {
            sceneView.sceneViewState.fxEnabled = true;
        }

        windowRect = GUILayout.Window(0, windowRect, DrawWindow, "Flocking tool");
        windowRect.position = new Vector2(Mathf.Min(window.position.size.x-windowRect.size.x, windowRect.position.x), Mathf.Min(window.position.size.y-50, windowRect.position.y));
        windowRect.position = new Vector2(Mathf.Max(0, windowRect.position.x), Mathf.Max(0, windowRect.position.y));
    }

    private void DrawWindow(int id) {
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(toolRadius)), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(toolSubdivAmount)), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(toolFoliageStartScale)), true);
        serializedObject.ApplyModifiedProperties();
    }

    private void TriangleToGrid(Vector3 v0, Vector3 v1, Vector3 v2, FlockData flockData) {
        Vector3 normal = Vector3.Cross((v1-v0).normalized, (v2-v0).normalized).normalized;
        if (flockData.backfaceCulling && Vector3.Dot(normal, flockData.normal) <= 0.1f) {
            return;
        }
        
        // Get the bounds
        Vector3Int v0i = Vector3Int.FloorToInt(v0*flockData.divisor);
        Vector3Int v1i = Vector3Int.FloorToInt(v1*flockData.divisor);
        Vector3Int v2i = Vector3Int.FloorToInt(v2*flockData.divisor);
        int minx = Mathf.Min(Mathf.Min(v0i.x, v1i.x), v2i.x);
        int miny = Mathf.Min(Mathf.Min(v0i.y, v1i.y), v2i.y);
        int minz = Mathf.Min(Mathf.Min(v0i.z, v1i.z), v2i.z);
        
        int maxx = Mathf.Max(Mathf.Max(v0i.x, v1i.x), v2i.x)+2;
        int maxy = Mathf.Max(Mathf.Max(v0i.y, v1i.y), v2i.y)+2;
        int maxz = Mathf.Max(Mathf.Max(v0i.z, v1i.z), v2i.z)+2;
        
        for (int x = minx; x < maxx; x++) {
            for (int y = miny; y < maxy; y++) {
                for (int z = minz; z < maxz; z++) {
                    Vector3 testPosition = new Vector3(x, y, z)/flockData.divisor;
                    if (Vector3.Distance(testPosition, flockData.position) > flockData.radius) {
                        continue;
                    }

                    float tolerance = 0.5f / flockData.divisor;
                    if (Mathf.Abs(Vector3.Dot(testPosition - v0, normal)) > tolerance) {
                        continue;
                    }

                    Vector3 edge0Normal = Vector3.Cross(normal, (v1 - v0).normalized).normalized;
                    if (Vector3.Dot(testPosition - v0, edge0Normal) < -tolerance) {
                        continue;
                    }
                    Vector3 edge1Normal = Vector3.Cross(normal, (v2 - v1).normalized).normalized;
                    if (Vector3.Dot(testPosition - v1, edge1Normal) < -tolerance) {
                        continue;
                    }
                    Vector3 edge2Normal = Vector3.Cross(normal, (v0 - v2).normalized).normalized;
                    if (Vector3.Dot(testPosition - v2, edge2Normal) < -tolerance) {
                        continue;
                    }

                    if (flockData.operation is FlockAddOperation add) {
                        FlockingData.AddPoint(testPosition, normal, Vector3.one*add.startScale, Random.Range(0f,360f));
                    } else if (flockData.operation is FlockRemoveOperation remove) {
                        FlockingData.RemovePoint(testPosition);
                    } else if (flockData.operation is FlockScaleOperation scale) {
                        FlockingData.ScalePoint(testPosition, scale.scaleMultiplier);
                    }
                }
            }
        }
    }
    private void CalculateIntersections(Collider collider, FlockData flockData) {
        if (collider is MeshCollider meshCollider) {
            // foreach triangle
            if (!meshCollider.TryGetComponent(out MeshRenderer meshRenderer)) {
                return;
            }
            if (!meshCollider.TryGetComponent(out MeshFilter meshFilter)) {
                return;
            }

            var mesh = meshFilter.sharedMesh;
            var matrix = meshRenderer.localToWorldMatrix;
            
            cachedTriangles ??= new List<int>();
            cachedVertices ??= new List<Vector3>();

            mesh.GetVertices(cachedVertices);
            for (int s = 0; s < mesh.subMeshCount; s++) {
                mesh.GetTriangles(cachedTriangles, s);
                for (int i = 0; i < cachedTriangles.Count; i += 3) {
                    Vector3 v0 = matrix.MultiplyPoint(cachedVertices[cachedTriangles[i]]);
                    Vector3 v1 = matrix.MultiplyPoint(cachedVertices[cachedTriangles[i + 1]]);
                    Vector3 v2 = matrix.MultiplyPoint(cachedVertices[cachedTriangles[i + 2]]);
                    TriangleToGrid(v0, v1, v2, flockData);
                }
            }
        }

        FlockingData.Regenerate();
    }
    
    private void OnSceneGUI(SceneView obj) {
        if (ToolManager.activeToolType != typeof(FlockingEditorTool)) {
            return;
        }

        switch (Event.current.type) {
            case EventType.MouseDown:
                if (Event.current.button == 0) {
                    GUIUtility.hotControl = controlID;
                    FlockingData.StartChange();
                    Event.current.Use();
                }
                break;
            case EventType.MouseUp:
                if (Event.current.button == 0) {
                    GUIUtility.hotControl = 0;
                    FlockingData.EndChange();
                    Event.current.Use();
                }
                break;
            case EventType.MouseDrag:
            case EventType.MouseMove:
                Vector2 position = Event.current.mousePosition;
                var ray = HandleUtility.GUIPointToWorldRay(position);
                int hitCount = Physics.RaycastNonAlloc(ray, cachedHits);
                int minHit = -1;
                float minHitDistance = float.MaxValue;
                for (int i = 0; i < hitCount; i++) {
                    if (cachedHits[i].distance < minHitDistance) {
                        minHit = i;
                        minHitDistance = cachedHits[i].distance;
                    }
                }

                if (minHit == -1) {
                    flockData = null;
                    return;
                }
                var hitInfo = cachedHits[minHit];
                FlockOperation operation;
                if (Event.current.shift) {
                    operation = new FlockScaleOperation() {
                        scaleMultiplier = Event.current.control ? 0.99f : 1.01f,
                    };
                } else {
                    if (!Event.current.control) {
                        operation = new FlockAddOperation() {
                            startScale = toolFoliageStartScale,
                        };
                    } else {
                        operation = new FlockRemoveOperation();
                    }
                }

                flockData = new FlockData {
                    position = hitInfo.point,
                    normal = hitInfo.normal,
                    radius = toolRadius,
                    divisor = 1 << toolSubdivAmount,
                    operation = operation,
                    hitCollider = hitInfo.collider,
                    backfaceCulling = true,
                };
                if (GUIUtility.hotControl == controlID && hitInfo.collider.gameObject.isStatic) {
                    CalculateIntersections(hitInfo.collider, flockData.Value);
                    FlockingData.RenderIfNeeded();
                }
                break;
            case EventType.Repaint:
                if (flockData.HasValue) {
                    Handles.color = flockData.Value.hitCollider.gameObject.isStatic ? Color.white : Color.gray;
                    Handles.DrawWireDisc(flockData.Value.position, flockData.Value.normal, toolRadius);
                }
                break;
        }
    }
}
#endif