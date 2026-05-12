using System.Collections;
using System.Collections.Generic;
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

    public List<Bounds> GetRoadBounds()
    {
        return roadBounds;
    }

    public void Generate(float[,] noiseMap, GameObject terrainObj, int mapSeed)
    {
        ClearVillage();

        // U?yj seeda z MapGenerator
        Random.InitState(mapSeed);

        if (!FindFlatArea(noiseMap, terrainObj, out villageCenter))
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

    bool FindFlatArea(float[,] noiseMap, GameObject terrainObj, out Vector3 worldCenter)
    {
        int size = noiseMap.GetLength(0);
        int step = 5, win = 20;
        float bestVar = float.MaxValue;
        int bx = size / 2, by = size / 2;

        for (int y = win; y < size - win; y += step)
            for (int x = win; x < size - win; x += step)
            {
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
                float v = sumSq / cnt - (sum / cnt) * (sum / cnt);
                if (v < bestVar) { bestVar = v; bx = x; by = y; }
            }

        if (bestVar > maxFlatVariance)
            Debug.LogWarning($"[VillageGenerator] Wariancja {bestVar:F4} > próg, u?ywam najlepszego miejsca.");

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

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
        }

        Queue<PlotType> pool = new Queue<PlotType>();
        for (int i = 0; i < churchCount; i++) pool.Enqueue(PlotType.Church);
        for (int i = 0; i < schoolCount; i++) pool.Enqueue(PlotType.School);
        for (int i = 0; i < shopCount; i++) pool.Enqueue(PlotType.Shop);
        for (int i = 0; i < houseCount; i++) pool.Enqueue(PlotType.House);

        int assigned = 0;
        foreach (var c in candidates)
        {
            if (pool.Count == 0) break;
            c.type = pool.Dequeue();
            assigned++;
        }

        if (pool.Count > 0)
            Debug.LogWarning($"[VillageGenerator] Zabrak?o miejsca na {pool.Count} budynków. Zwi?ksz Village Radius.");

        plots.AddRange(candidates);
        Debug.Log($"[VillageGenerator] Dzia?ek: {candidates.Count}, przydzielono budynków: {assigned}");
    }

    void PlaceBuildings()
    {
        placedBounds.Clear();
        int buildingsPlaced = 0;

        // Zbierz wszystkie budynki do postawienia jako list? (typ + prefab)
        List<(PlotType type, GameObject prefab)> buildingQueue = new List<(PlotType, GameObject)>();
        for (int i = 0; i < churchCount; i++) buildingQueue.Add((PlotType.Church, churchPrefab));
        for (int i = 0; i < schoolCount; i++) buildingQueue.Add((PlotType.School, schoolPrefab));
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

        // Zbierz wszystkie mo?liwe pozycje (generuj z wi?kszym zasi?giem)
        List<(Vector3 pos, Vector3 roadDir, bool leftSide)> allCandidates =
            GenerateAllCandidatePositions();

        // Potasuj kandydatów
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
                // Oblicz rotacj?
                Vector3 faceDir = candidate.leftSide
                    ? -Vector3.Cross(candidate.roadDir, Vector3.up)
                    : Vector3.Cross(candidate.roadDir, Vector3.up);
                faceDir.y = 0;
                Quaternion baseRot = faceDir != Vector3.zero
                    ? Quaternion.LookRotation(faceDir) : Quaternion.identity;
                Vector3 terrainNormal = GetTerrainNormal(candidate.pos);
                Quaternion finalRot = Quaternion.FromToRotation(Vector3.up, terrainNormal) * baseRot;

                // Sprawd? bounds w miejscu docelowym bez tworzenia obiektu
                GameObject temp = Instantiate(building.prefab, new Vector3(0, -9999, 0), finalRot);
                Bounds tempBounds = CalculateBounds(temp);
                float bottomOffset = temp.transform.position.y - tempBounds.min.y;
                DestroyImmediate(temp);

                Vector3 spawnPos = candidate.pos;
                spawnPos.y += bottomOffset;
                tempBounds.center += spawnPos - new Vector3(0, -9999, 0);

                if (IsOverlapping(tempBounds, minBuildingDistance, placedBounds))
                    continue;

                bool collidesWithRoad = false;
                foreach (Bounds rb in roadBounds)
                {
                    if (tempBounds.Intersects(rb)) { collidesWithRoad = true; break; }
                }
                if (collidesWithRoad) continue;

                // Mo?na postawi?!
                GameObject obj = Spawn(building.prefab, spawnPos, finalRot);
                if (obj != null)
                {
                    placedBounds.Add(CalculateBounds(obj));
                    buildingsPlaced++;
                    placed = true;
                    break; // przejd? do nast?pnego budynku
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

        // Generuj z pe?nym villageRadius (bez ograniczenia do 85%)
        foreach (var rd in roads)
        {
            float t = fixedPlotWidth * 0.5f;
            while (t < rd.length - fixedPlotWidth * 0.5f)
            {
                Vector3 rp = rd.start + rd.direction * t;
                Vector3 perp = Vector3.Cross(rd.direction, Vector3.up).normalized;

                for (int side = -1; side <= 1; side += 2)
                {
                    // Wypróbuj kilka odleg?o?ci od drogi (aby znale?? miejsce)
                    for (float depthMult = 1f; depthMult <= 3f; depthMult += 0.5f)
                    {
                        Vector3 pos = SnapToTerrain(rp + perp * side *
                            (roadWidth * 0.5f + fixedPlotDepth * depthMult * 0.5f + distanceFromRoadEdge));

                        if (Vector3.Distance(pos, villageCenter) > villageRadius * 1.2f)
                            continue;

                        candidates.Add((pos, rd.direction, side == -1));
                    }
                }
                t += fixedPlotWidth * 0.5f; // g?stszy krok = wi?cej opcji
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
            if (!started)
            {
                bounds = r.bounds;
                started = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
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
            {
                pts[hits++] = hit.point;
            }
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

        if (IsPositionOnAnyRoad(position))
            return;

        Bounds decorBounds = new Bounds(position, new Vector3(boundsRadius, 1f, boundsRadius));

        if (IsOverlapping(decorBounds, minDecorDistance, placedDecorBounds))
            return;

        if (IsOverlapping(decorBounds, 1f, placedBounds))
            return;

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
            float distToRoadCenter = Vector3.Distance(pos2D, roadCenter2D);

            if (distToRoadCenter < (roadWidth * 0.5f + 1f))
            {
                return true;
            }
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
}