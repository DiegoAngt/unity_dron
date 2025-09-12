// Archivo: PathVisualizer.cs
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class PathVisualizer : MonoBehaviour
{
    [Header("Configuración de Trayectoria")]
    public float pointRecordingInterval = 0.5f;
    public int maxPathPoints = 1000;
    public float pathWidth = 0.2f;

    [Header("Componentes")]
    public LineRenderer pathLineRenderer;
    public LineRenderer futurePathRenderer;
    public Transform droneTransform;
    public SearchAgent searchAgent;
    public NavMeshAgent navMeshAgent;

    [Header("Estilos (opcional)")]
    public Material pathMaterial;
    public Material futurePathMaterial;

    private readonly List<Vector3> pathPoints = new List<Vector3>();
    private float lastRecordTime;
    private bool referencesInitialized = false;

    // Para limpiar solo si los creamos nosotros
    private bool createdPathGO = false;
    private bool createdFutureGO = false;

    void Start()
    {
        InitializeRenderers();
        InitializeReferences();
    }

    void InitializeReferences()
    {
        if (droneTransform == null)
            droneTransform = transform;

        if (searchAgent == null)
            searchAgent = GetComponent<SearchAgent>();

        if (navMeshAgent == null)
            navMeshAgent = GetComponent<NavMeshAgent>();

        if (navMeshAgent == null)
        {
            navMeshAgent = FindAnyObjectByType<NavMeshAgent>();
            if (navMeshAgent == null)
            {
                Debug.LogWarning("[PathVisualizer] No se encontró NavMeshAgent. La trayectoria futura no se dibujará.");
            }
        }

        referencesInitialized = true;
    }

    void InitializeRenderers()
    {
        // Trayectoria pasada
        if (pathLineRenderer == null)
        {
            GameObject pathGO = new GameObject("PathRenderer");
            pathGO.transform.SetParent(transform, false);
            pathLineRenderer = pathGO.AddComponent<LineRenderer>();
            createdPathGO = true;

            pathLineRenderer.material = pathMaterial != null
                ? pathMaterial
                : new Material(Shader.Find("Sprites/Default"));

            pathLineRenderer.useWorldSpace = true;
            pathLineRenderer.startWidth = pathWidth;
            pathLineRenderer.endWidth = pathWidth;
            pathLineRenderer.startColor = Color.green;
            pathLineRenderer.endColor = Color.green;
            pathLineRenderer.numCapVertices = 5;
            pathLineRenderer.numCornerVertices = 5;
            pathLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pathLineRenderer.receiveShadows = false;
        }
        pathLineRenderer.positionCount = 0;

        // Trayectoria futura
        if (futurePathRenderer == null)
        {
            GameObject futureGO = new GameObject("FuturePathRenderer");
            futureGO.transform.SetParent(transform, false);
            futurePathRenderer = futureGO.AddComponent<LineRenderer>();
            createdFutureGO = true;

            futurePathRenderer.material = futurePathMaterial != null
                ? futurePathMaterial
                : new Material(Shader.Find("Sprites/Default"));

            futurePathRenderer.useWorldSpace = true;
            futurePathRenderer.startWidth = pathWidth * 0.8f;
            futurePathRenderer.endWidth = pathWidth * 0.3f;
            futurePathRenderer.startColor = Color.cyan;
            futurePathRenderer.endColor = Color.blue;
            futurePathRenderer.numCapVertices = 5;
            futurePathRenderer.numCornerVertices = 5;
            futurePathRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            futurePathRenderer.receiveShadows = false;
        }
        futurePathRenderer.positionCount = 0;
    }

    void Update()
    {
        if (!referencesInitialized) return;

        RecordPathPoint();
        UpdatePathVisualization();
        UpdateFuturePathPrediction();
        UpdatePathColors();
    }

    void RecordPathPoint()
    {
        if (droneTransform == null) return;

        if (Time.time - lastRecordTime >= pointRecordingInterval)
        {
            // Levantamos 10 cm para evitar z-fighting con el suelo
            Vector3 recordedPoint = droneTransform.position + Vector3.up * 0.1f;
            pathPoints.Add(recordedPoint);

            if (pathPoints.Count > maxPathPoints)
                pathPoints.RemoveAt(0);

            lastRecordTime = Time.time;
        }
    }

    void UpdatePathVisualization()
    {
        if (pathLineRenderer == null) return;

        if (pathPoints.Count > 0)
        {
            pathLineRenderer.positionCount = pathPoints.Count;
            pathLineRenderer.SetPositions(pathPoints.ToArray());
        }
        else
        {
            pathLineRenderer.positionCount = 0;
        }
    }

    void UpdateFuturePathPrediction()
    {
        if (futurePathRenderer == null || navMeshAgent == null || !navMeshAgent.isActiveAndEnabled)
        {
            if (futurePathRenderer != null) futurePathRenderer.positionCount = 0;
            return;
        }

        if (navMeshAgent.isOnNavMesh && navMeshAgent.hasPath)
        {
            NavMeshPath path = navMeshAgent.path;
            if (path != null && path.corners != null && path.corners.Length > 1)
            {
                futurePathRenderer.positionCount = path.corners.Length;
                futurePathRenderer.SetPositions(path.corners);
                return;
            }
        }

        futurePathRenderer.positionCount = 0;
    }

    void UpdatePathColors()
    {
        if (pathLineRenderer == null || searchAgent == null) return;
        if (pathPoints.Count == 0) return;

        Color phaseColor = GetColorForPhase(searchAgent.phase);
        pathLineRenderer.startColor = phaseColor;
        pathLineRenderer.endColor = phaseColor;
    }

    Color GetColorForPhase(AgentPhase phase)
    {
        switch (phase)
        {
            case AgentPhase.GoingToGPS: return Color.blue;
            case AgentPhase.Searching:  return Color.yellow;
            case AgentPhase.Approaching:return Color.magenta;
            case AgentPhase.Landing:    return Color.green;
            case AgentPhase.Done:       return Color.green;
            case AgentPhase.Abort:      return Color.red;
            default:                    return Color.white;
        }
    }

    public void ClearPath()
    {
        pathPoints.Clear();
        if (pathLineRenderer != null) pathLineRenderer.positionCount = 0;
        if (futurePathRenderer != null) futurePathRenderer.positionCount = 0;
    }

    public Vector3[] GetPathPoints() => pathPoints.ToArray();

    public float GetPathLength()
    {
        float length = 0f;
        for (int i = 1; i < pathPoints.Count; i++)
            length += Vector3.Distance(pathPoints[i - 1], pathPoints[i]);
        return length;
    }

    public void SetNavMeshAgent(NavMeshAgent agent)
    {
        navMeshAgent = agent;
        referencesInitialized = true;
    }

    void OnDestroy()
    {
        if (createdPathGO && pathLineRenderer != null)
            Destroy(pathLineRenderer.gameObject);
        if (createdFutureGO && futurePathRenderer != null)
            Destroy(futurePathRenderer.gameObject);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        if (pathPoints != null && pathPoints.Count > 0)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(pathPoints[pathPoints.Count - 1], 0.5f);
        }
    }
}
