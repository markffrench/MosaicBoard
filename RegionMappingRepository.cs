using System;
using System.Collections.Generic;
using System.Linq;
using Board;
using Helpers;
using UnityEngine;

public class RegionMappingRepository : MonoBehaviour
{
    [Serializable]
    private class SceneEntry
    {
        public string sceneId;
        public TextAsset json;
    }

    [SerializeField] private SceneEntry[] scenes;

    private Dictionary<string, RegionMapping[]> allMappings;

    private void Awake()
    {
        allMappings = new Dictionary<string, RegionMapping[]>();

        foreach (SceneEntry entry in scenes)
        {
            if (entry.json == null)
            {
                Debug.LogError($"RegionMappingRepository: json not assigned for sceneId '{entry.sceneId}'.");
                continue;
            }

            allMappings[entry.sceneId] = LoadRegionMapping(entry.json.text);
            Debug.Log($"RegionMappingRepository: loaded {allMappings[entry.sceneId].Length} mappings for '{entry.sceneId}'");
        }
    }

    public static RegionMapping[] LoadRegionMapping(string json)
    {
        return JsonArrayHelper.FromJson<RegionMapping>(json).OrderBy(r => r.RegionIndex).ToArray();
    }

    public int GetRegionCount(string sceneId)
    {
        RegionMapping[] mappings = GetMappings(sceneId);
        return mappings?.Length ?? 0;
    }

    public RegionMapping GetRegionMapping(string sceneId, int regionIndex)
    {
        RegionMapping[] mappings = GetMappings(sceneId);

        if (mappings == null || regionIndex < 0 || regionIndex >= mappings.Length)
        {
            Debug.LogError($"RegionMappingRepository: invalid region index {regionIndex} for scene '{sceneId}'");
            return null;
        }

        return mappings[regionIndex];
    }

    public RegionType[,] CreateRegionTypeMap(string sceneId, int[,] regionMap)
    {
        int width = regionMap.GetLength(0);
        int height = regionMap.GetLength(1);
        RegionType[,] regionTypeMap = new RegionType[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int regionIndex = regionMap[x, y];
                RegionMapping mapping = GetRegionMapping(sceneId, regionIndex);
                regionTypeMap[x, y] = mapping != null ? mapping.Type : RegionType.Empty;
            }
        }

        return regionTypeMap;
    }

    public bool[] CreateCrypticRegionMap(string sceneId)
    {
        RegionMapping[] mappings = GetMappings(sceneId);

        if (mappings == null)
            return Array.Empty<bool>();

        return mappings.Select(m => m.IsCrypticRegion).ToArray();
    }

    private RegionMapping[] GetMappings(string sceneId)
    {
        if (allMappings == null || !allMappings.TryGetValue(sceneId, out RegionMapping[] mappings))
        {
            Debug.LogError($"RegionMappingRepository: no mappings registered for scene '{sceneId}'");
            return null;
        }

        return mappings;
    }
}
