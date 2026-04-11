using UnityEngine;
using System.Collections;

public class MapDisplay : MonoBehaviour
{
    public Renderer textureRender; // Renderer do wyswietlania tekstury 2D
    public MeshFilter meshFilter;  // MeshFilter do siatki 3D terenu
    public MeshRenderer meshRenderer; // MeshRenderer do renderowania mesha

    public void DrawTexture(Texture2D texture)
    {
        textureRender.sharedMaterial.mainTexture = texture; // Podlaczamy teksture do materialu
        textureRender.transform.localScale = new Vector3(texture.width, 1, texture.height); 
    }

    public void DrawMesh(MeshData meshData, Texture2D texture)
    {
        meshFilter.sharedMesh = meshData.CreateMesh();
        meshRenderer.sharedMaterial.mainTexture = texture;

        
        MeshCollider collider = meshFilter.GetComponent<MeshCollider>();
        if (collider == null)
            collider = meshFilter.gameObject.AddComponent<MeshCollider>();
        collider.sharedMesh = meshFilter.sharedMesh;

        Debug.Log($"Mesh utworzony z {meshFilter.sharedMesh.vertexCount} wierzcho?kami");
    }
}