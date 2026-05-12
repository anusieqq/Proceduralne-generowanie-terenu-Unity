using UnityEngine;
using System.Collections.Generic;

public class ObjectSpawner : MonoBehaviour
{
    [Header("Ustawienia generowania")]
    public List<TerrainRegion> terrainRegions;
    public LayerMask groundLayer;
    public Transform objectsParent;  // Opcjonalny parent dla spawnionych obiektów

    [Header("Optymalizacja")]
    public int maxObjectsToSpawn = 5000;     // Maksymalna liczba obiektów
    public float spawnRadius = 100f;         
    public bool useFloatingOrigin = true;   

    private List<Vector3> spawnedPositions = new List<Vector3>();
    private Dictionary<Vector3, GameObject> spawnedObjects = new Dictionary<Vector3, GameObject>();

    
    public void SpawnObjectsOnTerrain(float[,] heightMap, int mapSize, Vector3 terrainPosition)
    {
       
        ClearSpawnedObjects();

        if (terrainRegions == null || terrainRegions.Count == 0)
            return;

       
        Random.InitState(GetHashCode());

        int spawnedCount = 0;
        int stepSize = Mathf.Max(1, mapSize / 50); 

        for (int x = 0; x < mapSize; x += stepSize)
        {
            for (int z = 0; z < mapSize; z += stepSize)
            {
                if (spawnedCount >= maxObjectsToSpawn)
                {
                    Debug.Log($"Osi?gni?to maksymaln? liczb? obiektów: {maxObjectsToSpawn}");
                    return;
                }

                float currentHeight = heightMap[x, z];

               
                TerrainRegion currentRegion = GetRegionAtHeight(currentHeight);
                if (currentRegion == null || currentRegion.regionObjects == null)
                    continue;

               
                foreach (SpawnableObject spawnable in currentRegion.regionObjects)
                {
                    if (ShouldSpawnObject(currentHeight, spawnable))
                    {
                        Vector3 spawnPosition = CalculateSpawnPosition(x, z, currentHeight, terrainPosition);

                       
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

        Debug.Log($"Wygenerowano {spawnedCount} obiektów na terenie");
    }

    private TerrainRegion GetRegionAtHeight(float height)
    {
        foreach (TerrainRegion region in terrainRegions)
        {
            if (height <= region.heightThreshold)
                return region;
        }
        return null;
    }

    private bool ShouldSpawnObject(float height, SpawnableObject obj)
    {
        
        if (height < obj.minHeight || height > obj.maxHeight)
            return false;

       
        if (Random.value > obj.spawnChance)
            return false;

        return true;
    }

    private Vector3 CalculateSpawnPosition(int x, int z, float height, Vector3 terrainPosition)
    {
        
        float offsetX = Random.Range(-0.5f, 0.5f);
        float offsetZ = Random.Range(-0.5f, 0.5f);

        Vector3 localPos = new Vector3(x + offsetX, height, z + offsetZ);
        Vector3 worldPos = terrainPosition + localPos;

        
        if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f, groundLayer))
        {
            worldPos.y = hit.point.y;
        }

        return worldPos;
    }

    private bool IsValidSpawnPosition(Vector3 position, float minDistance)
    {
        
        foreach (Vector3 existingPos in spawnedPositions)
        {
            if (Vector3.Distance(position, existingPos) < minDistance)
                return false;
        }

      
        if (Physics.CheckSphere(position, 0.5f, groundLayer))
            return false;

        return true;
    }

    private void SpawnObject(SpawnableObject obj, Vector3 position)
    {
        if (obj.prefab == null)
            return;

        GameObject newObj = Instantiate(obj.prefab, position, Quaternion.identity);

        
        float scale = Random.Range(obj.minScale, obj.maxScale);
        newObj.transform.localScale = Vector3.one * scale;

        
        if (obj.randomRotation)
        {
            newObj.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
        }

        
        if (obj.alignToTerrain && Physics.Raycast(position + Vector3.up, Vector3.down, out RaycastHit hit, 2f))
        {
            newObj.transform.position = hit.point;
        }

        if (objectsParent != null)
        {
            newObj.transform.parent = objectsParent;
        }

        spawnedObjects[position] = newObj;
    }

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

   
    public void ClearAllObjects()
    {
        ClearSpawnedObjects();
    }
}