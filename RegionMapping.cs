using System;
using UnityEngine;

namespace Board
{
    public enum RegionType
    {
        Empty,
        Discovery,
        Door,
        Walkable
    }

    [Serializable]
    public class RegionMapping
    {
        public int RegionIndex = 0;
        public int ProverbIndex = 0;
        public bool IsLocalised = false;
        public Vector2Int PaintingCoordinate = Vector2Int.zero;
        public int PaintingSize = 100;
        public RegionType Type = RegionType.Discovery;

        [Header("Region Gating (Discovery regions only)")]
        public bool IsGated = false;
        public int GateRegion;
        public int GateRegion2;

        [Header("Region linking (Linked region will automatically solve once this one is solved)")]
        public bool IsLinked = false;
        public int LinkedRegion;

        [Header("Opponent Region (Boss fight only)")]
        public bool IsOpponentRegion = false;

        [Header("Cryptic Region (Uses cryptic number sprites)")]
        public bool IsCrypticRegion = false;
    }
}
