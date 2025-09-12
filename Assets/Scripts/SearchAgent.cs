using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public enum AgentPhase { GoingToGPS, Searching, Approaching, Landing, Done, Abort }

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public class SearchAgent : MonoBehaviour
{
    [Header("Objetivo inicial")]
    public Vector3 gpsTarget;
    public PersonDescriptor targetDescriptor;

    [Header("Percepción & búsqueda")]
    public float searchRadius = 20f;
    public float fov = 120f;
    public float eyeHeight = 1.8f;

    [Header("Máscaras")]
    public LayerMask personMask;
    public LayerMask obstacleMask;
    public LayerMask groundMask;

    [Header("Patrulla")]
    public int maxLocalWaypoints = 10;

    [Header("Aproximación & aterrizaje")]
    public float minLandingDistance = 2f;
    public float maxLandingDistance = 4f;
    public float preLandingHeight = 5f;
    public float descendSpeed = 2.5f;
    public float ascendSpeed = 4f;
    public int maxLandingAttempts = 3;
    public float matchThreshold = 1.5f;
    public float landingClearance = 0.02f;

    [Header("Ajuste fino de aterrizaje")]
    [Tooltip("Baja un poco más el dron al finalizar el descenso (en metros).")]
    [Range(0f, 0.03f)]
    public float landingExtraDrop = 0.005f;

    [Header("Navegación GPS Larga Distancia")]
    public bool useLongDistanceNavigation = true;
    public float longDistanceAltitude = 150f;

    [Header("Debug")]
    public bool debugFSM = true;
    public float fsmUpdateFrequency = 0.1f; // Realtime

    public LineRenderer pathLineRenderer;
    public ParticleSystem landingParticles;

    [Header("Visualización de Trayectoria")]
    public PathVisualizer pathVisualizer;
    public bool enablePathRecording = true;

    [Header("Finalización/Salida")]
    [Tooltip("Al terminar (Done/Abort), detener el Play Mode del Editor.")]
    public bool stopEditorOnFinish = true;

    [Tooltip("Al terminar (Done/Abort) en build, cerrar la app.")]
    public bool quitAppOnFinish = false;

    [Header("Estado (solo lectura)")]
    [SerializeField] private AgentPhase _phase = AgentPhase.GoingToGPS;
    public Transform currentTarget;

    // Evento para notificar cambios de fase
    public event Action<AgentPhase> OnMissionPhaseChange;

    // Propiedad de fase con notificación
    public AgentPhase phase
    {
        get { return _phase; }
        set
        {
            if (_phase != value)
            {
                _phase = value;
                OnMissionPhaseChange?.Invoke(value);
            }
        }
    }

    private NavMeshAgent agent;
    private Vector3 localSearchCenter;
    private float lastWaypointTime;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    bool TryPlaceOnNavMesh(Vector3 desired, float maxDist, out Vector3 onNav)
    {
        if (NavMesh.SamplePosition(desired, out var hit, maxDist, NavMesh.AllAreas))
        { onNav = hit.position; return true; }
        onNav = desired; return false;
    }

    void Start()
    {
        // Inicializar visualización de trayectoria
        InitializePathVisualization();

        // Arrancamos SIEMPRE el monitor de debug para ver logs en consola
        phase = AgentPhase.GoingToGPS;
        StartCoroutine(FSMDebugMonitor());

        // Si hay LongDistanceNavigator, delegamos la navegación; la FSM corta no arranca.
        // (Si quieres que la detección de la FSM ocurra con LongDistanceNavigator activo, desactiva este return.)
        if (useLongDistanceNavigation && GetComponent<LongDistanceNavigator>() != null)
        {
            return;
        }

        // Navegación corta (setup inicial)
        if (TryPlaceOnNavMesh(transform.position, 10f, out var startPos))
            agent.Warp(startPos);
        else
            Debug.LogError("[SearchAgent] No hay NavMesh bajo el agente. Muévelo a zona azul o ajusta NavMeshSurface.");

        if (TryPlaceOnNavMesh(gpsTarget, 10f, out var navGps))
            gpsTarget = navGps;

        agent.enabled = true;
        agent.SetDestination(gpsTarget);

        StartCoroutine(FSM());
    }

    void InitializePathVisualization()
    {
        if (enablePathRecording && pathVisualizer == null)
        {
            pathVisualizer = gameObject.AddComponent<PathVisualizer>();
            pathVisualizer.droneTransform = transform;
            pathVisualizer.searchAgent = this;

            // Línea simple hacia el target (opcional, visual)
            if (pathLineRenderer == null)
            {
                GameObject lineRendererGO = new GameObject("PathLineRenderer");
                lineRendererGO.transform.SetParent(transform);
                pathLineRenderer = lineRendererGO.AddComponent<LineRenderer>();
                pathLineRenderer.startWidth = 0.2f;
                pathLineRenderer.endWidth = 0.2f;
                pathLineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                pathLineRenderer.startColor = Color.green;
                pathLineRenderer.endColor = Color.green;
                pathLineRenderer.useWorldSpace = true;
            }
        }
    }

    IEnumerator FSMDebugMonitor()
    {
        while (true)
        {
            if (debugFSM)
            {
                Debug.Log($"[FSM] Fase: {phase} | Pos: {transform.position} | TieneTarget: {currentTarget != null}");

                if (currentTarget != null)
                {
                    float distance = Vector3.Distance(transform.position, currentTarget.position);
                    Debug.Log($"[FSM] Distancia al target: {distance:F1} m");
                }

                if (agent != null && agent.isActiveAndEnabled)
                {
                    if (agent.hasPath)
                        Debug.Log($"[NAV] Destino: {agent.destination} | Restante: {agent.remainingDistance:F1} m");
                    else if (!agent.pathPending)
                        Debug.Log("[NAV] Sin path activo.");
                }
            }

            // Realtime para que loggee aunque Time.timeScale = 0
            yield return new WaitForSecondsRealtime(fsmUpdateFrequency);
        }
    }

    IEnumerator FSM()
    {
        while (true)
        {
            switch (phase)
            {
                case AgentPhase.GoingToGPS:
                    yield return StartCoroutine(GoingToGPSPhase());
                    break;

                case AgentPhase.Searching:
                    yield return StartCoroutine(SearchingPhase());
                    break;

                case AgentPhase.Approaching:
                    yield return StartCoroutine(ApproachingPhase());
                    break;

                case AgentPhase.Landing:
                    yield return StartCoroutine(LandingPhase());
                    break;

                case AgentPhase.Done:
                    Debug.Log("[FSM] Misión completada con éxito!");
                    OnMissionComplete(true);
                    EndSimulationIfNeeded();
                    yield break;

                case AgentPhase.Abort:
                    Debug.Log("[FSM] Misión abortada.");
                    OnMissionComplete(false);
                    EndSimulationIfNeeded();
                    yield break;
            }
            yield return null;
        }
    }

    void OnMissionComplete(bool success)
    {
        if (pathVisualizer != null)
        {
            float pathLength = pathVisualizer.GetPathLength();
            Debug.Log($"[MISIÓN] Distancia total recorrida: {pathLength:F1} m");
        }
    }

    void EndSimulationIfNeeded()
    {
        if (stopEditorOnFinish)
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #endif
        }

        if (quitAppOnFinish)
        {
            Application.Quit();
        }
    }

    IEnumerator GoingToGPSPhase()
    {
        Debug.Log("[FSM] Iniciando fase: GoingToGPS");
        float timeout = 60f;
        float startTime = Time.time;

        while (phase == AgentPhase.GoingToGPS)
        {
            if (Time.time - startTime > timeout)
            {
                Debug.LogWarning("[FSM] Timeout en GoingToGPS. Forzando búsqueda.");
                phase = AgentPhase.Searching;
                yield break;
            }

            if (!agent.enabled || !agent.isOnNavMesh)
            {
                if (TryPlaceOnNavMesh(transform.position, 10f, out var p))
                    agent.Warp(p);
                yield return null;
                continue;
            }

            if (!agent.hasPath && !agent.pathPending)
                agent.SetDestination(gpsTarget);

            if (!agent.pathPending && agent.remainingDistance < 1.5f)
            {
                localSearchCenter = transform.position;
                phase = AgentPhase.Searching;
                Debug.Log("[FSM] Llegó al GPS. Iniciando búsqueda.");
                yield break;
            }
            yield return null;
        }
    }

    IEnumerator SearchingPhase()
    {
        Debug.Log("[FSM] Iniciando fase: Searching");
        int wp = 0;
        lastWaypointTime = Time.time;

        while (phase == AgentPhase.Searching)
        {
            // DETECCIÓN -> NO finaliza simulación; solo log + continuar flujo normal
            if (TrySenseBestMatch(out Transform candidate))
            {
                currentTarget = candidate;

                // Log de confirmación con la descripción del perfil
                string desc = "(sin descripción)";
                var prof = candidate.GetComponent<PersonProfile>();
                if (prof != null && !string.IsNullOrEmpty(prof.publicDescription))
                    desc = prof.publicDescription;

                Debug.Log($"[FSM] ✅ Objetivo encontrado: {candidate.name} | Descripción: {desc}");

                phase = AgentPhase.Approaching; // seguir con el flujo normal
                yield break;
            }

            // Patrulla de búsqueda
            if (!agent.pathPending && agent.remainingDistance < 0.8f)
            {
                if (Time.time - lastWaypointTime > 3f)
                {
                    Vector3 p = RandomPointInDisk(localSearchCenter, searchRadius);
                    if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    {
                        agent.SetDestination(hit.position);
                        lastWaypointTime = Time.time;
                        Debug.Log($"[FSM] Nuevo waypoint: {hit.position}");
                    }

                    if (++wp >= maxLocalWaypoints)
                    {
                        wp = 0;
                        localSearchCenter = transform.position;
                        Debug.Log("[FSM] Reiniciando patrulla desde nueva posición.");
                    }
                }
            }

            // Timeout de búsqueda
            if (Time.time - lastWaypointTime > 30f)
            {
                Debug.LogWarning("[FSM] Timeout de búsqueda. Recentrando.");
                localSearchCenter = transform.position;
                lastWaypointTime = Time.time;

                Vector3 p = RandomPointInDisk(localSearchCenter, searchRadius);
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    agent.SetDestination(hit.position);
            }

            yield return null;
        }
    }

    IEnumerator ApproachingPhase()
    {
        Debug.Log("[FSM] Iniciando fase: Approaching");
        float timeout = 30f;
        float startTime = Time.time;

        if (currentTarget == null)
        {
            Debug.LogWarning("[FSM] No hay target para aproximarse. Volviendo a buscar.");
            phase = AgentPhase.Searching;
            yield break;
        }

        Vector3 landingXZ = SafeOffsetXZ(currentTarget.position, minLandingDistance, maxLandingDistance);

        if (NavMesh.SamplePosition(landingXZ, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            Debug.Log($"[FSM] Aproximándose a: {hit.position}");

            while (phase == AgentPhase.Approaching)
            {
                if (Time.time - startTime > timeout)
                {
                    Debug.LogWarning("[FSM] Timeout en aproximación. Volviendo a buscar.");
                    phase = AgentPhase.Searching;
                    yield break;
                }

                if (!agent.pathPending && agent.remainingDistance <= 0.6f)
                {
                    phase = AgentPhase.Landing;
                    Debug.Log("[FSM] Posición de aterrizaje alcanzada.");
                    yield break;
                }

                if (currentTarget == null)
                {
                    Debug.LogWarning("[FSM] Target perdido durante aproximación.");
                    phase = AgentPhase.Searching;
                    yield break;
                }

                yield return null;
            }
        }
        else
        {
            Debug.LogWarning("[FSM] No se encontró punto de aterrizaje válido. Volviendo a buscar.");
            phase = AgentPhase.Searching;
        }
    }

    IEnumerator LandingPhase()
    {
        Debug.Log("[FSM] Iniciando fase: Landing");
        int attempts = 0;
        bool success = false;

        if (currentTarget == null)
        {
            Debug.LogWarning("[FSM] No hay target para aterrizar.");
            phase = AgentPhase.Searching;
            yield break;
        }

        agent.enabled = false;

        while (attempts < maxLandingAttempts && !success)
        {
            attempts++;
            Debug.Log($"[FSM] Intento de aterrizaje {attempts}/{maxLandingAttempts}");

            yield return StartCoroutine(AscendTo(preLandingHeight));

            bool result = false;
            yield return StartCoroutine(DescendAndCheck(transform.position, r => result = r));
            success = result;

            if (!success && attempts < maxLandingAttempts)
            {
                Debug.Log("[FSM] Aterrizaje fallido. Reintentando...");
                Vector3 newXZ = SafeOffsetXZ(currentTarget.position, minLandingDistance, maxLandingDistance + 1.5f);
                agent.enabled = true;
                if (NavMesh.SamplePosition(newXZ, out NavMeshHit hit2, 2f, NavMesh.AllAreas))
                    agent.Warp(hit2.position);
                agent.enabled = false;
            }
        }

        agent.enabled = true;
        phase = success ? AgentPhase.Done : AgentPhase.Abort;
        Debug.Log($"[FSM] Aterrizaje {(success ? "éxito" : "fallido")}");
    }

    bool TrySenseBestMatch(out Transform best)
    {
        best = null;
        Collider[] hits = Physics.OverlapSphere(transform.position, searchRadius, personMask);

        Debug.Log($"[DETECCIÓN] Personas en radio ({searchRadius} m): {hits.Length}");

        float bestScore = -1f;

        foreach (var h in hits)
        {
            var profile = h.GetComponent<PersonProfile>();
            if (!profile)
            {
                Debug.LogWarning($"[DETECCIÓN] Objeto en capa Person sin PersonProfile: {h.name}");
                continue;
            }

            // FOV
            Vector3 dir = (h.transform.position - transform.position);
            Vector3 dirFlat = new Vector3(dir.x, 0, dir.z);
            float angle = Vector3.Angle(transform.forward, dirFlat.normalized);
            if (angle > fov * 0.5f)
            {
                Debug.Log($"[DETECCIÓN] Fuera de FOV: {angle:F1}° > {fov * 0.5f}°");
                continue;
            }

            // Línea de vista
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 targetCenter = h.bounds.center;
            Vector3 rayDir = (targetCenter - eye).normalized;
            int losMask = obstacleMask.value & ~(1 << gameObject.layer);

            if (Physics.Raycast(eye, rayDir, out RaycastHit rh, searchRadius, losMask, QueryTriggerInteraction.Ignore))
            {
                if (rh.collider.transform != h.transform)
                {
                    Debug.Log($"[DETECCIÓN] Obstruído por: {rh.collider.name}");
                    continue;
                }
            }

            float s = profile.ScoreMatch(targetDescriptor);
            Debug.Log($"[DETECCIÓN] {h.name}: {profile.publicDescription} | Puntuación: {s:F2}/{matchThreshold}");

            if (s > bestScore && s >= matchThreshold)
            {
                bestScore = s;
                best = h.transform;
                Debug.Log($"[DETECCIÓN] ✅ OBJETIVO ENCONTRADO: {s:F2} puntos");
            }
        }

        if (best == null)
            Debug.Log($"[DETECCIÓN] ❌ No se encontró objetivo >= umbral ({matchThreshold})");

        return best != null;
    }

    Vector3 RandomPointInDisk(Vector3 center, float r)
    {
        Vector2 v = Random.insideUnitCircle * r;
        return new Vector3(center.x + v.x, center.y, center.z + v.y);
    }

    Vector3 SafeOffsetXZ(Vector3 target, float min, float max)
    {
        float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        float d = Random.Range(min, max);
        Vector3 off = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * d;
        return target + off;
    }

    IEnumerator AscendTo(float height)
    {
        float targetY = Mathf.Max(transform.position.y, height);
        Debug.Log($"[ATERRIZAJE] Ascendiendo a {targetY} m");

        while (transform.position.y < targetY - 0.005f)
        {
            transform.position += Vector3.up * (ascendSpeed * Time.deltaTime);
            yield return null;
        }
    }

    IEnumerator DescendAndCheck(Vector3 dropPoint, Action<bool> onCompleted)
    {
        Debug.Log("[ATERRIZAJE] Iniciando descenso...");

        // 1) Ray al suelo
        if (!Physics.Raycast(dropPoint + Vector3.up * 50f, Vector3.down,
            out RaycastHit ground, 100f, groundMask, QueryTriggerInteraction.Ignore))
        {
            Debug.LogWarning("[ATERRIZAJE] No se encontró suelo para aterrizar.");
            onCompleted?.Invoke(false);
            yield break;
        }

        bool collidedWithPerson = false;

        var col = GetComponent<Collider>();
        if (!col)
        {
            Debug.LogError("[ATERRIZAJE] No hay Collider en el dron.");
            onCompleted?.Invoke(false);
            yield break;
        }

        // Desactivar colisión dura mientras bajamos
        bool originalTrigger = col.isTrigger;
        col.isTrigger = true;

        // Detector de persona durante el descenso
        var detector = gameObject.AddComponent<LandingCollisionDetector>();
        detector.personMask = personMask;
        detector.onPersonHit = () =>
        {
            collidedWithPerson = true;
            Debug.Log("[ATERRIZAJE] ⚠️ Colisión detectada durante descenso!");
        };

        // Offset desde el pivot a la base del collider
        float pivotToBottom = transform.position.y - col.bounds.min.y;

        // Altura objetivo: suelo + (clearance - extraDrop, clamp >= 0) + offset pivot→base
        float effectiveClearance = Mathf.Max(0f, landingClearance - landingExtraDrop);
        float targetY = ground.point.y + effectiveClearance + pivotToBottom;

        Debug.Log($"[ATERRIZAJE] Descendiendo hasta Y={targetY:F3} (suelo={ground.point.y:F3}, clearance={landingClearance:F3}, extraDrop={landingExtraDrop:F3}, clearanceEf={effectiveClearance:F3}, offset={pivotToBottom:F3})");

        // Descenso con tope exacto
        while (transform.position.y > targetY && !collidedWithPerson)
        {
            float step = descendSpeed * Time.deltaTime;
            float newY = Mathf.Max(transform.position.y - step, targetY);
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            yield return null;
        }

        if (!collidedWithPerson)
            transform.position = new Vector3(transform.position.x, targetY, transform.position.z);

        Destroy(detector);
        col.isTrigger = originalTrigger;

        bool success = !collidedWithPerson;
        Debug.Log($"[ATERRIZAJE] Descenso {(success ? "completado" : "fallido")} (base del collider a ~{effectiveClearance:F3} m del suelo)");
        onCompleted?.Invoke(success);
    }

    public void UpdateVisualPath()
    {
        if (pathLineRenderer != null && currentTarget != null)
        {
            pathLineRenderer.positionCount = 2;
            pathLineRenderer.SetPosition(0, transform.position);
            pathLineRenderer.SetPosition(1, currentTarget.position);
        }
    }

    void Update()
    {
        if (currentTarget != null)
            UpdateVisualPath();
    }

    void OnDestroy()
    {
        if (pathVisualizer != null)
            pathVisualizer.ClearPath();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, searchRadius);

        // Dibujar FOV
        Gizmos.color = Color.cyan;
        Vector3 eyePosition = transform.position + Vector3.up * eyeHeight;
        float halfFOV = fov / 2f;
        Quaternion leftRayRotation = Quaternion.AngleAxis(-halfFOV, Vector3.up);
        Quaternion rightRayRotation = Quaternion.AngleAxis(halfFOV, Vector3.up);
        Vector3 leftDirection = leftRayRotation * transform.forward;
        Vector3 rightDirection = rightRayRotation * transform.forward;
        Gizmos.DrawRay(eyePosition, leftDirection * searchRadius);
        Gizmos.DrawRay(eyePosition, rightDirection * searchRadius);
    }
}

public class LandingCollisionDetector : MonoBehaviour
{
    public LayerMask personMask;
    public Action onPersonHit;

    void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & personMask) != 0)
        {
            Debug.Log($"[COLISIÓN] Detectada persona: {other.name}");
            onPersonHit?.Invoke();
        }
    }
}
