using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Componentes de UI")]
    public TMP_Text missionStatusText;
    public TMP_Text phaseText;
    public TMP_Text distanceText;
    public TMP_Text targetDescriptionText;
    public TMP_Text gpsCoordinatesText;
    public Slider altitudeSlider;
    public Image altitudeFillImage;
    public GameObject missionPanel;
    public Button restartButton;

    [Header("Colores")]
    public Color successColor = Color.green;
    public Color warningColor = Color.yellow;
    public Color dangerColor = Color.red;
    public Color normalColor = Color.white;

    [Header("Referencias")]
    public MissionManager missionManager;
    public SearchAgent searchAgent;
    public LongDistanceNavigator navigator;
    public Transform droneTransform;

    private void Start()
    {
        if (missionManager != null)
        {
            missionManager.OnMissionStart += OnMissionStart;
            missionManager.OnMissionComplete += OnMissionComplete;
        }

        if (restartButton != null)
            restartButton.onClick.AddListener(RestartMission);
    }

    private void Update()
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        // Actualizar textos
        if (missionStatusText != null)
            missionStatusText.text = $"Estado: {missionManager.GetMissionStatus()}";

        if (phaseText != null && searchAgent != null)
            phaseText.text = $"Fase: {searchAgent.phase}";

        if (distanceText != null && searchAgent != null && searchAgent.currentTarget != null)
        {
            float distance = Vector3.Distance(droneTransform.position, searchAgent.currentTarget.position);
            distanceText.text = $"Distancia: {distance:F1}m";
        }

        if (targetDescriptionText != null && missionManager != null)
            targetDescriptionText.text = $"Objetivo: {missionManager.missionDescription}";

        if (gpsCoordinatesText != null && navigator != null)
            gpsCoordinatesText.text = $"GPS: {navigator.missionGPSTarget}";

        // Actualizar altímetro
        if (altitudeSlider != null && droneTransform != null)
        {
            altitudeSlider.value = droneTransform.position.y;
            altitudeFillImage.color = GetAltitudeColor(droneTransform.position.y);
        }
    }

    private Color GetAltitudeColor(float altitude)
    {
        if (altitude > 100f) return dangerColor;
        if (altitude > 50f) return warningColor;
        if (altitude > 10f) return normalColor;
        return successColor;
    }

    private void OnMissionStart()
    {
        if (missionPanel != null)
            missionPanel.SetActive(true);
    }

    private void OnMissionComplete(bool success)
    {
        if (restartButton != null)
            restartButton.gameObject.SetActive(true);

        if (missionStatusText != null)
            missionStatusText.color = success ? successColor : dangerColor;
    }

    private void RestartMission()
    {
        if (missionManager != null)
            missionManager.RestartMission();

        if (restartButton != null)
            restartButton.gameObject.SetActive(false);

        if (missionStatusText != null)
            missionStatusText.color = normalColor;
    }

    private void OnDestroy()
    {
        if (missionManager != null)
        {
            missionManager.OnMissionStart -= OnMissionStart;
            missionManager.OnMissionComplete -= OnMissionComplete;
        }
    }
}