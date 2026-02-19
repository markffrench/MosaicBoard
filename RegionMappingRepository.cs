using System;
using System.Linq;
using Board;
using Helpers;
using UnityEngine;

public class RegionMappingRepository : MonoBehaviour
{
    [SerializeField] private TextAsset regionMappingJson;

    protected RegionMapping[] regionMappings;

    protected virtual void Awake()
    {
        if (regionMappingJson == null)
        {
            Debug.LogError("RegionMappingRepository: regionMappingJson is not assigned.");
            return;
        }

        regionMappings = LoadRegionMapping(regionMappingJson.text);
        Debug.Log($"Loaded {regionMappings.Length} region mappings");
    }

    public static RegionMapping[] LoadRegionMapping(string json)
    {
        return JsonArrayHelper.FromJson<RegionMapping>(json).OrderBy(r => r.RegionIndex).ToArray();
    }

    public virtual RegionMapping GetRegionMapping(int regionIndex)
    {
        if (regionMappings == null || regionIndex < 0 || regionIndex >= regionMappings.Length)
        {
            Debug.LogError($"Invalid region index {regionIndex}");
            return null;
        }

        return regionMappings[regionIndex];
    }

    public virtual RegionType[,] CreateRegionTypeMap(int[,] regionMap)
    {
        int width = regionMap.GetLength(0);
        int height = regionMap.GetLength(1);
        RegionType[,] regionTypeMap = new RegionType[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int regionIndex = regionMap[x, y];
                RegionMapping mapping = GetRegionMapping(regionIndex);
                regionTypeMap[x, y] = mapping != null ? mapping.Type : RegionType.Empty;
            }
        }

        return regionTypeMap;
    }

    public virtual bool[] CreateCrypticRegionMap()
    {
        if (regionMappings == null)
            return Array.Empty<bool>();

        return regionMappings.Select(m => m.IsCrypticRegion).ToArray();
    }
}
