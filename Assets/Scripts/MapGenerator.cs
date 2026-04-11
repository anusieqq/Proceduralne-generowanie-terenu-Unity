using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { NoiseMap, ColourMap, Mesh };
    public DrawMode drawMode;

    const int mapChunkSize = 241;

    [Range(0, 6)]
    public int levelOfDetail;

    public float noiseScale;
    public int octaves;

    [Range(0, 1)]
    public float persistance;

    public float lacunarity;
    public int seed;
    public Vector2 offset;

    public float meshHeightMultiplier;
    public AnimationCurve meshHeightCurve;

    public bool autoUpdate;

    [Header("Regiony i obiekty")]
    public List<RegionData> regions = new List<RegionData>();

    [Header("Ustawienia generowania obiektów")]
    public bool generateObjects = true;
    public Transform objectsParent;

    private List<Vector3> spawnedPositions = new List<Vector3>();
    private List<GameObject> spawnedObjects = new List<GameObject>();

    private float[,] currentNoiseMap;
    private GameObject currentTerrainMesh;
    private Transform terrainParent;

    public void GenerateMap()
    {
        ClearSpawnedObjects();

        currentNoiseMap = Noise.GenerateNoiseMap(
            mapChunkSize, mapChunkSize,
            seed, noiseScale, octaves,
            persistance, lacunarity, offset
        );

        Color[] colourMap = new Color[mapChunkSize * mapChunkSize];

        for (int y = 0; y < mapChunkSize; y++)
        {
            for (int x = 0; x < mapChunkSize; x++)
            {
                float currentHeight = currentNoiseMap[x, y];
                colourMap[y * mapChunkSize + x] = GetRegionColor(currentHeight);
            }
        }

        MapDisplay display = FindObjectOfType<MapDisplay>();

        if (display != null)
        {
            if (drawMode == DrawMode.NoiseMap)
                display.DrawTexture(TextureGenerator.TextureFromHeightMap(currentNoiseMap));

            else if (drawMode == DrawMode.ColourMap)
                display.DrawTexture(TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize));

            else if (drawMode == DrawMode.Mesh)
            {
                MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                    currentNoiseMap,
                    meshHeightMultiplier,
                    meshHeightCurve,
                    levelOfDetail
                );

                display.DrawMesh(
                    meshData,
                    TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize)
                );

                currentTerrainMesh = display.meshFilter.gameObject;
                terrainParent = currentTerrainMesh.transform.parent;
            }
        }

        if (generateObjects && regions.Count > 0 && currentTerrainMesh != null)
        {
            StartCoroutine(GenerateObjectsWithDelay());
        }
    }

    IEnumerator GenerateObjectsWithDelay()
    {
        yield return null;
        yield return null;

        if (currentTerrainMesh != null)
        {
            MeshCollider collider = currentTerrainMesh.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = currentTerrainMesh.AddComponent<MeshCollider>();
                collider.sharedMesh = currentTerrainMesh.GetComponent<MeshFilter>().sharedMesh;
            }
        }

        GenerateObjects();
    }

    Color GetRegionColor(float height)
    {
        foreach (RegionData region in regions)
        {
            if (height >= region.minHeight && height <= region.maxHeight)
                return region.colour;
        }
        return Color.white;
    }

    string GetRegionName(float height)
    {
        foreach (RegionData region in regions)
        {
            if (height >= region.minHeight && height <= region.maxHeight)
                return region.name;
        }
        return "unknown";
    }

    RegionData GetRegionAtHeight(float height)
    {
        foreach (RegionData region in regions)
        {
            if (height >= region.minHeight && height <= region.maxHeight)
                return region;
        }
        return null;
    }

    void GenerateObjects()
    {
        if (currentTerrainMesh == null)
        {
            Debug.LogError("Brak mesha terenu!");
            return;
        }

        Random.InitState(seed);
        int totalSpawned = 0;

        Transform targetTransform = terrainParent != null ? terrainParent : currentTerrainMesh.transform;
        Vector3 meshWorldPosition = targetTransform.position;
        Vector3 meshWorldScale = targetTransform.lossyScale;

        float halfSize = (mapChunkSize - 1) / 2f;

        // Lista wszystkich pozycji, aby obiekty z ró?nych regionów te? si? nie nak?ada?y
        spawnedPositions.Clear();

        foreach (RegionData region in regions)
        {
            if (region.objectsToSpawn == null || region.objectsToSpawn.Count == 0) continue;

            foreach (RegionObjectData objData in region.objectsToSpawn)
            {
                if (objData.prefab == null) continue;

                int spawnedCount = 0;
                // U?ywamy g?sto?ci jako kroku w p?tli
                int stepSize = Mathf.Max(1, Mathf.RoundToInt(objData.spawnDensity));

                for (int y = 0; y < mapChunkSize; y += stepSize)
                {
                    for (int x = 0; x < mapChunkSize; x += stepSize)
                    {
                        // 1. Pobieramy surow? wysoko??
                        float rawHeight = currentNoiseMap[x, y];

                        // 2. KLUCZ: Sprawdzamy, czy surowa wysoko?? mie?ci si? w definicji regionu
                        // To gwarantuje, ?e palmy spawnuj? si? tylko tam, gdzie szum wskazuje na Sand
                        if (rawHeight < region.minHeight || rawHeight > region.maxHeight) continue;

                        // Szansa na spawn
                        if (Random.value > objData.spawnChance) continue;

                        // 3. Obliczanie pozycji ?wiatowej
                        float localX = (x - halfSize) + Random.Range(-0.5f, 0.5f);
                        float localZ = (halfSize - y) + Random.Range(-0.5f, 0.5f); // Odwrócone Y dla zgodno?ci z uk?adem wspó?rz?dnych

                        float worldX = meshWorldPosition.x + (localX * meshWorldScale.x);
                        float worldZ = meshWorldPosition.z + (localZ * meshWorldScale.z);

                        // 4. Raycast, aby postawi? obiekt dok?adnie na powierzchni mesha
                        Vector3 rayStart = new Vector3(worldX, 500f, worldZ);
                        RaycastHit hit;

                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1000f))
                        {
                            // Opcjonalnie: dodatkowe sprawdzenie nachylenia terenu (?eby nie sapa? na pionowych ?cianach)
                            if (Vector3.Angle(hit.normal, Vector3.up) > 30f) continue;

                            Vector3 spawnPos = hit.point;

                            // 5. Sprawdzanie dystansu mi?dzy obiektami
                            bool tooClose = false;
                            foreach (Vector3 pos in spawnedPositions)
                            {
                                if (Vector3.Distance(spawnPos, pos) < objData.minDistanceBetween)
                                {
                                    tooClose = true;
                                    break;
                                }
                            }

                            if (!tooClose)
                            {
                                SpawnObject(objData, spawnPos);
                                spawnedPositions.Add(spawnPos);
                                spawnedCount++;
                                totalSpawned++;
                            }
                        }
                    }
                }
                Debug.Log($"Region {region.name} - {objData.name}: {spawnedCount}");
            }
        }
    }

    void SpawnObject(RegionObjectData objData, Vector3 position)
    {
        GameObject obj = Instantiate(objData.prefab, position, Quaternion.identity);

        float scale = Random.Range(objData.minScale, objData.maxScale);
        obj.transform.localScale = Vector3.one * scale;

        if (objData.randomRotation)
            obj.transform.Rotate(0, Random.Range(0, 360), 0);

        if (objectsParent != null)
            obj.transform.parent = objectsParent;

        spawnedObjects.Add(obj);
    }

    void ClearSpawnedObjects()
    {
        foreach (GameObject obj in spawnedObjects)
        {
            if (obj != null)
                DestroyImmediate(obj);
        }

        spawnedObjects.Clear();
        spawnedPositions.Clear();
    }

    void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
    }
}

[System.Serializable]
public class RegionData
{
    public string name = "Nowy region";

    [Header("Zakres wysoko?ci")]
    [Range(0, 1)]
    public float minHeight = 0f;
    [Range(0, 1)]
    public float maxHeight = 1f;

    [Header("Kolor (do wizualizacji)")]
    public Color colour = Color.white;

    [Header("Obiekty do generowania w tym regionie")]
    public List<RegionObjectData> objectsToSpawn = new List<RegionObjectData>();
}

[System.Serializable]
public class RegionObjectData
{
    public string name = "Nowy obiekt";
    public GameObject prefab;

    [Header("G?sto?? spawnu")]
    [Range(0, 1)]
    public float spawnChance = 0.1f;
    public float spawnDensity = 5f;
    
    [Header("Skalowanie")]
    public float minScale = 0.8f;
    public float maxScale = 1.2f;

    [Header("Inne")]
    public bool randomRotation = true;
    public float minDistanceBetween = 3f;
}