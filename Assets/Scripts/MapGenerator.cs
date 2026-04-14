using System.Collections.Generic;
using UnityEngine;
using System.Collections;

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
    public LayerMask terrainLayer; // Przypisz warstw?, na której jest Mesh terenu

    [Header("Woda")]
    public GameObject waterPrefab;
    public float waterHeight = 10f;
    public float waterThickness = 3f;

    private GameObject spawnedWater;
    private List<Vector3> spawnedPositions = new List<Vector3>();
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private Coroutine objectGenerationCoroutine;

    private float[,] currentNoiseMap;
    private GameObject currentTerrainMesh;
    private Transform terrainParent;

    public void GenerateMap()
    {
        if (objectGenerationCoroutine != null) StopCoroutine(objectGenerationCoroutine);

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
                    currentNoiseMap, meshHeightMultiplier, meshHeightCurve, levelOfDetail
                );
                display.DrawMesh(meshData, TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize));

                currentTerrainMesh = display.meshFilter.gameObject;
                terrainParent = currentTerrainMesh.transform.parent;

                // Ustawienie warstwy dla terenu, aby Raycast wiedzia? w co strzela?
                if (terrainLayer != 0) currentTerrainMesh.layer = (int)Mathf.Log(terrainLayer.value, 2);

                HandleWater();
            }
        }

        if (generateObjects && regions.Count > 0 && currentTerrainMesh != null)
        {
            objectGenerationCoroutine = StartCoroutine(GenerateObjectsWithDelay());
        }
    }

    void HandleWater()
    {
        if (spawnedWater != null) DestroyImmediate(spawnedWater);
        if (waterPrefab == null) return;

        spawnedWater = Instantiate(waterPrefab);
        spawnedWater.name = "GeneratedWater";

        // Wy??czenie collidera wody na czas generowania obiektów, ?eby Raycast go nie ?apa?
        Collider waterCol = spawnedWater.GetComponent<Collider>();
        if (waterCol != null) waterCol.enabled = false;

        MeshFilter terrainMeshFilter = currentTerrainMesh.GetComponent<MeshFilter>();
        if (terrainMeshFilter != null && terrainMeshFilter.sharedMesh != null)
        {
            spawnedWater.transform.position = new Vector3(currentTerrainMesh.transform.position.x, waterHeight, currentTerrainMesh.transform.position.z);
            spawnedWater.transform.parent = terrainParent;

            float meshBaseSizeX = terrainMeshFilter.sharedMesh.bounds.size.x;
            float meshBaseSizeZ = terrainMeshFilter.sharedMesh.bounds.size.z;
            spawnedWater.transform.localScale = new Vector3((meshBaseSizeX / 10f) * 100.05f, waterThickness, (meshBaseSizeZ / 10f) * 100.05f);
        }
    }

    IEnumerator GenerateObjectsWithDelay()
    {
        yield return null;
        if (currentTerrainMesh != null)
        {
            MeshCollider collider = currentTerrainMesh.GetComponent<MeshCollider>();
            if (collider == null) collider = currentTerrainMesh.AddComponent<MeshCollider>();
            collider.sharedMesh = currentTerrainMesh.GetComponent<MeshFilter>().sharedMesh;
        }
        yield return null;
        GenerateObjects();

        // Po wygenerowaniu obiektów, mo?emy z powrotem w??czy? collider wody (opcjonalnie)
        if (spawnedWater != null && spawnedWater.GetComponent<Collider>() != null)
            spawnedWater.GetComponent<Collider>().enabled = true;
    }

    void GenerateObjects()
    {
        if (currentTerrainMesh == null) return;
        Random.InitState(seed);

        Vector3 meshWorldPosition = currentTerrainMesh.transform.position;
        Vector3 meshWorldScale = currentTerrainMesh.transform.localScale;
        float halfSize = (mapChunkSize - 1) / 2f;

        foreach (RegionData region in regions)
        {
            if (region.objectsToSpawn == null) continue;
            foreach (RegionObjectData objData in region.objectsToSpawn)
            {
                if (objData.prefab == null) continue;
                int stepSize = Mathf.Max(1, Mathf.RoundToInt(objData.spawnDensity));

                for (int y = 0; y < mapChunkSize; y += stepSize)
                {
                    for (int x = 0; x < mapChunkSize; x += stepSize)
                    {
                        float rawHeight = currentNoiseMap[x, y];
                        if (rawHeight < region.minHeight || rawHeight > region.maxHeight) continue;
                        if (Random.value > objData.spawnChance) continue;

                        float localX = (x - halfSize) + Random.Range(-0.4f, 0.4f);
                        float localZ = (halfSize - y) + Random.Range(-0.4f, 0.4f);

                        float worldX = meshWorldPosition.x + (localX * meshWorldScale.x);
                        float worldZ = meshWorldPosition.z + (localZ * meshWorldScale.z);

                        Vector3 rayStart = new Vector3(worldX, 500f, worldZ);
                        RaycastHit hit;

                        // U?ywamy maski terenu, aby ignorowa? wod? i inne obiekty
                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1000f, terrainLayer))
                        {
                            Vector3 spawnPos = hit.point;
                            if (!IsTooClose(spawnPos, objData.minDistanceBetween))
                            {
                                SpawnObject(objData, spawnPos);
                                spawnedPositions.Add(spawnPos);
                            }
                        }
                    }
                }
            }
        }
    }

    bool IsTooClose(Vector3 pos, float minDistance)
    {
        foreach (Vector3 spawnedPos in spawnedPositions)
        {
            if (Vector3.Distance(pos, spawnedPos) < minDistance) return true;
        }
        return false;
    }

    void SpawnObject(RegionObjectData objData, Vector3 position)
    {
        GameObject obj = Instantiate(objData.prefab, position, Quaternion.identity);
        obj.transform.localScale = Vector3.one * Random.Range(objData.minScale, objData.maxScale);
        if (objData.randomRotation) obj.transform.Rotate(0, Random.Range(0, 360), 0);

        // KLUCZ: Je?li objectsParent jest pusty, rodzicem NIE jest woda, tylko null (g?ówny poziom sceny)
        obj.transform.parent = (objectsParent != null) ? objectsParent : null;
        spawnedObjects.Add(obj);
    }

    void ClearSpawnedObjects()
    {
        foreach (GameObject obj in spawnedObjects) if (obj != null) DestroyImmediate(obj);
        spawnedObjects.Clear();
        spawnedPositions.Clear();

        if (objectsParent != null)
        {
            for (int i = objectsParent.childCount - 1; i >= 0; i--)
                DestroyImmediate(objectsParent.GetChild(i).gameObject);
        }
        if (spawnedWater != null) DestroyImmediate(spawnedWater);
    }

    Color GetRegionColor(float height)
    {
        foreach (RegionData region in regions)
            if (height >= region.minHeight && height <= region.maxHeight) return region.colour;
        return Color.white;
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
    [Range(0, 1)] public float minHeight;
    [Range(0, 1)] public float maxHeight;
    public Color colour;
    public List<RegionObjectData> objectsToSpawn;
}

[System.Serializable]
public class RegionObjectData
{
    public string name = "Nowy obiekt";
    public GameObject prefab;
    [Range(0, 1)] public float spawnChance = 0.1f;
    public float spawnDensity = 5f;
    public float minScale = 0.8f;
    public float maxScale = 1.2f;
    public bool randomRotation = true;
    public float minDistanceBetween = 3f;
}