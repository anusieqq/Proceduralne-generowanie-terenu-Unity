using UnityEngine;
using System.Collections.Generic;
public class EndlessTerrain : MonoBehaviour
{
    public const float maxViewDst = 450;
    public Transform Viewer;

    public static Vector2 viewerPosition;
    int chunkSize;
    int chunkVisibleViewDst;
    Dictionary<Vector2,TerrainChunk> terrainChunkDictionary = new Dictionary<Vector2,TerrainChunk>();
    List<TerrainChunk> terrainChunksVisibleLastUpdate = new List<TerrainChunk>();
     void Start()
    {
        chunkSize = MapGenerator.mapChunkSize - 1;
        chunkVisibleViewDst= Mathf.RoundToInt(maxViewDst/chunkSize);
    }
     void Update()
    {
        viewerPosition = new Vector2(Viewer.position.x, Viewer.position.z);
        UpdateVisibleChunks();
    }
    void UpdateVisibleChunks()
    {
        for(int i = 0;i<terrainChunksVisibleLastUpdate.Count;i++)
        {
            terrainChunksVisibleLastUpdate[i].SetVisible(false);
        }
        terrainChunksVisibleLastUpdate.Clear();

        int currentChunkCordX= Mathf.RoundToInt(viewerPosition.x/chunkSize);
        int currentChunkCordY = Mathf.RoundToInt(viewerPosition.y / chunkSize);

        for(int yOffset= -chunkVisibleViewDst;yOffset <= chunkVisibleViewDst;yOffset++) {
        for(int xOffset = -chunkVisibleViewDst; xOffset <= chunkVisibleViewDst; xOffset++)
            {
                Vector2 viewedChunkCoord = new Vector2(currentChunkCordX + xOffset, currentChunkCordY + yOffset);
                if(terrainChunkDictionary.ContainsKey(viewedChunkCoord))
                {
                    terrainChunkDictionary[viewedChunkCoord].UpdateTerrainChunk();
                    if (terrainChunkDictionary[viewedChunkCoord].isVisible())
                    {
                        terrainChunksVisibleLastUpdate.Add(terrainChunkDictionary[viewedChunkCoord]);
                    }
                } else
                {
                    terrainChunkDictionary.Add(viewedChunkCoord, new TerrainChunk(viewedChunkCoord,chunkSize,transform));
                }
            }
        }
    }
    public class TerrainChunk
    {
        GameObject meshObject;
        Vector2 position;
        Bounds bounds;
        public TerrainChunk(Vector2 coord, int size,Transform parent)
        {
            position=coord*size;
            bounds = new Bounds(position,Vector2.one*size);
            Vector3 positionV3 = new Vector3(position.x, 0, position.y);
            meshObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
            meshObject.transform.position = positionV3;
            meshObject.transform.localScale = Vector3.one * size / 10f;
            meshObject.transform.parent = parent;
            SetVisible(false);
        }
        public void UpdateTerrainChunk()
        {
            float viewerDstDromNearestEdge = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
            bool visible = viewerDstDromNearestEdge <= maxViewDst;
            SetVisible(visible);
        }
        public void SetVisible(bool visible)
        {
            meshObject.SetActive(visible);
        }

        public bool isVisible()
        {
            return meshObject.activeSelf;
        }
    }
}
