using UnityEngine;
using System.Collections;
using UnityEditor;


[CustomEditor(typeof(MapGenerator))]
public class MapGeneratorEditor : Editor
{
    // Rysuje niestandardowy panel w Inspectorze
    public override void OnInspectorGUI()
    {
        MapGenerator mapGen = (MapGenerator)target;


        if (DrawDefaultInspector())
        {
            if (mapGen.autoUpdate) // jesli autoUpdate wlaczone
            {
                mapGen.GenerateMap(); // generujemy mape automatycznie po zmianie wartosci
            }
        }

        // Dodaje przycisk "Generate" w Inspectorze
        if (GUILayout.Button("Generate"))
        {
            mapGen.GenerateMap();
        }
    }
}