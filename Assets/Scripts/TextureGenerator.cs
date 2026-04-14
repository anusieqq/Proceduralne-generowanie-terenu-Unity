using UnityEngine;

public static class TextureGenerator
{
    public static Texture2D TextureFromColourMap(Color[] colourMap, int width, int height)
    {
        // ?? mipmaps = TRUE daje smoothing w shaderze
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, true);

        texture.SetPixels(colourMap);

        // ?? KLUCZOWE FIXY
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.anisoLevel = 4;

        texture.Apply(updateMipmaps: true, makeNoLongerReadable: false);

        return texture;
    }

    public static Texture2D TextureFromHeightMap(float[,] heightMap)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        Color[] colourMap = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = heightMap[x, y];

                // ?? smoothstep (usuwa “pixel look” w biomes)
                value = value * value * (3f - 2f * value);

                colourMap[y * width + x] = Color.Lerp(Color.black, Color.white, value);
            }
        }

        return TextureFromColourMap(colourMap, width, height);
    }
}