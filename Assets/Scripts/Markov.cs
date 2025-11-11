using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Markov : MonoBehaviour
{
    [SerializeField] private List<GameObject> objects = new List<GameObject>();
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private List<int> spawnedTileIds = new List<int>();

    [SerializeField] private int nGram = 2;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            AddTile(1);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            AddTile(2);
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            AddTile(3);
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            AddTile(4);
        }

        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            AddTile(5);
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            GenerateLevelMarkov(nGram); // prueba con bi-gramas
        }
    }

    private void AddTile(int num)
    {
        int prefabIndex = num - 1;
        var inst = Instantiate(objects[prefabIndex]);
        spawnedObjects.Add(inst);
        spawnedTileIds.Add(prefabIndex);

        inst.gameObject.transform.position = new Vector3(spawnedObjects.Count + 1.2f, 0, 0);

        if (spawnedObjects.Count > 10)
        {
            Camera.main.transform.position += new Vector3(1f, 0, 0);
        }
    }

    private Dictionary<string, Dictionary<int, int>> BuildNGramModel(int n)
    {
        var model = new Dictionary<string, Dictionary<int, int>>();

        // Necesitamos al menos n+1 elementos para formar (contexto -> siguiente)
        if (spawnedTileIds.Count < n + 1)
            return model;

        for (int i = 0; i <= spawnedTileIds.Count - n - 1; i++)
        {
            // contexto de longitud n
            var contextSlice = spawnedTileIds.Skip(i).Take(n);
            string contextKey = string.Join(",", contextSlice);

            int nextId = spawnedTileIds[i + n];

            if (!model.TryGetValue(contextKey, out var freqDict))
            {
                freqDict = new Dictionary<int, int>();
                model[contextKey] = freqDict;
            }

            if (!freqDict.ContainsKey(nextId))
                freqDict[nextId] = 0;

            freqDict[nextId]++;
        }

        return model;
    }

    private void GenerateLevelMarkov(int ngram)
    {
        if (ngram <= 0)
        {
            Debug.LogWarning("N-Gram debe ser >= 1");
            return;
        }

        // Construimos el modelo
        var model = BuildNGramModel(ngram);

        if (model.Count == 0)
        {
            Debug.LogWarning("No hay suficientes tiles para crear el modelo.");
            return;
        }

        // Contexto actual: los últimos ngram índices
        if (spawnedTileIds.Count < ngram)
        {
            Debug.LogWarning("Secuencia insuficiente para usar este N-Gram.");
            return;
        }

        string currentContext = string.Join(",", spawnedTileIds.Skip(spawnedTileIds.Count - ngram).Take(ngram));

        // En Parte 2: usar este contexto y el modelo para elegir y crear el siguiente tile.
        Debug.Log($"Contexto actual N={ngram}: {currentContext} (modelo listo con {model.Count} contextos)");

        SelectNextTileAndSpawn(ngram, model);
    }

    private int WeightedPick(Dictionary<int, int> freq)
    {
        int total = 0;
        foreach (var v in freq.Values) total += v;

        int r = Random.Range(0, total);
        int acc = 0;

        foreach (var kv in freq)
        {
            acc += kv.Value;
            if (r < acc) return kv.Key;
        }

        return -1;
    }

    // Usa el modelo para elegir y spawnear el siguiente tile con backoff
    private void SelectNextTileAndSpawn(int n, Dictionary<string, Dictionary<int, int>> model)
    {
        // Intento con contexto de longitud n, luego backoff hasta 1
        for (int k = n; k >= 1; k--)
        {
            string ctx = string.Join(",", spawnedTileIds.Skip(spawnedTileIds.Count - k).Take(k));
            if (model.TryGetValue(ctx, out var freq) && freq.Count > 0)
            {
                int nextId = WeightedPick(freq);
                AddTile(nextId + 1);
                return;
            }
        }

        // Si no hay contexto conocido, elige aleatorio
        int rndIdx = Random.Range(0, objects.Count);
        AddTile(rndIdx + 1);
    }
}
