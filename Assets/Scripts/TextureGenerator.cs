using UnityEngine;
using System.Collections;

public static class TextureGenerator
{
    // Tworzy teksture 2D z tablicy kolorów
    public static Texture2D TextureFromColourMap(Color[] colourMap, int width, int height)
    {
        Texture2D texture = new Texture2D(width, height);
        texture.filterMode = FilterMode.Point; // brak wygladzania
        texture.wrapMode = TextureWrapMode.Clamp; // krawedzie nie powtarzaja sie
        texture.SetPixels(colourMap);
        texture.Apply();
        return texture;
    }

    // Tworzy teksture 2D z mapy wysoko?ci (czarno-biala)
    public static Texture2D TextureFromHeightMap(float[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        Color[] colourMap = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // mapowanie wartosci wysokosci na gradient od czerni do bieli
                colourMap[y * width + x] = Color.Lerp(Color.black, Color.white, heightMap[x, y]);
            }
        }

        return TextureFromColourMap(colourMap, width, height);
    }
}