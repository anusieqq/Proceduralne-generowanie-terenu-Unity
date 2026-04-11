using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SpawnableObject
{
    public string name;           // Nazwa obiektu
    public GameObject prefab;     // Prefab do spawnienia
    public TerrainRegion region;  // W jakim regionie ma si? generowa?

    [Header("Parametry generowania")]
    public float minHeight;       // Minimalna wysoko?? do generowania
    public float maxHeight;       // Maksymalna wysoko?? do generowania
    public float spawnChance;     // Szansa spawnu (0-1)
    public float minScale = 1f;   // Minimalna skala
    public float maxScale = 1f;   // Maksymalna skala

    [Header("Opcjonalne ograniczenia")]
    public bool randomRotation = true;     // Czy losowa rotacja
    public bool alignToTerrain = true;     // Czy dopasowa? do terenu
    public float minDistanceToOther = 2f;  // Minimalna odleg?o?? od innych obiektów
}

[System.Serializable]
public class TerrainRegion
{
    public string name;
    public float heightThreshold;  // Próg wysoko?ci dla tego regionu
    public Color colour;

    // Lista obiektów typowych dla tego regionu
    public List<SpawnableObject> regionObjects;

}