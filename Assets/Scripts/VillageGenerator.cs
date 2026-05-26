using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VillageGenerator : MonoBehaviour
{
    [Header("Generalne")]
    public float villageRadius = 60f;
    public float maxFlatVariance = 0.035f;
    public float minVillageHeight = 0.30f;
    public float maxVillageHeight = 0.60f;
    public LayerMask terrainLayer;
    public Transform villageParent;

    [Header("Dwie wioski")]
    public bool generateTwoVillages = true;
    public float villageSeparation = 200f; // minimalna odleg?o?? mi?dzy centrami wiosek

    [Header("Woda")]
    public float waterLevel = 0.3f; // komórki noiseMap poni?ej tej warto?ci s? wod? i s? omijane

    [Header("Ulice")]
    public GameObject roadSegmentPrefab;
    public float roadWidth = 4f;
    public float roadSegmentLength = 4f;

    [Header("Budynki – prefaby")]
    public GameObject[] housePrefabs;
    public GameObject[] shopPrefabs;
    public GameObject churchPrefab;
    public GameObject schoolPrefab;

    [Header("Budynki – liczby")]
    public int houseCount = 12;
    public int shopCount = 4;
    public int churchCount = 1;
    public int schoolCount = 1;

    [Header("Ozdoby")]
    public GameObject electricPolePrefab;
    public GameObject streetLightPrefab;
    public GameObject benchPrefab;
    public GameObject trashcanPrefab;

    [Header("Odleg?o?ci")]
    public float minBuildingDistance = 5f;
    public float decorSpacing = 8f;
    public float decorScale = 2f;
    public float decorOffsetFromRoad = 3f;
    public float minDecorDistance = 3f;

    enum PlotType { House, Shop, Church, School, Empty }

    class PlotData
    {
        public Vector3 position;
        public Vector3 roadDirection;
        public PlotType type;
        public bool leftSide;
    }

    class RoadData
    {
        public Vector3 start, end, direction;
        public float length;
    }

    List<GameObject> spawnedObjects = new List<GameObject>();
    List<PlotData> plots = new List<PlotData>();
    List<RoadData> roads = new List<RoadData>();
    List<Bounds> placedBounds = new List<Bounds>();
    List<Bounds> roadBounds = new List<Bounds>();
    List<Bounds> placedDecorBounds = new List<Bounds>();

    Vector3 villageCenter;

    // =========================================================================
    // Centra obu wiosek – zapisane po wygenerowaniu, u?ywane do po??czenia drog?
    // =========================================================================
    public Vector3 villageCenter1;
    public Vector3 villageCenter2;

    // Przechowywane na czas generowania – potrzebne w ConnectVillages
    float[,] _noiseMap;
    GameObject _terrainObj;

    public List<Bounds> GetRoadBounds()
    {
        return roadBounds;
    }

    // =========================================================================
    // G?ówna metoda generowania – teraz obs?uguje opcj? dwóch wiosek
    // =========================================================================
    public void Generate(float[,] noiseMap, GameObject terrainObj, int mapSeed)
    {
        ClearVillage();
        Random.InitState(mapSeed);
        _noiseMap = noiseMap;
        _terrainObj = terrainObj;

        if (generateTwoVillages)
        {
            GenerateTwoVillages(noiseMap, terrainObj, mapSeed);
        }
        else
        {
            GenerateSingleVillage(noiseMap, terrainObj, mapSeed);
        }
    }

    // =========================================================================
    // Generuje dwie wioski w ró?nych miejscach i ??czy je drog?
    // =========================================================================
    void GenerateTwoVillages(float[,] noiseMap, GameObject terrainObj, int mapSeed)
    {
        // --- Wioska 1 ---
        Random.InitState(mapSeed);
        if (!FindFlatArea(noiseMap, terrainObj, out villageCenter1, Vector2.zero))
        {
            Debug.LogWarning("[VillageGenerator] Nie znaleziono p?askiego terenu dla wioski 1!");
            return;
        }
        Debug.Log($"[VillageGenerator] Centrum wioski 1: {villageCenter1}");
        villageCenter = villageCenter1;
        GenerateRoads();
        AssignPlots();
        PlaceBuildings();
        PlaceDecorations();

        // --- Wioska 2 ---
        // Szukamy p?askiego miejsca z dala od wioski 1
        Random.InitState(mapSeed + 1);
        bool found2 = FindFlatAreaFarFrom(noiseMap, terrainObj, villageCenter1, villageSeparation, out villageCenter2);
        if (!found2)
        {
            Debug.LogWarning("[VillageGenerator] Nie znaleziono p?askiego terenu dla wioski 2 – generuj? tylko jedn? wiosk?.");
            return;
        }
        Debug.Log($"[VillageGenerator] Centrum wioski 2: {villageCenter2}");
        villageCenter = villageCenter2;
        GenerateRoads();
        AssignPlots();
        PlaceBuildings();
        PlaceDecorations();

        // --- Droga ??cz?ca obie wioski ---
        ConnectVillages(villageCenter1, villageCenter2);

        Debug.Log($"[VillageGenerator] Wygenerowano dwie wioski, dystans: {Vector3.Distance(villageCenter1, villageCenter2):F1} jednostek.");
    }

    // =========================================================================
    // Oryginalna logika generowania pojedynczej wioski (bez zmian)
    // =========================================================================
    void GenerateSingleVillage(float[,] noiseMap, GameObject terrainObj, int mapSeed)
    {
        if (!FindFlatArea(noiseMap, terrainObj, out villageCenter, Vector2.zero))
        {
            Debug.LogWarning("[VillageGenerator] Nie znaleziono p?askiego terenu!");
            return;
        }

        Debug.Log($"[VillageGenerator] Centrum: {villageCenter}");
        GenerateRoads();
        AssignPlots();
        PlaceBuildings();
        PlaceDecorations();

        int placed = 0;
        foreach (var p in plots) if (p.type != PlotType.Empty) placed++;
        Debug.Log($"[VillageGenerator] Gotowe – dzia?ek do zabudowy: {placed}, ulic: {roads.Count}, budynków: {placedBounds.Count}, dekoracji: {placedDecorBounds.Count}");
    }

    // =========================================================================
    // Droga ??cz?ca dwa centra wiosek – trasa wyznaczana A* omijaj?cym wod?
    // =========================================================================
    void ConnectVillages(Vector3 center1, Vector3 center2)
    {
        if (roadSegmentPrefab == null) return;

        List<Vector3> path = FindPathAvoidingWater(center1, center2);

        if (path == null || path.Count < 2)
        {
            // Fallback: prosta linia je?li A* nie znalaz? trasy
            Debug.LogWarning("[VillageGenerator] A* nie znalaz? trasy omijaj?cej wod? – ??cz? drog? prost?.");
            RoadData fallback = new RoadData
            {
                start = center1,
                end = center2,
                direction = (center2 - center1).normalized,
                length = Vector3.Distance(center1, center2)
            };
            roads.Add(fallback);
            SpawnRoad(fallback);
            return;
        }

        // Spawnjemy odcinki drogi mi?dzy kolejnymi punktami ?cie?ki
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 segStart = path[i];
            Vector3 segEnd = path[i + 1];
            Vector3 dir = (segEnd - segStart).normalized;
            if (dir == Vector3.zero) continue;

            RoadData seg = new RoadData
            {
                start = segStart,
                end = segEnd,
                direction = dir,
                length = Vector3.Distance(segStart, segEnd)
            };
            roads.Add(seg);
            SpawnRoad(seg);
        }

        Debug.Log($"[VillageGenerator] Po??czono wioski tras? A* ({path.Count} punktów), dystans: {Vector3.Distance(center1, center2):F1}.");
    }

    // =========================================================================
    // A* na siatce noiseMap – zwraca list? punktów w przestrzeni ?wiata
    // =========================================================================
    List<Vector3> FindPathAvoidingWater(Vector3 worldStart, Vector3 worldEnd)
    {
        if (_noiseMap == null || _terrainObj == null) return null;

        int mapSize = _noiseMap.GetLength(0);

        // Przelicz pozycje ?wiata na komórki mapy
        Vector2Int startCell = WorldToMapCell(worldStart, mapSize);
        Vector2Int endCell = WorldToMapCell(worldEnd, mapSize);

        // --- A* ---
        var openSet = new SortedList<float, Vector2Int>(Comparer<float>.Create((a, b) => a == b ? 1 : a.CompareTo(b)));
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();
        var inOpen = new HashSet<Vector2Int>();

        gScore[startCell] = 0f;
        float h0 = Heuristic(startCell, endCell);
        openSet.Add(h0, startCell);
        inOpen.Add(startCell);

        int maxIter = mapSize * mapSize; // zabezpieczenie przed niesko?czon? p?tl?
        int iter = 0;

        while (openSet.Count > 0 && iter++ < maxIter)
        {
            // We? w?ze? z najni?szym f
            var kv = openSet.First();
            Vector2Int current = kv.Value;
            openSet.RemoveAt(0);
            inOpen.Remove(current);

            if (current == endCell)
                return ReconstructPath(cameFrom, current, mapSize);

            // 8 s?siadów
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    Vector2Int nb = new Vector2Int(current.x + dx, current.y + dy);
                    if (nb.x < 0 || nb.x >= mapSize || nb.y < 0 || nb.y >= mapSize) continue;

                    // Kara za wod? – komórki poni?ej waterLevel s? bardzo kosztowne,
                    // ale nadal przechodne (fallback gdy nie ma innej trasy)
                    float h = _noiseMap[nb.x, nb.y];
                    float extra = h < waterLevel ? 1000f : 0f;
                    float step = (dx != 0 && dy != 0) ? 1.414f : 1f;
                    float tentG = gScore[current] + step + extra;

                    if (!gScore.ContainsKey(nb) || tentG < gScore[nb])
                    {
                        cameFrom[nb] = current;
                        gScore[nb] = tentG;
                        float f = tentG + Heuristic(nb, endCell);
                        if (!inOpen.Contains(nb))
                        {
                            openSet.Add(f, nb);
                            inOpen.Add(nb);
                        }
                    }
                }
        }

        return null; // brak trasy
    }

    float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
    }

    List<Vector3> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current, int mapSize)
    {
        var cells = new List<Vector2Int>();
        while (cameFrom.ContainsKey(current))
        {
            cells.Add(current);
            current = cameFrom[current];
        }
        cells.Add(current);
        cells.Reverse();

        // Upro?? ?cie?k? – zostaw tylko punkty gdzie zmienia si? kierunek
        var simplified = new List<Vector2Int> { cells[0] };
        for (int i = 1; i < cells.Count - 1; i++)
        {
            Vector2Int prev = cells[i - 1];
            Vector2Int next = cells[i + 1];
            Vector2Int dirA = cells[i] - prev;
            Vector2Int dirB = next - cells[i];
            if (dirA != dirB) simplified.Add(cells[i]);
        }
        simplified.Add(cells[cells.Count - 1]);

        // Przelicz komórki mapy na punkty ?wiata (przyci?gni?tye do terenu)
        var worldPath = new List<Vector3>();
        foreach (var cell in simplified)
            worldPath.Add(SnapToTerrain(MapToWorld(cell.x, cell.y, mapSize, _terrainObj)));

        return worldPath;
    }

    Vector2Int WorldToMapCell(Vector3 worldPos, int mapSize)
    {
        float half = (mapSize - 1) / 2f;
        Vector3 sc = _terrainObj.transform.localScale;
        Vector3 p = _terrainObj.transform.position;
        int mx = Mathf.RoundToInt((worldPos.x - p.x) / sc.x + half);
        int my = Mathf.RoundToInt(half - (worldPos.z - p.z) / sc.z);
        mx = Mathf.Clamp(mx, 0, mapSize - 1);
        my = Mathf.Clamp(my, 0, mapSize - 1);
        return new Vector2Int(mx, my);
    }

    public void ClearVillage()
    {
        foreach (var o in spawnedObjects) if (o != null) DestroyImmediate(o);
        spawnedObjects.Clear();
        plots.Clear();
        roads.Clear();
        placedBounds.Clear();
        roadBounds.Clear();
        placedDecorBounds.Clear();

        if (villageParent != null)
            for (int i = villageParent.childCount - 1; i >= 0; i--)
                DestroyImmediate(villageParent.GetChild(i).gameObject);
    }

    // =========================================================================
    // FindFlatArea – nowa wersja z opcjonalnym przesuni?ciem strefy poszukiwa?
    // =========================================================================
    bool FindFlatArea(float[,] noiseMap, GameObject terrainObj, out Vector3 worldCenter, Vector2 searchOffset)
    {
        int size = noiseMap.GetLength(0);
        int step = 5, win = 20;
        float bestVar = float.MaxValue;
        int bx = size / 2, by = size / 2;

        int ox = Mathf.RoundToInt(searchOffset.x);
        int oy = Mathf.RoundToInt(searchOffset.y);

        for (int y = win; y < size - win; y += step)
            for (int x = win; x < size - win; x += step)
            {
                float sum = 0, sumSq = 0; int cnt = 0; bool bad = false;
                for (int dy = -win / 2; dy <= win / 2; dy += 2)
                {
                    for (int dx = -win / 2; dx <= win / 2; dx += 2)
                    {
                        float h = noiseMap[Mathf.Clamp(x + dx + ox, 0, size - 1), Mathf.Clamp(y + dy + oy, 0, size - 1)];
                        if (h < minVillageHeight || h > maxVillageHeight) { bad = true; break; }
                        sum += h; sumSq += h * h; cnt++;
                    }
                    if (bad) break;
                }
                if (bad || cnt == 0) continue;
                float v = sumSq / cnt - (sum / cnt) * (sum / cnt);
                if (v < bestVar) { bestVar = v; bx = x + ox; by = y + oy; }
            }

        if (bestVar > maxFlatVariance)
            Debug.LogWarning($"[VillageGenerator] Wariancja {bestVar:F4} > próg, u?ywam najlepszego miejsca.");

        worldCenter = MapToWorld(bx, by, size, terrainObj);
        return true;
    }

    // =========================================================================
    // Szuka p?askiego terenu w odleg?o?ci co najmniej minDist od excludePos
    // =========================================================================
    bool FindFlatAreaFarFrom(float[,] noiseMap, GameObject terrainObj,
        Vector3 excludeWorldPos, float minDist, out Vector3 worldCenter)
    {
        int size = noiseMap.GetLength(0);
        int step = 5, win = 20;
        float bestVar = float.MaxValue;
        int bx = -1, by = -1;

        for (int y = win; y < size - win; y += step)
            for (int x = win; x < size - win; x += step)
            {
                // Wst?pne sprawdzenie odleg?o?ci w przestrzeni mapy
                // (precyzyjne sprawdzenie w przestrzeni ?wiata poni?ej)
                float sum = 0, sumSq = 0; int cnt = 0; bool bad = false;
                for (int dy = -win / 2; dy <= win / 2; dy += 2)
                {
                    for (int dx = -win / 2; dx <= win / 2; dx += 2)
                    {
                        float h = noiseMap[Mathf.Clamp(x + dx, 0, size - 1), Mathf.Clamp(y + dy, 0, size - 1)];
                        if (h < minVillageHeight || h > maxVillageHeight) { bad = true; break; }
                        sum += h; sumSq += h * h; cnt++;
                    }
                    if (bad) break;
                }
                if (bad || cnt == 0) continue;

                // Sprawd? odleg?o?? w przestrzeni ?wiata
                Vector3 candidateWorld = MapToWorld(x, y, size, terrainObj);
                if (Vector3.Distance(new Vector3(candidateWorld.x, 0, candidateWorld.z),
                                     new Vector3(excludeWorldPos.x, 0, excludeWorldPos.z)) < minDist)
                    continue;

                float v = sumSq / cnt - (sum / cnt) * (sum / cnt);
                if (v < bestVar) { bestVar = v; bx = x; by = y; }
            }

        if (bx == -1)
        {
            // Fallback: u?yj najlepszego miejsca bez ograniczenia odleg?o?ci i ostrze?
            Debug.LogWarning("[VillageGenerator] Brak miejsca spe?niaj?cego minDist – u?ywam najlepszego dost?pnego.");
            return FindFlatArea(noiseMap, terrainObj, out worldCenter, Vector2.zero);
        }

        if (bestVar > maxFlatVariance)
            Debug.LogWarning($"[VillageGenerator] Wioska 2: wariancja {bestVar:F4} > próg.");

        worldCenter = MapToWorld(bx, by, size, terrainObj);
        return true;
    }

    void GenerateRoads()
    {
        float r = villageRadius, off = r * .4f;
        AddRoad(villageCenter + Vector3.forward * (-r * .85f), villageCenter + Vector3.forward * (r * .85f));
        AddRoad(villageCenter + Vector3.right * (-r * .85f), villageCenter + Vector3.right * (r * .85f));
        AddRoad(villageCenter + Vector3.right * (-off) + Vector3.forward * (-r * .5f),
                villageCenter + Vector3.right * (-off) + Vector3.forward * (r * .5f));
        AddRoad(villageCenter + Vector3.right * (off) + Vector3.forward * (-r * .5f),
                villageCenter + Vector3.right * (off) + Vector3.forward * (r * .5f));

        if (roadSegmentPrefab != null)
            foreach (var rd in roads) SpawnRoad(rd);
    }

    void AddRoad(Vector3 s, Vector3 e)
    {
        Vector3 d = (e - s).normalized;
        roads.Add(new RoadData { start = s, end = e, direction = d, length = Vector3.Distance(s, e) });
    }

    void SpawnRoad(RoadData rd)
    {
        float t = 0;
        var rot = Quaternion.LookRotation(rd.direction);

        while (t < rd.length)
        {
            Vector3 pos = SnapToTerrain(rd.start + rd.direction * (t + roadSegmentLength * .5f));
            var seg = Spawn(roadSegmentPrefab, pos, rot);
            if (seg)
            {
                seg.transform.localScale = new Vector3(roadWidth, 0.05f, roadSegmentLength);
                Bounds roadBound = new Bounds(pos, new Vector3(roadWidth, 0.5f, roadSegmentLength));
                roadBounds.Add(roadBound);
            }
            t += roadSegmentLength;
        }
    }

    void AssignPlots()
    {
        // AssignPlots zachowany dla kompatybilno?ci (wype?nia list? plots u?ywan? przez inne systemy).
        // Logika budynków przeniesiona do PlaceBuildings.
        List<PlotData> candidates = new List<PlotData>();
        float fixedPlotWidth = 15f;
        float fixedPlotDepth = 15f;

        foreach (var rd in roads)
        {
            float t = fixedPlotWidth;
            while (t < rd.length - fixedPlotWidth)
            {
                Vector3 rp = rd.start + rd.direction * t;
                Vector3 perp = Vector3.Cross(rd.direction, Vector3.up).normalized;

                for (int side = -1; side <= 1; side += 2)
                {
                    float distanceFromRoadEdge = 2.5f;
                    Vector3 pos = SnapToTerrain(rp + perp * side * (roadWidth * 0.5f + fixedPlotDepth * 0.5f + distanceFromRoadEdge));

                    if (Vector3.Distance(pos, villageCenter) > villageRadius) continue;

                    bool isOnRoad = false;
                    foreach (Bounds roadBound in roadBounds)
                    {
                        if (Vector3.Distance(new Vector3(pos.x, 0, pos.z),
                                           new Vector3(roadBound.center.x, 0, roadBound.center.z)) < roadWidth + 1.5f)
                        {
                            isOnRoad = true;
                            break;
                        }
                    }

                    if (!isOnRoad)
                    {
                        candidates.Add(new PlotData
                        {
                            position = pos,
                            roadDirection = rd.direction,
                            type = PlotType.Empty,
                            leftSide = (side == -1)
                        });
                    }
                }
                t += fixedPlotWidth + 1f;
            }
        }

        plots.AddRange(candidates);
    }

    // -------------------------------------------------------------------------
    // Dopasuj budynek do terenu:
    //   1. Znajd? wysoko?ci terenu w 4 naro?nikach podstawy + centrum
    //   2. Wyznacz p?aszczyzn? najlepiej pasuj?c? (metoda Newella)
    //   3. Ustaw pozycj? tak, ?eby DOLNA kraw?d? boundsów siedzia?a na tej p?aszczy?nie
    // -------------------------------------------------------------------------
    bool SnapBuildingToTerrain(GameObject prefab, Vector3 centerPos, Quaternion facingRot,
        out Vector3 finalPos, out Quaternion finalRot)
    {
        finalPos = centerPos;
        finalRot = facingRot;

        // --- krok 1: poznaj rozmiary prefaba w kierunku patrzenia ---
        GameObject temp = Instantiate(prefab, Vector3.zero, facingRot);
        Bounds b = CalculateBounds(temp);
        DestroyImmediate(temp);

        // Je?li prefab nie ma ?adnych rendererów, nie mo?emy nic zrobi?
        if (b.size == Vector3.zero) return false;

        float hw = b.extents.x;   // po?owa szeroko?ci (o? X lokalnie)
        float hd = b.extents.z;   // po?owa g??boko?ci (o? Z lokalnie)

        Vector3 right = facingRot * Vector3.right;
        Vector3 forward = facingRot * Vector3.forward;

        // --- krok 2: 4 naro?niki + ?rodek podstawy w przestrzeni ?wiata ---
        Vector3[] samplePoints = new Vector3[]
        {
            centerPos + right *  hw + forward *  hd,   // przód-prawy
            centerPos - right *  hw + forward *  hd,   // przód-lewy
            centerPos - right *  hw - forward *  hd,   // ty?-lewy
            centerPos + right *  hw - forward *  hd,   // ty?-prawy
            centerPos                                   // ?rodek
        };

        Vector3[] groundPts = new Vector3[samplePoints.Length];
        int hits = 0;
        foreach (var sp in samplePoints)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(sp.x, 500f, sp.z), Vector3.down, out hit, 1000f, terrainLayer))
                groundPts[hits++] = hit.point;
        }

        if (hits < 3) return false;

        // --- krok 3: normalna p?aszczyzny metod? Newella ---
        Vector3 normal = Vector3.zero;
        for (int i = 0; i < hits; i++)
        {
            Vector3 curr = groundPts[i];
            Vector3 next = groundPts[(i + 1) % hits];
            normal.x += (curr.y - next.y) * (curr.z + next.z);
            normal.y += (curr.z - next.z) * (curr.x + next.x);
            normal.z += (curr.x - next.x) * (curr.y + next.y);
        }
        normal = normal.normalized;
        if (normal.y < 0) normal = -normal;

        // --- krok 4: centroid punktów terenu = punkt zakotwiczenia ---
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < hits; i++) centroid += groundPts[i];
        centroid /= hits;

        // --- krok 5: finalna rotacja = przechylenie wg normalnej + kierunek patrzenia ---
        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, normal);
        finalRot = tilt * facingRot;

        // --- krok 6: przesu? budynek w gór? tak, ?eby dolna ?cianka boundsów
        //             siedzia?a dok?adnie na centroidzie terenu ---
        GameObject temp2 = Instantiate(prefab, Vector3.zero, finalRot);
        Bounds b2 = CalculateBounds(temp2);
        DestroyImmediate(temp2);

        // bottomOffset = odleg?o?? od pivotu obiektu do dolnej kraw?dzi boundsów
        float bottomOffset = -b2.min.y;
        finalPos = centroid + Vector3.up * bottomOffset;

        return true;
    }

    void PlaceBuildings()
    {
        placedBounds.Clear();
        int buildingsPlaced = 0;

        // Kolejka: wszystkie budynki do postawienia
        List<(PlotType type, GameObject prefab)> buildingQueue = new List<(PlotType, GameObject)>();
        for (int i = 0; i < churchCount; i++)
            buildingQueue.Add((PlotType.Church, churchPrefab));
        for (int i = 0; i < schoolCount; i++)
            buildingQueue.Add((PlotType.School, schoolPrefab));
        for (int i = 0; i < shopCount; i++)
        {
            var p = shopPrefabs != null && shopPrefabs.Length > 0
                ? shopPrefabs[Random.Range(0, shopPrefabs.Length)] : null;
            buildingQueue.Add((PlotType.Shop, p));
        }
        for (int i = 0; i < houseCount; i++)
        {
            var p = housePrefabs != null && housePrefabs.Length > 0
                ? housePrefabs[Random.Range(0, housePrefabs.Length)] : null;
            buildingQueue.Add((PlotType.House, p));
        }

        // Wszystkie mo?liwe pozycje wzd?u? dróg
        List<(Vector3 pos, Vector3 roadDir, bool leftSide)> allCandidates =
            GenerateAllCandidatePositions();

        // Potasuj, ?eby budynki by?y roz?o?one ró?norodnie
        for (int i = allCandidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = allCandidates[i]; allCandidates[i] = allCandidates[j]; allCandidates[j] = tmp;
        }

        foreach (var building in buildingQueue)
        {
            if (building.prefab == null) continue;

            bool placed = false;

            foreach (var candidate in allCandidates)
            {
                // Kierunek "twarz? do drogi"
                Vector3 faceDir = candidate.leftSide
                    ? -Vector3.Cross(candidate.roadDir, Vector3.up)
                    : Vector3.Cross(candidate.roadDir, Vector3.up);
                faceDir.y = 0;
                Quaternion baseRot = faceDir != Vector3.zero
                    ? Quaternion.LookRotation(faceDir) : Quaternion.identity;

                // Dopasuj do terenu (przechylenie + pozycja)
                Vector3 finalPos;
                Quaternion finalRot;
                if (!SnapBuildingToTerrain(building.prefab, candidate.pos, baseRot,
                                           out finalPos, out finalRot))
                    continue;

                // Sprawd? kolizje z ju? postawionymi budynkami i drogami
                GameObject tempCheck = Instantiate(building.prefab, finalPos, finalRot);
                Bounds tempBounds = CalculateBounds(tempCheck);
                DestroyImmediate(tempCheck);

                if (IsOverlapping(tempBounds, minBuildingDistance, placedBounds))
                    continue;

                bool collidesWithRoad = false;
                foreach (Bounds rb in roadBounds)
                {
                    if (tempBounds.Intersects(rb)) { collidesWithRoad = true; break; }
                }
                if (collidesWithRoad) continue;

                // Wszystko OK – postaw budynek
                GameObject obj = Spawn(building.prefab, finalPos, finalRot);
                if (obj != null)
                {
                    placedBounds.Add(CalculateBounds(obj));
                    buildingsPlaced++;
                    placed = true;
                    break;
                }
            }

            if (!placed)
                Debug.LogWarning($"[VillageGenerator] Nie uda?o si? postawi?: {building.type}. Rozwa? zwi?kszenie villageRadius.");
        }

        Debug.Log($"[VillageGenerator] Postawiono {buildingsPlaced}/{buildingQueue.Count} budynków.");
    }

    List<(Vector3, Vector3, bool)> GenerateAllCandidatePositions()
    {
        var candidates = new List<(Vector3, Vector3, bool)>();
        float fixedPlotWidth = 15f;
        float fixedPlotDepth = 15f;
        float distanceFromRoadEdge = 2.5f;

        foreach (var rd in roads)
        {
            float t = fixedPlotWidth * 0.5f;
            while (t < rd.length - fixedPlotWidth * 0.5f)
            {
                Vector3 rp = rd.start + rd.direction * t;
                Vector3 perp = Vector3.Cross(rd.direction, Vector3.up).normalized;

                for (int side = -1; side <= 1; side += 2)
                {
                    // Kilka rz?dów odleg?o?ci od drogi – wi?cej szans na wolne miejsce
                    for (float depthMult = 1f; depthMult <= 3f; depthMult += 0.5f)
                    {
                        Vector3 pos = SnapToTerrain(rp + perp * side *
                            (roadWidth * 0.5f + fixedPlotDepth * depthMult * 0.5f + distanceFromRoadEdge));

                        if (Vector3.Distance(pos, villageCenter) > villageRadius * 1.2f)
                            continue;

                        candidates.Add((pos, rd.direction, side == -1));
                    }
                }
                t += fixedPlotWidth * 0.5f;
            }
        }

        return candidates;
    }

    bool IsOverlapping(Bounds newBounds, float extraDistance, List<Bounds> boundsList)
    {
        foreach (Bounds b in boundsList)
        {
            Bounds expanded = b;
            expanded.Expand(extraDistance);
            if (expanded.Intersects(newBounds))
                return true;
        }
        return false;
    }

    Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] rends = obj.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds(obj.transform.position, Vector3.zero);
        bool started = false;

        foreach (Renderer r in rends)
        {
            if (!started) { bounds = r.bounds; started = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    Vector3 GetTerrainNormal(Vector3 pos)
    {
        float d = 0.5f;
        Vector3[] pts = new Vector3[5];
        int hits = 0;
        Vector3[] offsets = {
            Vector3.zero,
            new Vector3( d, 0,  0),
            new Vector3(-d, 0,  0),
            new Vector3( 0, 0,  d),
            new Vector3( 0, 0, -d)
        };

        foreach (var o in offsets)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(pos.x + o.x, 500, pos.z + o.z),
                                 Vector3.down, out hit, 1000f, terrainLayer))
                pts[hits++] = hit.point;
        }

        if (hits < 3) return Vector3.up;

        Vector3 v1 = pts[1] - pts[0];
        Vector3 v2 = pts[3] - pts[0];
        Vector3 normal = Vector3.Cross(v1, v2).normalized;
        if (normal.y < 0) normal = -normal;
        return normal;
    }

    GameObject GetPrefab(PlotType type)
    {
        switch (type)
        {
            case PlotType.House:
                return housePrefabs != null && housePrefabs.Length > 0
                    ? housePrefabs[Random.Range(0, housePrefabs.Length)] : null;
            case PlotType.Shop:
                return shopPrefabs != null && shopPrefabs.Length > 0
                    ? shopPrefabs[Random.Range(0, shopPrefabs.Length)] : null;
            case PlotType.Church: return churchPrefab;
            case PlotType.School: return schoolPrefab;
            default: return null;
        }
    }

    void PlaceDecorations()
    {
        placedDecorBounds.Clear();
        foreach (var rd in roads) PlaceDecorAlongRoad(rd);
    }

    void PlaceDecorAlongRoad(RoadData rd)
    {
        float t = decorSpacing;
        Vector3 perp = Vector3.Cross(rd.direction, Vector3.up).normalized;
        Quaternion rr = Quaternion.LookRotation(rd.direction);
        bool alt = false;

        float distanceFromRoadCenter = roadWidth * 0.5f + decorOffsetFromRoad + 5f;

        while (t < rd.length - decorSpacing)
        {
            Vector3 bp = rd.start + rd.direction * t;

            TryPlaceDecor(electricPolePrefab, bp + perp * distanceFromRoadCenter, Quaternion.identity, 1.5f);
            TryPlaceDecor(streetLightPrefab, bp - perp * distanceFromRoadCenter, rr, 1.5f);

            if (alt)
                TryPlaceDecor(benchPrefab, bp + perp * (distanceFromRoadCenter + 0.5f), Quaternion.LookRotation(-perp), 2f);
            else
                TryPlaceDecor(trashcanPrefab, bp - perp * (distanceFromRoadCenter + 0.5f), Quaternion.identity, 1.5f);

            alt = !alt;
            t += decorSpacing;
        }
    }

    void TryPlaceDecor(GameObject prefab, Vector3 position, Quaternion rotation, float boundsRadius)
    {
        if (prefab == null) return;

        position = SnapToTerrain(position);

        if (IsPositionOnAnyRoad(position)) return;

        Bounds decorBounds = new Bounds(position, new Vector3(boundsRadius, 1f, boundsRadius));

        if (IsOverlapping(decorBounds, minDecorDistance, placedDecorBounds)) return;
        if (IsOverlapping(decorBounds, 1f, placedBounds)) return;

        var obj = Spawn(prefab, position, rotation);
        if (obj != null)
        {
            obj.transform.localScale = Vector3.one * decorScale;
            placedDecorBounds.Add(decorBounds);
        }
    }

    bool IsPositionOnAnyRoad(Vector3 pos)
    {
        Vector3 pos2D = new Vector3(pos.x, 0, pos.z);
        foreach (Bounds roadBound in roadBounds)
        {
            Vector3 roadCenter2D = new Vector3(roadBound.center.x, 0, roadBound.center.z);
            if (Vector3.Distance(pos2D, roadCenter2D) < (roadWidth * 0.5f + 1f))
                return true;
        }
        return false;
    }

    Vector3 MapToWorld(int mx, int my, int mapSize, GameObject terrainObj)
    {
        float half = (mapSize - 1) / 2f;
        Vector3 sc = terrainObj.transform.localScale;
        Vector3 p = terrainObj.transform.position;
        float wx = p.x + (mx - half) * sc.x;
        float wz = p.z + (half - my) * sc.z;
        RaycastHit hit;
        return Physics.Raycast(new Vector3(wx, 500, wz), Vector3.down, out hit, 1000f, terrainLayer)
            ? hit.point : new Vector3(wx, p.y, wz);
    }

    Vector3 SnapToTerrain(Vector3 pos)
    {
        RaycastHit hit;
        return Physics.Raycast(new Vector3(pos.x, 500, pos.z), Vector3.down, out hit, 1000f, terrainLayer)
            ? hit.point : pos;
    }

    GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot)
    {
        if (prefab == null) return null;
        var obj = Instantiate(prefab, pos, rot);
        if (villageParent != null) obj.transform.parent = villageParent;
        spawnedObjects.Add(obj);
        return obj;
    }

    bool LineIntersect(RoadData a, RoadData b, out Vector3 result)
    {
        result = Vector3.zero;
        float a1 = a.direction.z, b1 = -a.direction.x, c1 = a1 * a.start.x + b1 * a.start.z;
        float a2 = b.direction.z, b2 = -b.direction.x, c2 = a2 * b.start.x + b2 * b.start.z;
        float det = a1 * b2 - a2 * b1;
        if (Mathf.Abs(det) < 0.001f) return false;
        float x = (c1 * b2 - c2 * b1) / det, z = (a1 * c2 - a2 * c1) / det;
        result = new Vector3(x, a.start.y, z);
        float tA = Vector3.Dot(result - a.start, a.direction);
        float tB = Vector3.Dot(result - b.start, b.direction);
        return tA >= 0 && tA <= a.length && tB >= 0 && tB <= b.length;
    }
    // Dodaj na koncu klasy VillageGenerator, przed ostatnia klamra
    public List<Vector3> GetVillageCentersPreview(float[,] noiseMap, GameObject terrainObj, int mapSeed)
    {
        var centers = new List<Vector3>();

        Random.InitState(mapSeed);
        Vector3 center1;
        if (FindFlatArea(noiseMap, terrainObj, out center1, Vector2.zero))
            centers.Add(center1);

        if (generateTwoVillages && centers.Count > 0)
        {
            Random.InitState(mapSeed + 1);
            Vector3 center2;
            if (FindFlatAreaFarFrom(noiseMap, terrainObj, centers[0], villageSeparation, out center2))
                centers.Add(center2);
        }

        return centers;
    }
}