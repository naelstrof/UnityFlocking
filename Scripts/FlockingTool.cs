using System;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
//using UnityEditor.ShortcutManagement;

//[EditorTool("Flocking Tool")]
public class FlockingEditorTool : EditorTool {
    private Rect windowRect = new Rect(20, 20, 180, 50);
    [SerializeField, Range(0f,10f)]
    private float toolRadius = 0.5f;
    [SerializeField, Range(0,16)]
    private int toolSubdivAmount = 1;

    private int controlID;
    private RaycastHit[] cachedHits;
    
    protected SerializedObject serializedObject;
    private FlockData? flockData;
    private List<int> cachedTriangles;
    private List<Vector3> cachedVertices;
    private Dictionary<Vector3Int, OpData> cachedPoints;
    protected struct OpData {
        public Vector3 position;
        public Vector3 normal;
        public Plane surfaceInfo;
    }
    protected struct FlockData {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 mouseDelta;
        public Collider hitCollider;
        public float radius;
        public int divisor;
        public bool backfaceCulling;
        public bool ctrlHeld;
        public bool shiftHeld;
        public Camera camera;
    }
    
    void OnEnable() {
        serializedObject = new SerializedObject(this);
        cachedHits = new RaycastHit[32];
        controlID = GUIUtility.GetControlID(FocusType.Passive);
    }

    void OnDisable() {
        cachedHits = null;
    }

    //// The second "context" argument accepts an EditorWindow type.
    //[Shortcut("Activate Flocking Tool", typeof(SceneView), KeyCode.P)]
    //static void FlockingToolShortcut() {
        //ToolManager.SetActiveTool<FlockingEditorTool>();
    ////}

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

    protected virtual void DrawWindow(int id) {
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(toolRadius)), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(toolSubdivAmount)), true);
    }

    protected virtual void StartOperation() {
    }

    protected virtual void PrePointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
    }

    protected virtual void PostPointOperate(Vector3 point, Vector3 normal, Plane realSurface, FlockData data) {
        throw new NotImplementedException();
        /*switch (data.operation) {
            case FlockAddOperation add:
                FlockingData.AddPoint(point, normal, Vector3.one*add.startScale, Random.Range(0f,360f));
                break;
            case FlockRemoveOperation remove:
                FlockingData.RemovePoint(point);
                break;
            default:
                throw new UnityException("can't handle this operation with this tool!");
        }*/
    }

    private void TriangleToGrid(Vector3 v0, Vector3 v1, Vector3 v2, FlockData flockData, Operation op) {
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

                    Plane surfacePlane = new Plane(v0, v1, v2);
                    cachedPoints[new Vector3Int(x, y, z)] = new OpData() {
                        position = testPosition,
                        normal = normal,
                        surfaceInfo = surfacePlane,
                    };
                }
            }
        }
    }
    private void HandleMeshCollider(MeshCollider meshCollider, FlockData flockData, Operation op) {
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
                TriangleToGrid(v0, v1, v2, flockData, op);
            }
        }
    }

    private void HandleConvexCollider(Collider collider, FlockData flockData, Operation op) {
        var bounds = collider.bounds;
        Vector3Int min = Vector3Int.FloorToInt((bounds.center - bounds.extents) * flockData.divisor)-Vector3Int.one*4;
        Vector3Int max = Vector3Int.FloorToInt((bounds.center + bounds.extents) * flockData.divisor)+Vector3Int.one*4;
        float tolerance = 1f / flockData.divisor;
        for (int x = min.x; x < max.x; x++) {
            for (int y = min.y; y < max.y; y++) {
                for (int z = min.z; z < max.z; z++) {
                    Vector3 testPosition = new Vector3(x, y, z)/flockData.divisor;
                    Vector3 closest = collider.ClosestPoint(testPosition);
                    if (testPosition == closest) continue;
                    if (Vector3.Distance(testPosition, closest) > tolerance) continue;
                    
                    Vector3 dir = (closest - testPosition).normalized;
                    if (collider.Raycast(new Ray(testPosition, dir), out RaycastHit hitInfo, 10f)) {
                        cachedPoints[new Vector3Int(x, y, z)] = new OpData() {
                            position = testPosition,
                            normal = hitInfo.normal,
                            surfaceInfo = new Plane(hitInfo.normal, hitInfo.point),
                        };
                    }
                }
            }
        }
    }

    private delegate void Operation(Vector3 point, Vector3 normal, Plane realSurface, FlockData data);
    private void CalculateIntersections(Collider collider, FlockData flockData, Operation op) {
        if (collider is MeshCollider meshCollider) {
            HandleMeshCollider(meshCollider, flockData, op);
        } else {
            HandleConvexCollider(collider, flockData, op);
        }

        foreach (var pair in cachedPoints) {
            op.Invoke(pair.Value.position, pair.Value.normal, pair.Value.surfaceInfo, flockData);
        }

        FlockingData.Regenerate();
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
                flockData = new FlockData {
                    position = hitInfo.point,
                    normal = hitInfo.normal,
                    mouseDelta = Event.current.delta,
                    radius = toolRadius,
                    divisor = 1 << toolSubdivAmount,
                    ctrlHeld = Event.current.control,
                    shiftHeld = Event.current.shift,
                    hitCollider = hitInfo.collider,
                    backfaceCulling = true,
                    camera = obj.camera,
                };
                
                cachedPoints ??= new Dictionary<Vector3Int, OpData>();
                cachedPoints.Clear();
                
                if (GUIUtility.hotControl == controlID && hitInfo.collider.gameObject.isStatic) {
                    StartOperation();
                    CalculateIntersections(hitInfo.collider, flockData.Value, PrePointOperate);
                    CalculateIntersections(hitInfo.collider, flockData.Value, PostPointOperate);
                    obj.Repaint();
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