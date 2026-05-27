using UnityEngine;
using System.Collections.Generic;

// Odpowiada za proceduralne rozmieszczanie obiektow na terenie (np. drzewa, kamienie, propsy)
public class ObjectSpawner : MonoBehaviour
{
    [Header("Ustawienia generowania")]

    // Lista regionow terenu, gdzie kazdy region definiuje inne obiekty
    public List<TerrainRegion> terrainRegions;

    // Warstwa gruntu do raycastow i sprawdzania kolizji
    public LayerMask groundLayer;

    // Opcjonalny parent dla wszystkich spawnionych obiektow (organizacja hierarchii)
    public Transform objectsParent;

    [Header("Optymalizacja")]

    // Maksymalna liczba obiektow, zeby nie przegrzac sceny
    public int maxObjectsToSpawn = 5000;

    // Promien ograniczajacy obszar generowania (nie uzywany w tej wersji logiki)
    public float spawnRadius = 100f;

    // Flaga do systemu floating origin (przy duzych mapach, ogranicza blad floatow)
    public bool useFloatingOrigin = true;

    // Lista pozycji juz wygenerowanych obiektow (do sprawdzania odleglosci)
    private List<Vector3> spawnedPositions = new List<Vector3>();

    // Slownik przechowujacy obiekty wraz z ich pozycjami
    private Dictionary<Vector3, GameObject> spawnedObjects = new Dictionary<Vector3, GameObject>();

    // Glowna funkcja generujaca obiekty na podstawie heightmapy
    public void SpawnObjectsOnTerrain(float[,] heightMap, int mapSize, Vector3 terrainPosition)
    {
        // Usun poprzednie obiekty przed nowa generacja
        ClearSpawnedObjects();

        // Jesli brak regionow, nie ma co generowac
        if (terrainRegions == null || terrainRegions.Count == 0)
            return;

        // Ustawienie seed-a losowosci (stabilne generowanie)
        Random.InitState(GetHashCode());

        int spawnedCount = 0;

        // Krok siatki - im wiekszy, tym mniej sprawdzanych punktow (wydajnosc)
        int stepSize = Mathf.Max(1, mapSize / 50);

        // Iteracja po mapie wysokosci
        for (int x = 0; x < mapSize; x += stepSize)
        {
            for (int z = 0; z < mapSize; z += stepSize)
            {
                // Ochrona przed nadmiernym spawnem
                if (spawnedCount >= maxObjectsToSpawn)
                {
                    Debug.Log($"Osiagnieto maksymalna liczbe obiektow: {maxObjectsToSpawn}");
                    return;
                }

                float currentHeight = heightMap[x, z];

                // Wybor regionu terenu na podstawie wysokosci
                TerrainRegion currentRegion = GetRegionAtHeight(currentHeight);
                if (currentRegion == null || currentRegion.regionObjects == null)
                    continue;

                // Sprawdzenie kazdego obiektu przypisanego do regionu
                foreach (SpawnableObject spawnable in currentRegion.regionObjects)
                {
                    // Czy obiekt powinien sie wygenerowac w tym miejscu
                    if (ShouldSpawnObject(currentHeight, spawnable))
                    {
                        Vector3 spawnPosition =
                            CalculateSpawnPosition(x, z, currentHeight, terrainPosition);

                        // Sprawdzenie minimalnej odleglosci od innych obiektow
                        if (IsValidSpawnPosition(spawnPosition, spawnable.minDistanceToOther))
                        {
                            SpawnObject(spawnable, spawnPosition);

                            spawnedPositions.Add(spawnPosition);
                            spawnedCount++;
                        }
                    }
                }
            }
        }

        Debug.Log($"Wygenerowano {spawnedCount} obiektow na terenie");
    }

    // Zwraca region terenu na podstawie wysokosci (pierwszy pasujacy prog)
    private TerrainRegion GetRegionAtHeight(float height)
    {
        foreach (TerrainRegion region in terrainRegions)
        {
            if (height <= region.heightThreshold)
                return region;
        }
        return null;
    }

    // Decyduje czy dany obiekt moze zostac wygenerowany
    private bool ShouldSpawnObject(float height, SpawnableObject obj)
    {
        // Sprawdzenie zakresu wysokosci dla obiektu
        if (height < obj.minHeight || height > obj.maxHeight)
            return false;

        // Losowa szansa na spawn
        if (Random.value > obj.spawnChance)
            return false;

        return true;
    }

    // Wylicza pozycje w swiecie z lekkim random offsetem + dopasowanie do terenu
    private Vector3 CalculateSpawnPosition(int x, int z, float height, Vector3 terrainPosition)
    {
        float offsetX = Random.Range(-0.5f, 0.5f);
        float offsetZ = Random.Range(-0.5f, 0.5f);

        Vector3 localPos = new Vector3(x + offsetX, height, z + offsetZ);
        Vector3 worldPos = terrainPosition + localPos;

        // Dopasowanie do faktycznej powierzchni terenu (raycast w dol)
        if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down,
            out RaycastHit hit, 20f, groundLayer))
        {
            worldPos.y = hit.point.y;
        }

        return worldPos;
    }

    // Sprawdza czy pozycja jest bezpieczna do spawnienia
    private bool IsValidSpawnPosition(Vector3 position, float minDistance)
    {
        // Sprawdzenie odleglosci od innych obiektow
        foreach (Vector3 existingPos in spawnedPositions)
        {
            if (Vector3.Distance(position, existingPos) < minDistance)
                return false;
        }

        // Sprawdzenie kolizji z terenem / przeszkodami
        if (Physics.CheckSphere(position, 0.5f, groundLayer))
            return false;

        return true;
    }

    // Tworzy obiekt w scenie i ustawia jego parametry
    private void SpawnObject(SpawnableObject obj, Vector3 position)
    {
        if (obj.prefab == null)
            return;

        GameObject newObj = Instantiate(obj.prefab, position, Quaternion.identity);

        // Losowa skala obiektu
        float scale = Random.Range(obj.minScale, obj.maxScale);
        newObj.transform.localScale = Vector3.one * scale;

        // Losowa rotacja wokol osi Y
        if (obj.randomRotation)
        {
            newObj.transform.rotation =
                Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }

        // Dopasowanie do nachylenia terenu (opcjonalne)
        if (obj.alignToTerrain &&
            Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 2f))
        {
            newObj.transform.position = hit.point;
        }

        // Ustawienie parenta w hierarchii sceny
        if (objectsParent != null)
        {
            newObj.transform.parent = objectsParent;
        }

        // Zapis referencji do obiektu
        spawnedObjects[position] = newObj;
    }

    // Usuwa wszystkie wygenerowane obiekty
    private void ClearSpawnedObjects()
    {
        foreach (var obj in spawnedObjects.Values)
        {
            if (obj != null)
                DestroyImmediate(obj);
        }

        spawnedObjects.Clear();
        spawnedPositions.Clear();
    }

    // Publiczna funkcja do czyszczenia sceny
    public void ClearAllObjects()
    {
        ClearSpawnedObjects();
    }
}