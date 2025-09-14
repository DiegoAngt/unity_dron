// SearchAgent.cs
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
    [Range(0f, 0.03f)] public float landingExtraDrop = 0.005f;

    [Header("Navegación GPS Larga Distancia")]
    public bool useLongDistanceNavigation = true;
    public float longDistanceAltitude = 150f;

    [Header("Debug")]
    public bool debugFSM = true;
    public float fsmUpdateFrequency = 0.1f;

    public LineRenderer pathLineRenderer;
    public ParticleSystem landingParticles;

    [Header("Visualización de Trayectoria")]
    public PathVisualizer pathVisualizer;
    public bool enablePathRecording = true;

    [Header("Objetivo designado (opcional)")]
    [Tooltip("Si el spawner lo asigna, este agente intentará perseguir exactamente ese Transform.")]
    public Transform designatedTarget;
    public bool alwaysChaseDesignated = true;

    [Header("Finalización/Salida")]
    public bool stopEditorOnFinish = true;
    public bool quitAppOnFinish = false;

    [Header("Estado (solo lectura)")]
    [SerializeField] private AgentPhase _phase = AgentPhase.GoingToGPS;
    public Transform currentTarget;

    public event Action<AgentPhase> OnMissionPhaseChange;

    public AgentPhase phase
    {
        get => _phase;
        set { if (_phase != value) { _phase = value; OnMissionPhaseChange?.Invoke(value); } }
    }

    private NavMeshAgent agent;
    private Vector3 localSearchCenter;
    private float lastWaypointTime;

    // ===== RESERVA (CLAIM) =====
    private ClaimableTarget currentClaim;

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
        InitializePathVisualization();

        phase = AgentPhase.GoingToGPS;
        StartCoroutine(FSMDebugMonitor());

        if (useLongDistanceNavigation && GetComponent<LongDistanceNavigator>() != null)
            return;

        if (TryPlaceOnNavMesh(transform.position, 10f, out var startPos))
            agent.Warp(startPos);
        else
            Debug.LogError("[SearchAgent] No hay NavMesh bajo el agente.");

        if (TryPlaceOnNavMesh(gpsTarget, 1000f, out var navGps))
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
                    if (agent.hasPath) Debug.Log($"[NAV] Destino: {agent.destination} | Restante: {agent.remainingDistance:F1} m");
                    else if (!agent.pathPending) Debug.Log("[NAV] Sin path activo.");
                }
            }
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
                    yield return StartCoroutine(GoingToGPSPhase()); break;
                case AgentPhase.Searching:
                    yield return StartCoroutine(SearchingPhase()); break;
                case AgentPhase.Approaching:
                    yield return StartCoroutine(ApproachingPhase()); break;
                case AgentPhase.Landing:
                    yield return StartCoroutine(LandingPhase()); break;
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
        ReleaseClaim(); // liberar reserva
    }

    void EndSimulationIfNeeded()
    {
#if UNITY_EDITOR
        if (stopEditorOnFinish) UnityEditor.EditorApplication.isPlaying = false;
#endif
        if (quitAppOnFinish) Application.Quit();
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
                phase = AgentPhase.Searching; yield break;
            }

            if (!agent.enabled || !agent.isOnNavMesh)
            {
                if (TryPlaceOnNavMesh(transform.position, 10f, out var p)) agent.Warp(p);
                yield return null; continue;
            }

            if (!agent.hasPath && !agent.pathPending) agent.SetDestination(gpsTarget);

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
            // 1) Prioridad: objetivo designado (si existe) -> intentar CLAIM
            if (alwaysChaseDesignated && designatedTarget != null)
            {
                if (TryClaimAndSet(designatedTarget))
                {
                    var prof = designatedTarget.GetComponent<PersonProfile>();
                    string desc = (prof != null && !string.IsNullOrEmpty(prof.publicDescription)) ? prof.publicDescription : "(sin descripción)";
                    Debug.Log($"[FSM] 🎯 Designado reclamado por {name}: {desc}");
                    phase = AgentPhase.Approaching; yield break;
                }
                // si no se pudo reclamar, otro dron ya lo tiene → seguir buscando
            }

            // 2) Búsqueda por coincidencia -> intentar CLAIM del candidato
            if (TrySenseBestMatch(out Transform candidate))
            {
                if (TryClaimAndSet(candidate))
                {
                    var prof = candidate.GetComponent<PersonProfile>();
                    string desc = (prof != null && !string.IsNullOrEmpty(prof.publicDescription)) ? prof.publicDescription : "(sin descripción)";
                    Debug.Log($"[FSM] ✅ Coincidencia reclamada por {name}: {desc}");
                    phase = AgentPhase.Approaching; yield break;
                }
            }

            // 3) Patrulla
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
                        wp = 0; localSearchCenter = transform.position;
                        Debug.Log("[FSM] Reiniciando patrulla desde nueva posición.");
                    }
                }
            }

            // 4) Timeout de búsqueda
            if (Time.time - lastWaypointTime > 30f)
            {
                Debug.LogWarning("[FSM] Timeout de búsqueda. Recentrando.");
                localSearchCenter = transform.position; lastWaypointTime = Time.time;

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
            ReleaseClaim();
            phase = AgentPhase.Searching; yield break;
        }

        Vector3 landingXZ = SafeOffsetXZ(currentTarget.position, minLandingDistance, maxLandingDistance);

        if (NavMesh.SamplePosition(landingXZ, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            Debug.Log($"[FSM] Aproximándose a: {hit.position}");

            while (phase == AgentPhase.Approaching)
            {
                RefreshClaim(); // mantener la reserva viva

                if (Time.time - startTime > timeout)
                {
                    Debug.LogWarning("[FSM] Timeout en aproximación. Volviendo a buscar.");
                    ReleaseClaim();
                    phase = AgentPhase.Searching; yield break;
                }

                if (!agent.pathPending && agent.remainingDistance <= 0.6f)
                {
                    phase = AgentPhase.Landing;
                    Debug.Log("[FSM] Posición de aterrizaje alcanzada."); yield break;
                }

                if (currentTarget == null)
                {
                    Debug.LogWarning("[FSM] Target perdido durante aproximación.");
                    ReleaseClaim();
                    phase = AgentPhase.Searching; yield break;
                }

                yield return null;
            }
        }
        else
        {
            Debug.LogWarning("[FSM] No se encontró punto de aterrizaje válido. Volviendo a buscar.");
            ReleaseClaim();
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
            ReleaseClaim();
            phase = AgentPhase.Searching; yield break;
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

            RefreshClaim(); // refrescar durante los intentos
        }

        agent.enabled = true;
        phase = success ? AgentPhase.Done : AgentPhase.Abort;
        Debug.Log($"[FSM] Aterrizaje {(success ? "éxito" : "fallido")}");

        // La reserva se libera en OnMissionComplete()
    }

    bool TrySenseBestMatch(out Transform best)
    {
        best = null;

        // Si hay designado y queremos usarlo siempre, devolverlo como mejor candidato
        if (alwaysChaseDesignated && designatedTarget != null)
        {
            best = designatedTarget;
            return true;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, searchRadius, personMask);
        float bestScore = -1f;

        foreach (var h in hits)
        {
            var profile = h.GetComponent<PersonProfile>();
            if (!profile) continue;

            // FOV
            Vector3 dir = (h.transform.position - transform.position);
            Vector3 dirFlat = new Vector3(dir.x, 0, dir.z);
            float angle = Vector3.Angle(transform.forward, dirFlat.normalized);
            if (angle > fov * 0.5f) continue;

            // Línea de vista
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 targetCenter = h.bounds.center;
            Vector3 rayDir = (targetCenter - eye).normalized;
            int losMask = obstacleMask.value & ~(1 << gameObject.layer);
            if (Physics.Raycast(eye, rayDir, out RaycastHit rh, searchRadius, losMask, QueryTriggerInteraction.Ignore))
            {
                if (rh.collider.transform != h.transform) continue;
            }

            float s = profile.ScoreMatch(targetDescriptor);
            if (s > bestScore && s >= matchThreshold)
            {
                bestScore = s;
                best = h.transform;
            }
        }
        return best != null;
    }

    // ==== CLAIM helpers ====
    bool TryClaimAndSet(Transform t)
    {
        if (t == null) return false;
        var c = t.GetComponent<ClaimableTarget>();
        if (c == null)
        {
            // Sin sistema de claim, seguir normal
            currentTarget = t;
            return true;
        }
        if (c.TryClaim(this))
        {
            currentClaim = c;
            currentTarget = t;
            return true;
        }
        return false; // otro dron ya lo reclamó
    }

    void RefreshClaim()
    {
        if (currentClaim != null) currentClaim.Refresh(this);
    }

    void ReleaseClaim()
    {
        if (currentClaim != null) currentClaim.Release(this);
        currentClaim = null;
    }

    // ==== Utilidades varias ====
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
            RefreshClaim();
            yield return null;
        }
    }

    IEnumerator DescendAndCheck(Vector3 dropPoint, Action<bool> onCompleted)
    {
        Debug.Log("[ATERRIZAJE] Iniciando descenso...");

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

        bool originalTrigger = col.isTrigger;
        col.isTrigger = true;

        var detector = gameObject.AddComponent<LandingCollisionDetector>();
        detector.personMask = personMask;
        detector.onPersonHit = () =>
        {
            collidedWithPerson = true;
            Debug.Log("[ATERRIZAJE] ⚠️ Colisión detectada durante descenso!");
        };

        float pivotToBottom = transform.position.y - col.bounds.min.y;
        float effectiveClearance = Mathf.Max(0f, landingClearance - landingExtraDrop);
        float targetY = ground.point.y + effectiveClearance + pivotToBottom;

        while (transform.position.y > targetY && !collidedWithPerson)
        {
            float step = descendSpeed * Time.deltaTime;
            float newY = Mathf.Max(transform.position.y - step, targetY);
            transform.position = new Vector3(transform.position.x, newY, transform.position.z);
            RefreshClaim();
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
        {
            UpdateVisualPath();
            RefreshClaim(); // refresco continuo por seguridad
        }
    }

    void OnDestroy()
    {
        ReleaseClaim();
        if (pathVisualizer != null) pathVisualizer.ClearPath();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, searchRadius);

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
