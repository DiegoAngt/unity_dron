using UnityEngine;
using System;

public class MissionManager : MonoBehaviour
{
    // Configuración de Misión
    [Header("Configuración de Misión")]
    public string missionDescription;
    public Vector3d missionGPSTarget;
    
    // Componentes del Sistema
    [Header("Componentes del Sistema")]
    public SearchAgent searchAgent;
    public LongDistanceNavigator navigator;
    public DescriptionParser descriptionParser;
    
    // Eventos de Misión
    public event Action OnMissionStart;
    public event Action<bool> OnMissionComplete;
    
    // Estado (Solo Lectura)
    [Header("Estado (Solo Lectura)")]
    public bool missionActive = false;
    public bool missionSuccessful = false;
    
    void Start()
    {
        // Buscar componentes si no están asignados (usando métodos no obsoletos)
        if (searchAgent == null)
            searchAgent = FindAnyObjectByType<SearchAgent>();
            
        if (navigator == null)
            navigator = FindAnyObjectByType<LongDistanceNavigator>();
            
        if (descriptionParser == null)
            descriptionParser = FindAnyObjectByType<DescriptionParser>();
            
        // Iniciar misión automáticamente al inicio
        if (!string.IsNullOrEmpty(missionDescription))
        {
            StartMission(missionDescription, missionGPSTarget);
        }
    }
    
    public void StartMission(string description, Vector3d gpsTarget)
    {
        missionDescription = description;
        missionGPSTarget = gpsTarget;
        missionActive = true;
        missionSuccessful = false;
        
        Debug.Log($"Iniciando misión: {description}");
        
        // Parsear la descripción textual
        PersonDescriptor targetDescriptor = descriptionParser.ParseTextDescription(description);
        
        // Configurar el navegador
        navigator.missionGPSTarget = gpsTarget;
        navigator.InitializeNavigation();
        
        // Configurar el agente de búsqueda
        searchAgent.targetDescriptor = targetDescriptor;
        
        // Suscribirse a eventos del agente
        searchAgent.OnMissionPhaseChange += HandleMissionPhaseChange;
        
        // Disparar evento de inicio de misión
        OnMissionStart?.Invoke();
    }
    
    private void HandleMissionPhaseChange(AgentPhase newPhase)
    {
        switch (newPhase)
        {
            case AgentPhase.Done:
                MissionCompleted(true);
                break;
                
            case AgentPhase.Abort:
                MissionCompleted(false);
                break;
        }
    }
    
    private void MissionCompleted(bool success)
    {
        missionActive = false;
        missionSuccessful = success;
        
        Debug.Log(success ? 
            "¡Misión completada con éxito! Persona encontrada y aterrizaje exitoso." :
            "La misión ha fallado. No se pudo completar el objetivo.");
        
        // Disparar evento de finalización
        OnMissionComplete?.Invoke(success);
        
        // Desuscribirse de eventos
        if (searchAgent != null)
            searchAgent.OnMissionPhaseChange -= HandleMissionPhaseChange;
    }
    
    public void RestartMission()
    {
        if (missionActive) return;
        
        StartMission(missionDescription, missionGPSTarget);
    }
    
    void OnDestroy()
    {
        // Limpieza de eventos
        if (searchAgent != null)
            searchAgent.OnMissionPhaseChange -= HandleMissionPhaseChange;
    }
    
    // Para UI o debugging
    public string GetMissionStatus()
    {
        if (!missionActive) 
            return missionSuccessful ? "Completada" : "Fallida";
        
        return $"En progreso - Fase: {searchAgent.phase}";
    }
}