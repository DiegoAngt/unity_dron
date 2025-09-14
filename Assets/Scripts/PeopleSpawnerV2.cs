// PeopleSpawnerV2.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PeopleSpawnerV2 : MonoBehaviour
{
    [Header("Prefab y cantidad")]
    public GameObject personPrefab;
    public int count = 20;

    [Header("Centro de spawn")]
    public bool centerFromGPSTarget = true;
    public Vector3 centerOffset = Vector3.zero;
    public Vector3 center = Vector3.zero;
    public float radius = 25f;

    [Header("Ajuste al terreno / NavMesh")]
    public bool projectToNavMesh = true;
    public float navSampleMaxDistance = 3f;
    public bool alignToGround = true;
    public LayerMask groundMask;
    public float groundRayHeight = 50f;
    public float extraGroundClearance = 0.0f;

    [Header("Descriptores (opcional)")]
    public DescriptionDB db;
    public PersonDescriptor[] inlinePool;

    [Header("Forzar objetivo del agente")]
    [Tooltip("Incluye SIEMPRE una persona que coincide con el targetDescriptor del SearchAgent.")]
    public bool forceIncludeAgentTarget = true;
    [Range(0f, 1f)] public float includeTargetProbability = 1f;

    [Tooltip("SearchAgent del dron que buscará (para pasarle el designado).")]
    public SearchAgent searchAgent;
    public bool alsoSetAgentCurrentTarget = false;

    private Vector3 runtimeCenter;

    void Start()
    {
        runtimeCenter = ComputeCenter();

        var unique = BuildUniqueCandidates();
        Shuffle(unique);

        int toSpawn = Mathf.Min(count, Mathf.Max(1, unique.Count));
        if (count > unique.Count)
            Debug.LogWarning($"[PeopleSpawnerV2] Pediste {count}, pero solo hay {unique.Count}. Se generan {toSpawn}.");

        bool willIncludeTarget = forceIncludeAgentTarget && searchAgent != null && Random.value <= includeTargetProbability;
        int forcedIndex = willIncludeTarget ? Random.Range(0, toSpawn) : -1;

        for (int i = 0; i < toSpawn; i++)
        {
            // Descriptor elegido
            PersonDescriptor desc = (i == forcedIndex && searchAgent != null)
                ? searchAgent.targetDescriptor
                : unique[i % unique.Count];

            // Posición tentativa
            Vector2 v = Random.insideUnitCircle * radius;
            Vector3 pos = new Vector3(runtimeCenter.x + v.x, runtimeCenter.y, runtimeCenter.z + v.y);

            // Proyección al NavMesh
            if (projectToNavMesh)
            {
                if (!NavMesh.SamplePosition(pos, out NavMeshHit nh, navSampleMaxDistance, NavMesh.AllAreas))
                {
                    // Fallback amplio
                    if (NavMesh.SamplePosition(pos, out nh, 1000f, NavMesh.AllAreas))
                        pos = nh.position;
                }
                else pos = nh.position;
            }

            // Instanciar
            var go = Instantiate(personPrefab, pos, Quaternion.identity);

            // Añadir ClaimableTarget (para que pueda reservarse)
            if (!go.TryGetComponent<ClaimableTarget>(out _))
                go.AddComponent<ClaimableTarget>();

            // Alinear a suelo
            if (alignToGround) SnapBottomToGround(go);

            // Configurar perfil
            var p = go.GetComponent<PersonProfile>();
            if (!p) p = go.AddComponent<PersonProfile>();
            p.hasJacket    = desc.requireJacket;
            p.jacketColor  = desc.jacketColor;
            p.headgearType = desc.headgearType;
            p.headgearColor = desc.headgearColor;
            p.hasBackpack  = desc.requireBackpackSpecified ? desc.hasBackpack : (Random.value > 0.5f);
            p.publicDescription = db ? db.ToHumanText(desc) : desc.ToString();

            // Marcar el objetivo forzado y pasarlo al agente
            if (i == forcedIndex)
            {
                go.name = "TARGET_" + go.name;
                if (searchAgent != null)
                {
                    searchAgent.designatedTarget = go.transform; // clave
                    if (alsoSetAgentCurrentTarget)
                        searchAgent.currentTarget = go.transform;
                }
            }
        }

        Debug.Log($"[PeopleSpawnerV2] Spawn center={runtimeCenter} r={radius:F1} (count={toSpawn})");
    }

    Vector3 ComputeCenter()
    {
        if (centerFromGPSTarget && searchAgent != null)
        {
            var gps = searchAgent.gpsTarget;
            var c = new Vector3(gps.x, center.y, gps.z) + centerOffset;

            // Asegurar centro sobre NavMesh si se desea
            if (projectToNavMesh && NavMesh.SamplePosition(c, out var hitCenter, 1000f, NavMesh.AllAreas))
                c = hitCenter.position;

            return c;
        }
        return center + centerOffset;
    }

    void SnapBottomToGround(GameObject go)
    {
        var t = go.transform;
        Vector3 pos = t.position;

        Vector3 from = pos + Vector3.up * groundRayHeight;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit gh, groundRayHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            return;

        float pivotToBottom = 0f;

        Collider col = go.GetComponentInChildren<Collider>();
        if (col != null) pivotToBottom = pos.y - col.bounds.min.y;
        else
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null) pivotToBottom = pos.y - r.bounds.min.y;
        }

        float newY = gh.point.y + pivotToBottom + extraGroundClearance;
        t.position = new Vector3(pos.x, newY, pos.z);
    }

    List<PersonDescriptor> BuildUniqueCandidates()
    {
        var set = new HashSet<PersonDescriptor>();
        var list = new List<PersonDescriptor>();

        if (db && db.pool != null && db.pool.Length > 0) AddUnique(db.pool, set, list);
        if (inlinePool != null && inlinePool.Length > 0) AddUnique(inlinePool, set, list);

        if (list.Count == 0)
        {
            ItemColor[] colors = (ItemColor[])System.Enum.GetValues(typeof(ItemColor));
            HeadgearType[] headgears = (HeadgearType[])System.Enum.GetValues(typeof(HeadgearType));

            foreach (bool jacket in new[] { false, true })
            foreach (var jColor in colors)
            foreach (var hg in headgears)
            {
                if (hg == HeadgearType.None)
                {
                    foreach (var b in new[] { false, true })
                    {
                        var d = new PersonDescriptor
                        {
                            requireJacket = jacket,
                            jacketColor = jColor,
                            headgearType = HeadgearType.None,
                            requireBackpackSpecified = true,
                            hasBackpack = b
                        };
                        if (set.Add(d)) list.Add(d);
                    }
                }
                else
                {
                    foreach (var hColor in colors)
                    foreach (var b in new[] { false, true })
                    {
                        var d = new PersonDescriptor
                        {
                            requireJacket = jacket,
                            jacketColor = jColor,
                            headgearType = hg,
                            headgearColor = hColor,
                            requireBackpackSpecified = true,
                            hasBackpack = b
                        };
                        if (set.Add(d)) list.Add(d);
                    }
                }
            }
        }
        return list;
    }

    void AddUnique(PersonDescriptor[] source, HashSet<PersonDescriptor> set, List<PersonDescriptor> list)
    {
        foreach (var d in source) if (set.Add(d)) list.Add(d);
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 c = Application.isPlaying ? runtimeCenter :
                    (centerFromGPSTarget && searchAgent != null
                        ? new Vector3(searchAgent.gpsTarget.x, center.y, searchAgent.gpsTarget.z) + centerOffset
                        : center + centerOffset);
        Gizmos.DrawWireSphere(c, radius);
    }
}
