using System.Collections.Generic;
using UnityEngine;
using System.Collections;

public class MapGenerator : MonoBehaviour
{
    // Tryb wyswietlania mapy
    public enum DrawMode { NoiseMap, ColourMap, Mesh };
    public DrawMode drawMode;

    // Rozmiar chunku mapy
    const int mapChunkSize = 241;

    [Range(0, 6)]
    public int levelOfDetail;

    // Parametry generowania noise
    public float noiseScale;
    public int octaves;

    [Range(0, 1)]
    public float persistance;

    public float lacunarity;
    public int seed;
    public Vector2 offset;

    // Wysokosc i ksztalt terenu
    public float meshHeightMultiplier;
    public AnimationCurve meshHeightCurve;

    public bool autoUpdate;

    [Header("Regiony i obiekty")]

    // Lista regionow mapy
    public List<RegionData> regions = new List<RegionData>();

    [Header("Ustawienia generowania obiektow")]

    // Czy generowac obiekty na mapie
    public bool generateObjects = true;

    // Parent dla wszystkich generowanych obiektow
    public Transform objectsParent;

    // Warstwa terenu do raycastow
    public LayerMask terrainLayer;

    [Header("Woda")]

    // Prefab wody
    public GameObject waterPrefab;

    // Wysokosc poziomu wody
    public float waterHeight = 10f;

    public float waterThickness = 3f;

    [Header("Rzeka")]

    // Czy generowac rzeke
    public bool generateRiver = true;

    [Range(1, 15)]

    // Szerokosc rzeki
    public int riverWidth = 4;

    [Tooltip("Glebokosc wciecia koryta rzeki w jednostkach Unity")]

    // Glebokosc rzeki
    public float riverDepth = 5f;

    // Jak bardzo rzeka moze skrecac
    public int riverWiggle = 3;


    [Header("Obiekty wewnatrz rzeki")]

    [Tooltip("Prefab, ktory ma sie pojawic na srodku rzeki")]

    // Prefab segmentu wody w rzece
    public GameObject riverObjectPrefab;

    [Range(0f, 1f)]

    [Tooltip("Szansa na zespawnowanie obiektu w danym punkcie rzeki (0 = brak, 1 = w kazdym punkcie)")]

    // Szansa na spawn segmentu rzeki
    public float riverObjectSpawnChance = 0.5f;

    // Aktualnie wygenerowana woda
    private GameObject spawnedWater;

    // Lista pozycji wszystkich spawnietych obiektow
    private List<Vector3> spawnedPositions = new List<Vector3>();

    // Lista wszystkich wygenerowanych obiektow
    private List<GameObject> spawnedObjects = new List<GameObject>();

    private Coroutine objectGenerationCoroutine;

    // Aktualna mapa wysokosci
    private float[,] currentNoiseMap;

    // Aktualny mesh terenu
    private GameObject currentTerrainMesh;

    private Transform terrainParent;

    // Lista bounds drog z wioski
    private List<Bounds> roadBoundsFromVillage = new List<Bounds>();

    // Punkty srodka rzeki
    private List<Vector3> riverCenterIndices = new List<Vector3>();


