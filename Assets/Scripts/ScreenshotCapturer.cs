using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public class ScreenshotCapturer : MonoBehaviour
{
    [Header("Captura")]
    [Tooltip("Cámara desde la que se captura. Si está vacío, usa Camera.main.")]
    public Camera captureCamera;

    [Tooltip("Escala de la resolución (1 = tamaño de pantalla).")]
    [Range(1, 4)]
    public int superSize = 1;

    [Header("Destino")]
    [Tooltip("Carpeta (dentro de Assets) para PNG. Se creará si no existe.")]
    public string relativeScreenshotFolder = "Assets/Screenshots";

    [Tooltip("Carpeta (dentro de Assets) para TXT. Se creará si no existe.")]
    public string relativeTextFolder = "Assets/LevelTexts";

    [Tooltip("Prefijo del nombre de archivo (PNG/TXT comparten timestamp).")]
    public string filePrefix = "capture";

    [Header("Fuente de datos")]
    [Tooltip("Referencia al Markov que genera tiles.")]
    public Markov markov;

    [Header("Patrones por tile (8 filas, de arriba a abajo)")]
    public List<TilePattern> tilePatterns = new List<TilePattern>();

    [Serializable]
    public class TilePattern
    {
        [Tooltip("Índice del tile (0-based) correspondiente en Markov.objects")]
        public int tileIndex;

        [Tooltip("8 strings (de arriba a abajo). Cada string se concatenará horizontalmente en su fila.")]
        public string[] rows8 = new string[8] { "0", "0", "0", "0", "0", "0", "0", "0" };
    }

    private void Awake()
    {
        if (captureCamera == null) captureCamera = Camera.main;
        if (markov != null) SyncPatternsWithMarkov();
    }

    public System.Collections.IEnumerator CaptureNow(int num)
    {
        // Asegurar carpetas
        string pngFolder = ResolveUnderAssets(relativeScreenshotFolder, "Screenshots");
        string txtFolder = ResolveUnderAssets(relativeTextFolder, "LevelTexts");
        Directory.CreateDirectory(pngFolder);
        Directory.CreateDirectory(txtFolder);

        // Esperar fin de frame para tener el framebuffer listo
        yield return new WaitForEndOfFrame();

        // Guardar PNG
        try
        {
            string pngPath = Path.Combine(pngFolder, $"Level{num}.png");
            ScreenCapture.CaptureScreenshot(pngPath, superSize);
            Debug.Log($"PNG solicitado: {pngPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al guardar PNG: {ex.Message}");
        }

        // Guardar TXT (8 filas)
        try
        {
            string txt = BuildLevelTextFromMarkov();
            string txtPath = Path.Combine(txtFolder, $"Level{num}.txt");
            File.WriteAllText(txtPath, txt, Encoding.UTF8);
            Debug.Log($"TXT guardado: {txtPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al generar/guardar TXT: {ex.Message}");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H))
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            StartCoroutine(CaptureAndExportEndOfFrame(timestamp));
        }
    }

    private System.Collections.IEnumerator CaptureAndExportEndOfFrame(string timestamp)
    {
        // Esperar al final del frame para que Markov haya generado con H
        yield return new WaitForEndOfFrame();

        // 1) Guardar PNG
        try
        {
            string pngFolder = ResolveUnderAssets(relativeScreenshotFolder, "Screenshots");
            Directory.CreateDirectory(pngFolder);
            string pngPath = Path.Combine(pngFolder, $"{filePrefix}_{timestamp}.png");
            ScreenCapture.CaptureScreenshot(pngPath, superSize);
            Debug.Log($"PNG solicitado: {pngPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al guardar PNG: {ex.Message}");
        }

        // 2) Construir texto (8 filas) y guardar TXT
        try
        {
            string txt = BuildLevelTextFromMarkov();
            string txtFolder = ResolveUnderAssets(relativeTextFolder, "LevelTexts");
            Directory.CreateDirectory(txtFolder);
            string txtPath = Path.Combine(txtFolder, $"{filePrefix}_{timestamp}.txt");
            File.WriteAllText(txtPath, txt, Encoding.UTF8);
            Debug.Log($"TXT guardado: {txtPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al generar/guardar TXT: {ex.Message}");
        }
    }

    private string BuildLevelTextFromMarkov()
    {
        if (markov == null || markov.AppearedTileIds == null || markov.AppearedTileIds.Count == 0)
            return string.Empty;

        // Map rápido: tileIndex -> patrón
        var map = new Dictionary<int, TilePattern>();
        foreach (var p in tilePatterns)
        {
            if (p == null) continue;
            if (p.rows8 == null || p.rows8.Length != 8) continue;
            map[p.tileIndex] = p;
        }

        var rowBuilders = new StringBuilder[8];
        for (int r = 0; r < 8; r++) rowBuilders[r] = new StringBuilder();

        foreach (var tileIndex in markov.AppearedTileIds)
        {
            if (!map.TryGetValue(tileIndex, out var pat) || pat.rows8 == null || pat.rows8.Length != 8)
            {
                // Fallback: si no hay patrón, usar "0" en cada fila
                for (int r = 0; r < 8; r++) rowBuilders[r].Append("0");
                continue;
            }

            for (int r = 0; r < 8; r++)
            {
                string piece = string.IsNullOrEmpty(pat.rows8[r]) ? "0" : pat.rows8[r];
                rowBuilders[r].Append(piece);
            }
        }

        var sb = new StringBuilder();
        for (int r = 0; r < 8; r++)
        {
            sb.AppendLine(rowBuilders[r].ToString());
        }
        return sb.ToString();
    }

    private string ResolveUnderAssets(string rel, string defaultFolderName)
    {
        if (string.IsNullOrWhiteSpace(rel)) rel = defaultFolderName;
        rel = rel.Replace("\\", "/");
        if (rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            rel = rel.Substring("Assets/".Length);
        return Path.Combine(Application.dataPath, rel);
    }

    [ContextMenu("Sincronizar patrones con Markov.objects")]
    private void SyncPatternsWithMarkov()
    {
        if (markov == null) { Debug.LogWarning("Asigna un Markov primero."); return; }

        var existing = new HashSet<int>();
        foreach (var p in tilePatterns) if (p != null) existing.Add(p.tileIndex);

        for (int i = 0; i < markov.ObjectCount; i++)
        {
            if (existing.Contains(i)) continue;
            tilePatterns.Add(new TilePattern
            {
                tileIndex = i,
                rows8 = new string[8] { "0", "0", "0", "0", "0", "0", "0", "0" }
            });
        }

        Debug.Log($"Patrones sincronizados. Total: {tilePatterns.Count}");
    }
}