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

    [Header("Ustawienia generowania obiektow")]
    public bool generateObjects = true;
    public Transform objectsParent;
    public LayerMask terrainLayer;

    [Header("Woda")]
    public GameObject waterPrefab;
    public float waterHeight = 10f;
    public float waterThickness = 3f;

    [Header("Rzeka")]
    public bool generateRiver = true;

    [Range(1, 15)]
    public int riverWidth = 4;

    [Tooltip("Glebokosc wci?cia koryta rzeki w jednostkach Unity")]
    public float riverDepth = 5f;

    public int riverWiggle = 3;

    // --- NOWE PARAMETRY W INSPEKTORZE ---
    [Header("Obiekty wewnatrz rzeki")]
    [Tooltip("Prefab, ktory ma sie pojawic na srodku rzeki")]
    public GameObject riverObjectPrefab;
    [Range(0f, 1f)]
    [Tooltip("Szansa na zespawnowanie obiektu w danym punkcie rzeki (0 = brak, 1 = w kazdym punkcie)")]
    public float riverObjectSpawnChance = 0.5f;
    // -------------------------------------

    private GameObject spawnedWater;
    private List<Vector3> spawnedPositions = new List<Vector3>();
    private List<GameObject> spawnedObjects = new List<GameObject>();
    private Coroutine objectGenerationCoroutine;

    private float[,] currentNoiseMap;
    private GameObject currentTerrainMesh;
    private Transform terrainParent;

    private List<Bounds> roadBoundsFromVillage = new List<Bounds>();

    // --- NOWA ZMIENNA PRYWATNA ---
    private List<Vector3> riverCenterIndices = new List<Vector3>(); // Przechowuje lokalne X, Y oraz glebokosc rzeki
    // ------------------------------

    public void GenerateMap()
    {
        if (objectGenerationCoroutine != null)
            StopCoroutine(objectGenerationCoroutine);

        ClearSpawnedObjects();
        roadBoundsFromVillage.Clear();
        riverCenterIndices.Clear(); // NOWE: Czyszczenie listy przy nowej generacji

        currentNoiseMap = Noise.GenerateNoiseMap(
            mapChunkSize,
            mapChunkSize,
            seed,
            noiseScale,
            octaves,
            persistance,
            lacunarity,
            offset
        );

        Color[] colourMap = new Color[mapChunkSize * mapChunkSize];

        MapDisplay display = FindObjectOfType<MapDisplay>();

        if (display != null)
        {
            if (drawMode == DrawMode.NoiseMap)
            {
                display.DrawTexture(
                    TextureGenerator.TextureFromHeightMap(currentNoiseMap)
                );
            }
            else if (drawMode == DrawMode.ColourMap)
            {
                for (int y = 0; y < mapChunkSize; y++)
                {
                    for (int x = 0; x < mapChunkSize; x++)
                    {
                        colourMap[y * mapChunkSize + x] = GetRegionColor(currentNoiseMap[x, y]);
                    }
                }
                display.DrawTexture(
                    TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize)
                );
            }
            else if (drawMode == DrawMode.Mesh)
            {
                MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                    currentNoiseMap,
                    meshHeightMultiplier,
                    meshHeightCurve,
                    levelOfDetail
                );

                if (generateRiver)
                {
                    GenerateRiverOnMesh(meshData);
                }

                for (int y = 0; y < mapChunkSize; y++)
                {
                    for (int x = 0; x < mapChunkSize; x++)
                    {
                        colourMap[y * mapChunkSize + x] = GetRegionColor(currentNoiseMap[x, y]);
                    }
                }

                display.DrawMesh(
                    meshData,
                    TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize)
                );

                currentTerrainMesh = display.meshFilter.gameObject;
                terrainParent = currentTerrainMesh.transform.parent;

                if (terrainLayer != 0)
                {
                    currentTerrainMesh.layer = (int)Mathf.Log(terrainLayer.value, 2);
                }

                HandleWater();
            }
        }

        if (generateObjects && regions.Count > 0 && currentTerrainMesh != null)
        {
            objectGenerationCoroutine = StartCoroutine(GenerateObjectsWithDelay());
        }
    }

    void GenerateRiverOnMesh(MeshData meshData)
    {
        int meshIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2;
        int verticesPerLine = (mapChunkSize - 1) / meshIncrement + 1;

        int expectedCount = verticesPerLine * verticesPerLine;
        if (meshData.vertices.Length != expectedCount)
        {
            Debug.LogError($"[River] Niezgodnosc rozmiaru: vertices={meshData.vertices.Length}, oczekiwano={expectedCount}");
            return;
        }

        Random.InitState(seed + 999);

        int halfWidth = Mathf.Max(1, Mathf.RoundToInt(riverWidth / (float)meshIncrement));
        int margin = halfWidth + 2;
        margin = Mathf.Clamp(margin, 2, verticesPerLine / 4);

        int riverVX = Random.Range(margin, verticesPerLine - margin);

        Debug.Log($"[River] verticesPerLine={verticesPerLine}, halfWidth={halfWidth}, startX={riverVX}, riverDepth={riverDepth}");

        for (int vy = 0; vy < verticesPerLine; vy++)
        {
            riverVX += Random.Range(-riverWiggle, riverWiggle + 1);
            riverVX = Mathf.Clamp(riverVX, margin, verticesPerLine - margin);

            riverCenterIndices.Add(new Vector3(riverVX * meshIncrement, vy * meshIncrement, riverDepth));

            for (int dvx = -halfWidth; dvx <= halfWidth; dvx++)
            {
                int drawVX = riverVX + dvx;
                if (drawVX < 0 || drawVX >= verticesPerLine) continue;

                float t = (halfWidth == 0) ? 0f : Mathf.Abs(dvx) / (float)halfWidth;
                float mask = Mathf.Cos(t * Mathf.PI * 0.5f);
                mask = mask * mask;

                int vertexIndex = vy * verticesPerLine + drawVX;
                meshData.vertices[vertexIndex].y -= riverDepth * mask;
            }
        }
    }

    void HandleWater()
    {
        if (spawnedWater != null)
            DestroyImmediate(spawnedWater);

        if (waterPrefab == null) return;

        spawnedWater = Instantiate(waterPrefab);
        spawnedWater.name = "GeneratedWater";

        Collider waterCol = spawnedWater.GetComponent<Collider>();
        if (waterCol != null)
            waterCol.enabled = false;

        MeshFilter terrainMeshFilter = currentTerrainMesh.GetComponent<MeshFilter>();

        if (terrainMeshFilter != null && terrainMeshFilter.sharedMesh != null)
        {
            spawnedWater.transform.position = new Vector3(
                currentTerrainMesh.transform.position.x,
                waterHeight,
                currentTerrainMesh.transform.position.z
            );

            spawnedWater.transform.parent = terrainParent;

            float meshBaseSizeX = terrainMeshFilter.sharedMesh.bounds.size.x;
            float meshBaseSizeZ = terrainMeshFilter.sharedMesh.bounds.size.z;

            spawnedWater.transform.localScale = new Vector3(
                (meshBaseSizeX / 10f) * 100.05f,
                waterThickness,
                (meshBaseSizeZ / 10f) * 100.05f
            );
        }
    }

    IEnumerator GenerateObjectsWithDelay()
    {
        yield return null;

        if (currentTerrainMesh != null)
        {
            MeshCollider col = currentTerrainMesh.GetComponent<MeshCollider>();
            if (col == null)
                col = currentTerrainMesh.AddComponent<MeshCollider>();

            col.sharedMesh = currentTerrainMesh.GetComponent<MeshFilter>().sharedMesh;
        }

        yield return null;

        VillageGenerator village = FindObjectOfType<VillageGenerator>();
        if (village != null)
        {
            village.Generate(currentNoiseMap, currentTerrainMesh, seed);
            roadBoundsFromVillage = village.GetRoadBounds();
        }

        // --- NOWE: Wywo?anie spawnowania obiektów w rzece ---
        if (generateRiver && riverObjectPrefab != null)
        {
            SpawnObjectsInRiverChannel();
        }
        // ----------------------------------------------------

        GenerateObjectsAvoidingRoads();

        if (spawnedWater != null)
        {
            Collider waterCol = spawnedWater.GetComponent<Collider>();
            if (waterCol != null)
                waterCol.enabled = true;
        }
    }

    void SpawnObjectsInRiverChannel()
    {
        Vector3 meshWorldPosition = currentTerrainMesh.transform.position;
        Vector3 meshWorldScale = currentTerrainMesh.transform.localScale;
        float halfSize = (mapChunkSize - 1) / 2f;

        List<Vector3> worldRiverPoints = new List<Vector3>();

        // Pobieramy filtr mesha, aby wyci?gn?? z niego ostatecznie ukszta?towane wierzcho?ki
        MeshFilter meshFilter = currentTerrainMesh.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("[River] Brak Mesha do odczytania wysoko?ci!");
            return;
        }

        Vector3[] vertices = meshFilter.sharedMesh.vertices;

        // Obliczamy struktur? siatki w zale?no?ci od LOD (tak samo jak przy generowaniu mesha)
        int meshIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2;
        int verticesPerLine = (mapChunkSize - 1) / meshIncrement + 1;

        // Przechodzimy po zapisanych punktach ?rodka rzeki
        foreach (Vector3 riverPoint in riverCenterIndices)
        {
            // Odzyskiwany indeks wierzcho?ka (vy i vx)
            int vx = Mathf.RoundToInt(riverPoint.x / meshIncrement);
            int vy = Mathf.RoundToInt(riverPoint.y / meshIncrement);

            int vertexIndex = vy * verticesPerLine + vx;

            if (vertexIndex >= 0 && vertexIndex < vertices.Length)
            {
                // Pobieramy pozycj? lokaln? wprost z geometrii mesha (ma ju? w sobie obni?enie rzeki i wysoko?? góry!)
                Vector3 localVertexPos = vertices[vertexIndex];

                // Przeliczamy pozycj? lokaln? wierzcho?ka na pozycj? w ?wiecie (World Space)
                float worldX = meshWorldPosition.x + (localVertexPos.x * meshWorldScale.x);
                float worldY = meshWorldPosition.y + (localVertexPos.y * meshWorldScale.y);
                float worldZ = meshWorldPosition.z + (localVertexPos.z * meshWorldScale.z);

                worldRiverPoints.Add(new Vector3(worldX, worldY, worldZ));
            }
        }

        // Spawnowanie i ??czenie segmentów
        for (int i = 0; i < worldRiverPoints.Count; i++)
        {
            Vector3 currentPoint = worldRiverPoints[i];
            Quaternion rotation = Quaternion.identity;
            float distanceToNext = 2.0f;

            if (i < worldRiverPoints.Count - 1)
            {
                Vector3 nextPoint = worldRiverPoints[i + 1];
                Vector3 direction = nextPoint - currentPoint;

                if (direction != Vector3.zero)
                {
                    rotation = Quaternion.LookRotation(direction);
                    distanceToNext = direction.magnitude;
                }
            }
            else if (i > 0)
            {
                rotation = spawnedObjects[spawnedObjects.Count - 1].transform.rotation;
            }

            // Tworzenie wody – dotyka idealnie punktu wierzcho?ka mesha
            GameObject obj = Instantiate(riverObjectPrefab, currentPoint, rotation);
            obj.name = "RiverWater_Segment";

            // Skalowanie
            float segmentScaleX = meshWorldScale.x * riverWidth;
            float segmentScaleZ = distanceToNext * 1.4f; // Nak?adanie segmentów zapobiega dziurom

            obj.transform.localScale = new Vector3(segmentScaleX, waterThickness, segmentScaleZ);
            obj.transform.parent = (terrainParent != null) ? terrainParent : null;

            Collider waterCol = obj.GetComponent<Collider>();
            if (waterCol != null)
                waterCol.enabled = false;

            spawnedObjects.Add(obj);
            spawnedPositions.Add(currentPoint);
        }
    }

    void GenerateObjectsAvoidingRoads()
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

                        if (rawHeight < region.minHeight || rawHeight > region.maxHeight)
                            continue;

                        if (Random.value > objData.spawnChance)
                            continue;

                        float localX = (x - halfSize) + Random.Range(-0.4f, 0.4f);
                        float localZ = (halfSize - y) + Random.Range(-0.4f, 0.4f);

                        float worldX = meshWorldPosition.x + (localX * meshWorldScale.x);
                        float worldZ = meshWorldPosition.z + (localZ * meshWorldScale.z);

                        Vector3 rayStart = new Vector3(worldX, 500f, worldZ);
                        RaycastHit hit;

                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1000f, terrainLayer))
                        {
                            Vector3 spawnPos = hit.point;

                            if (IsPositionOnRoad(spawnPos)) continue;

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

    bool IsPositionOnRoad(Vector3 position)
    {
        foreach (Bounds roadBound in roadBoundsFromVillage)
        {
            if (position.x >= roadBound.min.x - 2f && position.x <= roadBound.max.x + 2f &&
                position.z >= roadBound.min.z - 2f && position.z <= roadBound.max.z + 2f)
            {
                if (Vector3.Distance(new Vector3(position.x, 0, position.z),
                    new Vector3(roadBound.center.x, 0, roadBound.center.z)) < roadBound.extents.x + 2f)
                {
                    return true;
                }
            }
        }
        return false;
    }

    bool IsTooClose(Vector3 pos, float minDistance)
    {
        foreach (Vector3 spawnedPos in spawnedPositions)
        {
            if (Vector3.Distance(pos, spawnedPos) < minDistance)
                return true;
        }
        return false;
    }

    void SpawnObject(RegionObjectData objData, Vector3 position)
    {
        GameObject obj = Instantiate(objData.prefab, position, Quaternion.identity);

        obj.transform.localScale = Vector3.one * Random.Range(objData.minScale, objData.maxScale);

        if (objData.randomRotation)
            obj.transform.Rotate(0, Random.Range(0, 360), 0);

        obj.transform.parent = (objectsParent != null) ? objectsParent : null;

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

        if (objectsParent != null)
        {
            for (int i = objectsParent.childCount - 1; i >= 0; i--)
                DestroyImmediate(objectsParent.GetChild(i).gameObject);
        }

        if (spawnedWater != null)
            DestroyImmediate(spawnedWater);
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

    void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
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
}