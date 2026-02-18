using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MosaicPuzzle;
using Helpers;
using UnityEditor;
using UnityEngine;

namespace Board.Editor
{
    public class RegionMappingEditor : EditorWindow
    {
        private static int currentRegion = 0;
        private static int NumRegions = 10;
        private static Texture2D regionsTexture;
        private static Texture2D illustrationTexture;
        private static Texture2D displayTexture;
        private static Texture2D highlightTexture;
        private static Texture2D solvedTexture;

        private static List<List<Vector2Int>> regionCoordinates;
        private int[,] regionMap;

        private Sprite[] regionSprites;
        private static RegionMapping[] regionMappings;

        // Configurable paths
        private string regionsTexturePath   = "Assets/Board/regions.png";
        private string illustrationTexturePath = "Assets/Board/illustration.png";
        private string solvedTexturePath    = "Assets/Board/solved.png";
        private string mappingJsonPath      = "Assets/Board/region_mapping.json";
        private string clueResourcesFolder  = "clues/"; // path inside Resources/

        private static int validationDifficulty = 0;

        [MenuItem("Tools/Region Mapping")]
        public static void ShowWindow()
        {
            GetWindow<RegionMappingEditor>("Region Mapping");
        }

        private void OnGUI()
        {
            GUILayout.Label("Paths", EditorStyles.boldLabel);
            regionsTexturePath      = EditorGUILayout.TextField("Regions Texture",    regionsTexturePath);
            illustrationTexturePath = EditorGUILayout.TextField("Illustration Texture", illustrationTexturePath);
            solvedTexturePath       = EditorGUILayout.TextField("Solved Texture",     solvedTexturePath);
            mappingJsonPath         = EditorGUILayout.TextField("Mapping JSON",       mappingJsonPath);
            clueResourcesFolder     = EditorGUILayout.TextField("Clue Folder (Resources)", clueResourcesFolder);

            GUILayout.Space(5);

            if (regionsTexture == null || illustrationTexture == null)
            {
                if (GUILayout.Button("Load Textures"))
                {
                    LoadTextures();
                }

                return;
            }

            bool hasMapping = regionMappings != null && regionMappings.Length == regionCoordinates.Count;

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Mapping"))
                {
                    CreateMapping();
                }

                if (GUILayout.Button("Load Mapping"))
                {
                    LoadMapping();
                }
            }

            if (hasMapping)
            {
                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Save Mapping"))
                    {
                        SaveMapping();
                    }

                    if (GUILayout.Button("Enable Gating on All Discovery Regions"))
                    {
                        EnableGatingOnAllDiscoveryRegions();
                    }

                    if (GUILayout.Button("Enable Localization on All Discovery Regions"))
                    {
                        EnableLocalizationOnAllDiscoveryRegions();
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Clue Validation", EditorStyles.boldLabel);

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Validation Difficulty:");
                    validationDifficulty = EditorGUILayout.IntSlider(validationDifficulty, 0, 4);
                }

                using (new GUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Validate All Difficulties"))
                    {
                        ValidateAllDifficulties();
                    }

                    if (GUILayout.Button("Validate Current Difficulty"))
                    {
                        ValidateSpecificDifficulty(validationDifficulty);
                    }
                }
            }

            if (hasMapping)
            {
                DrawMapping();
            }
        }

        private void LoadTextures()
        {
            regionsTexture      = AssetDatabase.LoadAssetAtPath<Texture2D>(regionsTexturePath);
            illustrationTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(illustrationTexturePath);
            solvedTexture       = AssetDatabase.LoadAssetAtPath<Texture2D>(solvedTexturePath);

            if (regionsTexture == null)
            {
                Debug.LogError("Region texture not found at: " + regionsTexturePath);
                return;
            }

            regionCoordinates = TileBoard.CreateRegionCoords(regionsTexture);

            int width  = regionsTexture.width;
            int height = regionsTexture.height;

            regionMap = new int[width, height];
            regionMap.Populate(-1);
            for (int i = 0; i < regionCoordinates.Count; i++)
            {
                foreach (Vector2Int pos in regionCoordinates[i])
                {
                    regionMap[pos.x, pos.y] = i;
                }
            }

            regionSprites = TileBoard.CreateRegionSprites(regionCoordinates, illustrationTexture);
            NumRegions    = regionCoordinates.Count;

            JumpToRegion(0);
        }

