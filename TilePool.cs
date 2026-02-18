using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public class TilePool
{
    private Queue<ClickableTile> pool;
    private ClickableTile        tilePrefab;
    private int                  prevTileMilestone = 0;
    
    //faster than toggling the gameobject on and off
    private readonly Vector3 hiddenPosition = new Vector3(-3000, -3000, 0);
    private Transform container;
    
    public int TileCount { get; private set; }

    public TilePool(Transform container, ClickableTile tilePrefab, int initialSize)
    {
        this.container = container;
        this.tilePrefab = tilePrefab;
        pool = new Queue<ClickableTile>();

        for (int i = 0; i < initialSize; i++)
        {
            CreateTile();
        }
    }

    private void CreateTile()
    {
        ClickableTile tile = Object.Instantiate(tilePrefab, container);
        tile.transform.position = hiddenPosition;

        //tile.gameObject.SetActive(false);
        pool.Enqueue(tile);
        TileCount++;
        
        if (TileCount > prevTileMilestone + 100)
        {
            //Debug.Log($"Tile pool size: {tileCount}");
            prevTileMilestone = TileCount;
        }
    }
    
    public ClickableTile GetTile()
    {
        if (pool.Count == 0)
        {
            CreateTile();
        }

        ClickableTile pooledTile = pool.Dequeue();
        //pooledTile.gameObject.SetActive(true);
        return pooledTile;
    }
    
    public void ReturnTile(ClickableTile tile)
    {
        tile.transform.position = hiddenPosition;
        pool.Enqueue(tile);
    }

    public void HideAll()
    {
        foreach (var tile in pool)
        {
            tile.transform.position = hiddenPosition;
        }
    }
}