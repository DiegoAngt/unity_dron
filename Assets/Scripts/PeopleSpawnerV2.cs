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

    [Header("Escalado de radio con distancia al GPS (opcional)")]
    public bool scaleRadiusByDistanceToTarget = false;
    public float radiusPerMeter = 0.1f;
    public float minRadius = 5f;
    public float maxRadius = 60f;

    [Header("Ajuste al terreno / NavMesh")]
    public bool projectToNavMesh = true;
    public float navSampleMaxDistance = 3f;

    public bool alignToGround = true;
    public LayerMask groundMask;
    public float groundRayHeight = 50f;

    [Tooltip("Levanta un poquito el objeto sobre el suelo (m).")]
    public float extraGroundClearance = 0.0f;

    [Header("Descriptores (opcional)")]
    public DescriptionDB db;
    public PersonDescriptor[] inlinePool;

    [Header("Forzar objetivo del agente")]
    public bool forceIncludeAgentTarget = true;
    [Range(0f, 1f)] public float includeTargetProbability = 1f;
    public SearchAgent searchAgent;
    public bool alsoSetAgentCurrentTarget = false;

    private Vector3 runtimeCenter;

    void Start()
    {
        runtimeCenter = ComputeCenter();
        float finalRadius = ComputeRadius(runtimeCenter);

        var unique = BuildUniqueCandidates();
        Shuffle(unique);

        int toSpawn = Mathf.Min(count, Mathf.Max(1, unique.Count));
        if (count > unique.Count)
            Debug.LogWarning($"[PeopleSpawnerV2] Pediste {count}, pero solo hay {unique.Count} combinaciones únicas. Se generan {toSpawn}.");

        bool willIncludeTarget = forceIncludeAgentTarget && searchAgent != null && Random.value <= includeTargetProbability;
        int forcedIndex = willIncludeTarget ? Random.Range(0, toSpawn) : -1;

        for (int i = 0; i < toSpawn; i++)
        {
            // Descriptor
            PersonDescriptor desc = (i == forcedIndex && searchAgent != null)
                ? searchAgent.targetDescriptor
                : unique[i % unique.Count];

            // Posición XZ
            Vector2 v = Random.insideUnitCircle * finalRadius;
            Vector3 pos = new Vector3(runtimeCenter.x + v.x, runtimeCenter.y, runtimeCenter.z + v.y);

            // Opcional: ajustar a NavMesh
            if (projectToNavMesh && NavMesh.SamplePosition(pos, out NavMeshHit nh, navSampleMaxDistance, NavMesh.AllAreas))
                pos = nh.position;

            // Instanciar
            var go = Instantiate(personPrefab, pos, Quaternion.identity);

            // Alinear a suelo: coloca la BASE del collider/render justo en el suelo
            if (alignToGround)
                SnapBottomToGround(go);

            // (Opcional) Forzar capa "Person" si la usas en personMask
            // go.layer = LayerMask.NameToLayer("Person");

            // PersonProfile
            var p = go.GetComponent<PersonProfile>();
            if (!p) p = go.AddComponent<PersonProfile>();
            p.hasJacket    = desc.requireJacket;
            p.jacketColor  = desc.jacketColor;
            p.headgearType = desc.headgearType;
            p.headgearColor = desc.headgearColor;
            p.hasBackpack  = desc.requireBackpackSpecified ? desc.hasBackpack : (Random.value > 0.5f);
            p.publicDescription = db ? db.ToHumanText(desc) : desc.ToString();

            if (i == forcedIndex)
            {
                go.name = "TARGET_" + go.name;
                if (alsoSetAgentCurrentTarget && searchAgent != null)
                    searchAgent.currentTarget = go.transform;
            }
        }

        Debug.Log($"[PeopleSpawnerV2] Spawn center={runtimeCenter} radius={finalRadius:F1} (count={toSpawn})");
    }

    // === Alinear base del objeto al suelo ===
    void SnapBottomToGround(GameObject go)
    {
        var t = go.transform;
        Vector3 pos = t.position;

        // Raycast al suelo en la XZ del objeto
        Vector3 from = pos + Vector3.up * groundRayHeight;
        if (!Physics.Raycast(from, Vector3.down, out RaycastHit gh, groundRayHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
            return; // no hay suelo bajo ese punto

        // Calcula offset pivot→base usando bounds en mundo del collider (o renderer si no hay collider)
        float pivotToBottom = 0f;

        Collider col = go.GetComponentInChildren<Collider>();
        if (col != null)
        {
            // bounds.min.y es la base del collider en mundo
            pivotToBottom = pos.y - col.bounds.min.y;
        }
        else
        {
            Renderer r = go.GetComponentInChildren<Renderer>();
            if (r != null)
                pivotToBottom = pos.y - r.bounds.min.y;
            else
                pivotToBottom = 0f; // fallback si no hay nada (no mover en Y)
        }

        float newY = gh.point.y + pivotToBottom + extraGroundClearance;
        t.position = new Vector3(pos.x, newY, pos.z);
    }

    Vector3 ComputeCenter()
    {
        if (centerFromGPSTarget && searchAgent != null)
        {
            var gps = searchAgent.gpsTarget;
            return new Vector3(gps.x, center.y, gps.z) + centerOffset;
        }
        else
        {
            return center + centerOffset;
        }
    }

    float ComputeRadius(Vector3 spawnCenter)
    {
        if (!scaleRadiusByDistanceToTarget || searchAgent == null)
            return radius;

        Vector2 ag = new Vector2(searchAgent.transform.position.x, searchAgent.transform.position.z);
        Vector2 gp = new Vector2(searchAgent.gpsTarget.x, searchAgent.gpsTarget.z);
        float d = Vector2.Distance(ag, gp);

        float r = radius + d * radiusPerMeter;
        return Mathf.Clamp(r, minRadius, maxRadius);
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
