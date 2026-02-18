using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Code.Board;
using MosaicPuzzle;
using Helpers;
using I2.Loc;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Board
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

        private ValidationState[] validationStates;
        private StoryScene currentScene;
        private static int validationDifficulty = 0;

        private enum ValidationState
        {
            Unlocalised,
            Localised_OK,
            Localised_Missing,
            No_Coordinates,
            Ignored
        }

        [MenuItem("Tools/Region Mapping")]
        public static void ShowWindow()
        {
            GetWindow<RegionMappingEditor>("Region Mapping");
        }

        private static string GetRegionTexturePath(StoryScene scene)
        {
            switch (scene)
            {
                case StoryScene.Office:
                    return "office";
                case StoryScene.Forest:
                    return "forest";
                case StoryScene.Forensics:
                    return "forensics";
                case StoryScene.Museum:
                    return "museum";
                case StoryScene.Apartment:
                    return "apartment";
                case StoryScene.Parlour:
                    return "parlour";
                case StoryScene.FinalBoss:
                    return "finalboss";
                default:
                    throw new ArgumentOutOfRangeException(nameof(scene), scene, null);
            }
        }
        
        private void OnGUI()
        {
            EventType type = Event.current.type;

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("<<"))
                {
                    currentScene--;
                    if ((int)currentScene < 0)
                        currentScene = (StoryScene)6; // Wrap to FinalBoss
                    LoadScene(currentScene);
                }

                GUILayout.Label(currentScene.ToString());

                if (GUILayout.Button(">>"))
                {
                    currentScene++;
                    currentScene = (StoryScene)((int)currentScene % 7);
                    LoadScene(currentScene);
                }
            }
            
            
            if (regionsTexture == null || illustrationTexture == null)
            {
                if (GUILayout.Button("Load Textures"))
                {
                    LoadScene(currentScene);
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

            using (new GUILayout.HorizontalScope())
            {
                if (hasMapping)
                {
                    if (GUILayout.Button("Save Mapping"))
                    {
                        SaveMapping();
                    }

                    if (GUILayout.Button("Validate"))
                    {
                        ValidateMapping();
                    }

                    if (GUILayout.Button("Enable Gating on All Discovery Regions"))
                    {
                        EnableGatingOnAllDiscoveryRegions();
                    }

                    if (GUILayout.Button("Enable Localization on All Discovery Regions"))
                    {
                        EnableLocalizationOnAllDiscoveryRegions();
                    }

                    if (GUILayout.Button("Apply Links File"))
                    {
                        ApplyLinksFile();
                    }

                    if (GUILayout.Button("List Description Lengths"))
                    {
                        ListDescriptionLengths();
                    }
                }

            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate All Scene Reports"))
                {
                    GenerateAllSceneReports();
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

        private void LoadScene(StoryScene storyScene)
        {
            string scenePath = GetRegionTexturePath(storyScene);
            regionsTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Board/{scenePath}/{scenePath}_regions.png");
            illustrationTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Board/{scenePath}/{scenePath}_illustration.png");
            solvedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Board/{scenePath}/{scenePath}_solved.png");

            if (regionsTexture == null)
            {
                Debug.LogError("Region texture not found "+scenePath);
                return;
            }
                    
            regionCoordinates = TileBoard.CreateRegionCoords(regionsTexture);

            int width = regionsTexture.width;
            int height = regionsTexture.height;

            regionMap = new int[width, height];
            regionMap.Populate(-1);
            for (var i = 0; i < regionCoordinates.Count; i++)
            {
                var region = regionCoordinates[i];
                foreach (Vector2Int pos in region)
                {
                    regionMap[pos.x, pos.y] = i;
                }
            }

            regionSprites = TileBoard.CreateRegionSprites(regionCoordinates, illustrationTexture);

            NumRegions = regionCoordinates.Count;

            JumpToRegion(0);
        }

        private void DrawMapping()
        {
            GUILayout.Label("Region Mapping", EditorStyles.boldLabel);
            GUILayout.Label("This tool is used to map regions to achievements and other game elements.");

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

            //draw the serialized properties
            RegionMapping mapping = regionMappings[currentRegion];
            DrawRegionMappingFields(mapping);
            
            GUILayout.Space(10);

            //draw the region texture
            GUILayout.Label("Region Texture");
            GUILayout.Box(highlightTexture);

            var rect = GUILayoutUtility.GetLastRect();

            //detect coordinate of clicks on the highlight texture
            if (Event.current.type == EventType.MouseDown)
            {
                Vector2 mousePos = Event.current.mousePosition;
                Vector2Int pos = new Vector2Int((int)mousePos.x, (int)mousePos.y);

                pos -= new Vector2Int((int)rect.x, (int)rect.y);
                pos.y = (int)rect.height - pos.y;

                //find the region that contains this square
                for (int i = 0; i < regionCoordinates.Count; i++)
                {
                    if (regionCoordinates[i].Contains(pos))
                    {
                        JumpToRegion(i);
                        break;
                    }
                }
            }
                        

            bool needsLocalisation = mapping.IsLocalised;
            int proverbIndex = mapping.ProverbIndex;
            LocalizedString locTerm = RegionMapping.GetTitleKey(currentScene, proverbIndex);
            LocalizedString locDesc = RegionMapping.GetDescKey(currentScene, proverbIndex);
            GUILayout.Label($"Localization Term: {locTerm.mTerm}");

            
            foreach (string language in LocalizationManager.GetAllLanguagesCode())
            {
                LocalizationManager.CurrentLanguageCode = language;

                if (!needsLocalisation)
                {
                    GUI.color = Color.grey;
                }
                else if (IsLocalisationMissing(locTerm) || IsLocalisationMissing(locDesc))
                {
                    GUI.color = Color.red;
                }

                string titleText = locTerm.ToString();
                string descText = locDesc.ToString();

                // Truncate non-English languages for readability
                if (language != "en" && language != "en-GB")
                {
                    titleText = TruncateText(titleText, 50);
                    descText = TruncateText(descText, 100);
                }

                GUILayout.Label($"{language}:\n{titleText}\n{descText}");

                GUI.color = Color.white;
            }
            
            LocalizationManager.CurrentLanguageCode = "en-GB";
        }

        private void DrawRegionMappingFields(RegionMapping mapping)
        {
            if (mapping == null)
            {
                EditorGUILayout.HelpBox("No mapping found", MessageType.Error);
                return;
            }
            
            mapping.RegionIndex = EditorGUILayout.IntField("Region Index", mapping.RegionIndex);
            mapping.ProverbIndex = EditorGUILayout.IntField("Proverb Index", mapping.ProverbIndex);
            mapping.IsLocalised = EditorGUILayout.Toggle("Is Localised", mapping.IsLocalised);
            mapping.PaintingCoordinate = EditorGUILayout.Vector2IntField("Painting Coordinate", mapping.PaintingCoordinate);
            mapping.PaintingSize = EditorGUILayout.IntField("Painting Size", mapping.PaintingSize);
            mapping.Type = (RegionType)EditorGUILayout.EnumPopup("Region Type", mapping.Type);
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Cryptic Region", EditorStyles.boldLabel);
            mapping.IsCrypticRegion = EditorGUILayout.Toggle("Is Cryptic Region", mapping.IsCrypticRegion);

            // Region Gating fields (only for Discovery regions)
            if (mapping.Type == RegionType.Discovery)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Region Gating", EditorStyles.boldLabel);
                mapping.IsGated = EditorGUILayout.Toggle("Is Gated", mapping.IsGated);

                if (mapping.IsGated)
                {
                    mapping.GateScene = (StoryScene)EditorGUILayout.EnumPopup("Gate Scene", mapping.GateScene);
                    
                    // Display gate region with title
                    using (new GUILayout.HorizontalScope())
                    {
                        mapping.GateRegion = EditorGUILayout.IntField("Gate Region", mapping.GateRegion);
                        
                        // Look up and display the gate region title
                        string gateTitle = GetGateRegionTitle(mapping.GateScene, mapping.GateRegion);
                        if (!string.IsNullOrEmpty(gateTitle))
                        {
                            EditorGUILayout.LabelField(gateTitle, EditorStyles.miniLabel);
                        }
                    }
                    
                    mapping.GateScene2 = (StoryScene)EditorGUILayout.EnumPopup("Gate Scene 2", mapping.GateScene2);
                    
                    // Display gate region with title
                    using (new GUILayout.HorizontalScope())
                    {
                        mapping.GateRegion2 = EditorGUILayout.IntField("Gate Region 2", mapping.GateRegion2);
                        
                        // Look up and display the gate region title
                        string gateTitle = GetGateRegionTitle(mapping.GateScene2, mapping.GateRegion2);
                        if (!string.IsNullOrEmpty(gateTitle))
                        {
                            EditorGUILayout.LabelField(gateTitle, EditorStyles.miniLabel);
                        }
                    }
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
        
        private void ValidateMapping()
        {
            validationStates = new ValidationState[regionMappings.Length];
            
            for (int i = 0; i < regionMappings.Length; i++)
            {
// #if DEMO
//                 if (TileBoard.DemoRegionIndices.Contains(i) == false)
//                 {
//                     validationStates[i] = ValidationState.Ignored;
//                     continue;
//                 }
// #endif

                string locPath = RegionMapping.GetLocPath(currentScene);
                
                if (regionMappings[i].IsLocalised)
                {
                    LocalizedString locTerm = $"{locPath}/discover_{currentRegion}";
                    LocalizedString locDesc = $"{locPath}/discovery_desc{currentRegion}";

                    validationStates[i] = ValidationState.Localised_OK;

                    foreach (string languageCode in LocalizationManager.GetAllLanguagesCode())
                    {
                        LocalizationManager.CurrentLanguageCode = languageCode;

                        if (IsLocalisationMissing(locTerm) || IsLocalisationMissing(locDesc))
                        {
                            validationStates[i] = ValidationState.Localised_Missing;
                            break;
                        }
                        
                        // if(regionMappings[i].PaintingCoordinate == Vector2Int.zero)
                        // {
                        //     validationStates[i] = ValidationState.No_Coordinates;
                        //     break;
                        // }
                    }
                }
                else
                {
                    validationStates[i] = ValidationState.Unlocalised;
                }
            }
        }

        private bool IsLocalisationMissing(LocalizedString term)
        {
            return string.IsNullOrEmpty(term) || term.ToString() == "UNTRANSLATED";
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }

        private string GetGateRegionTitle(StoryScene gateScene, int gateRegion)
        {
            if (gateRegion <= 0)
                return null;

            RegionMapping gateMapping = null;

            // If gate scene is current scene, use existing mappings
            if (gateScene == currentScene && regionMappings != null && gateRegion >= 0 && gateRegion < regionMappings.Length)
            {
                gateMapping = regionMappings[gateRegion];
            }
            else
            {
                // Load mappings for the gate scene
                try
                {
                    string path = GetMappingPath(gateScene);
                    if (System.IO.File.Exists(path))
                    {
                        string json = System.IO.File.ReadAllText(path);
                        if (!string.IsNullOrEmpty(json))
                        {
                            RegionMapping[] gateSceneMappings = JsonArrayHelper.FromJson<RegionMapping>(json);
                            if (gateSceneMappings != null && gateRegion >= 0 && gateRegion < gateSceneMappings.Length)
                            {
                                gateMapping = gateSceneMappings[gateRegion];
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"Failed to load gate scene mappings: {ex.Message}");
                    return null;
                }
            }

            if (gateMapping == null)
                return null;

            // Get the localized title
            LocalizedString locTerm = RegionMapping.GetTitleKey(gateScene, gateMapping.ProverbIndex);
            string regionTitle = locTerm.ToString();
            
            return TruncateText(regionTitle, 40);
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
                    regionMappings[i].GateScene = currentScene;
                    regionMappings[i].GateScene2 = currentScene;
                    enabledCount++;
                }
            }

            Debug.Log($"Enabled gating on {enabledCount} discovery regions for {currentScene}, gate scene set to {currentScene}");
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

            Debug.Log($"Enabled localization on {enabledCount} discovery regions for {currentScene}");
        }

        private void ApplyLinksFile()
        {
            string linksFilePath = "Assets/Board/Editor/links.txt";
            
            if (!System.IO.File.Exists(linksFilePath))
            {
                Debug.LogError($"Links file not found at {linksFilePath}");
                EditorUtility.DisplayDialog("Error", $"Links file not found at {linksFilePath}", "OK");
                return;
            }

            string[] lines = System.IO.File.ReadAllLines(linksFilePath);
            Dictionary<StoryScene, RegionMapping[]> allSceneMappings = new Dictionary<StoryScene, RegionMapping[]>();
            HashSet<StoryScene> modifiedScenes = new HashSet<StoryScene>();

            // Load all scene mappings
            foreach (StoryScene scene in System.Enum.GetValues(typeof(StoryScene)))
            {
                string path = GetMappingPath(scene);
                if (System.IO.File.Exists(path))
                {
                    string json = System.IO.File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(json))
                    {
                        allSceneMappings[scene] = JsonArrayHelper.FromJson<RegionMapping>(json);
                    }
                }
            }

            StoryScene? currentGateScene = null;
            int? currentGateRegion = null;
            StoryScene? currentGateScene2 = null;
            int? currentGateRegion2 = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;

                // Parse GATE line
                if (line.StartsWith("GATE ", System.StringComparison.OrdinalIgnoreCase))
                {
                    string gateContent = line.Substring(5).Trim();
                    
                    // Check if there are two gates (comma-separated)
                    if (gateContent.Contains(","))
                    {
                        // Parse two gates
                        string[] gateParts = gateContent.Split(',');
                        if (gateParts.Length >= 2)
                        {
                            // Parse first gate
                            string[] firstGateParts = gateParts[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (firstGateParts.Length >= 2)
                            {
                                if (System.Enum.TryParse<StoryScene>(firstGateParts[0], true, out StoryScene gateScene))
                                {
                                    if (int.TryParse(firstGateParts[1], out int gateRegion))
                                    {
                                        currentGateScene = gateScene;
                                        currentGateRegion = gateRegion;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"Invalid first gate region number on line {i + 1}: {line}");
                                        currentGateScene = null;
                                        currentGateRegion = null;
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"Invalid first gate scene name on line {i + 1}: {line}");
                                    currentGateScene = null;
                                    currentGateRegion = null;
                                }
                            }
                            
                            // Parse second gate
                            string[] secondGateParts = gateParts[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (secondGateParts.Length >= 2)
                            {
                                if (System.Enum.TryParse<StoryScene>(secondGateParts[0], true, out StoryScene gateScene2))
                                {
                                    if (int.TryParse(secondGateParts[1], out int gateRegion2))
                                    {
                                        currentGateScene2 = gateScene2;
                                        currentGateRegion2 = gateRegion2;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"Invalid second gate region number on line {i + 1}: {line}");
                                        currentGateScene2 = null;
                                        currentGateRegion2 = null;
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"Invalid second gate scene name on line {i + 1}: {line}");
                                    currentGateScene2 = null;
                                    currentGateRegion2 = null;
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"Invalid second gate format on line {i + 1}: {line}");
                                currentGateScene2 = null;
                                currentGateRegion2 = null;
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Invalid GATE format (expected two gates) on line {i + 1}: {line}");
                        }
                    }
                    else
                    {
                        // Parse single gate (original behavior)
                        string[] parts = gateContent.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            if (System.Enum.TryParse<StoryScene>(parts[0], true, out StoryScene gateScene))
                            {
                                if (int.TryParse(parts[1], out int gateRegion))
                                {
                                    currentGateScene = gateScene;
                                    currentGateRegion = gateRegion;
                                    // Clear second gate when only one is specified
                                    currentGateScene2 = null;
                                    currentGateRegion2 = null;
                                }
                                else
                                {
                                    Debug.LogWarning($"Invalid gate region number on line {i + 1}: {line}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"Invalid gate scene name on line {i + 1}: {line}");
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Invalid GATE format on line {i + 1}: {line}");
                        }
                    }
                }
                // Parse UNLOCKS line
                else if (line.StartsWith("UNLOCKS ", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (!currentGateScene.HasValue || !currentGateRegion.HasValue)
                    {
                        Debug.LogWarning($"UNLOCKS line {i + 1} has no preceding GATE line: {line}");
                        continue;
                    }

                    string restOfLine = line.Substring(8).Trim();
                    string[] parts = restOfLine.Split(new[] { ' ' }, 2);
                    if (parts.Length >= 2)
                    {
                        if (System.Enum.TryParse<StoryScene>(parts[0], true, out StoryScene unlocksScene))
                        {
                            // Parse comma-separated region indices (handle spaces after commas)
                            string[] regionIndices = parts[1].Split(',');
                            
                            if (!allSceneMappings.ContainsKey(unlocksScene))
                            {
                                Debug.LogWarning($"Scene {unlocksScene} not found in mappings. Skipping UNLOCKS line {i + 1}");
                                continue;
                            }

                            RegionMapping[] sceneMappings = allSceneMappings[unlocksScene];
                            int appliedCount = 0;

                            foreach (string regionStr in regionIndices)
                            {
                                if (int.TryParse(regionStr.Trim(), out int regionIndex))
                                {
                                    if (regionIndex >= 0 && regionIndex < sceneMappings.Length)
                                    {
                                        sceneMappings[regionIndex].IsGated = true;
                                        sceneMappings[regionIndex].GateScene = currentGateScene.Value;
                                        sceneMappings[regionIndex].GateRegion = currentGateRegion.Value;
                                        
                                        // Set second gate if it exists
                                        if (currentGateScene2.HasValue && currentGateRegion2.HasValue)
                                        {
                                            sceneMappings[regionIndex].GateScene2 = currentGateScene2.Value;
                                            sceneMappings[regionIndex].GateRegion2 = currentGateRegion2.Value;
                                        }
                                        
                                        appliedCount++;
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"Invalid region index {regionIndex} for scene {unlocksScene} on line {i + 1}");
                                    }
                                }
                            }

                            modifiedScenes.Add(unlocksScene);
                            string gateInfo = currentGateScene2.HasValue && currentGateRegion2.HasValue
                                ? $"{currentGateScene} {currentGateRegion} and {currentGateScene2} {currentGateRegion2}"
                                : $"{currentGateScene} {currentGateRegion}";
                            Debug.Log($"Applied gate {gateInfo} to {appliedCount} regions in {unlocksScene}");
                        }
                        else
                        {
                            Debug.LogWarning($"Invalid UNLOCKS scene name on line {i + 1}: {line}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Invalid UNLOCKS format on line {i + 1}: {line}");
                    }
                }
            }

            // Save all modified scene mappings
            int savedCount = 0;
            foreach (StoryScene scene in modifiedScenes)
            {
                RegionMapping[] mappings = allSceneMappings[scene];
                
                // Set scene on all mappings
                foreach (RegionMapping mapping in mappings)
                {
                    mapping.Scene = scene;
                }

                string json = JsonArrayHelper.ToJson(mappings, true);
                string path = GetMappingPath(scene);
                System.IO.File.WriteAllText(path, json);
                savedCount++;
            }

            // Reload current scene mapping if it was modified
            if (modifiedScenes.Contains(currentScene))
            {
                LoadMapping();
            }

            Debug.Log($"Applied links file: Modified {savedCount} scene(s)");
            EditorUtility.DisplayDialog("Success", $"Applied links file to {savedCount} scene(s)", "OK");
        }

        private void ListDescriptionLengths()
        {
            if (regionMappings == null)
            {
                Debug.LogError("No region mappings loaded. Load mapping first.");
                EditorUtility.DisplayDialog("Error", "No region mappings loaded. Load mapping first.", "OK");
                return;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine($"=== DESCRIPTION LENGTH REPORT FOR {currentScene} ===");
            report.AppendLine();

            // Store current language to restore later
            string originalLanguage = LocalizationManager.CurrentLanguageCode;
            LocalizationManager.CurrentLanguageCode = "en-GB";

            List<(int regionIndex, int proverbIndex, int titleLength, int descLength, int titleWords, int descWords, int aiScore, int emDashes)> discoveryData = new List<(int, int, int, int, int, int, int, int)>();

            for (int i = 0; i < regionMappings.Length; i++)
            {
                RegionMapping mapping = regionMappings[i];

                if (mapping.Type == RegionType.Discovery && mapping.IsLocalised)
                {
                    LocalizedString locTitle = RegionMapping.GetTitleKey(currentScene, mapping.ProverbIndex);
                    LocalizedString locDesc = RegionMapping.GetDescKey(currentScene, mapping.ProverbIndex);

                    string titleText = locTitle.ToString();
                    string descText = locDesc.ToString();

                    // Skip if localization is missing
                    if (IsLocalisationMissing(locTitle) || IsLocalisationMissing(locDesc))
                    {
                        report.AppendLine($"Region {i}: MISSING LOCALIZATION");
                        continue;
                    }

                    int titleLength = titleText.Length;
                    int descLength = descText.Length;
                    int titleWords = CountWords(titleText);
                    int descWords = CountWords(descText);

                    // Analyze combined text for AI patterns
                    string combinedText = titleText + " " + descText;
                    int aiScore = AnalyzeAIPatterns(combinedText);
                    int emDashes = CountEmDashes(combinedText);

                    discoveryData.Add((i, mapping.ProverbIndex, titleLength, descLength, titleWords, descWords, aiScore, emDashes));
                }
            }

            // Sort by description length (descending)
            discoveryData.Sort((a, b) => b.descLength.CompareTo(a.descLength));

            report.AppendLine($"Found {discoveryData.Count} discovery regions with localization:");
            report.AppendLine();
            report.AppendLine("Region | Proverb | Title Len | Title Words | Desc Len | Desc Words | AI Score | Em Dashes | Title");
            report.AppendLine("-------|---------|-----------|-------------|----------|------------|----------|-----------|------");

            foreach (var (regionIndex, proverbIndex, titleLength, descLength, titleWords, descWords, aiScore, emDashes) in discoveryData)
            {
                LocalizedString locTitle = RegionMapping.GetTitleKey(currentScene, proverbIndex);
                string titleText = locTitle.ToString();

                // Truncate title for display
                string displayTitle = TruncateText(titleText, 30);

                report.AppendLine($"{regionIndex,6} | {proverbIndex,7} | {titleLength,9} | {titleWords,11} | {descLength,8} | {descWords,10} | {aiScore,8} | {emDashes,10}| {displayTitle}");
            }

            report.AppendLine();

            if (discoveryData.Count > 0)
            {
                int totalTitleChars = discoveryData.Sum(d => d.titleLength);
                int totalDescChars = discoveryData.Sum(d => d.descLength);
                int totalTitleWords = discoveryData.Sum(d => d.titleWords);
                int totalDescWords = discoveryData.Sum(d => d.descWords);
                int totalAiScore = discoveryData.Sum(d => d.aiScore);
                int totalEmDashes = discoveryData.Sum(d => d.emDashes);

                float avgTitleLength = (float)totalTitleChars / discoveryData.Count;
                float avgDescLength = (float)totalDescChars / discoveryData.Count;
                float avgTitleWords = (float)totalTitleWords / discoveryData.Count;
                float avgDescWords = (float)totalDescWords / discoveryData.Count;
                float avgAiScore = (float)totalAiScore / discoveryData.Count;

                int maxTitleLength = discoveryData.Max(d => d.titleLength);
                int maxDescLength = discoveryData.Max(d => d.descLength);
                int maxTitleWords = discoveryData.Max(d => d.titleWords);
                int maxDescWords = discoveryData.Max(d => d.descWords);
                int maxAiScore = discoveryData.Max(d => d.aiScore);

                int minTitleLength = discoveryData.Min(d => d.titleLength);
                int minDescLength = discoveryData.Min(d => d.descLength);
                int minTitleWords = discoveryData.Min(d => d.titleWords);
                int minDescWords = discoveryData.Min(d => d.descWords);

                int regionsWithAiPatterns = discoveryData.Count(d => d.aiScore > 0);
                int regionsWithEmDashes = discoveryData.Count(d => d.emDashes > 0);
                int suspiciousRegions = discoveryData.Count(d => d.aiScore > 2);

                report.AppendLine("STATISTICS:");
                report.AppendLine($"Total Title Characters: {totalTitleChars}");
                report.AppendLine($"Total Description Characters: {totalDescChars}");
                report.AppendLine($"Total Title Words: {totalTitleWords}");
                report.AppendLine($"Total Description Words: {totalDescWords}");
                report.AppendLine($"Average Title Length: {avgTitleLength:F1} characters ({avgTitleWords:F1} words)");
                report.AppendLine($"Average Description Length: {avgDescLength:F1} characters ({avgDescWords:F1} words)");
                report.AppendLine($"Title Length Range: {minTitleLength} - {maxTitleLength} characters");
                report.AppendLine($"Title Word Range: {minTitleWords} - {maxTitleWords} words");
                report.AppendLine($"Description Length Range: {minDescLength} - {maxDescLength} characters");
                report.AppendLine($"Description Word Range: {minDescWords} - {maxDescWords} words");
                report.AppendLine();
                report.AppendLine("AI PATTERN ANALYSIS:");
                report.AppendLine($"Total AI Score: {totalAiScore}");
                report.AppendLine($"Average AI Score: {avgAiScore:F1}");
                report.AppendLine($"Max AI Score: {maxAiScore}");
                report.AppendLine($"Regions with AI patterns: {regionsWithAiPatterns}/{discoveryData.Count} ({(float)regionsWithAiPatterns / discoveryData.Count * 100:F1}%)");
                report.AppendLine($"Regions with em dashes: {regionsWithEmDashes}/{discoveryData.Count} ({(float)regionsWithEmDashes / discoveryData.Count * 100:F1}%)");
                report.AppendLine($"Total em dashes: {totalEmDashes}");
                report.AppendLine($"Suspicious regions (AI score > 2): {suspiciousRegions}/{discoveryData.Count} ({(float)suspiciousRegions / discoveryData.Count * 100:F1}%)");

                if (suspiciousRegions > 0)
                {
                    report.AppendLine();
                    report.AppendLine("REGIONS WITH HIGH AI SCORES:");
                    var suspiciousData = discoveryData.Where(d => d.aiScore > 2).OrderByDescending(d => d.aiScore);
                    foreach (var (regionIndex, _, _, _, _, _, aiScore, emDashes) in suspiciousData)
                    {
                        LocalizedString locTitle = RegionMapping.GetTitleKey(currentScene, regionIndex);
                        string titleText = locTitle.ToString();
                        report.AppendLine($"Region {regionIndex}: AI Score {aiScore}, Em Dashes {emDashes} - {TruncateText(titleText, 50)}");
                    }
                }
            }

            // Restore original language
            LocalizationManager.CurrentLanguageCode = originalLanguage;

            string reportContent = report.ToString();
            Debug.Log(reportContent);

            // Write to file
            try
            {
                string reportPath = GetDescriptionLengthReportPath(currentScene);
                string directory = System.IO.Path.GetDirectoryName(reportPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                System.IO.File.WriteAllText(reportPath, reportContent);
                EditorUtility.DisplayDialog("Success", $"Description length report written to:\n{reportPath}", "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to write description length report: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to write report to file: {ex.Message}", "OK");
            }
        }

        private string GetDescriptionLengthReportPath(StoryScene scene)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Board/{scenePath}/{scenePath}_description_lengths.txt";
        }

        private int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Split on whitespace and filter out empty entries
            string[] words = text.Split(new char[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            return words.Length;
        }

        private int AnalyzeAIPatterns(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Convert to lowercase for case-insensitive matching
            string lowerText = text.ToLower();
            int score = 0;

            // AI-characteristic terms and phrases
            string[] aiTerms = {
                "align", "aligns", "aligning with",
                "crucial",
                "delve", "delves", "delving",
                "emphasizing",
                "enduring",
                "enhance", "enhances", "enhancing",
                "fostering",
                "garnered", "garnering",
                "highlight", "highlighted", "highlighting", "highlights",  // as verb
                "interplay",
                "intricate", "intricacies",
                "pivotal",
                "showcase", "showcased", "showcases", "showcasing",
                "tapestry",  // as abstract noun
                "testament",
                "underscore", "underscored", "underscores", "underscoring",
                "landscape",  // as abstract noun
                "key"  // as adjective (harder to detect context, so just presence)
            };

            foreach (string term in aiTerms)
            {
                // Count occurrences of each term
                int index = 0;
                while ((index = lowerText.IndexOf(term, index)) != -1)
                {
                    // Check if it's a whole word (not part of another word)
                    bool isWholeWord = true;

                    // Check character before
                    if (index > 0 && char.IsLetter(lowerText[index - 1]))
                        isWholeWord = false;

                    // Check character after
                    if (index + term.Length < lowerText.Length && char.IsLetter(lowerText[index + term.Length]))
                        isWholeWord = false;

                    if (isWholeWord)
                        score++;

                    index += term.Length;
                }
            }

            return score;
        }

        private int CountEmDashes(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            int count = 0;
            foreach (char c in text)
            {
                if (c == '—')  // Em dash character
                    count++;
            }
            return count;
        }

        private void GenerateAllSceneReports()
        {
            int totalProcessedScenes = 0;
            int totalFailedScenes = 0;

            foreach (StoryScene scene in System.Enum.GetValues(typeof(StoryScene)))
            {
                try
                {
                    GenerateSceneReport(scene);
                    totalProcessedScenes++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to process scene {scene}: {ex.Message}");
                    totalFailedScenes++;
                }
            }

            Debug.Log($"All scene reports generated. Processed: {totalProcessedScenes}, Failed: {totalFailedScenes}");
            EditorUtility.DisplayDialog("Success", $"Generated individual reports for {totalProcessedScenes} scenes.\nFailed: {totalFailedScenes} scenes.", "OK");
        }

        private void GenerateSceneReport(StoryScene scene)
        {
            // Load textures and coordinates for this scene
            string scenePath = GetRegionTexturePath(scene);
            Texture2D sceneRegionsTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Board/{scenePath}/{scenePath}_regions.png");

            if (sceneRegionsTexture == null)
            {
                Debug.LogWarning($"No regions texture found for {scene}");
                return;
            }

            List<List<Vector2Int>> sceneRegionCoordinates = TileBoard.CreateRegionCoords(sceneRegionsTexture);

            // Load region mappings for this scene
            string mappingPath = GetMappingPath(scene);
            if (!System.IO.File.Exists(mappingPath))
            {
                Debug.LogWarning($"No mapping file found for {scene}");
                return;
            }

            string json = System.IO.File.ReadAllText(mappingPath);
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning($"Empty mapping file for {scene}");
                return;
            }

            RegionMapping[] sceneMappings = JsonArrayHelper.FromJson<RegionMapping>(json);
            if (sceneMappings == null || sceneMappings.Length != sceneRegionCoordinates.Count)
            {
                Debug.LogWarning($"Invalid mapping data for {scene}");
                return;
            }

            // Generate report for this scene
            string sceneReport = GenerateDescriptionLengthReport(scene, sceneMappings);

            // Write individual scene report
            string sceneReportPath = GetDescriptionLengthReportPath(scene);
            string directory = System.IO.Path.GetDirectoryName(sceneReportPath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(sceneReportPath, sceneReport);
            Debug.Log($"Generated report for {scene}: {sceneReportPath}");
        }

        private string GenerateDescriptionLengthReport(StoryScene scene, RegionMapping[] sceneMappings)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine($"=== DESCRIPTION LENGTH REPORT FOR {scene} ===");
            report.AppendLine();

            // Store current language to restore later
            string originalLanguage = LocalizationManager.CurrentLanguageCode;
            LocalizationManager.CurrentLanguageCode = "en-GB";

            List<(int regionIndex, int proverbIndex, int titleLength, int descLength, int titleWords, int descWords, int aiScore, int emDashes)> discoveryData =
                new List<(int, int, int, int, int, int, int, int)>();

            for (int i = 0; i < sceneMappings.Length; i++)
            {
                RegionMapping mapping = sceneMappings[i];

                if (mapping.Type == RegionType.Discovery && mapping.IsLocalised)
                {
                    LocalizedString locTitle = RegionMapping.GetTitleKey(scene, mapping.ProverbIndex);
                    LocalizedString locDesc = RegionMapping.GetDescKey(scene, mapping.ProverbIndex);

                    string titleText = locTitle.ToString();
                    string descText = locDesc.ToString();

                    // Skip if localization is missing
                    if (IsLocalisationMissing(locTitle) || IsLocalisationMissing(locDesc))
                    {
                        report.AppendLine($"Region {i}: MISSING LOCALIZATION");
                        continue;
                    }

                    int titleLength = titleText.Length;
                    int descLength = descText.Length;
                    int titleWords = CountWords(titleText);
                    int descWords = CountWords(descText);

                    // Analyze combined text for AI patterns
                    string combinedText = titleText + " " + descText;
                    int aiScore = AnalyzeAIPatterns(combinedText);
                    int emDashes = CountEmDashes(combinedText);

                    discoveryData.Add((i, mapping.ProverbIndex, titleLength, descLength, titleWords, descWords, aiScore, emDashes));
                }
            }

            // Sort by description length (descending)
            discoveryData.Sort((a, b) => b.descLength.CompareTo(a.descLength));

            report.AppendLine($"Found {discoveryData.Count} discovery regions with localization:");
            report.AppendLine();
            report.AppendLine("Region | Proverb | Title Len | Title Words | Desc Len | Desc Words | AI Score | Em Dashes | Title");
            report.AppendLine("-------|---------|-----------|-------------|----------|------------|----------|-----------|------");

            foreach (var (regionIndex, proverbIndex, titleLength, descLength, titleWords, descWords, aiScore, emDashes) in discoveryData)
            {
                LocalizedString locTitle = RegionMapping.GetTitleKey(scene, proverbIndex);
                string titleText = locTitle.ToString();

                // Truncate title for display
                string displayTitle = TruncateText(titleText, 30);

                report.AppendLine($"{regionIndex,6} | {proverbIndex,7} | {titleLength,9} | {titleWords,11} | {descLength,8} | {descWords,10} | {aiScore,8} | {emDashes,10}| {displayTitle}");
            }

            report.AppendLine();

            if (discoveryData.Count > 0)
            {
                // Calculate and add all statistics (same as original method)
                int totalTitleChars = discoveryData.Sum(d => d.titleLength);
                int totalDescChars = discoveryData.Sum(d => d.descLength);
                int totalTitleWords = discoveryData.Sum(d => d.titleWords);
                int totalDescWords = discoveryData.Sum(d => d.descWords);
                int totalAiScore = discoveryData.Sum(d => d.aiScore);
                int totalEmDashes = discoveryData.Sum(d => d.emDashes);

                float avgTitleLength = (float)totalTitleChars / discoveryData.Count;
                float avgDescLength = (float)totalDescChars / discoveryData.Count;
                float avgTitleWords = (float)totalTitleWords / discoveryData.Count;
                float avgDescWords = (float)totalDescWords / discoveryData.Count;
                float avgAiScore = (float)totalAiScore / discoveryData.Count;

                int maxTitleLength = discoveryData.Max(d => d.titleLength);
                int maxDescLength = discoveryData.Max(d => d.descLength);
                int maxTitleWords = discoveryData.Max(d => d.titleWords);
                int maxDescWords = discoveryData.Max(d => d.descWords);
                int maxAiScore = discoveryData.Max(d => d.aiScore);

                int minTitleLength = discoveryData.Min(d => d.titleLength);
                int minDescLength = discoveryData.Min(d => d.descLength);
                int minTitleWords = discoveryData.Min(d => d.titleWords);
                int minDescWords = discoveryData.Min(d => d.descWords);

                int regionsWithAiPatterns = discoveryData.Count(d => d.aiScore > 0);
                int regionsWithEmDashes = discoveryData.Count(d => d.emDashes > 0);
                int suspiciousRegions = discoveryData.Count(d => d.aiScore > 2);

                report.AppendLine("STATISTICS:");
                report.AppendLine($"Total Title Characters: {totalTitleChars}");
                report.AppendLine($"Total Description Characters: {totalDescChars}");
                report.AppendLine($"Total Title Words: {totalTitleWords}");
                report.AppendLine($"Total Description Words: {totalDescWords}");
                report.AppendLine($"Average Title Length: {avgTitleLength:F1} characters ({avgTitleWords:F1} words)");
                report.AppendLine($"Average Description Length: {avgDescLength:F1} characters ({avgDescWords:F1} words)");
                report.AppendLine($"Title Length Range: {minTitleLength} - {maxTitleLength} characters");
                report.AppendLine($"Title Word Range: {minTitleWords} - {maxTitleWords} words");
                report.AppendLine($"Description Length Range: {minDescLength} - {maxDescLength} characters");
                report.AppendLine($"Description Word Range: {minDescWords} - {maxDescWords} words");
                report.AppendLine();
                report.AppendLine("AI PATTERN ANALYSIS:");
                report.AppendLine($"Total AI Score: {totalAiScore}");
                report.AppendLine($"Average AI Score: {avgAiScore:F1}");
                report.AppendLine($"Max AI Score: {maxAiScore}");
                report.AppendLine($"Regions with AI patterns: {regionsWithAiPatterns}/{discoveryData.Count} ({(float)regionsWithAiPatterns / discoveryData.Count * 100:F1}%)");
                report.AppendLine($"Regions with em dashes: {regionsWithEmDashes}/{discoveryData.Count} ({(float)regionsWithEmDashes / discoveryData.Count * 100:F1}%)");
                report.AppendLine($"Total em dashes: {totalEmDashes}");
                report.AppendLine($"Suspicious regions (AI score > 2): {suspiciousRegions}/{discoveryData.Count} ({(float)suspiciousRegions / discoveryData.Count * 100:F1}%)");

                if (suspiciousRegions > 0)
                {
                    report.AppendLine();
                    report.AppendLine("REGIONS WITH HIGH AI SCORES:");
                    var suspiciousData = discoveryData.Where(d => d.aiScore > 2).OrderByDescending(d => d.aiScore);
                    foreach (var (regionIndex, _, _, _, _, _, aiScore, emDashes) in suspiciousData)
                    {
                        LocalizedString locTitle = RegionMapping.GetTitleKey(scene, regionIndex);
                        string titleText = locTitle.ToString();
                        report.AppendLine($"Region {regionIndex}: AI Score {aiScore}, Em Dashes {emDashes} - {TruncateText(titleText, 50)}");
                    }
                }
            }

            // Restore original language
            LocalizationManager.CurrentLanguageCode = originalLanguage;

            return report.ToString();
        }

        private void CreateMapping()
        {
            regionMappings = new RegionMapping[regionCoordinates.Count];
            validationStates = new ValidationState[regionCoordinates.Count];
            LocalizationManager.CurrentLanguageCode = "en-GB";

            string locPath = RegionMapping.GetLocPath(currentScene);
            
            for (int i = 0; i < regionCoordinates.Count; i++)
            {
                LocalizedString locTerm = $"{locPath}/discover_{currentRegion}";

                bool localisationIsMissing = IsLocalisationMissing(locTerm);

                regionMappings[i] = new RegionMapping()
                {
                    RegionIndex = i,
                    ProverbIndex = i,
                    IsLocalised = !localisationIsMissing
                };
            }
        }

        public static string GetMappingPath(StoryScene scene)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Board/{scenePath}/{scenePath}_region_mapping.json";
        }

        public static string GetDiscoveryCountPath(StoryScene scene)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Resources/Board/{scenePath}/{scenePath}_discovery_count.txt";
        }

        private int CalculateDiscoveryTileCount()
        {
            int discoveryTileCount = 0;

            for (int i = 0; i < regionMappings.Length; i++)
            {
                if (regionMappings[i].Type == RegionType.Discovery)
                {
                    // Count tiles in this discovery region
                    discoveryTileCount += regionCoordinates[i].Count;
                }
            }

            return discoveryTileCount;
        }
        
        private void SaveMapping()
        {
            //serialize the region mappings as json
            foreach (RegionMapping mapping in regionMappings)
            {
                mapping.Scene = currentScene;
            }

            string json = JsonArrayHelper.ToJson(regionMappings, true);
            string path = GetMappingPath(currentScene);
            System.IO.File.WriteAllText(path, json);

            // Calculate and save discovery tile count
            int discoveryTileCount = CalculateDiscoveryTileCount();
            string discoveryCountPath = GetDiscoveryCountPath(currentScene);

            // Ensure directory exists
            string directory = System.IO.Path.GetDirectoryName(discoveryCountPath);
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(discoveryCountPath, discoveryTileCount.ToString());

            Debug.Log($"Saved {discoveryTileCount} discovery tiles for {currentScene} to {discoveryCountPath}");
        }

        private void LoadMapping()
        {
            // regionsTexture = null;
            // illustrationTexture = null;
            // paintingTexture = null;
            //
            //deserialize the region mappings from json
            string path = GetMappingPath(currentScene);
            string json = System.IO.File.ReadAllText(path);

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("Load failed");
                return;
            }

            regionMappings = JsonArrayHelper.FromJson<RegionMapping>(json);

            if (regionMappings.Length != regionCoordinates.Count)
            {
                Debug.LogError($"Mapping length {regionMappings.Length} does not match region count {regionCoordinates.Count} - you probably need to create a new mapping");
                return;
            }
            ValidateMapping();
        }

        private void ChangeRegion(int delta)
        {
            int region = currentRegion;
            region += delta;
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
            highlightTexture = new Texture2D(regionsTexture.width, regionsTexture.height);

            Color[] colors = illustrationTexture.GetPixels();
            //Color[] regionColors = regionsTexture.GetPixels();

            highlightTexture.SetPixels(colors);

            // Get gate region for current mapping if it exists and is in the same scene
            int gateRegionIndex = -1;
            if (regionMappings != null && regionMappings.IsValidIndex(currentRegion))
            {
                var currentMapping = regionMappings[currentRegion];
                if (currentMapping.IsGated && currentMapping.GateScene == currentScene)
                {
                    gateRegionIndex = currentMapping.GateRegion;
                }
            }

            if (validationStates != null && validationStates.IsValidIndex(currentRegion))
            {
                ValidationState validationState = validationStates[currentRegion];

                int region;

                for (int x = 0; x < highlightTexture.width; x++)
                {
                    for (int y = 0; y < highlightTexture.height; y++)
                    {
                        region = regionMap[x, y];

                        if (!validationStates.IsValidIndex(region))
                        {
                            continue;
                        }

                        validationState = validationStates[region];

                        if (region == currentRegion)
                        {
                            highlightTexture.SetPixel(x, y, Color.white);
                        }
                        else if (region == gateRegionIndex)
                        {
                            // Highlight gate region in turquoise/cyan
                            highlightTexture.SetPixel(x, y, Color.cyan);
                        }
                        else
                        {
                            switch (validationState)
                            {
                                case ValidationState.Unlocalised:
                                case ValidationState.Localised_OK:
                                    highlightTexture.SetPixel(x, y, colors[y * highlightTexture.width + x]);
                                    break;
                                case ValidationState.Ignored:
                                    highlightTexture.SetPixel(x, y, Color.black);
                                    break;
                                case ValidationState.Localised_Missing:
                                case ValidationState.No_Coordinates:
                                    highlightTexture.SetPixel(x, y, Color.red);
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }
                        }
                    }
                }
            }

            highlightTexture.Apply();
        }

        private void ValidateAllDifficulties()
        {
            LoadMapping();
            
            StringBuilder report = new StringBuilder();
            report.AppendLine($"=== CLUE VALIDATION REPORT FOR {currentScene} ===");
            report.AppendLine();

            // Check for duplicate clues between difficulty levels
            report.AppendLine("--- CLUE DUPLICATION CHECK ---");
            ValidateClueUniqueness(report);
            report.AppendLine();

            // Generate clue visualization files for each region
            report.AppendLine("--- CLUE VISUALIZATION GENERATION ---");
            GenerateRegionClueFiles(report);
            report.AppendLine();

            for (int difficulty = 0; difficulty <= 4; difficulty++)
            {
                report.AppendLine($"--- DIFFICULTY {difficulty} ---");
                ValidateDifficultyInternal(difficulty, report);
                report.AppendLine();
            }

            // Write to disk
            string reportContent = report.ToString();
            string reportPath = GetValidationReportPath(currentScene);

            try
            {
                // Ensure directory exists
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
            report.AppendLine($"=== CLUE VALIDATION REPORT FOR {currentScene} - DIFFICULTY {difficulty} ===");
            report.AppendLine();

            ValidateDifficultyInternal(difficulty, report);

            Debug.Log(report.ToString());
        }

        private void ValidateDifficultyInternal(int difficulty, StringBuilder report)
        {
            string clueFilePath = GetClueFilePath(currentScene, difficulty);

            if (!System.IO.File.Exists(clueFilePath))
            {
                report.AppendLine($"ERROR: Clue file not found at {clueFilePath}");
                return;
            }

            try
            {
                // Load clues
                int[,] clues = LoadCluesFromFile(clueFilePath);

                // Create solver
                MosaicSolver solver = new MosaicSolver(clues.GetLength(0), clues.GetLength(1));

                // Get discovery regions
                List<int> discoveryRegions = GetDiscoveryRegions();

                if (discoveryRegions.Count == 0)
                {
                    report.AppendLine("No discovery regions found.");
                    return;
                }

                int solvableRegions = 0;
                int totalAdvancedDeductions = 0;
                int totalAdvancedChanges = 0;
                int totalSimpleChanges = 0;
                List<string> unsolvedRegions = new List<string>();

                foreach (int regionIndex in discoveryRegions)
                {
                    SolveResult result = solver.TrySolveSingleRegion(clues, regionMap, regionIndex, difficulty > 0);

                    if (result.Finished || result.RemainingTiles == 0)
                    {
                        solvableRegions++;
                        totalAdvancedDeductions += result.AdvancedDeductions;
                        totalAdvancedChanges += result.AdvancedChanges;
                        totalSimpleChanges += (result.NumTilesAttempted - result.AdvancedDeductions);
                    }
                    else
                    {
                        unsolvedRegions.Add($"Region {regionIndex}");
                    }
                }

                // Calculate statistics
                float solvabilityPercentage = discoveryRegions.Count > 0 ? (float)solvableRegions / discoveryRegions.Count * 100f : 0f;
                int totalChanges = totalSimpleChanges + totalAdvancedChanges;
                float advancedPercentage = totalChanges > 0 ? (float)totalAdvancedChanges / totalChanges * 100f : 0f;

                // Report results
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
                report.AppendLine($"Result: {(allSolvable ? "✓ ALL REGIONS SOLVABLE" : "✗ SOME REGIONS UNSOLVABLE")}");
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"ERROR: {ex.Message}");
            }
        }

        private string GetClueFilePath(StoryScene scene, int difficulty)
        {
            string resourcesPath = $"clues/{scene.ToString().ToLower()}/";
            string difficultyFileName = GetClueFileForDifficulty(difficulty);
            return $"{Application.dataPath}/Resources/{resourcesPath}{difficultyFileName}.txt";
        }

        private int GetTotalDeductionsFromResult(SolveResult result)
        {
            // Count tiles that were solved (not empty or remaining)
            int totalTiles = result.BoardState.Width * result.BoardState.Height;
            return totalTiles - result.RemainingTiles;
        }

        private string GetClueFileForDifficulty(int difficulty)
        {
            switch (difficulty)
            {
                case 0:
                    return "clues";
                case 1:
                    return "clues_medium";
                case 2:
                    return "clues_challenging";
                case 3:
                    return "clues_expert";
                case 4:
                    return "clues_master";
                default:
                    throw new System.Exception("Unsupported difficulty " + difficulty);
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
                // Load all clue files for this scene
                Dictionary<int, int[,]> allClues = new Dictionary<int, int[,]>();

                for (int difficulty = 0; difficulty <= 4; difficulty++)
                {
                    string clueFilePath = GetClueFilePath(currentScene, difficulty);
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

                // Get discovery regions to check
                List<int> discoveryRegions = GetDiscoveryRegions();
                bool foundIdenticalRegions = false;

                // Compare each pair of difficulties
                foreach (var kvp1 in allClues)
                {
                    int difficulty1 = kvp1.Key;
                    int[,] clues1 = kvp1.Value;

                    foreach (var kvp2 in allClues)
                    {
                        int difficulty2 = kvp2.Key;
                        int[,] clues2 = kvp2.Value;

                        // Skip if same difficulty or already compared this pair
                        if (difficulty1 >= difficulty2) continue;

                        // Check each discovery region for identical clues
                        
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
                    report.AppendLine("✓ No identical clues found between difficulty levels");
                }
                else
                {
                    report.AppendLine("✗ Identical clues detected - this may indicate insufficient difficulty differentiation");
                }
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"ERROR during clue uniqueness check: {ex.Message}");
            }
        }

        private int CountRegionClueDifferences(int[,] clues1, int[,] clues2, int regionIndex)
        {
            // Check if clue arrays have same dimensions
            if (clues1.GetLength(0) != clues2.GetLength(0) || clues1.GetLength(1) != clues2.GetLength(1))
            {
                return -1; // Cannot compare different sized arrays
            }

            int differences = 0;

            // Count differences in all tiles in this region
            for (int x = 0; x < regionMap.GetLength(0); x++)
            {
                for (int y = 0; y < regionMap.GetLength(1); y++)
                {
                    if (regionMap[x, y] == regionIndex)
                    {
                        // Check bounds for clue arrays
                        if (x >= clues1.GetLength(0) || y >= clues1.GetLength(1) ||
                            x >= clues2.GetLength(0) || y >= clues2.GetLength(1))
                        {
                            differences++; // Count out-of-bounds as a difference
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

        private void GenerateRegionClueFiles(StringBuilder report)
        {
            try
            {
                // Load all clue files for this scene
                Dictionary<int, int[,]> allClues = new Dictionary<int, int[,]>();

                for (int difficulty = 0; difficulty <= 4; difficulty++)
                {
                    string clueFilePath = GetClueFilePath(currentScene, difficulty);
                    if (System.IO.File.Exists(clueFilePath))
                    {
                        allClues[difficulty] = LoadCluesFromFile(clueFilePath);
                    }
                }

                if (allClues.Count == 0)
                {
                    report.AppendLine("No clue files found to generate visualizations.");
                    return;
                }

                // Get discovery regions to process
                List<int> discoveryRegions = GetDiscoveryRegions();
                int filesGenerated = 0;

                foreach (int regionIndex in discoveryRegions)
                {
                    foreach (var kvp in allClues)
                    {
                        int difficulty = kvp.Key;
                        int[,] clues = kvp.Value;

                        string regionClueContent = GenerateRegionDifficultyVisualization(regionIndex, difficulty, clues);
                        string regionFilePath = GetRegionDifficultyFilePath(currentScene, regionIndex, difficulty);

                        try
                        {
                            // Ensure directory exists
                            string directory = System.IO.Path.GetDirectoryName(regionFilePath);
                            if (!System.IO.Directory.Exists(directory))
                            {
                                System.IO.Directory.CreateDirectory(directory);
                            }

                            System.IO.File.WriteAllText(regionFilePath, regionClueContent);
                            filesGenerated++;
                        }
                        catch (System.Exception ex)
                        {
                            report.AppendLine($"ERROR writing region {regionIndex} difficulty {difficulty} clue file: {ex.Message}");
                        }
                    }
                }

                report.AppendLine($"Generated {filesGenerated} region clue visualization files");
            }
            catch (System.Exception ex)
            {
                report.AppendLine($"ERROR during clue visualization generation: {ex.Message}");
            }
        }

        private string GenerateRegionDifficultyVisualization(int regionIndex, int difficulty, int[,] clues)
        {
            StringBuilder content = new StringBuilder();
            content.AppendLine($"=== REGION {regionIndex} - DIFFICULTY {difficulty} ===");
            content.AppendLine($"Scene: {currentScene}");
            content.AppendLine();

            // Find the bounds of this region
            var regionBounds = GetRegionBounds(regionIndex);
            if (regionBounds == null)
            {
                content.AppendLine("ERROR: Could not determine region bounds");
                return content.ToString();
            }

            int minX = regionBounds.Value.minX;
            int maxX = regionBounds.Value.maxX;
            int minY = regionBounds.Value.minY;
            int maxY = regionBounds.Value.maxY;

            // Generate grid for this region
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x < regionMap.GetLength(0) && y < regionMap.GetLength(1) &&
                        regionMap[x, y] == regionIndex)
                    {
                        // This tile is part of our region
                        if (x < clues.GetLength(0) && y < clues.GetLength(1))
                        {
                            int clue = clues[x, y];
                            if (clue == -1)
                            {
                                content.Append(" ");
                            }
                            else
                            {
                                content.Append(clue.ToString());
                            }
                        }
                        else
                        {
                            content.Append("?");
                        }
                    }
                    else
                    {
                        // Not part of our region
                        content.Append(" ");
                    }
                }
                content.AppendLine();
            }

            return content.ToString();
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

            return foundAny ? (minX, maxX, minY, maxY) : null;
        }

        private string GetRegionClueFilePath(StoryScene scene, int regionIndex)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Board/{scenePath}/clue_visualizations/region_{regionIndex:D2}_clues.txt";
        }

        private string GetRegionDifficultyFilePath(StoryScene scene, int regionIndex, int difficulty)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Board/{scenePath}/clue_visualizations/region_{regionIndex:D2}_difficulty_{difficulty}.txt";
        }

        private string GetValidationReportPath(StoryScene scene)
        {
            string scenePath = GetRegionTexturePath(scene);
            return $"Assets/Board/{scenePath}/{scenePath}_validation_report.txt";
        }
    }
}