// ClaimableTarget.cs
using UnityEngine;

public class ClaimableTarget : MonoBehaviour
{
    public SearchAgent claimedBy;
    public float claimTimestamp;
    [Tooltip("Si el agente no refresca en este tiempo, se libera solo.")]
    public float claimTimeout = 15f;

    public bool TryClaim(SearchAgent agent)
    {
        CleanupIfStale();
        if (claimedBy == null)
        {
            claimedBy = agent;
            claimTimestamp = Time.time;
            return true;
        }
        return claimedBy == agent; // reentrante: el mismo agente la sigue teniendo
    }

    public void Refresh(SearchAgent agent)
    {
        if (claimedBy == agent) claimTimestamp = Time.time;
    }

    public void Release(SearchAgent agent)
    {
        if (claimedBy == agent) claimedBy = null;
    }

    void CleanupIfStale()
    {
        if (claimedBy != null)
        {
            if (!claimedBy || !claimedBy.isActiveAndEnabled || Time.time - claimTimestamp > claimTimeout)
                claimedBy = null;
        }
    }
}
