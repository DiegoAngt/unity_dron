using UnityEngine;

[System.Serializable]
public struct Vector3d
{
    public double x;
    public double y;
    public double z;
    
    public Vector3d(double x, double y, double z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
    
    public override string ToString()
    {
        return $"({x:F6}, {y:F6}, {z:F1})";
    }
}

public class LongDistanceNavigator : MonoBehaviour
{
    [Header("Configuración GPS")]
    public Vector3d realWorldGPSOrigin; // Origen de coordenadas (lat, lon, alt)
    public Vector3d missionGPSTarget;   // Objetivo de la misión
    
    [Header("Referencias")]
    public SearchAgent searchAgent;
    
    void Start()
    {
        if (searchAgent == null)
            searchAgent = GetComponent<SearchAgent>();
            
        InitializeNavigation();
    }
    
    public void InitializeNavigation()
    {
        // Convertir coordenadas GPS a posición Unity
        Vector3 unityTarget = ConvertGPSToUnityPosition(missionGPSTarget);
        
        
        searchAgent.gpsTarget = unityTarget;
        searchAgent.phase = AgentPhase.GoingToGPS;
        
        Debug.Log($"Navegando hacia: {missionGPSTarget} -> Unity: {unityTarget}");
    }
    
    public Vector3 ConvertGPSToUnityPosition(Vector3d gpsCoordinate)
    {
        
        double latDiff = gpsCoordinate.x - realWorldGPSOrigin.x;
        double lonDiff = gpsCoordinate.y - realWorldGPSOrigin.y;
        double altDiff = gpsCoordinate.z - realWorldGPSOrigin.z;
        
        double x = lonDiff * 111000 * System.Math.Cos(realWorldGPSOrigin.x * System.Math.PI / 180);
        double z = latDiff * 111000;
        double y = altDiff;
        
        return new Vector3((float)x, (float)y, (float)z);
    }
    
    // Para debugging en el editor
    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            Gizmos.color = Color.red;
            Vector3 targetPos = ConvertGPSToUnityPosition(missionGPSTarget);
            Gizmos.DrawWireSphere(targetPos, 5f);
            Gizmos.DrawLine(transform.position, targetPos);
        }
    }
}