        private void DrawMapping()
        {
            GUILayout.Label("Region Mapping", EditorStyles.boldLabel);
            GUILayout.Space(10);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("<<"))
                {
                    ChangeRegion(-1);
                }

                GUILayout.Label($"Current Region: {currentRegion + 1}/{NumRegions}");

                if (GUILayout.Button(">>"))
                {
                    ChangeRegion(1);
                }
            }

            GUILayout.Space(10);

            RegionMapping mapping = regionMappings[currentRegion];
            DrawRegionMappingFields(mapping);

            GUILayout.Space(10);

            GUILayout.Label("Region Texture");
            GUILayout.Box(highlightTexture);

            Rect rect = GUILayoutUtility.GetLastRect();

            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 mousePos = Event.current.mousePosition;
                Vector2Int pos   = new Vector2Int((int)mousePos.x, (int)mousePos.y);

                pos -= new Vector2Int((int)rect.x, (int)rect.y);
                pos.y = (int)rect.height - pos.y;

                for (int i = 0; i < regionCoordinates.Count; i++)
                {
                    if (regionCoordinates[i].Contains(pos))
                    {
                        JumpToRegion(i);
                        break;
                    }
                }
            }
        }

        private void DrawRegionMappingFields(RegionMapping mapping)
        {
            if (mapping == null)
            {
                EditorGUILayout.HelpBox("No mapping found", MessageType.Error);
                return;
            }

            mapping.RegionIndex        = EditorGUILayout.IntField("Region Index",        mapping.RegionIndex);
            mapping.ProverbIndex       = EditorGUILayout.IntField("Proverb Index",       mapping.ProverbIndex);
            mapping.IsLocalised        = EditorGUILayout.Toggle("Is Localised",          mapping.IsLocalised);
            mapping.PaintingCoordinate = EditorGUILayout.Vector2IntField("Painting Coordinate", mapping.PaintingCoordinate);
            mapping.PaintingSize       = EditorGUILayout.IntField("Painting Size",       mapping.PaintingSize);
            mapping.Type               = (RegionType)EditorGUILayout.EnumPopup("Region Type", mapping.Type);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cryptic Region", EditorStyles.boldLabel);
            mapping.IsCrypticRegion = EditorGUILayout.Toggle("Is Cryptic Region", mapping.IsCrypticRegion);

            if (mapping.Type == RegionType.Discovery)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Region Gating", EditorStyles.boldLabel);
                mapping.IsGated = EditorGUILayout.Toggle("Is Gated", mapping.IsGated);

                if (mapping.IsGated)
                {
                    mapping.GateRegion  = EditorGUILayout.IntField("Gate Region",  mapping.GateRegion);
                    mapping.GateRegion2 = EditorGUILayout.IntField("Gate Region 2", mapping.GateRegion2);
                }

                mapping.IsLinked = EditorGUILayout.Toggle("Is Linked", mapping.IsLinked);

                if (mapping.IsLinked)
                {
                    mapping.LinkedRegion = EditorGUILayout.IntField("Linked Region", mapping.LinkedRegion);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Opponent Region", EditorStyles.boldLabel);
                mapping.IsOpponentRegion = EditorGUILayout.Toggle("Is Opponent Region", mapping.IsOpponentRegion);
            }
        }

        private void EnableGatingOnAllDiscoveryRegions()
        {
            if (regionMappings == null)
            {
                Debug.LogError("No region mappings loaded");
                return;
            }

            int enabledCount = 0;

            for (int i = 0; i < regionMappings.Length; i++)
            {
                if (regionMappings[i].Type == RegionType.Discovery)
                {
                    regionMappings[i].IsGated = true;
                    enabledCount++;
                }
            }

            Debug.Log($"Enabled gating on {enabledCount} discovery regions");
        }

        private void EnableLocalizationOnAllDiscoveryRegions()
        {
            if (regionMappings == null)
            {
                Debug.LogError("No region mappings loaded");
                return;
            }

            int enabledCount = 0;

            for (int i = 0; i < regionMappings.Length; i++)
            {
                if (regionMappings[i].Type == RegionType.Discovery)
                {
                    regionMappings[i].IsLocalised = true;
                    enabledCount++;
                }
            }

            Debug.Log($"Enabled localization on {enabledCount} discovery regions");
        }

        private void CreateMapping()
        {
            regionMappings = new RegionMapping[regionCoordinates.Count];

            for (int i = 0; i < regionCoordinates.Count; i++)
            {
                regionMappings[i] = new RegionMapping()
                {
                    RegionIndex  = i,
                    ProverbIndex = i,
                    IsLocalised  = false
                };
            }
        }

        public string GetMappingPath()
        {
            return mappingJsonPath;
        }

        private int CalculateDiscoveryTileCount()
        {
            int discoveryTileCount = 0;

            for (int i = 0; i < regionMappings.Length; i++)
            {
                if (regionMappings[i].Type == RegionType.Discovery)
                {
                    discoveryTileCount += regionCoordinates[i].Count;
                }
            }

            return discoveryTileCount;
        }

        private void SaveMapping()
        {
            string json = JsonArrayHelper.ToJson(regionMappings, true);
            System.IO.File.WriteAllText(mappingJsonPath, json);

            int discoveryTileCount = CalculateDiscoveryTileCount();
            Debug.Log($"Saved mapping to {mappingJsonPath} with {discoveryTileCount} discovery tiles");
        }

        private void LoadMapping()
        {
            if (!System.IO.File.Exists(mappingJsonPath))
            {
                Debug.LogError("Mapping file not found at: " + mappingJsonPath);
                return;
            }

            string json = System.IO.File.ReadAllText(mappingJsonPath);

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("Load failed: empty file");
                return;
            }

            regionMappings = JsonArrayHelper.FromJson<RegionMapping>(json);

            if (regionCoordinates != null && regionMappings.Length != regionCoordinates.Count)
            {
                Debug.LogError($"Mapping length {regionMappings.Length} does not match region count {regionCoordinates.Count} - you probably need to create a new mapping");
                return;
            }
        }

        private void ChangeRegion(int delta)
        {
            int region = currentRegion + delta;

            if (region < 0)
            {
                region = NumRegions - 1;
            }
            else if (region >= NumRegions)
            {
                region = 0;
            }

            currentRegion = region;
            RefreshRegion();
        }

        private void JumpToRegion(int region)
        {
            currentRegion = region;
            RefreshRegion();
        }

        private void RefreshRegion()
        {
            if (regionsTexture == null || illustrationTexture == null)
                return;

            highlightTexture = new Texture2D(regionsTexture.width, regionsTexture.height);
            Color[] colors   = illustrationTexture.GetPixels();
            highlightTexture.SetPixels(colors);

            for (int x = 0; x < highlightTexture.width; x++)
            {
                for (int y = 0; y < highlightTexture.height; y++)
                {
                    int region = regionMap[x, y];

                    if (region == currentRegion)
                    {
                        highlightTexture.SetPixel(x, y, Color.white);
                    }
                    else if (region >= 0 && regionMappings != null && regionMappings.IsValidIndex(region))
                    {
                        // Check if this region is the gate region for the current mapping
                        if (regionMappings.IsValidIndex(currentRegion) &&
                            regionMappings[currentRegion].IsGated &&
                            (regionMappings[currentRegion].GateRegion == region ||
                             regionMappings[currentRegion].GateRegion2 == region))
                        {
                            highlightTexture.SetPixel(x, y, Color.cyan);
                        }
                        else
                        {
                            highlightTexture.SetPixel(x, y, colors[y * highlightTexture.width + x]);
                        }
                    }
                }
            }

            highlightTexture.Apply();
        }

        private void ValidateAllDifficulties()
        {
            if (regionMappings == null)
                LoadMapping();

            StringBuilder report = new StringBuilder();
            report.AppendLine($"=== CLUE VALIDATION REPORT ===");
            report.AppendLine();

            report.AppendLine("--- CLUE DUPLICATION CHECK ---");
            ValidateClueUniqueness(report);
            report.AppendLine();

            for (int difficulty = 0; difficulty <= 4; difficulty++)
            {
                report.AppendLine($"--- DIFFICULTY {difficulty} ---");
                ValidateDifficultyInternal(difficulty, report);
                report.AppendLine();
            }

            string reportContent = report.ToString();
            string reportPath    = System.IO.Path.ChangeExtension(mappingJsonPath, "_validation_report.txt");

            try
            {
                string directory = System.IO.Path.GetDirectoryName(reportPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllText(reportPath, reportContent);
                Debug.Log($"Validation report written to: {reportPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to write validation report: {ex.Message}");
            }

            Debug.Log(reportContent);
        }

        private void ValidateSpecificDifficulty(int difficulty)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine($"=== CLUE VALIDATION REPORT - DIFFICULTY {difficulty} ===");
            report.AppendLine();

            ValidateDifficultyInternal(difficulty, report);

            Debug.Log(report.ToString());
        }

        private void ValidateDifficultyInternal(int difficulty, StringBuilder report)
        {
            string clueFilePath = GetClueFilePath(difficulty);

            if (!System.IO.File.Exists(clueFilePath))
            {
                report.AppendLine($"ERROR: Clue file not found at {clueFilePath}");
                return;
            }

            try
            {
                int[,] clues             = LoadCluesFromFile(clueFilePath);
                MosaicSolver solver      = new MosaicSolver(clues.GetLength(0), clues.GetLength(1));
                List<int> discoveryRegions = GetDiscoveryRegions();

                if (discoveryRegions.Count == 0)
                {
                    report.AppendLine("No discovery regions found.");
                    return;
                }

                int solvableRegions       = 0;
                int totalAdvancedDeductions = 0;
                int totalAdvancedChanges  = 0;
                int totalSimpleChanges    = 0;
                List<string> unsolvedRegions = new List<string>();

                foreach (int regionIndex in discoveryRegions)
                {
                    SolveResult result = solver.TrySolveSingleRegion(clues, regionMap, regionIndex, difficulty > 0);

                    if (result.Finished || result.RemainingTiles == 0)
                    {
                        solvableRegions++;
                        totalAdvancedDeductions += result.AdvancedDeductions;
                        totalAdvancedChanges    += result.AdvancedChanges;
                        totalSimpleChanges      += (result.NumTilesAttempted - result.AdvancedDeductions);
                    }
                    else
                    {
                        unsolvedRegions.Add($"Region {regionIndex}");
                    }
                }

                float solvabilityPercentage = discoveryRegions.Count > 0
                    ? (float)solvableRegions / discoveryRegions.Count * 100f
                    : 0f;
                int   totalChanges          = totalSimpleChanges + totalAdvancedChanges;
                float advancedPercentage    = totalChanges > 0
                    ? (float)totalAdvancedChanges / totalChanges * 100f
                    : 0f;

                report.AppendLine($"Discovery Regions: {discoveryRegions.Count}");
                report.AppendLine($"Solvable Regions: {solvableRegions}/{discoveryRegions.Count} ({solvabilityPercentage:F1}%)");
                report.AppendLine($"Simple Changes: {totalSimpleChanges}");
                report.AppendLine($"Advanced Changes: {totalAdvancedChanges}");
                report.AppendLine($"Advanced Deductions: {totalAdvancedDeductions}");
                report.AppendLine($"Advanced Percentage: {advancedPercentage:F1}%");

                if (unsolvedRegions.Count > 0)
                {
                    report.AppendLine($"Unsolvable Regions: {string.Join(", ", unsolvedRegions)}");
                }

                bool allSolvable = solvableRegions == discoveryRegions.Count;
                report.AppendLine($"Result: {(allSolvable ? "ALL REGIONS SOLVABLE" : "SOME REGIONS UNSOLVABLE")}");
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"ERROR: {ex.Message}");
            }
        }

        private string GetClueFilePath(int difficulty)
        {
            string difficultyFileName = GetClueFileForDifficulty(difficulty);
            return $"{Application.dataPath}/Resources/{clueResourcesFolder}{difficultyFileName}.txt";
        }

        private string GetClueFileForDifficulty(int difficulty)
        {
            switch (difficulty)
            {
                case 0: return "clues";
                case 1: return "clues_medium";
                case 2: return "clues_challenging";
                case 3: return "clues_expert";
                case 4: return "clues_master";
                default: throw new System.Exception("Unsupported difficulty " + difficulty);
            }
        }

        private int[,] LoadCluesFromFile(string filePath)
        {
            string cluesText = System.IO.File.ReadAllText(filePath);
            return MosaicSolver.ParseCSVString(cluesText);
        }

        private List<int> GetDiscoveryRegions()
        {
            List<int> discoveryRegions = new List<int>();

            if (regionMappings == null)
                return discoveryRegions;

            for (int i = 0; i < regionMappings.Length; i++)
            {
                if (regionMappings[i].Type == RegionType.Discovery)
                {
                    discoveryRegions.Add(i);
                }
            }

            return discoveryRegions;
        }

        private void ValidateClueUniqueness(StringBuilder report)
        {
            try
            {
                Dictionary<int, int[,]> allClues = new Dictionary<int, int[,]>();

                for (int difficulty = 0; difficulty <= 4; difficulty++)
                {
                    string clueFilePath = GetClueFilePath(difficulty);
                    if (System.IO.File.Exists(clueFilePath))
                    {
                        allClues[difficulty] = LoadCluesFromFile(clueFilePath);
                    }
                    else
                    {
                        report.AppendLine($"WARNING: Clue file not found for difficulty {difficulty}");
                    }
                }

                if (allClues.Count < 2)
                {
                    report.AppendLine("Not enough clue files to check for duplicates.");
                    return;
                }

                List<int> discoveryRegions     = GetDiscoveryRegions();
                bool      foundIdenticalRegions = false;

                foreach (KeyValuePair<int, int[,]> kvp1 in allClues)
                {
                    int   difficulty1 = kvp1.Key;
                    int[,] clues1     = kvp1.Value;

                    foreach (KeyValuePair<int, int[,]> kvp2 in allClues)
                    {
                        int   difficulty2 = kvp2.Key;
                        int[,] clues2     = kvp2.Value;

                        if (difficulty1 >= difficulty2)
                            continue;

                        List<int> identicalRegions = new List<int>();

                        foreach (int regionIndex in discoveryRegions)
                        {
                            int differences = CountRegionClueDifferences(clues1, clues2, regionIndex);

                            if (differences == 0)
                            {
                                foundIdenticalRegions = true;
                                identicalRegions.Add(regionIndex);
                            }
                        }

                        if (identicalRegions.Count > 0)
                        {
                            report.AppendLine($"IDENTICAL REGIONS IN DIFFICULTIES {difficulty1} AND {difficulty2}: {string.Join(", ", identicalRegions)}");
                        }
                    }
                }

                if (!foundIdenticalRegions)
                {
                    report.AppendLine("No identical clues found between difficulty levels");
                }
                else
                {
                    report.AppendLine("Identical clues detected - this may indicate insufficient difficulty differentiation");
                }
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"ERROR during clue uniqueness check: {ex.Message}");
            }
        }

        private int CountRegionClueDifferences(int[,] clues1, int[,] clues2, int regionIndex)
        {
            if (clues1.GetLength(0) != clues2.GetLength(0) || clues1.GetLength(1) != clues2.GetLength(1))
            {
                return -1;
            }

            int differences = 0;

            for (int x = 0; x < regionMap.GetLength(0); x++)
            {
                for (int y = 0; y < regionMap.GetLength(1); y++)
                {
                    if (regionMap[x, y] == regionIndex)
                    {
                        if (x >= clues1.GetLength(0) || y >= clues1.GetLength(1) ||
                            x >= clues2.GetLength(0) || y >= clues2.GetLength(1))
                        {
                            differences++;
                            continue;
                        }

                        if (clues1[x, y] != clues2[x, y])
                        {
                            differences++;
                        }
                    }
                }
            }

            return differences;
        }

        private (int minX, int maxX, int minY, int maxY)? GetRegionBounds(int regionIndex)
        {
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            bool foundAny = false;

            for (int x = 0; x < regionMap.GetLength(0); x++)
            {
                for (int y = 0; y < regionMap.GetLength(1); y++)
                {
                    if (regionMap[x, y] == regionIndex)
                    {
                        foundAny = true;
                        minX = System.Math.Min(minX, x);
                        maxX = System.Math.Max(maxX, x);
                        minY = System.Math.Min(minY, y);
                        maxY = System.Math.Max(maxY, y);
                    }
                }
            }

            return foundAny ? (minX, maxX, minY, maxY) : ((int, int, int, int)?)null;
        }
    }
}
