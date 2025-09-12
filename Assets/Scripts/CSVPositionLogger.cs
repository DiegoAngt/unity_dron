// // Archivo: CSVPositionLogger.cs
// using UnityEngine;
// using System.IO;
// using System.Text;
// using System.Globalization;

// public class CSVPositionLogger : MonoBehaviour
// {
//     [Header("Objeto a registrar")]
//     public Transform droneTransform;

//     [Header("Frecuencia de muestreo")]
//     public float logInterval = 0.5f;

//     [Header("Salida")]
//     public string folderName = "logs";
//     public string fileNamePrefix = "path_log";

//     [Header("Control")]
//     public bool autoStart = true;

//     private string filePath;
//     private float startTime;
//     private float lastLogTime;
//     private StreamWriter writer;
//     private bool logging;

//     void Start()
//     {
//         if (droneTransform == null) droneTransform = transform; // fallback
//         if (autoStart) BeginLog();
//     }

//     void Update()
//     {
//         if (!logging) return;
//         if (Time.time - lastLogTime < logInterval) return;

//         WriteRow();
//         lastLogTime = Time.time;
//     }

//     public void BeginLog()
//     {
//         if (logging) return;

//         var dir = Path.Combine(Application.persistentDataPath, folderName);
//         Directory.CreateDirectory(dir);
//         var stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
//         filePath = Path.Combine(dir, $"{fileNamePrefix}_{stamp}.csv");

//         // Crear el writer ANTES de escribir
//         writer = new StreamWriter(filePath, false, Encoding.UTF8);
//         writer.AutoFlush = true; // opcional: escribe a disco sin esperar
//         writer.WriteLine("t,elapsed,pos_x,pos_y,pos_z");

//         startTime = Time.time;
//         lastLogTime = startTime;
//         logging = true;

//         Debug.Log($"[CSVPositionLogger] Logging iniciado → {filePath}");
//         Debug.Log($"[CSVPositionLogger] persistentDataPath → {Application.persistentDataPath}");
//         #if UNITY_EDITOR
//         UnityEditor.EditorUtility.RevealInFinder(filePath); // abre la carpeta
//         #endif
//     }

//     public void EndLog()
//     {
//         if (!logging) return;

//         try { writer?.Flush(); writer?.Close(); } catch {}
//         writer = null;
//         logging = false;
//         Debug.Log($"[CSVPositionLogger] CSV guardado en: {filePath}");
//     }

//     void WriteRow()
//     {
//         if (writer == null || droneTransform == null) return;

//         float t = Time.time;
//         float elapsed = t - startTime;
//         Vector3 p = droneTransform.position;

//         writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
//             "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3}",
//             t, elapsed, p.x, p.y, p.z));
//         // Si quieres máxima seguridad ante crasheos, añade: writer.Flush();
//     }

//     void OnDestroy()        { EndLog(); }
//     void OnApplicationQuit(){ EndLog(); }

//     public string GetFilePath() => filePath;
// }

// Archivo: CSVPositionLogger.cs
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Globalization;

public class CSVPositionLogger : MonoBehaviour
{
    [Header("Objeto a registrar")]
    public Transform droneTransform;          // Arrastra aquí el transform del dron/agente

    [Header("Frecuencia de muestreo")]
    [Tooltip("Cada cuántos segundos se escribe una fila en el CSV")]
    public float logInterval = 0.5f;

    [Header("Nombre de archivo")]
    [Tooltip("Prefijo del archivo CSV")]
    public string fileNamePrefix = "path_log";

    [Header("Dónde guardar")]
    [Tooltip("Si está activo, guarda en el Escritorio del usuario")]
    public bool saveToDesktop = true;
    [Tooltip("Subcarpeta dentro del Escritorio (dejar vacío para guardar directo en el Escritorio)")]
    public string desktopSubfolder = "UnityLogs";

    [Tooltip("Si saveToDesktop está desactivado, se usará Application.persistentDataPath/logs")]
    public string fallbackFolderName = "logs";

    [Header("Control")]
    public bool autoStart = true;             // Si true, inicia automáticamente

    [Header("Debug en consola (opcional)")]
    public bool logToConsole = false;         // Actívalo si quieres ver posiciones en Console
    public int consoleEveryNRows = 10;        // Imprime cada N filas

    // --- Internos ---
    private string filePath;
    private float startTime;
    private float lastLogTime;
    private StreamWriter writer;
    private bool logging;
    private int rowIndex = 0;

    void Start()
    {
        if (droneTransform == null)
            droneTransform = transform; // fallback

        if (autoStart)
            BeginLog();
    }

    void Update()
    {
        if (!logging) return;
        if (Time.time - lastLogTime < logInterval) return;

        WriteRow();
        lastLogTime = Time.time;
    }

    public void BeginLog()
    {
        if (logging) return;

        // --- Resolver carpeta de salida ---
        string dir;
        if (saveToDesktop)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            dir = string.IsNullOrEmpty(desktopSubfolder) ? desktop : Path.Combine(desktop, desktopSubfolder);
        }
        else
        {
            dir = Path.Combine(Application.persistentDataPath, fallbackFolderName);
        }

        Directory.CreateDirectory(dir);

        // --- Crear archivo ---
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        filePath = Path.Combine(dir, $"{fileNamePrefix}_{stamp}.csv");

        writer = new StreamWriter(filePath, false, Encoding.UTF8);
        writer.AutoFlush = true; // escribe en disco sin esperar
        writer.WriteLine("t,elapsed,pos_x,pos_y,pos_z"); // cabecera

        startTime = Time.time;
        lastLogTime = startTime;
        logging = true;

        Debug.Log($"[CSVPositionLogger] Logging iniciado → {filePath}");
        if (!saveToDesktop)
            Debug.Log($"[CSVPositionLogger] persistentDataPath → {Application.persistentDataPath}");

        #if UNITY_EDITOR
        // Abre el archivo en Finder/Explorer para confirmar la ruta
        UnityEditor.EditorUtility.RevealInFinder(filePath);
        #endif
    }

    public void EndLog()
    {
        if (!logging) return;

        try { writer?.Flush(); writer?.Close(); } catch {}
        writer = null;
        logging = false;
        Debug.Log($"[CSVPositionLogger] CSV guardado en: {filePath}");
    }

    void WriteRow()
    {
        if (writer == null || droneTransform == null) return;

        float t = Time.time;
        float elapsed = t - startTime;
        Vector3 p = droneTransform.position;

        // Asegurar punto decimal con InvariantCulture
        writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "{0:F3},{1:F3},{2:F3},{3:F3},{4:F3}",
            t, elapsed, p.x, p.y, p.z));

        // Logging a consola controlado
        rowIndex++;
        if (logToConsole && (rowIndex % Mathf.Max(1, consoleEveryNRows) == 0))
        {
            Debug.Log($"[CSV] #{rowIndex} t={elapsed:F2}s pos=({p.x:F2}, {p.y:F2}, {p.z:F2}) → {Path.GetFileName(filePath)}");
        }
    }

    void OnDestroy()        { EndLog(); }
    void OnApplicationQuit(){ EndLog(); }

    public string GetFilePath() => filePath;
}

