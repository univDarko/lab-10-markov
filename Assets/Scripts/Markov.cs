using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class Markov : MonoBehaviour
{
    [SerializeField] private List<GameObject> objects = new List<GameObject>();
    private List<GameObject> spawnedObjects = new List<GameObject>();

    // Lista de tiles que han aparecido (0-based): incluye importados y generados
    private List<int> appearedTileIds = new List<int>();

    // Secuencias importadas desde texto (0-based). Este es el dataset para entrenar el modelo.
    private List<List<int>> trainingSequences = new List<List<int>>();

    // GETTERS de solo lectura para otros componentes (no se aprende de estas listas)
    public IReadOnlyList<int> AppearedTileIds => appearedTileIds;
    public int ObjectCount => objects != null ? objects.Count : 0;

    [Header("Importación de secuencias")]
    [SerializeField] private TextAsset sequenceText;
    [SerializeField] private bool loadFromStreamingAssets = false;
    [SerializeField] private string streamingAssetsFileName = "tiles.txt";
    [SerializeField] private bool clearBeforeImport = true;

    [Header("Modelo")]
    [SerializeField] private int nGram = 2;

    // Referencia opcional para capturas automáticas
    [Header("Capturas")]
    [Tooltip("Componente que realizará las capturas PNG/TXT por nivel.")]
    [SerializeField] private ScreenshotCapturer screenshotCapturer;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            ImportSequences();
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            ClearSpawned();
            GenerateLevelMarkov(nGram); // generar 1 más
        }

        if (Input.GetKeyDown(KeyCode.H))
        {
            ClearSpawned();
            GenerateMore(8);
        }

        // Lanzar lote de N niveles y capturar cada uno (ejemplo con tecla J)
        if (Input.GetKeyDown(KeyCode.J))
        {
            // Ajusta el número deseado de niveles
            int levelsToRun = 600;
            StartCoroutine(RunBatchGenerateAndCapture(levelsToRun, 8));
        }
    }

    public IEnumerator RunBatchGenerateAndCapture(int levelsCount, int tilesPerLevel)
    {
        if (levelsCount <= 0) yield break;

        // Validar que hay datos de entrenamiento
        var modelCheck = BuildNGramModel(nGram);
        var unigramCheck = BuildUnigramFrequencies();
        if (modelCheck.Count == 0 && unigramCheck.Count == 0)
        {
            Debug.LogWarning("No hay datos importados para crear el modelo. Importa secuencias antes de ejecutar el lote.");
            yield break;
        }

        for (int i = 0; i < levelsCount; i++)
        {
            // 1) Limpiar escena/estado previo del nivel
            ClearSpawned();

            // 2) Generar el nivel (cantidad fija de tiles)
            GenerateMore(tilesPerLevel);

            // 3) Esperar al final del frame para asegurar render
            yield return new WaitForEndOfFrame();

            // 4) Capturar PNG + TXT con timestamp por nivel (si hay capturador)
            if (screenshotCapturer != null)
            {
                yield return screenshotCapturer.CaptureNow(i);
            }
            else
            {
                Debug.LogWarning("ScreenshotCapturer no asignado. Se omiten capturas.");
            }

            // 5) Pequeña espera opcional para I/O (ajústalo si necesitas throttling)
            yield return null;
        }
    }

    private void AddTile(int index0)
    {
        if (index0 < 0 || index0 >= objects.Count)
        {
            Debug.LogWarning($"Índice de tile {index0} fuera de rango. Debe estar entre 0 y {objects.Count - 1}.");
            return;
        }

        var inst = Instantiate(objects[index0]);
        spawnedObjects.Add(inst);

        appearedTileIds.Add(index0);

        inst.gameObject.transform.position = new Vector3(transform.position.x + (spawnedObjects.Count * 1.28f) - 1.28f, transform.position.y, 0);
    }

    private void ClearSpawned()
    {
        foreach (var go in spawnedObjects)
        {
            if (go != null) Destroy(go);
        }
        spawnedObjects.Clear();

        // Limpiar la traza de aparición
        appearedTileIds.Clear();
    }

    private List<List<int>> ParseSequences(string content)
    {
        // Formato: números (0-based) separados por coma.
        // '.' o salto de línea ('\n') finalizan una secuencia.
        // Ejemplos válidos:
        // "0,1,2.\n3,4,5" -> [[0,1,2],[3,4,5]]
        // "7,8,9.\r\n10,11" -> [[7,8,9],[10,11]]
        var sequences = new List<List<int>>();
        if (string.IsNullOrWhiteSpace(content)) return sequences;

        // Normalizar finales de línea a '\n'
        content = content.Replace("\r", string.Empty);

        // Dividir por delimitadores de fin de secuencia: '.' y '\n'
        var rawSequences = content.Split(new[] { '.', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawSeq in rawSequences)
        {
            var seq = new List<int>();
            var tokens = rawSeq.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                var t = token.Trim();
                if (t.Length == 0) continue;
                if (int.TryParse(t, out int num))
                {
                    seq.Add(num); // ya 0-based
                }
                else
                {
                    Debug.LogWarning($"Token no válido en secuencia: \"{t}\"");
                }
            }

            if (seq.Count > 0)
                sequences.Add(seq);
        }

        return sequences;
    }

    private void ImportSequences()
    {
        try
        {
            string content = null;

            if (loadFromStreamingAssets)
            {
                var path = Path.Combine(Application.streamingAssetsPath, streamingAssetsFileName);
                if (!File.Exists(path))
                {
                    Debug.LogError($"Archivo no encontrado en StreamingAssets: {path}");
                    return;
                }
                content = File.ReadAllText(path);
            }
            else
            {
                if (sequenceText == null)
                {
                    Debug.LogError("No se asignó un TextAsset en 'sequenceText'.");
                    return;
                }
                content = sequenceText.text;
            }

            var sequences0Based = ParseSequences(content);
            if (sequences0Based.Count == 0)
            {
                Debug.LogWarning("No se encontraron secuencias válidas en el documento.");
                return;
            }

            ImportSequencesAndSpawn(sequences0Based, clearBeforeImport);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error al importar secuencias: {ex.Message}");
        }
    }

    // sequences0Based: secuencias leídas (0-based). clear controla si se limpia escena y dataset.
    private void ImportSequencesAndSpawn(List<List<int>> sequences0Based, bool clear)
    {
        if (clear)
        {
            ClearSpawned();            // limpia escena + appearedTileIds
            trainingSequences.Clear(); // limpia dataset
        }

        // Guardar en dataset (0-based y dentro de rango)
        foreach (var seq in sequences0Based)
        {
            var filtered = seq
                .Where(x => x >= 0 && x < objects.Count) // filtrar fuera de rango
                .ToList();

            if (filtered.Count > 0)
                trainingSequences.Add(filtered);
        }

        int totalTilesDataset = trainingSequences.Sum(s => s.Count);
        Debug.Log($"Importadas {trainingSequences.Count} secuencia(s) al dataset (total tiles dataset: {totalTilesDataset}). " +
                  $"Mostrados {appearedTileIds.Count} tiles en escena.");
    }

    // Construye el modelo n-gram SOLO desde el dataset importado (trainingSequences)
    private Dictionary<string, Dictionary<int, int>> BuildNGramModel(int n)
    {
        var model = new Dictionary<string, Dictionary<int, int>>();
        if (n <= 0) return model;

        foreach (var seq in trainingSequences)
        {
            if (seq == null || seq.Count < n + 1) continue;

            for (int i = 0; i <= seq.Count - n - 1; i++)
            {
                var contextSlice = seq.Skip(i).Take(n);
                string contextKey = string.Join(",", contextSlice);

                int nextId = seq[i + n];

                if (!model.TryGetValue(contextKey, out var freqDict))
                {
                    freqDict = new Dictionary<int, int>();
                    model[contextKey] = freqDict;
                }

                if (!freqDict.ContainsKey(nextId))
                    freqDict[nextId] = 0;

                freqDict[nextId]++;
            }
        }

        return model;
    }

    // Frecuencia global (unigram) SOLO del dataset; sirve como fallback cuando no hay contexto
    private Dictionary<int, int> BuildUnigramFrequencies()
    {
        var freq = new Dictionary<int, int>();
        foreach (var seq in trainingSequences)
        {
            if (seq == null) continue;
            foreach (var id in seq)
            {
                if (id < 0 || id >= objects.Count) continue;
                if (!freq.ContainsKey(id)) freq[id] = 0;
                freq[id]++;
            }
        }
        return freq;
    }

    private void GenerateLevelMarkov(int ngram)
    {
        if (ngram <= 0)
        {
            Debug.LogWarning("N-Gram debe ser >= 1");
            return;
        }

        // Construimos el modelo desde el dataset importado
        var model = BuildNGramModel(ngram);
        var unigram = BuildUnigramFrequencies();

        if (model.Count == 0 && unigram.Count == 0)
        {
            Debug.LogWarning("No hay datos importados para crear el modelo.");
            return;
        }

        // Contexto actual: los últimos ngram índices de los tiles que han aparecido
        // Si no hay suficiente contexto, aún podemos generar usando el unigram del dataset.
        string currentContext = appearedTileIds.Count >= ngram
            ? string.Join(",", appearedTileIds.Skip(appearedTileIds.Count - ngram).Take(ngram))
            : "(contexto insuficiente)";

        Debug.Log($"Contexto actual N={ngram}: {currentContext} (model={model.Count} ctx, unigram={unigram.Count} ids)");

        SelectNextTileAndSpawn(ngram, model, unigram);
    }

    private void GenerateMore(int count)
    {
        for (int i = 0; i < count; i++)
        {
            GenerateLevelMarkov(nGram);
        }
    }

    private int WeightedPick(Dictionary<int, int> freq)
    {
        int total = 0;
        foreach (var v in freq.Values) total += v;
        if (total <= 0) return -1;

        int r = UnityEngine.Random.Range(0, total);
        int acc = 0;

        foreach (var kv in freq)
        {
            acc += kv.Value;
            if (r < acc) return kv.Key;
        }

        return -1;
    }

    // Usa el modelo para elegir y spawnear el siguiente tile con backoff; si falla, usa unigram del dataset
    private void SelectNextTileAndSpawn(int n, Dictionary<string, Dictionary<int, int>> model, Dictionary<int, int> unigram)
    {
        // Intento con contexto de longitud n, luego backoff hasta 1
        for (int k = n; k >= 1; k--)
        {
            if (AppearedTileIds.Count < k) continue;

            string ctx = string.Join(",", AppearedTileIds.Skip(AppearedTileIds.Count - k).Take(k));
            if (model.TryGetValue(ctx, out var freq) && freq.Count > 0)
            {
                int nextId = WeightedPick(freq); // 0-based
                if (nextId >= 0) AddTile(nextId);  // AddTile espera 0-based
                return;
            }
        }

        // Fallback: elegir según unigram del dataset (NO aleatorio sobre objects)
        if (unigram != null && unigram.Count > 0)
        {
            int nextId = WeightedPick(unigram);
            if (nextId >= 0) { AddTile(nextId); return; }
        }

        Debug.LogWarning("No se pudo generar: sin contexto en el modelo y sin unigram disponible.");
    }

    [ContextMenu("Importar desde TextAsset")]
    private void ContextMenu_ImportFromTextAsset()
    {
        loadFromStreamingAssets = false;
        ImportSequences();
    }
}