    public void GenerateMap()
    {
        // Zatrzymanie poprzedniego generowania obiektow
        if (objectGenerationCoroutine != null)
            StopCoroutine(objectGenerationCoroutine);

        // Czyszczenie poprzednich obiektow
        ClearSpawnedObjects();

        roadBoundsFromVillage.Clear();
        riverCenterIndices.Clear();

        // Generowanie mapy noise
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
            // Wyswietlenie samego noise
            if (drawMode == DrawMode.NoiseMap)
            {
                display.DrawTexture(
                    TextureGenerator.TextureFromHeightMap(currentNoiseMap)
                );
            }

            // Wyswietlenie mapy kolorow regionow
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

            // Generowanie pelnego mesha
            else if (drawMode == DrawMode.Mesh)
            {
                MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                    currentNoiseMap,
                    meshHeightMultiplier,
                    meshHeightCurve,
                    levelOfDetail
                );

                // Generowanie rzeki na meshu
                if (generateRiver)
                {
                    GenerateRiverOnMesh(meshData);
                }

                // Przypisywanie kolorow regionow
                for (int y = 0; y < mapChunkSize; y++)
                {
                    for (int x = 0; x < mapChunkSize; x++)
                    {
                        colourMap[y * mapChunkSize + x] = GetRegionColor(currentNoiseMap[x, y]);
                    }
                }

                // Rysowanie mesha
                display.DrawMesh(
                    meshData,
                    TextureGenerator.TextureFromColourMap(colourMap, mapChunkSize, mapChunkSize)
                );

                currentTerrainMesh = display.meshFilter.gameObject;
                terrainParent = currentTerrainMesh.transform.parent;

                // Ustawienie layera terenu
                if (terrainLayer != 0)
                {
                    currentTerrainMesh.layer = (int)Mathf.Log(terrainLayer.value, 2);
                }

                // Generowanie wody
                HandleWater();
            }
        }

        // Generowanie obiektow po wygenerowaniu mapy
        if (generateObjects && regions.Count > 0 && currentTerrainMesh != null)
        {
            objectGenerationCoroutine = StartCoroutine(GenerateObjectsWithDelay());
        }
    }

    void GenerateRiverOnMesh(MeshData meshData)
    {
        // Wyliczanie ilosci vertexow
        int meshIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2;
        int verticesPerLine = (mapChunkSize - 1) / meshIncrement + 1;

        int expectedCount = verticesPerLine * verticesPerLine;

        // Sprawdzenie poprawnosci verteksow
        if (meshData.vertices.Length != expectedCount)
        {
            Debug.LogError($"[River] Niezgodnosc rozmiaru: vertices={meshData.vertices.Length}, oczekiwano={expectedCount}");
            return;
        }

        Random.InitState(seed + 999);

        // Polowa szerokosci rzeki
        int halfWidth = Mathf.Max(1, Mathf.RoundToInt(riverWidth / (float)meshIncrement));

        int margin = halfWidth + 2;
        margin = Mathf.Clamp(margin, 2, verticesPerLine / 4);

        // Losowy start rzeki
        int riverVX = Random.Range(margin, verticesPerLine - margin);

        Debug.Log($"[River] verticesPerLine={verticesPerLine}, halfWidth={halfWidth}, startX={riverVX}, riverDepth={riverDepth}");

        // Przechodzenie przez cala mape
        for (int vy = 0; vy < verticesPerLine; vy++)
        {
            // Losowe przesuwanie rzeki
            riverVX += Random.Range(-riverWiggle, riverWiggle + 1);

            riverVX = Mathf.Clamp(riverVX, margin, verticesPerLine - margin);

            // Zapisywanie srodka rzeki
            riverCenterIndices.Add(new Vector3(riverVX * meshIncrement, vy * meshIncrement, riverDepth));

            // Tworzenie szerokosci rzeki
            for (int dvx = -halfWidth; dvx <= halfWidth; dvx++)
            {
                int drawVX = riverVX + dvx;

                if (drawVX < 0 || drawVX >= verticesPerLine) continue;

                // Maska wygladzajaca brzegi rzeki
                float t = (halfWidth == 0) ? 0f : Mathf.Abs(dvx) / (float)halfWidth;

                float mask = Mathf.Cos(t * Mathf.PI * 0.5f);
                mask = mask * mask;

                int vertexIndex = vy * verticesPerLine + drawVX;

                // Obnizanie verteksow
                meshData.vertices[vertexIndex].y -= riverDepth * mask;
            }
        }
    }

    void HandleWater()
    {
        // Usuniecie poprzedniej wody
        if (spawnedWater != null)
            DestroyImmediate(spawnedWater);

        if (waterPrefab == null) return;

        // Tworzenie nowej wody
        spawnedWater = Instantiate(waterPrefab);
        spawnedWater.name = "GeneratedWater";

        Collider waterCol = spawnedWater.GetComponent<Collider>();

        if (waterCol != null)
            waterCol.enabled = false;

        MeshFilter terrainMeshFilter = currentTerrainMesh.GetComponent<MeshFilter>();

        if (terrainMeshFilter != null && terrainMeshFilter.sharedMesh != null)
        {
            // Ustawienie pozycji wody
            spawnedWater.transform.position = new Vector3(
                currentTerrainMesh.transform.position.x,
                waterHeight,
                currentTerrainMesh.transform.position.z
            );

            spawnedWater.transform.parent = terrainParent;

            // Skalowanie wody do rozmiaru mapy
            float meshBaseSizeX = terrainMeshFilter.sharedMesh.bounds.size.x;
            float meshBaseSizeZ = terrainMeshFilter.sharedMesh.bounds.size.z;

            spawnedWater.transform.localScale = new Vector3(
                (meshBaseSizeX / 10f) * 100.05f,
                waterThickness,
                (meshBaseSizeZ / 10f) * 100.05f
            );
        }
    }

    // Coroutine opozniajacy generowanie obiektow
    IEnumerator GenerateObjectsWithDelay()
    {
        yield return null;

        // Dodanie MeshCollidera
        if (currentTerrainMesh != null)
        {
            MeshCollider col = currentTerrainMesh.GetComponent<MeshCollider>();

            if (col == null)
                col = currentTerrainMesh.AddComponent<MeshCollider>();

            col.sharedMesh = currentTerrainMesh.GetComponent<MeshFilter>().sharedMesh;
        }

        yield return null;

        // Generowanie wioski
        VillageGenerator village = FindObjectOfType<VillageGenerator>();

        if (village != null)
        {
            village.Generate(currentNoiseMap, currentTerrainMesh, seed);
            roadBoundsFromVillage = village.GetRoadBounds();
        }

        // Spawn segmentow rzeki
        if (generateRiver && riverObjectPrefab != null)
        {
            SpawnObjectsInRiverChannel();
        }

        // Spawn normalnych obiektow
        GenerateObjectsAvoidingRoads();

        // Wlaczenie collidera wody
        if (spawnedWater != null)
        {
            Collider waterCol = spawnedWater.GetComponent<Collider>();

            if (waterCol != null)
                waterCol.enabled = true;
        }
    }

    // Spawn obiektow w kanale rzeki
    void SpawnObjectsInRiverChannel()
    {
        Vector3 meshWorldPosition = currentTerrainMesh.transform.position;
        Vector3 meshWorldScale = currentTerrainMesh.transform.localScale;

        List<Vector3> worldRiverPoints = new List<Vector3>();

        MeshFilter meshFilter = currentTerrainMesh.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("[River] Brak Mesha do odczytania wysokosci!");
            return;
        }

        Vector3[] vertices = meshFilter.sharedMesh.vertices;

        int meshIncrement = (levelOfDetail == 0) ? 1 : levelOfDetail * 2;
        int verticesPerLine = (mapChunkSize - 1) / meshIncrement + 1;

        // Zamiana punktow rzeki na world position
        foreach (Vector3 riverPoint in riverCenterIndices)
        {
            int vx = Mathf.RoundToInt(riverPoint.x / meshIncrement);
            int vy = Mathf.RoundToInt(riverPoint.y / meshIncrement);

            int vertexIndex = vy * verticesPerLine + vx;

            if (vertexIndex >= 0 && vertexIndex < vertices.Length)
            {
                Vector3 localVertexPos = vertices[vertexIndex];

                float worldX = meshWorldPosition.x + (localVertexPos.x * meshWorldScale.x);
                float worldY = meshWorldPosition.y + (localVertexPos.y * meshWorldScale.y);
                float worldZ = meshWorldPosition.z + (localVertexPos.z * meshWorldScale.z);

                worldRiverPoints.Add(new Vector3(worldX, worldY, worldZ));
            }
        }

        // Tworzenie segmentow rzeki
        for (int i = 0; i < worldRiverPoints.Count; i++)
        {
            Vector3 currentPoint = worldRiverPoints[i];

            Quaternion rotation = Quaternion.identity;

            float distanceToNext = 2.0f;

            // Obrot segmentu w kierunku kolejnego punktu
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

            GameObject obj = Instantiate(riverObjectPrefab, currentPoint, rotation);

            obj.name = "RiverWater_Segment";

            // Skalowanie segmentu
            float segmentScaleX = meshWorldScale.x * riverWidth;
            float segmentScaleZ = distanceToNext * 1.4f;

            obj.transform.localScale = new Vector3(segmentScaleX, waterThickness, segmentScaleZ);

            obj.transform.parent = (terrainParent != null) ? terrainParent : null;

            Collider waterCol = obj.GetComponent<Collider>();

            if (waterCol != null)
                waterCol.enabled = false;

            spawnedObjects.Add(obj);
            spawnedPositions.Add(currentPoint);
        }
    }

    // Generowanie obiektow z omijaniem drog
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

                        // Sprawdzenie zakresu wysokosci regionu
                        if (rawHeight < region.minHeight || rawHeight > region.maxHeight)
                            continue;

                        // Losowa szansa spawnu
                        if (Random.value > objData.spawnChance)
                            continue;

                        float localX = (x - halfSize) + Random.Range(-0.4f, 0.4f);
                        float localZ = (halfSize - y) + Random.Range(-0.4f, 0.4f);

                        float worldX = meshWorldPosition.x + (localX * meshWorldScale.x);
                        float worldZ = meshWorldPosition.z + (localZ * meshWorldScale.z);

                        Vector3 rayStart = new Vector3(worldX, 500f, worldZ);

                        RaycastHit hit;

                        // Raycast do terenu
                        if (Physics.Raycast(rayStart, Vector3.down, out hit, 1000f, terrainLayer))
                        {
                            Vector3 spawnPos = hit.point;

                            // Pomijanie drog
                            if (IsPositionOnRoad(spawnPos)) continue;

                            // Sprawdzenie minimalnej odleglosci
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

    // Sprawdzenie czy pozycja jest na drodze
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

    // Sprawdzenie minimalnej odleglosci miedzy obiektami
    bool IsTooClose(Vector3 pos, float minDistance)
    {
        foreach (Vector3 spawnedPos in spawnedPositions)
        {
            if (Vector3.Distance(pos, spawnedPos) < minDistance)
                return true;
        }

        return false;
    }

    // Spawn pojedynczego obiektu
    void SpawnObject(RegionObjectData objData, Vector3 position)
    {
        GameObject obj = Instantiate(objData.prefab, position, Quaternion.identity);

        // Losowe skalowanie
        obj.transform.localScale = Vector3.one * Random.Range(objData.minScale, objData.maxScale);

        // Losowy obrot
        if (objData.randomRotation)
            obj.transform.Rotate(0, Random.Range(0, 360), 0);

        obj.transform.parent = (objectsParent != null) ? objectsParent : null;

        spawnedObjects.Add(obj);
    }

    // Usuwanie wszystkich wygenerowanych obiektow
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

    // Pobranie koloru regionu na podstawie wysokosci
    Color GetRegionColor(float height)
    {
        foreach (RegionData region in regions)
        {
            if (height >= region.minHeight && height <= region.maxHeight)
                return region.colour;
        }

        return Color.white;
    }

    // Walidacja wartosci w inspectorze
    void OnValidate()
    {
        if (lacunarity < 1) lacunarity = 1;
        if (octaves < 0) octaves = 0;
    }

    [System.Serializable]

    // Dane pojedynczego regionu
    public class RegionData
    {
        public string name = "Nowy region";

        [Range(0, 1)] public float minHeight;
        [Range(0, 1)] public float maxHeight;

        // Kolor regionu
        public Color colour;

        // Lista obiektow regionu
        public List<RegionObjectData> objectsToSpawn;
    }

    [System.Serializable]

    // Dane obiektu do generowania
    public class RegionObjectData
    {
        public string name = "Nowy obiekt";

        // Prefab obiektu
        public GameObject prefab;

        // Szansa na spawn
        [Range(0, 1)] public float spawnChance = 0.1f;

        // Gestosc spawnienia
        public float spawnDensity = 5f;

        // Zakres losowego scale
        public float minScale = 0.8f;
        public float maxScale = 1.2f;

        // Losowy obrot obiektu
        public bool randomRotation = true;

        // Minimalna odleglosc od innych obiektow
        public float minDistanceBetween = 3f;
    }
}