using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Board;
using Framework;
using Framework.Input;
using Helpers;
using InputHelpers;
using MosaicPuzzle;

using UnityEngine;
using UnityEngine.EventSystems;
using Debug = UnityEngine.Debug;

public class TileBoard : MonoBehaviour
{
    public const int MaxDifficulty = 4;
    private const float RegionRevealClickFrequency = 0.04f;
    private const float RegionRevealDuration = 0.5f;
    
    [Inject] private CameraController2D cameraController2D;
    [Inject] private RegionMappingRepository regionMappingRepository;

    // ── Host-provided delegate ─────────────────────────────────────────────────
    // Set before calling Initialise(). Returns true if the region is available to play.
    public Func<int, bool> IsRegionPlayable;

    // ── Events ─────────────────────────────────────────────────────────────────
    public event Action<int>        OnRegionCompleted;   // regionIndex — host handles story/achievements
    public event Action<int, bool>  OnRegionClicked;     // regionIndex, isSolved — zoomed-out click
    public event Action<float>      OnProgressChanged;   // normalised 0–1
    public event Action<ProgressSave> OnSaveRequested;   // host persists the save
    public event Action             OnHintRequested;     // raised by TileBoardController hint button
    public event Action<Vector2Int> OnTileClicked;       // for game-specific per-tile reactions
    public event Action<int, int, int> OnAdvancedHintShown;  // clueA, clueB, overlapCount
    public event Action             OnAdvancedHintCleared;
    public event Action<string>     OnAchievementUnlocked; // achievementID — host handles platform achievements
    public event Action<string>     OnBoardRefreshed;      // sceneId — host re-initialises scene-specific components (e.g. MarkupController)

    public IBoardSFX SFX { get; set; }

    // Called by TileBoardController when the hint button is pressed.
    public void RaiseHintRequested() => OnHintRequested?.Invoke();

    [Header("Scene Configuration")]
    [SerializeField] public string SceneId;              // e.g. "office", used for clue paths and replay/save-PNG paths
    private string ClueFolder => $"clues/{SceneId.ToLower()}/";
    [SerializeField] private bool  isNonPuzzleBoard;     // true for non-puzzle scenes (e.g. Office hub)

    [SerializeField] private Transform[] tileHighlights;
    [SerializeField] private SpriteRenderer hintHighlight;
    private SpriteRenderer hintHighlightClone;

    [Header("Advanced Hint Mini Highlights")]
    [SerializeField] private GameObject miniHighlightFirstClue;
    [SerializeField] private GameObject miniHighlightSecondClue;
    [SerializeField] private GameObject miniHighlightOverlap;
    [SerializeField] private GameObject miniHighlightUnderline;

    [Header("Region Glow")]
    [SerializeField] private Color regionGlowColor = Color.yellow;

    private readonly List<GameObject> activeClueHighlights = new List<GameObject>();
    private readonly Dictionary<Vector2Int, GameObject> activeChangeHighlights = new Dictionary<Vector2Int, GameObject>();
    private readonly HashSet<int> glowingRegions = new HashSet<int>();
    private Color[] originalDisplayColors;
    private int hoveredDoorRegion = -1;
    
    [SerializeField] private ClickableTile tilePrefab;
    
    [SerializeField] private Texture2D solutionTexture;
    [SerializeField] private Texture2D regionsTexture;
    [SerializeField] private Texture2D baseIllustration;
    [SerializeField] private Texture2D solvedIllustration;
    
    [SerializeField] private SpriteRenderer fullIllustration;
    [SerializeField] private Texture2D[] cursors;
    [SerializeField] private Vector2 cursorHotspot;

    [SerializeField] private GameObject[] normalEffects;
    [SerializeField] private GameObject[] reverseEffects;
    
    [SerializeField] private Gradient regionColorMapping;
    [SerializeField] private Gradient lockedRegionColorMapping;
    
    [Header("Tile Colors")]
    [SerializeField] private Color blackTileColor = new Color(246f/255f, 142f/255f, 86f/255f, 1f);
    [SerializeField] private Color whiteTileColor = new Color(135f/255f, 68f/255f, 76f/255f, 1f);
    
    [SerializeField] private GameObject[] hideWhenZoomedIn;
    
    private int currentDifficulty;
    
    private Sprite[] regionSprites;

    public event Action OnErrorsCleared;
    public event Action OnSetupComplete;

    //how many tiles are filled (correctly or otherwise)
    public int ProgressCount { get; private set; }
    
    //how many tiles are correctly solved
    private int SolvedCount { get; set; }
    
    //how many tiles can the player flip on the entire board
    public int MaxProgress { get; private set; }

    public int Width => boardStateTexture.Width;
    public int Height => boardStateTexture.Height;
    
    //what % of the board is filled (correctly or otherwise)
    public float ProgressNormalized {get; private set; }
    
    //what % of the puzzle is correctly solved
    public float SolvedProgressNormalized {get; private set; }

    //whether the camera is zoomed in on tiles
    public bool IsZoomedIn { get; private set; }
    
    private float SaveTimer;
    private const float SaveFrequency = 30f;
    private bool SaveDirty;

    public Vector2Int focusPos;
    private bool isPointerOverUIElement;
    private Vector2 lastMousePos;
    
    [NonSerialized] public bool ControlHeld;
    [NonSerialized] public bool AltHeld;
    [NonSerialized] public bool ShiftHeld;

    // 5, 6, 7, 9, 11, 12, 14, 16, 18, 20, 26, 29
    [SerializeField] private int[] DemoRegionIndices;
    
    private Sprite illustrationSprite;
    
    private TilePool tilePool;
    private UndoSystem undoSystem;

    private bool[,] solution;
    private int [,] clues;
    private int[,] regionMap;
    private RegionType[,] regionTypeMap;
    private bool[] solvedRegions;
    private bool[] crypticRegions;
    private int[,] replayData;
    
    private bool[,] hints;
    private bool[,] errors;
    private bool[,] solved;
    private int[,] countdownValues;
    private bool[,] cluesVisible;
    private bool[,] nextToWall;
    private bool[,] opponentTiles;
    private bool[,] crypticTiles;
    private byte[,] borderFlags;
    
    private int replayIndex;
    public int HighlightedRegion { get; private set; } = -1;
    public int RegionSize { get; private set; }
    public int RegionEmpty { get; private set; }
    public int RegionErrors { get; private set; }
    // Set by host before Initialise() if this board uses cryptic number sprites
    public bool ShowCryptic { get; set; }

    private enum EditMode
    {
        None,
        EditSolution
    }
    
    private EditMode editMode = EditMode.None;

    private readonly Dictionary<Vector2Int, ClickableTile> onScreenTiles = new();
    private RectInt prevBounds;
    private Rect prevBoundsRect;
    
    private BoardState boardStateTexture;
    private Texture2D displayTexture;

    private List<List<Vector2Int>> regions;
    private TileState drawingState = TileState.Black;
    private int drawingRegion = -1;
    private int UILayer;

    // Shimmer animation fields
    private Coroutine shimmerCoroutine;
    private bool isShimmering;
    private bool inputEnabled;

    // Public state flags
    public bool IsRevealingRegions { get; private set; }

    private void Start()
    {
        undoSystem = new UndoSystem();
        
        UILayer = LayerMask.NameToLayer("UI");
        QualitySettings.vSyncCount = 1;
        //Application.targetFrameRate = 60;

        if (baseIllustration != null)
        {
            if (regionsTexture.width != baseIllustration.width || regionsTexture.height != baseIllustration.height)
                throw new Exception("Textures must be the same size");
        }
        else
        {
            baseIllustration = new Texture2D(regionsTexture.width, regionsTexture.height);
        }

        ClickableTile.SetupWallSprites();
        ClickableTile.SetupNumberSprites();
        hintHighlight.gameObject.SetActive(false);

        // Create a clone of the hint highlight for dual tile highlighting
        CreateHintHighlightClone();

        // Hide tile highlights in office scene
        if (isNonPuzzleBoard)
        {
            foreach (Transform tileHighlight in tileHighlights)
            {
                tileHighlight.gameObject.SetActive(false);
            }
        }
    }

    public void Initialise()
    {
        Setup(regionsTexture, baseIllustration);
        
        cameraController2D.FullyZoomOut();
        cameraController2D.SetInputEnabled(false);
    }

    private void CreateHintHighlightClone()
    {
        if (hintHighlight != null)
        {
            GameObject clone = Instantiate(hintHighlight.gameObject, hintHighlight.transform.parent);
            hintHighlightClone = clone.GetComponent<SpriteRenderer>();
            hintHighlightClone.gameObject.SetActive(false);
            clone.name = "HintHighlightClone";
        }
    }

    private List<Vector2Int> GetClueAreaOfInfluence(Vector2Int cluePosition)
    {
        List<Vector2Int> area = new List<Vector2Int>();

        int region = regionMap[cluePosition.x, cluePosition.y];
        
        // Get 3x3 area around the clue position (including the clue itself)
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                Vector2Int pos = new Vector2Int(cluePosition.x + dx, cluePosition.y + dy);

                // Check if position is within board bounds
                if (boardStateTexture.IsValidCoord(pos.x, pos.y) && regionMap[pos.x, pos.y] == region)
                {
                    area.Add(pos);
                }
            }
        }

        return area;
    }

    private void SpawnMiniHighlights(Vector2Int firstClue, Vector2Int secondClue)
    {
        // Clear any existing clue highlights
        ClearClueHighlights();

        // Use the advanced solver to get the actual changes
        MosaicSolver solver = new(boardStateTexture.Width, boardStateTexture.Height);
        solver.SetBoardState(boardStateTexture);
        MosaicSolver.AdvancedSolve.Result result = solver.TrySolveAdvancedSingleRegion(clues, regionMap, regionMap[firstClue.x, firstClue.y]);

        if (result.Success)
        {
            // Get areas of influence for both clues to determine which prefab to use
            List<Vector2Int> firstArea = GetClueAreaOfInfluence(firstClue);
            List<Vector2Int> secondArea = GetClueAreaOfInfluence(secondClue);
            HashSet<Vector2Int> overlapPositions = new HashSet<Vector2Int>(firstArea);
            overlapPositions.IntersectWith(secondArea);
            
            // HashSet<Vector2Int> nonOverlapPositions = new HashSet<Vector2Int>(firstArea);
            // nonOverlapPositions.ExceptWith(secondArea);

            int clueA = clues[firstClue.x, firstClue.y];
            int clueB = clues[secondClue.x, secondClue.y];
            
            //calculate the count inside the overlap area
            int overlapCount = clueA;

            //subtract 
            foreach (var pos in firstArea)
            {
                var state = boardStateTexture.GetState(pos.x, pos.y);
                
                if(state == TileState.Black)
                    overlapCount--;
                
                //Debug.Log($"In A {pos} = {state} : overlap count = {overlapCount}");
            }
            
            // Spawn highlights only where there are actual changes
            foreach (var (x, y, state) in result.GetChanges())
            {
                if (state == TileState.Empty && !overlapPositions.Contains(new Vector2Int(x, y))) 
                    continue; // Skip positions with no change

                Vector2Int pos = new Vector2Int(x, y);

                // Check if the tile is currently empty on the board
                if (boardStateTexture.IsValidCoord(x, y))
                {
                    TileState currentState = boardStateTexture.GetState(x, y);
                    if (currentState != TileState.Empty) 
                        continue; // Skip if tile is already filled
                }

                if (activeChangeHighlights.ContainsKey(pos))
                {
                    Debug.Log($"Skipping clue highlight {pos} because it's already on the board");
                    continue;
                }

                if (overlapPositions.Contains(pos))
                {
                    SpawnMiniHighlight(pos, miniHighlightOverlap);
                }
                else if (firstArea.Contains(pos))
                {
                    SpawnMiniHighlightWithColor(pos, miniHighlightFirstClue, state);
                    
                    if(state == TileState.Black)
                        overlapCount--;
                }
                else if (secondArea.Contains(pos))
                {
                    SpawnMiniHighlightWithColor(pos, miniHighlightSecondClue, state);
                }
                
                SpawnMiniHighlight(firstClue, miniHighlightUnderline);
                SpawnMiniHighlight(secondClue, miniHighlightUnderline);

            }

            if (MosaicPrivacyAndSettings.GetCountdownMode())
            {
                clueA = countdownValues[firstClue.x, firstClue.y];
                clueB = countdownValues[secondClue.x, secondClue.y];
            }

            OnAdvancedHintShown?.Invoke(clueA, clueB, overlapCount);
        }
    }

    private void SpawnMiniHighlight(Vector2Int position, GameObject prefab)
    {
        if (prefab == null) 
            return;

        GameObject highlight = Instantiate(prefab, transform);
        highlight.transform.position = new Vector3(position.x, position.y, ClickableTile.GetZOffset(position) - 3f);
        activeClueHighlights.Add(highlight);
    }

    private void SpawnMiniHighlightWithColor(Vector2Int position, GameObject prefab, TileState targetState)
    {
        if (prefab == null) 
            return;

        GameObject highlight = Instantiate(prefab, transform);
        highlight.transform.position = new Vector3(position.x, position.y, ClickableTile.GetZOffset(position) - 3f);

        // Tint the highlight to match the target tile state color
        SpriteRenderer spriteRenderer = highlight.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Color tintColor = GetTileStateColor(targetState);
            spriteRenderer.color = tintColor;
        }

        activeChangeHighlights[position] = highlight;
    }

    private Color GetTileStateColor(TileState state)
    {
        switch (state)
        {
            case TileState.Black:
                return GetBlackTileColor();
            case TileState.White:
                return GetWhiteTileColor();
            default:
                return Color.white; // Default color for empty or other states
        }
    }
    
    public void Setup(Texture2D regionTexture, Texture2D illustratedTexture)
    {
        int width = regionTexture.width;
        int height = regionTexture.height;
        
        // Board state is always initialised clean here.
        // The host applies saved progress via ApplySaveData() after Initialise() returns.
        boardStateTexture = CreateNewBoardStateTexture(width, height);

        displayTexture = CreateDisplayTexture(illustratedTexture);

        replayData = new int[width, height];
        LoadReplay();
        

        if (regions == null)
        {
            regions = CreateRegionCoords(regionTexture);
        }

        nextToWall = new bool[width, height];
        solvedRegions = new bool[regions.Count];
        
        regionMap = CreateRegionMap(width, height, regions);
        regionTypeMap = regionMappingRepository.CreateRegionTypeMap(SceneId, regionMap);
        crypticRegions = regionMappingRepository.CreateCrypticRegionMap(SceneId);

        borderFlags = new byte[regionTexture.width, regionTexture.height];

        for (int x = 0; x < regionTexture.width; x++)
        {
            for (int y = 0; y < regionTexture.height; y++)
            {
                if (y == 0 || regionMap[x,y-1] != regionMap[x, y]) //s
                    borderFlags[x, y] |= 0b0100;
                if (x == regionTexture.width - 1 || regionMap[x+1,y] != regionMap[x, y]) //e
                    borderFlags[x, y] |= 0b0010;
                if (y == regionTexture.height - 1 || regionMap[x,y+1] != regionMap[x, y]) //s 
                    borderFlags[x, y] |= 0b0001;
                if (x == 0 || regionMap[x-1,y] != regionMap[x, y]) //w
                    borderFlags[x, y] |= 0b1000;
            }
        }
        
        regionSprites = CreateRegionSprites(regions, illustratedTexture);
        
        solution = ParseSolution(solutionTexture);

        CheckForPartiallySolvedRegions();
        
        CalculateProgress();

        //ProgressNormalized = (float)ProgressCount / MaxProgress;
        
        illustrationSprite = Sprite.Create(displayTexture, new Rect(0, 0, displayTexture.width, displayTexture.height), Vector2.one * 0.5f);
        fullIllustration.sprite = illustrationSprite;
        
        if (tilePool == null)
        {
            // Office scene doesn't need tiles, so use size 0
            int poolSize = isNonPuzzleBoard ? 0 : 4096;
            tilePool = new TilePool(transform, tilePrefab, poolSize);
        }
        
        // Clear history when setting up a new board
        undoSystem.ClearHistory();
        
        currentDifficulty = MosaicPrivacyAndSettings.GetDifficulty();
        
        if(currentDifficulty < 0)
            currentDifficulty = 0;

        // Office scene doesn't need clues - use blank array
        if (isNonPuzzleBoard)
        {
            clues = CreateBlankCluesArray(width, height);
        }
        else
        {
            try
            {
                clues = LoadCluesFromDisk(currentDifficulty);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error loading clues: {e.Message}");

                if(Application.isEditor)
                    DebugRegenFullClueSet();
            }
        }

        int r = -1;
        
        opponentTiles = new bool[width, height];
        crypticTiles = new bool[width, height];

        List<int> hiddenRegions = new List<int>();
        
        foreach (List<Vector2Int> regionCoords in regions)
        {
            r++;
            
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, r);
            bool shouldHide = false;

            if (mapping.Type is RegionType.Empty or RegionType.Door or RegionType.Walkable)
            {
                shouldHide = true;
            }
            else if (mapping.Type == RegionType.Discovery && !IsRegionUnlocked(r))
            {
                // Hide locked discovery regions
                shouldHide = true;
            }

#if DEMO
            if(!DemoRegionIndices.Contains(r))
                shouldHide = true;
#endif

            if (shouldHide)
            {
                hiddenRegions.Add(r);
            }

            if (mapping.IsOpponentRegion)
            {
                foreach (Vector2Int pos in regionCoords)
                {
                    opponentTiles[pos.x, pos.y] = true;
                }
            }

            if (mapping.IsCrypticRegion)
            {
                foreach (Vector2Int pos in regionCoords)
                {
                    crypticTiles[pos.x, pos.y] = true;
                }
            }
        }
        
        HideRegions(hiddenRegions.ToArray());
        
        cluesVisible = new bool[width, height];
        cluesVisible.Populate(true);
        
        cameraController2D.SetBounds(new Vector2(width, height));

        SetupHintsAndErrors();
        
        GrantRegionAchievements();

        GrantProgressAchievements();
        GrantEdgeAchievements();
        
        ForceRefreshBoard();

        SetHighlightBrightness(MosaicPrivacyAndSettings.GetHighlightBrightness());
        
        //todo: Fix. this was sometimes causing the head area to get re-hidden
        // Hide Forest region 4 until the first cutscene has been played
        // if (false /* REMOVED: scene-specific forest check */ && !CutsceneProgressManager.Instance.HasCutsceneBeenPlayed("forest_intro"))
        // {
        //     HideRegion(4);
        //     RefreshDisplayTexture();
        // }

        // Start the shimmer animation
        if (shimmerCoroutine != null)
        {
            StopCoroutine(shimmerCoroutine);
        }
        shimmerCoroutine = StartCoroutine(ShimmerAnimationCoroutine());

        // ShowCryptic is set by the host before Initialise() is called — no modification needed here.

        OnSetupComplete?.Invoke();
    }

    public void CompleteLinkedRegion(int regionIndex)
    {
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if (regionMap[x, y] == regionIndex)
                {
                    boardStateTexture.SetState(x,y, TileState.Linked);
                }
            }
        }
        
        solvedRegions[regionIndex] = true;
    }
    
    public void SolveRegion(int regionIndex)
    {
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if (regionMap[x, y] == regionIndex)
                {
                    boardStateTexture.SetState(x,y, solution[x,y] ? TileState.SolvedBlack : TileState.SolvedWhite);
                }
            }
        }
        
        solvedRegions[regionIndex] = true;
    }

    public bool GetRegionSolvedStatus(int regionIndex)
    {
        if (regionIndex < 0 || regionIndex >= solvedRegions.Length)
            return false;

        return solvedRegions[regionIndex];
    }

    public bool[] GetSolvedRegions()
    {
        if(solvedRegions == null)
            throw new Exception("Solved regions not initialised");
        
        return solvedRegions;
    }

    private bool IsRegionUnlocked(int regionIndex)
    {
        RegionMapping regionMapping = regionMappingRepository.GetRegionMapping(SceneId, regionIndex);
        return IsRegionPlayable?.Invoke(regionIndex) ?? false;
    }

    public bool IsOpponentTile(int x, int y)
    {
        if (opponentTiles == null || x < 0 || y < 0 || x >= opponentTiles.GetLength(0) || y >= opponentTiles.GetLength(1))
            return false;

        return opponentTiles[x, y];
    }

    private bool IsCrypticTile(int x, int y)
    {
        if (crypticTiles == null || x < 0 || y < 0 || x >= crypticTiles.GetLength(0) || y >= crypticTiles.GetLength(1))
            return false;

        if(ShowCryptic)
            return crypticTiles[x, y];
        
        // First check if this tile is supposed to be cryptic
        if (!crypticTiles[x, y])
            return false;

        // At difficulty 0: 1 in 4 tiles (modulo 4)
        // At difficulty 4: all tiles (no modulo check)
        if (currentDifficulty >= MaxDifficulty)
            return true;
        
        // Scale from modulo 4 (difficulty 0) to modulo 1 (difficulty 3)  
        int modulo = (MaxDifficulty+1) - currentDifficulty;
        return (x + y) % modulo == 0;
    }

    private bool IsCrypticRegion(int regionIndex)
    {
        if (crypticRegions == null || regionIndex < 0 || regionIndex >= crypticRegions.Length)
            return false;

        return crypticRegions[regionIndex];
    }
    
    public void HideRegions(params int[] regions)
    {
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                int r = regionMap[x,y];

                if (regions.Contains(r))
                {
                    boardStateTexture.SetState(x,y, TileState.Hidden);
                    displayTexture.SetPixel(x,y,baseIllustration.GetPixel(x,y));
                }
            }
        }
        
        displayTexture.Apply();
    }
    
    public void HideRegion(int regionIndex)
    {
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if (regionMap[x, y] == regionIndex)
                {
                    boardStateTexture.SetState(x,y, TileState.Hidden);
                    displayTexture.SetPixel(x,y,baseIllustration.GetPixel(x,y));
                }
            }
        }
        
        displayTexture.Apply();
    }

    public static int[,] CreateRegionMap(int width, int height, List<List<Vector2Int>> regionCoords)
    {
        int[,] map = new int[width, height];
        map.Populate(-1);
        
        for (var i = 0; i < regionCoords.Count; i++)
        {
            var region = regionCoords[i];
            foreach (Vector2Int pos in region)
            {
                map[pos.x, pos.y] = i;
            }
        }

        return map;
    }

    private void CheckForPartiallySolvedRegions()
    {
        foreach (List<Vector2Int> regionPositions in regions)
        {
            bool regionSolved = false;
            
            foreach (Vector2Int pos in regionPositions)
            {
                if (boardStateTexture.IsSolved(pos.x, pos.y))
                {
                    regionSolved = true;
                    break;
                }
            }

            if (regionSolved)
            {
                foreach (Vector2Int pos in regionPositions)
                {
                    boardStateTexture.SetState(pos.x, pos.y, solution[pos.x, pos.y] ? 
                        TileState.SolvedBlack : TileState.SolvedWhite);
                }
            }
        }
    }

    private void CalculateProgress()
    {
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        ProgressCount = 0;
        SolvedCount = 0;
        MaxProgress = 0;

        int debugDiscoveryCount = 0;
        int debugWalkableCount = 0;
        int debugDoorCount = 0;
        int debugEmptyCount = 0;
        int debugDemoCount = 0;
        
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                TileState state = boardStateTexture.GetState(i, j);
                RegionType type = regionTypeMap[i, j];

                switch (type)
                {
                    case RegionType.Empty:
                        debugEmptyCount++;
                        break;
                    case RegionType.Discovery:
                        debugDiscoveryCount++;
                        break;
                    case RegionType.Door:
                        debugDoorCount++;
                        break;
                    case RegionType.Walkable:
                        debugWalkableCount++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (type == RegionType.Discovery)
                {
                    if (Defines.IsDemo())
                    {
                        int region = regionMap[i, j];

                        if (!DemoRegionIndices.Contains(region))
                            continue;

                        debugDemoCount++;
                    }

                    MaxProgress++;

                    if (state == TileState.Hidden)
                    {
                        //do nowt
                    }
                    else if (state != TileState.Empty)
                    {
                        ProgressCount++;

                        if (!IsMistake(state, i, j))
                            SolvedCount++;
                    }
                }
            }
        }
        
        Debug.Log($"Debug region info: {debugDemoCount} demo, {debugDiscoveryCount} discovery, {debugWalkableCount} walkable, {debugDoorCount} door, {debugEmptyCount} empty");
        Debug.Log($"Debug progress: {ProgressCount} / {MaxProgress} ({SolvedCount} solved)");
        
        ProgressNormalized = (float)ProgressCount / MaxProgress;
        SolvedProgressNormalized = (float)SolvedCount / MaxProgress;
    }

    public static Sprite[] CreateRegionSprites(List<List<Vector2Int>> regionMap, Texture2D illustration)
    {
        Sprite[] sprites = new Sprite[regionMap.Count];

        for (int i = 0; i < regionMap.Count; i++)
        {
            List<Vector2Int> region = regionMap[i];

            if (region.Count == 0)
            {
                Debug.LogError($"Empty region {i}, sounds like your region mapping is fucked bro");
                continue;
            }
            
            int xMin = region.Min(p => p.x);
            int xMax = region.Max(p => p.x);
            int yMin = region.Min(p => p.y);
            int yMax = region.Max(p => p.y);
            
            int w = xMax - xMin + 1;
            int h = yMax - yMin + 1;
            
            Texture2D spriteTexture = new Texture2D(w,h);

            spriteTexture.filterMode = FilterMode.Point;

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    spriteTexture.SetPixel(x, y, illustration.GetPixel(x+xMin, y+yMin));
                }
            }
            
            // foreach (var pos in region)
            // {
            //     spriteTexture.SetPixel(pos.x - xMin, pos.y - yMin, illustrationTexture.GetPixel(pos.x, pos.y));
            // }
            
            spriteTexture.Apply();
            
            sprites[i] = Sprite.Create(spriteTexture, new Rect(0, 0, w, h), Vector2.one * 0.5f);
        }

        return sprites;
    }

#if MEGA_MOSAIC
    private bool IsPartOfSolutionForAnotherRegion(int x, int y)
    {
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                if (x + i < 0 || x + i >= boardStateTexture.Width)
                    continue;
                
                if (y + j < 0 || y + j >= boardStateTexture.Height)
                    continue;

                int neighbourRegion = regionMap[x + i, y + j];
                
                //neighbour is a wall tile
                if (neighbourRegion < 0)
                    continue;
                
                // if(neighbourRegion == region)
                //     continue;
                
                //has unsolved neighbour
                if (!solvedRegions[neighbourRegion])
                    return true;
            }
        }

        return false;
    }
#endif
    
    private bool[,] FindNextToWalls()
    {
        #if MEGA_MOSAIC
        //for each region -1
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if (IsPartOfSolutionForAnotherRegion(x,y))
                {
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            if (x + i < 0 || x + i >= boardStateTexture.Width) 
                                continue;
                            
                            if (y + j < 0 || y + j >= boardStateTexture.Height) 
                                continue;
                            
                            nextToWallPositions[x+i, y+j] = true;
                        }
                    }
                }
            }
        }


        return nextToWallPositions;
#else
        //not required in regional solve mode
        return new bool[boardStateTexture.Width, boardStateTexture.Height];
#endif
    }

    private void GrantRegionAchievements()
    {
        // Achievement ID generation is now handled by the host via OnRegionCompleted.
        // This method only handles the board-side clue hiding.
        int[] newlyCompletedRegions = CheckRegionCompletion().ToArray();

        foreach (int regionID in newlyCompletedRegions)
        {
            HideCluesInRegion(regionID);
        }
    }
    
    private void GrantEdgeAchievements()
    {
        //check that the north, south, west and east edges of the board are complete
        bool northComplete = true;
        bool southComplete = true;
        bool westComplete = true;
        bool eastComplete = true;
        
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            TileState southState = boardStateTexture.GetState(x, 0);
            if (southState == TileState.Empty || southState.IsBlack() != solution[x, 0])
            {
                southComplete = false;
            }

            TileState northState = boardStateTexture.GetState(x, boardStateTexture.Height - 1);
            if (northState == TileState.Empty || northState.IsBlack() != solution[x, boardStateTexture.Height - 1])
            {
                northComplete = false;
            }
        }
        
        for (int y = 0; y < boardStateTexture.Height; y++)
        {
            TileState westState = boardStateTexture.GetState(0, y);
            if (westState == TileState.Empty || westState.IsBlack() != solution[0, y])
            {
                westComplete = false;
            }

            TileState eastState = boardStateTexture.GetState(boardStateTexture.Width - 1, y);
            if (eastState == TileState.Empty || eastState.IsBlack() != solution[boardStateTexture.Width - 1, y])
            {
                eastComplete = false;
            }
        }
        
        if (northComplete)
            MarkAchievementsComplete("EDGE_NORTH");
        
        if (southComplete)
            MarkAchievementsComplete("EDGE_SOUTH");
        
        if (westComplete)
            MarkAchievementsComplete("EDGE_WEST");
        
        if (eastComplete)
            MarkAchievementsComplete("EDGE_EAST");
    }

    private void HideCluesInRegion(int regionID)
    {
        var regionTiles = regions[regionID];

        foreach (var pos in regionTiles)
        {
            if(nextToWall[pos.x, pos.y] == false)
            {
                cluesVisible[pos.x, pos.y] = false;

                // Update any existing on-screen tiles immediately
                if (onScreenTiles.TryGetValue(pos, out var tile))
                {
                    tile.SetClueVisible(false);
                }
            }
        }
    }

    public void SetupHintsAndErrors()
    {
        ProgressCount = 0;
        SolvedCount = 0;

        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        hints = new bool[width, height];
        errors = new bool[width, height];
        solved = new bool[width, height];
        countdownValues = new int[width, height];

        // Initialize countdown values based on original clues minus existing black tiles nearby
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                countdownValues[x, y] = clues[x, y];

                int region = regionMap[x, y];
                // If this is a clue tile and countdown mode is enabled, adjust for existing black tiles
                if (clues[x, y] >= 0 && MosaicPrivacyAndSettings.GetCountdownMode())
                {
                    int adjacentBlackTiles = 0;

                    // Count adjacent black tiles
                    for (int i = -1; i <= 1; i++)
                    {
                        for (int j = -1; j <= 1; j++)
                        {
                            int nx = x + i;
                            int ny = y + j;
                            
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                if(regionMap[nx,ny] != region)
                                    continue;
                                
                                TileState state = boardStateTexture.GetState(nx, ny);
                                if (state == TileState.Black || state == TileState.SolvedBlack)
                                {
                                    adjacentBlackTiles++;
                                }
                            }
                        }
                    }

                    countdownValues[x, y] = clues[x, y] - adjacentBlackTiles;
                }
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileState state = boardStateTexture.GetState(x, y);

                if (state == TileState.Hidden)
                {
                    continue;
                }
                
                if (state != TileState.Empty && regionTypeMap[x, y] == RegionType.Discovery)
                {
                    ProgressCount++;
                    
                    if(!IsMistake(state, x, y))
                        SolvedCount++;
                }

                bool error = CheckError(x, y, out bool hint, out bool clueSolved);

                errors[x, y] = error;
                hints[x, y] = hint;
                solved[x, y] = clueSolved;
            }
        }
        
        ProgressNormalized = (float)ProgressCount / MaxProgress;
        SolvedProgressNormalized = (float)SolvedCount / MaxProgress;
    }
    
    GenerationResult lastResult;
    
    private void OnGUI()
    {
        if (boardStateTexture == null || solution == null)
            return;
        
        int maxClues = solution.GetLength(0) * solution.GetLength(1);

        if (lastResult.InProgress)
        {
            float perClue = elapsedTime / lastResult.CluesPlaced;
            float remaining = perClue * (maxClues - lastResult.CluesPlaced);
            GUI.Label(new Rect(10, Screen.height - 60, 400, 60),
                $"Progress: {lastResult.CluesPlaced} placed, {lastResult.NumSteps} open\n{elapsedTime:0.00}/{remaining:0.00} seconds elapsed");
        }
    }

    private float elapsedTime;

    public IEnumerator PruneRegions()
    {
        int width = solution.GetLength(0);
        int height = solution.GetLength(1);
        
        MosaicGenerator generator = new(width, height);
        
        GenerationResult result = new();
        int steps = 0;
        
        float startTime = Time.realtimeSinceStartup;
        float lastTime = Time.realtimeSinceStartup;
        
        foreach (var r in generator.PruneRegions(solution, regionMap, clues))
        {
            lastResult = r;
            steps++;
            elapsedTime = Time.realtimeSinceStartup - startTime;

            if(Time.realtimeSinceStartup - lastTime < 10f)
            {
                yield return new WaitForEndOfFrame();
                continue;
            }
            
            Debug.Log("Steps per second: "+steps);
            steps = 0;
            lastTime = Time.realtimeSinceStartup;

            result = r;

            if (result.Clues == null)
            {
                throw new Exception("Returned null clues");
            }
            
            clues = result.Clues;
            string path = GetAbsolutePathForClueFile(currentDifficulty);
            File.WriteAllText(path, MosaicSolver.GetCluesAsCSV(clues));
        }
    }

    private static float GetPctAdvancedForDifficulty(int difficulty)
    {
        //throw every advanced deduction we've got at master difficulty
        if (difficulty == 4)
            return 1f;
        
        return difficulty * 0.01f;
    }

    private string GetAbsolutePathForClueFile(int difficulty)
    {
        return $"{Application.dataPath}/Resources/{ClueFolder}{GetClueFileForDifficulty(difficulty)}.txt";
    }

    private string GetResourcesPathForClueFolder()
    {
        return ClueFolder;
    }

    private void EnsureClueDirectoryExists()
    {
        string resourcesPath = GetResourcesPathForClueFolder();
        string fullPath = $"{Application.dataPath}/Resources/{resourcesPath}";

        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            Debug.Log($"Created clue directory: {fullPath}");
        }
    }

    private static string GetClueFileForDifficulty(int difficulty)
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
                throw new Exception("Unsupported difficulty " + difficulty);
        }
    }

    private int[,] LoadCluesFromDisk(int difficulty)
    {
        if (Application.isEditor)
        {
            // In editor, load directly from file to avoid Resources caching issues
            string filePath = GetAbsolutePathForClueFile(difficulty);
            if (File.Exists(filePath))
            {
                string csvContent = File.ReadAllText(filePath);
                int[,] loadedClues = MosaicSolver.ParseCSVString(csvContent);
                return loadedClues;
            }
            else
            {
                throw new Exception($"Clue file not found: {filePath}");
            }
        }
        else
        {
            // In builds, use Resources system as normal
            string fileName = GetResourcesPathForClueFolder() + GetClueFileForDifficulty(difficulty);
            TextAsset textAsset = Resources.Load<TextAsset>(fileName);

            if (textAsset == null)
            {
                throw new Exception($"{fileName}: file not found");
            }

            string loadedString = textAsset.text;
            int[,] loadedClues = MosaicSolver.ParseCSVString(loadedString);
            return loadedClues;
        }
    }

    private static int[,] CreateBlankCluesArray(int width, int height)
    {
        int[,] blankClues = new int[width, height];
        blankClues.Populate(-1);
        return blankClues;
    }

    private bool[,] ParseSolution(Texture2D solvedTexture)
    {
        int width = solvedTexture.width;
        int height = solvedTexture.height;
        
        bool [,] solutionMap = new bool[width, height];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                solutionMap[x, y] = solvedTexture.GetPixel(x, y).r < 0.5f;
            }
        }

        return solutionMap;
    }

    private Texture2D CreateDisplayTexture(Texture2D illustratedTexture)
    {
        int width = illustratedTexture.width;
        int height = illustratedTexture.height;
        
        var tex = new Texture2D(illustratedTexture.width, illustratedTexture.height);
        tex.SetPixels(illustratedTexture.GetPixels());
        tex.filterMode = FilterMode.Point;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var state = boardStateTexture.GetState(x, y);
                if (!state.IsSolved() && state != TileState.Hidden)
                {
                    var pixelColor = state.GetColor();
                    tex.SetPixel(x, y, pixelColor);
                }
            }
        }

        tex.Apply();
        return tex;
    }

    private static BoardState CreateNewBoardStateTexture(int width, int height)
    {
        var tex = new BoardState(width, height);
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                tex.SetState(x, y, TileState.Empty);
            }
        }
        
        return tex;
    }

    public static List<List<Vector2Int>> CreateRegionCoords(Texture2D regionTexture)
    {
        List<Vector2Int> blankRegion = new List<Vector2Int>();
        List<List<Vector2Int>> regionMap = new List<List<Vector2Int>>();
        regionMap.Add(blankRegion);
        
        RectInt bounds = new(0, 0, regionTexture.width, regionTexture.height);
        
        bool[,] visited = new bool[regionTexture.width, regionTexture.height];
        
        var pixels = regionTexture.GetPixels();
        int width = regionTexture.width;
        
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (visited[pos.x, pos.y])
                continue;

            if (pixels[pos.x + pos.y * width] == Color.black)
            {
                blankRegion.Add(pos);
                continue;
            }
            
            List<Vector2Int> region = FloodFill(regionTexture, pos);

            if (region.Any())
            {
                regionMap.Add(region);
                
                for (int i = 0; i < region.Count; i++)
                {
                    visited[region[i].x, region[i].y] = true;
                }
            }

            //Debug.Log($"Floodfill region {regionMap.Count} at {pos} of size {region.Count}");
        }
        
        Debug.Log("Num regions: " + regionMap.Count);

        if (regionMap.Any())
        {
            Debug.Log("Largest region: " + regionMap.Max(r => r.Count));
            Debug.Log("Smallest region: " + regionMap.Min(r => r.Count));
        }

        if (blankRegion.Count == 0)
        {
            Debug.Log("No blank regions found - are your sure your black areas are really black?");
        }
        
        return regionMap;
    }

    public static List<Vector2Int> FloodFill(Texture2D regionTexture, Vector2Int origin)
    {
        Color originColor = regionTexture.GetPixel(origin.x, origin.y);
        List<Vector2Int> region = new List<Vector2Int>();
        bool[,] visited = new bool[regionTexture.width, regionTexture.height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);

        Color pixel;
        
        while (queue.Count > 0)
        {
            Vector2Int pos = queue.Dequeue();
            
            if (visited[pos.x, pos.y]) 
                continue;
            
            visited[pos.x, pos.y] = true;
            
            pixel = regionTexture.GetPixel(pos.x, pos.y);
            
            if (pixel != originColor) 
                continue;
            
            region.Add(pos);
            
            if(pos.x > 0 && !visited[pos.x - 1, pos.y]) 
                queue.Enqueue(new Vector2Int(pos.x - 1, pos.y));
            
            if(pos.x < regionTexture.width - 1 && !visited[pos.x + 1, pos.y])  
                queue.Enqueue(new Vector2Int(pos.x + 1, pos.y));
            
            if(pos.y > 0 && !visited[pos.x, pos.y - 1]) 
                queue.Enqueue(new Vector2Int(pos.x, pos.y - 1));
            
            if(pos.y < regionTexture.height - 1 && !visited[pos.x, pos.y + 1]) 
                queue.Enqueue(new Vector2Int(pos.x, pos.y + 1));
        }
        return region;
    }

    private void Update()
    {
        //wait until Setup() has run
        if(boardStateTexture == null || tilePool == null)
            return;
        
        //todo: only recalculate if the focusPos has changed
        UpdateHighlightedRegion(focusPos);
        
        RectInt newScreenBounds = cameraController2D.GetCameraBounds();

        if (!newScreenBounds.Equals(prevBounds))
        {
            Rect newBoundsRect = cameraController2D.GetCameraBoundsRect();
            Vector2 sizeDelta = newBoundsRect.size - prevBoundsRect.size;
            prevBoundsRect = newBoundsRect;
            
            Vector2Int delta = Vector2Int.RoundToInt(newScreenBounds.center - prevBounds.center);
         
            bool isZoomingWithMouseWheel = ControlSchemeSwapper.currentControlScheme is 
                                               ControlScheme.KeyboardAndMouse or ControlScheme.Touch &&
                                           sizeDelta.sqrMagnitude > 0; 
            
            RebuildScreen(newScreenBounds);
            
            //todo: don't move the cursor if we're zooming but the mouse is still onscreen
            if (inputEnabled && //skip moving cursor in markup mode
                !cameraController2D.IsPanning &&
                !cameraController2D.IsTouchDragging &&
                !cameraController2D.IsClickDragging &&
                !isZoomingWithMouseWheel)
            {
                focusPos += delta;

                if (!newScreenBounds.Contains(focusPos))
                {
                    if (focusPos.x < newScreenBounds.xMin)
                        focusPos.x = newScreenBounds.xMin;
                    if (focusPos.x >= newScreenBounds.xMax)
                        focusPos.x = newScreenBounds.xMax - 1;
                    if (focusPos.y < newScreenBounds.yMin)
                        focusPos.y = newScreenBounds.yMin;
                    if (focusPos.y >= newScreenBounds.yMax)
                        focusPos.y = newScreenBounds.yMax - 1;

                    Debug.Log($"Nudged focus pos to {focusPos}");
                }

                MoveHighlightTo(focusPos);
            }
        }

        if (Defines.IsDevelopment() || Application.isEditor)
        {
            ClickableTile tile = onScreenTiles.GetValueOrDefault(focusPos);

            if (boardStateTexture.IsValidCoord(focusPos.x, focusPos.y))
            {
                TileState textureState = boardStateTexture.GetState(focusPos.x, focusPos.y);
                TileState solutionState = solution[focusPos.x, focusPos.y] ? TileState.Black : TileState.White;
                DebugString = $"{focusPos} {tile?.CurrentState} {textureState} {solutionState}";
            }
        }
        
        SaveTimer += Time.deltaTime;

        if(SaveDirty && SaveTimer > SaveFrequency)
        {
            SaveProgress();
            SaveReplay();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (Application.isMobilePlatform && !hasFocus && SaveTimer > 0)
        {
            Debug.Log("OnApplicationFocus: Saving "+SaveTimer);
            SaveImmediate();
        }
    }

    private void OnApplicationQuit()
    {
        if (SaveTimer > 0)
        {
            Debug.Log("OnApplicationQuit: Saving "+SaveTimer);
            SaveImmediate();
        }
    }

    private Vector2Int[] GetRegionNeighbours(Vector2Int pos)
    {
        Vector2Int[] neighbours = new Vector2Int[9];
        neighbours.Populate(new Vector2Int(-3000, -3000));
        
        if(pos.x < 0 || pos.x >= boardStateTexture.Width || pos.y < 0 || pos.y >= boardStateTexture.Height)
            return neighbours;
        
        int region = regionMap[pos.x, pos.y];
        int index = -1;

        //the highlights are assigned from bottom-left to top-right in the inspector
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                index++;
                Vector2Int neighbour = pos + new Vector2Int(i, j);
                if (neighbour.x < 0 || neighbour.x >= boardStateTexture.Width)
                    continue;
                
                if (neighbour.y < 0 || neighbour.y >= boardStateTexture.Height)
                    continue;
                
                if(regionMap[neighbour.x, neighbour.y] != region)
                    continue;
                
                neighbours[index] = neighbour;
            }
        }

        return neighbours.ToArray();
    }

    public Sprite GetRegionSprite(int regionID)
    {
        return regionSprites[regionID];
    }
    

    public void SwitchMode()
    {
        editMode = editMode == EditMode.None ? EditMode.EditSolution : EditMode.None;
        ForceRefreshBoard();
    }

    private void UpdateHighlightedRegion(Vector2Int focusPos)
    {
        if (focusPos.x < 0 ||
            focusPos.x >= boardStateTexture.Width ||
            focusPos.y < 0 ||
            focusPos.y >= boardStateTexture.Height) 
            return;
        
        int region = regionMap[focusPos.x, focusPos.y];

        bool crypticOn = ShowCryptic;
        bool bossfight = ShowCryptic;
        
        if (region != HighlightedRegion)
        {
            HighlightedRegion = region;
                
            RegionSize = 0;
            RegionEmpty = 0;
            RegionErrors = 0;
                
            if (HighlightedRegion < 0)
            {
                // Always show cryptic panel in FinalBoss scene if enabled, otherwise hide it
                ShowCryptic = crypticOn && bossfight;
                return;
            }

            RegionSize = regions[region].Count;

            foreach (var pos in regions[region])
            {
                if(IsMistake(pos.x, pos.y))
                    RegionErrors++;
            }

            RegionEmpty = regions[region]
                .Count(p => boardStateTexture.GetState(p.x, p.y) == TileState.Empty);
            
            ShowCryptic = false;

            if (crypticOn)
            {
                if (bossfight) //always true for FinalBoss scene
                {
                    ShowCryptic = true;
                }
                else if (IsCrypticRegion(HighlightedRegion)) //otherwise based on whether region is cryptic and unsolved
                {
                    ShowCryptic = RegionEmpty > 0;
                }
            }
        }

        // Check for door hover effect
        CheckDoorHoverEffect();
    }

    private void GrantProgressAchievements()
    {
        float solvedProgress = (float)SolvedCount / MaxProgress;

        int lastPctInt = Mathf.FloorToInt(SolvedProgressNormalized * 100);
        int newCorrectPctInt = Mathf.FloorToInt(solvedProgress * 100);

        //only assign this once we've grabbed the lastPctInt
        ProgressNormalized = (float)ProgressCount / MaxProgress;
        SolvedProgressNormalized = solvedProgress;

        if (newCorrectPctInt > lastPctInt || (MaxProgress - SolvedCount <= 0))
        {
            OnProgressChanged?.Invoke(SolvedProgressNormalized);
        }
    }

    public void MarkAchievementsComplete(string achievementID)
    {
        if (string.IsNullOrEmpty(achievementID))
            return;

        OnAchievementUnlocked?.Invoke(achievementID);
    }
    
    private IEnumerable<int> CheckRegionCompletion(bool prepareRevealAnim = false)
    {
        List<int> regionSizes = new List<int>();
        regionSizes.AddRange(regions.Select(region => region.Count));
        
        for(int x = 0; x < boardStateTexture.Width; x++)
        {
            for(int y = 0; y < boardStateTexture.Height; y++)
            {
                int region = regionMap[x, y];

                if (region < 0)
                {
                    boardStateTexture.SetState(x, y, TileState.SolvedBlack);
                    continue;
                }

                var state = boardStateTexture.GetState(x, y);
                
                if(state == TileState.Empty)
                    continue;

                bool black = solution[x, y];

                if(state.IsBlack() && black)
                {
                    regionSizes[region]--;
                }
                else if(state.IsWhite() && !black)
                {
                    regionSizes[region]--;
                }
            }
        }
        
        int solvedCount = 0;
        
        for (int i = 0; i < regionSizes.Count; i++)
        {
            if (regionSizes[i] == 0)
            {
                if(solvedRegions[i])
                    continue;

                if (!prepareRevealAnim)
                {
                    foreach (var pos in regions[i])
                    {
                        bool black = solution[pos.x, pos.y];
                        TileState state = black ? TileState.SolvedBlack : TileState.SolvedWhite;
                        boardStateTexture.SetState(pos.x, pos.y, state);
                    }
                }

                solvedCount++;
                
                solvedRegions[i] = true;
                
                //might need to be careful that we're yielding here before applying texture changes?
                yield return i;
            }
        }

        if(solvedCount > 0)
        {
            //relies on region completion being set
            nextToWall = FindNextToWalls();
            
            displayTexture.Apply();
        }
    }

    public IEnumerator PuzzleGenerationAll(bool allDifficulties = true)
    {
        // Ensure clue folder exists for this scene
        EnsureClueDirectoryExists();

        for (int difficulty = 0; difficulty <= MaxDifficulty; difficulty++)
        {
            if(!allDifficulties && difficulty != currentDifficulty)
                continue;
            
            for (int i = 0; i < regions.Count; i++)
            {
                RegionMapping regionMapping = regionMappingRepository.GetRegionMapping(SceneId, i);

                if (regionMapping.Type == RegionType.Discovery)
                {
                    Debug.Log("Preparing region " + i);
                    
                    //3. Prune clues for all difficulties
                    yield return PruneRegionCoroutine(i, difficulty);
                }
            }
        }

        Debug.Log("Finished generating all puzzles!");
    }
    
    public void PrepareAllRegionsAllDifficulties()
    {
        StartCoroutine(PuzzleGenerationAll());
    }
    
    public void PrepareAllRegionsCurrentDifficulty()
    {
        StartCoroutine(PuzzleGenerationAll(false));
    }

    
    private void RebuildScreen(RectInt newScreenBounds)
    {
        prevBounds = newScreenBounds;

        int area = prevBounds.width * prevBounds.height;

        const float bufferMultiplier = 1.2f;
        int bufferedSize = Mathf.CeilToInt(area * bufferMultiplier);
        
        bool wasZoomedIn = IsZoomedIn;
        
        IsZoomedIn = bufferedSize <= tilePool.TileCount;
        
        if (IsZoomedIn)
        {
            RebuildTiles(prevBounds);
        }
        else
        {
            //show texture
            HideTiles();
        }

        if(wasZoomedIn != IsZoomedIn)
        {
            foreach (GameObject go in hideWhenZoomedIn)
            {
                go.SetActive(!IsZoomedIn);
            }
        }
    }

    private void HideTiles()
    {
        foreach (var kvp in onScreenTiles)
        {
            tilePool.ReturnTile(kvp.Value);
        }
        
        onScreenTiles.Clear();
    }
    
    private void ResetTiles()
    {
        foreach (var kvp in onScreenTiles)
        {
            tilePool.ReturnTile(kvp.Value);
        }
        
        onScreenTiles.Clear();
    }
    
    private void MakeAllTilesClickable()
    {
        foreach (var kvp in onScreenTiles)
        {
            kvp.Value.Clicked = false;
        }
    }

    private void RebuildTiles(RectInt bounds)
    {
        List<Vector2Int> removeList = new List<Vector2Int>();

        foreach (var kvp in onScreenTiles)
        {
            if (bounds.Contains(kvp.Key))
            {
                continue;
            }

            //kvp.Value.OnClickTile = null;
            tilePool.ReturnTile(kvp.Value);
            removeList.Add(kvp.Key);
        }

        foreach (var key in removeList)
        {
            onScreenTiles.Remove(key);
        }

        bool hintsEnabled = MosaicPrivacyAndSettings.GetHintsSetting();
        bool errorsEnabled = MosaicPrivacyAndSettings.GetShowErrors();
        bool countdownMode = MosaicPrivacyAndSettings.GetCountdownMode();
        
        TileState state;
        Color displayPixel;
        ClickableTile tile;
        
        foreach (Vector2Int pos in bounds.allPositionsWithin)
        {
            if (pos.x < 0 || pos.x >= boardStateTexture.Width) continue;
            if (pos.y < 0 || pos.y >= boardStateTexture.Height) continue;
                
            if (onScreenTiles.ContainsKey(pos))
                continue;

            tile = tilePool.GetTile();
            
            //tile.OnClickTile = OnTileClick;
            tile.SetPosition(pos);
            tile.SetRegion(regionMap[pos.x, pos.y], GetEmptyColor(pos.x, pos.y));
            tile.SetTileColors(blackTileColor, whiteTileColor);

            state = boardStateTexture.GetState(pos.x, pos.y);

            int region = regionMap[pos.x, pos.y];
            if (region < 0)
            {
                //wall
                displayPixel = Color.black;
            }
            else if (state.IsSolved())
            {
                displayPixel = solvedIllustration.GetPixel(pos.x, pos.y);
            }
            else
            {
                displayPixel = baseIllustration.GetPixel(pos.x, pos.y);
                
                if(displayPixel == Color.clear)
                    displayPixel = solvedIllustration.GetPixel(pos.x, pos.y);
            }

            tile.SetColor(displayPixel);

            if (editMode == EditMode.EditSolution)
            {
                tile.SetStateInstant(solution[pos.x, pos.y] ? TileState.Black : TileState.White);
            }
            else
            {
                tile.SetStateInstant(state);
            }

            tile.Clicked = false;
            
            tile.SetOpponentRegion(IsOpponentTile(pos.x, pos.y));
            tile.SetCrypticRegion(IsCrypticTile(pos.x, pos.y));
            tile.SetClueVisible(cluesVisible[pos.x, pos.y]);
            tile.SetWalls(borderFlags[pos.x, pos.y]);

            // Set count display based on countdown mode
            if (countdownMode && clues[pos.x, pos.y] >= 0)
            {
                tile.SetCountdownDisplay(countdownValues[pos.x, pos.y]);
            }
            else
            {
                tile.SetCount(clues[pos.x, pos.y]);
            }

            tile.ShowClueState(errorsEnabled && errors[pos.x, pos.y],
                hintsEnabled && hints[pos.x, pos.y],
                nextToWall[pos.x, pos.y],
                solved[pos.x, pos.y]);

            onScreenTiles.Add(pos, tile);
        }
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        
        var lastMove = undoSystem.UndoLastMove();
        if (lastMove == null)
            return;

        var move = lastMove.Value;
        Vector2Int pos = move.position;
        
        // Don't undo solved tiles
        if (boardStateTexture.IsSolved(pos.x, pos.y))
            return;
            
        // Restore the previous state without recording it as a new move (use instant = true)
        FlipTile(pos, move.fromState, true);
        SFX?.TileFlipClear();
        
        SaveDirty = true;

        // Pan camera to the undone tile if it's not visible
        var screenBounds = cameraController2D.GetCameraBounds();
        bool onscreen = screenBounds.Contains(pos);

        if (!onscreen)
        {
            cameraController2D.PanTowards(new Vector3(pos.x, pos.y, 0));
        }
    }
    
    private void OnTileClick(ClickableTile tile, bool leftClick)
    {
        var pos = tile.Position;

        // Clear clue highlights when any tile is placed
        ClearClueHighlights();

        // Clear change highlight at this specific position if it exists
        ClearChangeHighlightAt(pos);

        var currentState = tile.CurrentState;
        if(currentState.IsSolved() || currentState == TileState.Hidden)
            return;

        // Check if this is an opponent tile - if so, don't allow interaction
        if (IsOpponentTile(pos.x, pos.y))
            return;
        tile.Clicked = true;
        
        //tile.OnClickTile -= OnTileClick;
        
        int region = regionMap[pos.x, pos.y];
        
        if (drawingRegion >= 0 && region != drawingRegion)
        {
            return;
        }
        
#if DEMO
        if (!DemoRegionIndices.Contains(region))
            return;
#endif
        
        if(editMode == EditMode.EditSolution)
        {
            SFX?.StartDrag();
            
            TileState state = solution[pos.x, pos.y] ? TileState.White : TileState.Black;
            
            if (leftClick)
                state = TileState.Black;
            else
                state = TileState.White;
            
            solution[tile.Position.x, tile.Position.y] = state == TileState.Black;
            tile.FlipTile(state);
            
            //update clues
            int width = solution.GetLength(0);
            int height = solution.GetLength(1);
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (pos.x + i < 0 || pos.x + i >= width)
                        continue;

                    if (pos.y + j < 0 || pos.y + j >= height)
                        continue;
                    
                    int localCount = MosaicSolver.GetLocalCountRegional(solution, regionMap, pos.x + i, pos.y + j);
                    clues[pos.x + i, pos.y + j] = localCount;
                    countdownValues[pos.x + i, pos.y + j] = localCount; // Reset countdown when clues are recalculated

                    if (onScreenTiles.TryGetValue(new Vector2Int(pos.x + i, pos.y + j), out var ntile))
                    {
                        if (MosaicPrivacyAndSettings.GetCountdownMode() && localCount >= 0)
                        {
                            ntile.SetCountdownDisplay(countdownValues[pos.x + i, pos.y + j]);
                        }
                        else
                        {
                            ntile.SetCount(localCount);
                        }
                        ntile.SetOpponentRegion(IsOpponentTile(pos.x + i, pos.y + j));
                        ntile.SetCrypticRegion(IsCrypticTile(pos.x + i, pos.y + j));

                        if(i != 0 || j != 0)
                            ntile.SetStateInstant(ntile.CurrentState);
                    }
                }
            }
            
            //SaveProgress();
            return;
        }

        if (drawingRegion == -1)
        {
            SFX?.StartDrag();

            drawingRegion = region;
            
            drawingState = boardStateTexture.GetState(pos.x, pos.y);
            
            bool inverted = MosaicPrivacyAndSettings.GetInvertedInput();

            bool primaryButton = leftClick ^ inverted;
            
            if (primaryButton)
            {
                switch (drawingState)
                {
                    case TileState.Empty:
                        drawingState = TileState.Black;
                        break;
                    case TileState.Black:
                        if(MosaicPrivacyAndSettings.GetClickBehaviour() == ClickBehaviour.Cycle)
                            drawingState = TileState.White;
                        else
                            drawingState = TileState.Empty;
                        break;
                    case TileState.White:
                        if(MosaicPrivacyAndSettings.GetClickBehaviour() == ClickBehaviour.Cycle)
                            drawingState = TileState.Empty;
                        else
                            drawingState = TileState.Black;
                        break;
                }
            }
            else
            {
                switch (drawingState)
                {
                    case TileState.Empty:
                        drawingState = TileState.White;
                        break;
                    case TileState.Black:
                        if(MosaicPrivacyAndSettings.GetClickBehaviour() == ClickBehaviour.Cycle)
                            drawingState = TileState.Empty;
                        else
                            drawingState = TileState.White;
                        break;
                    case TileState.White:
                        if(MosaicPrivacyAndSettings.GetClickBehaviour() == ClickBehaviour.Cycle)
                            drawingState = TileState.Black;
                        else
                            drawingState = TileState.Empty;
                        break;
                }
            }
            
            Cursor.SetCursor(cursors[(int)drawingState], cursorHotspot, CursorMode.Auto);
        }

        //debug tools for solving large chunks
        if (Defines.IsDevelopment() || Application.isEditor)
        {
            if(AltHeld)
            {
                //solve neighbours
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        int nx = tile.Position.x + i;
                        int ny = tile.Position.y + j;

                        if (nx < 0 || nx >= boardStateTexture.Width)
                            continue;

                        if (ny < 0 || ny >= boardStateTexture.Height)
                            continue;

                        var tileState = boardStateTexture.GetState(nx, ny);
                        if (tileState.IsSolved() || tileState == TileState.Hidden)
                            continue;

                        TileState state = drawingState;
                        
                        if (ControlHeld)
                        {
                            bool black = solution[nx, ny];
                            state = black ? TileState.Black : TileState.White;
                        }

                        replayData[tile.Position.x, tile.Position.y] = replayIndex;
                        FlipTile(new Vector2Int(nx, ny), state, true);
                    }
                }
            }
            
            if (ControlHeld)
            {
                bool black = solution[tile.Position.x, tile.Position.y];
                drawingState = black ? TileState.Black : TileState.White;
            }
        }

        replayIndex++;
        replayData[tile.Position.x, tile.Position.y] = replayIndex;
        FlipTile(pos, drawingState);

        switch (drawingState)
        {
            case TileState.Empty:
                SFX?.TileFlipClear();
                break;
            case TileState.Black:
                SFX?.TileFlipBlack();
                break;
            case TileState.White:
                SFX?.TileFlipWhite();
                break;
        }
    }

    private void FlipTile(Vector2Int pos, TileState newState, bool instant = false)
    {
        var prevState = boardStateTexture.GetState(pos.x, pos.y);

        if (prevState != newState)
        {
            // Record the move in the undo system (only for user-initiated moves, not instant debug moves)
            if (!instant)
            {
                undoSystem.RecordMove(pos, prevState, newState);
            }
            
            if (prevState.IsSolved())
            {
                //do nowt
            }
            else if (newState == TileState.Empty && prevState != TileState.Empty)
            {
                ProgressCount--;
                
                //the prevState was correct, so deduct one solved count
                if (!IsMistake(prevState, pos.x, pos.y))
                    SolvedCount--;
            }
            else if (newState != TileState.Empty && prevState == TileState.Empty)
            {
                ProgressCount++;
                
                //the newState is correct, so add one solved count
                if (!IsMistake(newState, pos.x, pos.y))
                    SolvedCount++;
            }
        }

        if (onScreenTiles.TryGetValue(pos, out var tile))
        {
            if (prevState != newState)
            {
                if(instant)
                    tile.SetStateInstant(newState);
                else
                    tile.FlipTile(newState);
            }
        }
        
        boardStateTexture.SetState(pos.x, pos.y, newState);

        bool anyErrors = false;

        bool hintsEnabled = MosaicPrivacyAndSettings.GetHintsSetting();
        bool errorsEnabled = MosaicPrivacyAndSettings.GetShowErrors();

        int region = regionMap[pos.x, pos.y];
        
        // Handle countdown mode for newly placed black tiles
        if (MosaicPrivacyAndSettings.GetCountdownMode() && newState == TileState.Black && prevState != TileState.Black)
        {
            UpdateCountdownForPosition(pos, -1); // Decrement countdown for nearby clues
        }
        // Handle countdown mode for removed black tiles
        else if (MosaicPrivacyAndSettings.GetCountdownMode() && prevState == TileState.Black && newState != TileState.Black)
        {
            UpdateCountdownForPosition(pos, 1); // Increment countdown for nearby clues
        }
        
        //check all neighbours for errors/hints
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                int nx = pos.x + i;
                int ny = pos.y + j;

                if (nx < 0 || nx >= boardStateTexture.Width)
                    continue;

                if (ny < 0 || ny >= boardStateTexture.Height)
                    continue;

                if(regionMap[nx, ny] != region)
                    continue;
                
                bool error = CheckError(nx, ny, out bool hint, out bool clueSolved);

                errors[nx, ny] = error;
                clueSolved = solved[nx, ny] = clueSolved;
                anyErrors |= error;

                if (onScreenTiles.TryGetValue(new Vector2Int(nx, ny), out var ntile))
                {
                    ntile.ShowClueState(errorsEnabled && error,
                        hintsEnabled && hint,
                        nextToWall[nx, ny],
                        clueSolved);
                }

                hints[nx, ny] = hint;

                Color displayColor;

                var neighbourState = boardStateTexture.GetState(nx, ny);

                if (newState == TileState.Hidden)
                {
                    displayColor = baseIllustration.GetPixel(nx, ny);
                }
                else if (newState.IsSolved())
                {
                    displayColor = solvedIllustration.GetPixel(nx, ny);
                }
                else if (errorsEnabled && errors[nx, ny])
                {
                    displayColor = Color.red;
                }
                else if (hintsEnabled && hints[nx, ny])
                {
                    displayColor = Color.yellow;
                }
                else if (neighbourState == TileState.Empty)
                {
                    displayColor = GetEmptyColor(nx, ny);
                }
                else
                {
                    displayColor = GetDisplayColor(neighbourState);
                }

                displayTexture.SetPixel(nx, ny, displayColor);
                displayTexture.Apply();
            }
        }

        HighlightedRegion = -1;
        // ShowCryptic stays as set by the host — no modification on tile flip.
        
        ProgressNormalized = (float)ProgressCount / MaxProgress;
        
        UpdateHighlightedRegion(pos);

        if (anyErrors && errorsEnabled && !instant)
            SFX?.PlayError();
    }

    private bool CheckError(int nx, int ny, out bool hint, out bool filled)
    {
        hint = false;
        filled = false;
        int clue = clues[nx, ny];

        if (clue < 0)
        {
            filled = true;
            return false;
        }
        
        int region = regionMap[nx, ny];

        int countBlack = GetLocalCount(boardStateTexture, nx, ny, region, TileState.Black);
        countBlack += GetLocalCount(boardStateTexture, nx, ny, region, TileState.SolvedBlack);

        int countEmpty = GetLocalCount(boardStateTexture, nx, ny, region, TileState.Empty);
        
        hint = countEmpty > 0 && (countBlack + countEmpty == clue || countBlack == clue);

        if (countEmpty == 0)
        {
            //if complete, black count must match the clue
            filled = true;
            return countBlack != clue;
        }

        if (countBlack > clue)
        {
            //check if we've placed too many blacks
            return true;
        }

        if (countBlack + countEmpty < clue)
        {
            //check if there's enough empty spaces to place the blacks we need
            return true;
        }

        return false;
    }

    private int GetLocalCount(BoardState boardData, int x, int y, int region, TileState state)
    {
        int count = 0;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                if (x + i < 0 || x + i >= boardStateTexture.Width) 
                    continue;
                
                if (y + j < 0 || y + j >= boardStateTexture.Height) 
                    continue;
                
                if(regionMap[x+i, y+j] != region)
                    continue;
                
                if (boardData.GetState(x + i, y + j) == state) 
                    count++;
            }
        }
        return count;
    }
    
    public void DebugTestReplay()
    {
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        MosaicSolver solver = new MosaicSolver(width, height);

        int replayOffset = 0;
        
        for (int i = 0; i < regions.Count; i++)
        {
            SolveResult result = solver.TrySolveSingleRegion(clues, regionMap, i);
            
            //apply solution
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (result.BoardState.GetState(x, y) == TileState.Black)
                    {
                        boardStateTexture.SetState(x, y, TileState.Black);
                        displayTexture.SetPixel(x, y, GetDisplayColor(TileState.Black));
                    }
                    else if (result.BoardState.GetState(x, y) == TileState.White)
                    {
                        boardStateTexture.SetState(x, y, TileState.White);
                        displayTexture.SetPixel(x, y, GetDisplayColor(TileState.White));
                    }
                }
            }
            
            int replayLength = 0;
            
            foreach (var pos in regions[i])
            {
                int r = result.Replay[pos.x, pos.y];
                if(r > replayLength)
                    replayLength = r;
                
                replayData[pos.x, pos.y] = r + replayOffset;
            }
            
            replayOffset += replayLength;
        }

        //hopefully?
        ProgressCount = MaxProgress;
        SolvedCount = MaxProgress;
        replayIndex = width * height;
        
        displayTexture.Apply();

        CheckRegionCompletion();
        
        GrantProgressAchievements();
        GrantRegionAchievements();
        GrantEdgeAchievements();
        
        ForceRefreshBoard();
        
        SaveProgress();
        
        Replay();        
    }

    public void DebugSolveAllRevealed()
    {
        for (int regionId = 0; regionId < regions.Count; regionId++)
        {
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, regionId);
            
            if(mapping.Type != RegionType.Discovery)
                continue;
            
            if (TrySolveRegion(regionId))
            {
                Debug.Log($"Region {regionId} solved.");
            }
            else
            {
                Debug.LogError($"Region {regionId} not solvable.");
            }
        }
        
        ForceRefreshBoard();
        OnClickRelease();
    }
    
    public void DebugSolveSingleRegion()
    {
        if (TrySolveRegion(HighlightedRegion))
        {
            Debug.Log($"Region {HighlightedRegion} solved.");
        }
        else
        {
            Debug.LogError($"Region {HighlightedRegion} not solvable.");
        }

        ForceRefreshBoard();
        OnClickRelease();
    }

    private bool TrySolveRegion(int regionIndex)
    {
        if (regionMappingRepository.GetRegionMapping(SceneId, regionIndex).Type != RegionType.Discovery)
        {
            Debug.LogError($"Region {regionIndex} is not a discovery region");
            return false;
        }

        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        MosaicSolver solver = new(width, height);

        SolveResult result = solver.TrySolveSingleRegion(clues, regionMap, regionIndex, currentDifficulty > 0);

        foreach (var pos in regions[regionIndex])
        {
            var state = result.BoardState.GetState(pos.x, pos.y);
            
            if(boardStateTexture.IsSolved(pos.x, pos.y))
                continue;
            
            boardStateTexture.SetState(pos.x, pos.y, state);
        }

        return result.Finished;
    }

    public void DebugSolve()
    {
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        MosaicSolver solver = new MosaicSolver(width, height);
        SolveResult result = solver.TrySolveRegional(clues, regionMap, advanced:currentDifficulty > 0);

        if (result.Finished == false)
        {
            Debug.LogError("Solve failed");
        }
        
        ProgressCount = MaxProgress - result.RemainingTiles;
        SolvedCount = MaxProgress - result.RemainingTiles;
        replayData = result.Replay;
        replayIndex = width * height;
        
        //apply solution
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if(regionTypeMap[x,y] != RegionType.Discovery)
                    continue;
                
    #if DEMO
                int region = regionMap[x, y];

                if (!DemoRegionIndices.Contains(region))
                    continue;
    #endif
                
                if (result.BoardState.GetState(x, y) == TileState.Black)
                {
                    boardStateTexture.SetState(x, y, TileState.Black);
                    displayTexture.SetPixel(x, y, GetDisplayColor(TileState.Black));
                }
                else if (result.BoardState.GetState(x, y) == TileState.White)
                {
                    boardStateTexture.SetState(x, y, TileState.White);
                    displayTexture.SetPixel(x, y, GetDisplayColor(TileState.White));
                }
            }
        }
        
        displayTexture.Apply();

        _ = CheckRegionCompletion();
        
        GrantProgressAchievements();
        GrantRegionAchievements();
        GrantEdgeAchievements();
        
        ForceRefreshBoard();
        
        SaveProgress();
    }
    
    public void DebugPruneRegion()
    {
        int region = GetRegionAtCenterOfScreen();
        
        if (region < 0)
        {
            Debug.LogError("Not in a region");
            return;
        }
        
        if(pruningRegion)
            return;
        
        StartCoroutine(PruneRegionCoroutine(region, currentDifficulty));
    }
    
    public void DebugFixSolution()
    {
        int region = GetRegionAtCenterOfScreen();
        
        if (region < 0)
        {
            Debug.LogError("Not in a region");
            return;
        }
        
        if(pruningRegion)
            return;
        
        StartCoroutine(FixSolutionCoroutine(region));
    }
    
    public void DebugFixAll()
    {
        if(pruningRegion)
            return;
        
        StartCoroutine(FixAllRegionsCoroutine());
    }
    
    private bool pruningRegion;

    private IEnumerator PruneRegionCoroutine(int region, int difficulty)
    {
        Stopwatch timer = new();
        timer.Start();
        Debug.Log($"Pruning region {region}/{regions.Count}");
        pruningRegion = true;
        
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        MosaicGenerator generator = new(width, height);

        float advancedPct = GetPctAdvancedForDifficulty(difficulty);
        
        foreach (GenerationResult result in generator.PruneRegion(solution, regionMap, clues, region, advancedPct))
        {
            yield return new WaitForEndOfFrame();

            if (result.InProgress == false)
            {
                Debug.Log("Pruning complete in " + result.NumSteps + " steps, removed " + result.CluesPlaced + " clues. Advanced deductions required: "+result.AdvancedClues);
                clues = result.Clues;
                
                ForceRefreshBoard();
            }
        }
        
        pruningRegion = false;
        //save clues to file
        string fileName = GetAbsolutePathForClueFile(difficulty);
        
        File.WriteAllText(fileName, MosaicSolver.GetCluesAsCSV(clues));
        
        timer.Stop();
        Debug.Log($"Pruning complete in {timer.Elapsed:g}");
    }
    
    private IEnumerator FixSolutionCoroutine(int region)
    {
        Debug.Log($"Fixing solution for region {region}/{regions.Count}");
        pruningRegion = true;
        
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        MosaicGenerator generator = new(width, height);
        
        int[,] fullset = MosaicGenerator.GenerateFullClueSetRegionLocked(solution, regionMap);

        Dictionary<int,int[,]> allDifficulties = new();
        
        for (int difficulty = 0; difficulty <= MaxDifficulty; difficulty++)
        {
            allDifficulties[difficulty] = LoadCluesFromDisk(difficulty);

            foreach (Vector2Int pos in regions[region])
            {
                allDifficulties[difficulty][pos.x, pos.y] = fullset[pos.x, pos.y];
            }
            
            string path = GetAbsolutePathForClueFile(difficulty);
            File.WriteAllText(path, MosaicSolver.GetCluesAsCSV(allDifficulties[difficulty]));
        }

        //fix the solution in classic difficulty
        foreach (GenerationResult result in generator.FixSolution(solution, regionMap, fullset, region))
        {
            yield return new WaitForEndOfFrame();

            if (result.InProgress == false)
            {
                if (result.Failure)
                {
                    Debug.LogError("Failed to fix solution after maximum iterations");
                    break;
                }

                if (result.CluesPlaced == 0)
                {
                    Debug.Log("No clues changed - the region was already solvable");
                    break;
                }

                Debug.Log("Solution fixing complete in " + result.NumSteps + " steps, randomized " + result.CluesPlaced + " tiles");
                fullset = result.Clues;
                
                ForceRefreshBoard();
            }
        }
        
        pruningRegion = false;

        for (int difficulty = 0; difficulty <= MaxDifficulty; difficulty++)
        {
            //copy the full set of classic difficulty clues over to the other difficulties
            foreach (Vector2Int pos in regions[region])
            {
                allDifficulties[difficulty][pos.x, pos.y] = fullset[pos.x, pos.y];
            }
            
            string path = GetAbsolutePathForClueFile(difficulty);
            File.WriteAllText(path, MosaicSolver.GetCluesAsCSV(allDifficulties[difficulty]));
        }
        
        //save updated solution to disk
        SavePNG();

        clues = allDifficulties[currentDifficulty];
        
        SetupHintsAndErrors();
        ForceRefreshBoard();
    }
    
    private IEnumerator FixAllRegionsCoroutine()
    {
        for (int i = 0; i < regions.Count; i++)
        {
            yield return FixSolutionCoroutine(i);
        }
    }
    
    public void Replay()
    {
        StartCoroutine(ReplayCoroutine());
    }

    private IEnumerator ReplayCoroutine()
    {
        cameraController2D.FullyZoomOutSmooth();
        cameraController2D.SetInputEnabled(false);

        yield return new WaitForSeconds(1f);
        
        SFX?.PlayReplayStart();

        for (int i = 0; i < displayTexture.width; i++)
        {
            for (int j = 0; j < displayTexture.height; j++)
            {
                displayTexture.SetPixel(i,j, baseIllustration.GetPixel(i,j));  
                
                // if(onScreenTiles.TryGetValue(new Vector2Int(i, j), out var tile)){
                //     tile.FlipTile(state);
                // }
            }
        }
        Debug.Log("Displaying final state");
        
        displayTexture.Apply();

        ToggleEffects(true);

        yield return new WaitForSeconds(0.5f);

        //set the board to the state of the start of the replay, in case it's incomplete
        for (int i = 0; i < displayTexture.width; i++)
        {
            for (int j = 0; j < displayTexture.height; j++)
            {
                int region = regionMap[i, j];
                
                if (region < 0 || regionTypeMap[i,j] != RegionType.Discovery)
                {
                    //unsolvable areas start off filled
                    displayTexture.SetPixel(i, j, baseIllustration.GetPixel(i, j));
                }
                else if (replayData[i, j] > 0) 
                {
                    //we have replay data for this square, so start it off empty
                    displayTexture.SetPixel(i, j, GetEmptyColor(i,j));
                }
                else
                {
                    //there's no replay data for this area, so show it's current state.
                    TileState state = boardStateTexture.GetState(i, j);
                    
                    if (state == TileState.Empty)
                    {
                        displayTexture.SetPixel(i, j, GetEmptyColor(i,j));
                    }
                    else if (state.IsSolved() || state == TileState.Hidden)
                    {
                        displayTexture.SetPixel(i, j, baseIllustration.GetPixel(i, j));
                    }
                    else
                    {
                        displayTexture.SetPixel(i, j, GetDisplayColor(state));
                    }
                }
            }
        }
        
        displayTexture.Apply();
        
        Debug.Log("Displaying start of replay state");

        yield return new WaitForSeconds(0.5f);

        int rIndex = 0;
        int lastIndex = 0;
        float duration = 6.5f;
        float timer = 0f;
        var wait = new WaitForEndOfFrame();
        
        SFX?.PlayReplayLoop();
        
        int[] regionSizes = regions.Select(region => region.Count).ToArray();

        List<int> completeRegions = new List<int>();
        
        while (rIndex <= replayIndex)
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            rIndex = Mathf.FloorToInt(t * replayIndex);

            for (int x = 0; x < boardStateTexture.Width; x++)
            {
                for (int y = 0; y < boardStateTexture.Height; y++)
                {
                    if (replayData[x, y] <= rIndex && replayData[x, y] > lastIndex)
                    {
                        TileState state = solution[x, y] ? TileState.Black : TileState.White;

                        displayTexture.SetPixel(x, y, GetDisplayColor(state));
                        int regionId = regionMap[x, y];
                        
                        if (regionId >= 0 && regionSizes[regionId] > 0)
                        {
                            regionSizes[regionId]--;
                            
                            if (regionSizes[regionId] == 0)
                            {
                                completeRegions.Add(regionId);
                            }
                        }
                    }
                }
            }

            foreach (int regionId in completeRegions)
            {
                List<Vector2Int> toFill = regions[regionId];
                
                foreach (var pos in toFill)
                {
                    var pixel = solvedIllustration.GetPixel(pos.x, pos.y);
                    
                    if(pixel == Color.clear)
                        pixel = baseIllustration.GetPixel(pos.x, pos.y);
                    
                    displayTexture.SetPixel(pos.x, pos.y, pixel);
                }
            }
            
            completeRegions.Clear();

            displayTexture.Apply();
            lastIndex = rIndex;

            yield return wait;
            
            if(rIndex >= replayIndex)
                break;
        }

        //for some reason the last region doesn't fill in the loop and I can't be arsed to work out why
        for (int regionId = 0; regionId < regionSizes.Length; regionId++)
        {
            if (regionSizes[regionId] <= 0)
                continue;

            List<Vector2Int> toFill = regions[regionId];

            foreach (var pos in toFill)
            {
                displayTexture.SetPixel(pos.x, pos.y, baseIllustration.GetPixel(pos.x, pos.y));
            }
            
            displayTexture.Apply();
        }

        SFX?.PlayReplayEnd();

        yield return new WaitForSeconds(1f);

        ForceRefreshBoard();
        ToggleEffects(false);
        cameraController2D.SetInputEnabled(true);
    }

    private void ToggleEffects(bool reverse)
    {
        foreach (GameObject effect in normalEffects)
        {
            effect.SetActive(!reverse);
        }

        foreach (GameObject effect in reverseEffects)
        {
            effect.SetActive(reverse);
        }
    }

    public void Reset()
    {
        undoSystem.ClearHistory();
        Setup(regionsTexture, baseIllustration);
    }

    public void SavePNG()
    {
        int width = solution.GetLength(0);
        int height = solution.GetLength(1);
        
        Color[] solutionPixels = new Color[width * height];
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                solutionPixels[y * width + x] = solution[x, y] ? Color.black : Color.white;
            }
        }
        
        solutionTexture.SetPixels(solutionPixels);
        solutionTexture.Apply();
        
        byte[] textureData = solutionTexture.EncodeToPNG();

        string folderName = SceneId;
        string fileName = $"{SceneId.ToLowerInvariant()}_solution.png";
        string path = $"{Application.dataPath}/Board/{folderName}/{fileName}";

        File.WriteAllBytes(path, textureData);
    }
    
    public void SaveReplay()
    {
        if (replayData == null)
        {
            Debug.LogError("No replay data to save");
            return;
        }
    
        try
        {
            string path = string.Format(ReplayFilePath, currentSaveIndex);
            byte[] data = new byte[replayData.Length * sizeof(int)];

            int width = replayData.GetLength(0);
            int height = replayData.GetLength(1);
            
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    byte[] bytes = BitConverter.GetBytes(replayData[x, y]);
                    
                    int index = y * width + x;
                    
                    bytes.CopyTo(data, index * sizeof(int));
                }
            }

            File.WriteAllBytes(path, data);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }
    
    public void LoadReplay()
    {
        replayIndex = 1;
        DateTime latestTimestamp = DateTime.MinValue;
        byte[] latestData = null;
        
        for (int i = 0; i < MaxBackups; i++)
        {
            string path = string.Format(ReplayFilePath, i);
            
            if (File.Exists(path))
            {
                DateTime timestamp = File.GetLastWriteTime(path);
                
                if (timestamp > latestTimestamp)
                {
                    latestTimestamp = timestamp;
                    try
                    {
                        latestData = File.ReadAllBytes(path);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Failed to read replay file {path}: {ex}");
                    }
                }
            }
        }
        
        if (latestData != null)
        {
            try
            {
                int width = replayData.GetLength(0);
                int height = replayData.GetLength(1);
                
                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        int index = y * width + x;
                        replayData[x, y] = BitConverter.ToInt32(latestData, index * sizeof(int));

                        if (replayData[x, y] > replayIndex)
                            replayIndex = replayData[x, y];
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse replay data: {ex}");
            }
        }
        else
        {
            string legacyPath = Application.persistentDataPath + "/replay.sav";
            if (File.Exists(legacyPath))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(legacyPath);
                    int width = replayData.GetLength(0);
                    int height = replayData.GetLength(1);
                    
                    for (int x = 0; x < width; x++)
                    {
                        for (int y = 0; y < height; y++)
                        {
                            int index = y * width + x;
                            replayData[x, y] = BitConverter.ToInt32(data, index * sizeof(int));

                            if (replayData[x, y] > replayIndex)
                                replayIndex = replayData[x, y];
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load legacy replay: {ex}");
                }
            }
        }
        
        Debug.Log("Replay length loaded: "+replayIndex);
    }
    
    private const int MaxBackups = 5;
    private string ReplayFilePath => $"{Application.persistentDataPath}/{SceneId.ToLower()}/replay_{{0}}.sav";
    public string DebugString { get; private set; }

    private int currentSaveIndex;

    public void SaveProgress()
    {
        SaveTimer = 0;

        try
        {
            ProgressSave save = GetSaveData();
            Debug.Log($"Saved {SceneId} with board state");
            OnSaveRequested?.Invoke(save);
            SaveDirty = false;
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }

    public ProgressSave GetSaveData()
    {
        ProgressSave save = new ProgressSave();

        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;

        save.width = width;
        save.height = height;
        save.state = new byte[boardStateTexture.Width * boardStateTexture.Height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                save.state[y * width + x] = (byte)boardStateTexture.GetState(x, y);
            }
        }

        Vector3 camPos = cameraController2D.GetCurrentPosition();
        save.cameraX = camPos.x;
        save.cameraY = camPos.y;
        save.cameraZoom = camPos.z;

        return save;
    }

    public void ApplySaveData(ProgressSave save)
    {
        int width = save.width;
        int height = save.height;
            
        boardStateTexture = new BoardState(width, height);
        
        // Clear undo history when loading a save since the board state might be different
        undoSystem.ClearHistory();

        int progress = 0;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileState state = (TileState)save.state[y * width + x];

                if(state != TileState.Empty)
                    progress++;
                
                boardStateTexture.SetState(x, y, state);
            }
        }
        
        Debug.Log("Loaded save with "+progress+" tiles filled");

        // Populate solvedRegions from the restored tile states so that the first
        // player interaction doesn't re-trigger completion animations for already-solved regions.
        foreach (int _ in CheckRegionCompletion()) { }

        // Sync the display texture and on-screen tiles to the loaded state.
        ForceRefreshBoard();

        Vector2 cameraPos = new Vector2(save.cameraX, save.cameraY);
        float zoom = save.cameraZoom;

        cameraController2D.SetLastPosition(cameraPos, zoom);

        focusPos = Vector2Int.RoundToInt(cameraPos);
    }
    

    public void ForceRefreshBoard()
    {
        //refresh display texture
        RefreshDisplayTexture();
        ResetTiles();
        var bounds = cameraController2D.GetCameraBounds();
        RebuildScreen(bounds);

        OnBoardRefreshed?.Invoke(SceneId);
    }

    private void RefreshDisplayTexture()
    {
        bool hintsEnabled = MosaicPrivacyAndSettings.GetHintsSetting();
        bool errorsEnabled = MosaicPrivacyAndSettings.GetShowErrors();
        
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                var state = boardStateTexture.GetState(x, y);

                Color displayColor;

                if (state == TileState.Empty)
                {
                    displayColor = GetEmptyColor(x, y);
                }
                else if (state == TileState.Hidden)
                {
                    displayColor = baseIllustration.GetPixel(x, y);
                }
                else if (state.IsSolved())
                {
                    displayColor = solvedIllustration.GetPixel(x, y);
                }
                else if(errorsEnabled && errors[x, y])
                {
                    displayColor = Color.red;
                }
                else if(hintsEnabled && hints[x, y])
                {
                    displayColor = Color.yellow;
                }
                else
                {
                    displayColor = GetDisplayColor(boardStateTexture.GetState(x, y));
                }
                
                displayTexture.SetPixel(x, y, displayColor);
            }
        }

        displayTexture.Apply();
    }

    private Color GetEmptyColor(int x, int y)
    {
        int region = regionMap[x, y];

        // Use modulo to create repeating pattern for more chaotic gradient sampling
        float gradientSample = region >= 0 ? (region % 5) / 5f : 0f;

        Color displayColor;
        if ((Defines.IsDemo() && !DemoRegionIndices.Contains(region)) || opponentTiles[x, y])
        {
            displayColor = lockedRegionColorMapping.Evaluate(gradientSample);
        }
        else
        {
            displayColor = regionColorMapping.Evaluate(gradientSample);
        }
        return displayColor;
    }
    
    public Color GetBlackTileColor(bool inverted = false)
    {
        return inverted ? whiteTileColor : blackTileColor;
    }
    
    public Color GetWhiteTileColor(bool inverted = false)
    {
        return inverted ? blackTileColor : whiteTileColor;
    }
    
    public Color GetDisplayColor(TileState state)
    {
        bool inverted = MosaicPrivacyAndSettings.GetInvertedColors();
        
        switch (state)
        {
            case TileState.SolvedBlack:
            case TileState.SolvedWhite:
            case TileState.Empty:
            case TileState.Hidden:
            case TileState.Linked:
                Debug.LogError("Don't use this method for empty or solved tiles! "+state);
                return Color.magenta;
            case TileState.Black:
                return GetBlackTileColor(inverted);
            case TileState.White:
                return GetWhiteTileColor(inverted);
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    //Returns 'true' if we touched or hovering on Unity UI element.
    private bool CheckIfOverUIElement(Vector2 mousePos)
    {
        PointerEventData eventData = new (EventSystem.current)
        {
            position = mousePos
        };

        List<RaycastResult> results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        
        foreach (RaycastResult result in results)
        {
            if (result.gameObject.layer == UILayer)
                return true;
        }

        return false;
    }

    public enum HintResult
    {
        NoneFound,
        FoundSimple,
        FoundAdvanced
    }

    public struct HintInfo
    {
        public HintResult Result;
        public Vector2Int FirstTile;
        public Vector2Int SecondTile; // Only used for advanced hints

        public HintInfo(HintResult result, Vector2Int firstTile, Vector2Int secondTile = default)
        {
            Result = result;
            FirstTile = firstTile;
            SecondTile = secondTile;
        }
    }
    
    public HintResult GoToNearestHint()
    {
        var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2Int centerPos = cameraController2D.ScreenToGrid(screenCenter);

        HintInfo hintInfo = FindNearestHint(centerPos);

        // Handle flashing based on hint type
        switch (hintInfo.Result)
        {
            case HintResult.FoundSimple:
                StartCoroutine(FlashTile(hintInfo.FirstTile.x, hintInfo.FirstTile.y));
                break;
            case HintResult.FoundAdvanced:
                StartCoroutine(FlashTwoTiles(hintInfo.FirstTile, hintInfo.SecondTile));
                break;
            case HintResult.NoneFound:
                Debug.Log("No hints found");
                break;
        }

        return hintInfo.Result;
    }

    public HintInfo FindNearestHint(Vector2Int centerPos)
    {
        int region = regionMap[centerPos.x, centerPos.y];

        Vector2Int pos;

        var state = boardStateTexture.GetState(centerPos.x, centerPos.y);
        
        //cursor is over a hidden region - we must do a global search for a hint
        if (state is not (TileState.Hidden or TileState.Linked))
        {
            if (TryFindHint(out pos, centerPos, region) == false) //no simple clue found in highlighted region
            {
                //try an advanced solve if they're expected at this difficulty
                if (currentDifficulty > 0)
                {
                    MosaicSolver solver = new(boardStateTexture.Width, boardStateTexture.Height);
                    solver.SetBoardState(boardStateTexture);
                    MosaicSolver.AdvancedSolve.Result
                        result = solver.TrySolveAdvancedSingleRegion(clues, regionMap, region);

                    if (result.Success)
                    {
                        var first = new Vector2Int(result.ax, result.ay);
                        var second = new Vector2Int(result.bx, result.by);

                        return new HintInfo(HintResult.FoundAdvanced, first, second);
                    }
                }
            }

            return new HintInfo(HintResult.FoundSimple, pos);
        }

        if (TryFindHint(out pos, centerPos))
        {
            return new HintInfo(HintResult.FoundSimple, pos);
        }
            
        //global search for advanced hint
        if (currentDifficulty > 0)
        {
            MosaicSolver solver = new(boardStateTexture.Width, boardStateTexture.Height);
            solver.SetBoardState(boardStateTexture);
                
            foreach (int regionIndex in GetRevealedUnsolvedDiscoveryRegions())
            {
                MosaicSolver.AdvancedSolve.Result
                    result = solver.TrySolveAdvancedSingleRegion(clues, regionMap, regionIndex);

                if (result.Success)
                {
                    var first = new Vector2Int(result.ax, result.ay);
                    var second = new Vector2Int(result.bx, result.by);

                    return new HintInfo(HintResult.FoundAdvanced, first, second);
                }
            }
        }

        return new HintInfo(HintResult.NoneFound, Vector2Int.zero);
    }

    private bool TryFindHint(out Vector2Int hintPos, Vector2Int pos, int region = -1)
    {
        int bestX = -1;
        int bestY = -1;
        int bestDistance = int.MaxValue;

        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if(region >= 0 && regionMap[x, y] != region)
                    continue;

                // Skip hidden tiles
                if (boardStateTexture.GetState(x, y) == TileState.Hidden)
                    continue;

                if (hints[x, y])
                {
                    int distance = (int)Vector2Int.Distance(new Vector2Int(x, y), pos);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestX = x;
                        bestY = y;
                    }
                }
            }
        }

        hintPos = new Vector2Int(bestX, bestY);

        return bestX >= 0;
    }

    //flash both tiles simultaneously and show their areas of influence with mini highlights
    public IEnumerator FlashTwoTiles(Vector2Int first, Vector2Int second)
    {
        // Set focus position to midpoint between the two tiles
        Vector2 midpoint = new Vector2((first.x + second.x) / 2f, (first.y + second.y) / 2f);
        focusPos = first; // Keep first tile as focus for highlight system
        MoveHighlightTo(focusPos);

        // Pan camera to show both tiles and their areas of influence
        if(IsZoomedIn && Defines.IsMobile())
        {
            cameraController2D.PanTowards(midpoint);
        }
        else
        {
            cameraController2D.PanAndZoomInOn(midpoint);
        }

        yield return new WaitForSeconds(0.5f);
        SFX?.PlayHint();

        // Spawn mini highlights to show areas of influence and overlap
        SpawnMiniHighlights(first, second);

        // Show both clues with mini highlights
        //SpawnMiniHighlight(first, miniHighlightOverlap);
        //SpawnMiniHighlight(second, miniHighlightOverlap);

        // Highlights will remain active until ClearPersistentHighlights() is called
    }

    public void ClearPersistentHighlights()
    {
        if (hintHighlight != null && hintHighlight.gameObject.activeSelf)
        {
            hintHighlight.gameObject.SetActive(false);
        }

        if (hintHighlightClone != null && hintHighlightClone.gameObject.activeSelf)
        {
            hintHighlightClone.gameObject.SetActive(false);
        }

        // Clear clue highlights and all change highlights
        ClearClueHighlights();
        ClearChangeHighlights();
    }

    private void ClearClueHighlights()
    {
        OnAdvancedHintCleared?.Invoke();
        
        foreach (GameObject highlight in activeClueHighlights)
        {
            if (highlight != null)
            {
                Destroy(highlight);
            }
        }
        activeClueHighlights.Clear();
    }

    private void ClearChangeHighlights()
    {
        foreach (var kvp in activeChangeHighlights)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        activeChangeHighlights.Clear();
    }

    private void ClearChangeHighlightAt(Vector2Int position)
    {
        if (activeChangeHighlights.TryGetValue(position, out GameObject highlight))
        {
            if (highlight != null)
            {
                Destroy(highlight);
            }
            activeChangeHighlights.Remove(position);
        }
    }

    public void ShowRegionGlow(int regionIndex)
    {
        if (regionIndex >= regions.Count)
            return;

        bool wasEmpty = glowingRegions.Count == 0;
        glowingRegions.Add(regionIndex);

        if (wasEmpty)
        {
            StartCoroutine(UpdateRegionGlows());
        }
    }

    public void HideRegionGlow(int regionIndex)
    {
        glowingRegions.Remove(regionIndex);
    }

    private void CheckDoorHoverEffect()
    {
        // Clear previous hover effect
        if (hoveredDoorRegion >= 0)
        {
            ClearDoorHoverEffect();
        }

        // Check if we should show hover effect on current highlighted region
        if (HighlightedRegion >= 0 && HighlightedRegion < regions.Count)
        {
            // Check if region is a door
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, HighlightedRegion);
            if (mapping.Type != RegionType.Door)
                return;

            // Skip if this door is already pulsing
            if (glowingRegions.Contains(HighlightedRegion))
                return;

            // Apply static hover glow
            hoveredDoorRegion = HighlightedRegion;
            ApplyStaticDoorGlow();
        }
    }

    private void ClearDoorHoverEffect()
    {
        if (hoveredDoorRegion >= 0 && hoveredDoorRegion < regions.Count)
        {
            StoreOriginalDisplayColors();
            Color[] pixels = displayTexture.GetPixels();

            foreach (Vector2Int pos in regions[hoveredDoorRegion])
            {
                int pixelIndex = pos.y * displayTexture.width + pos.x;
                if (pixelIndex < pixels.Length && pixelIndex < originalDisplayColors.Length)
                {
                    pixels[pixelIndex] = originalDisplayColors[pixelIndex];
                }
            }

            displayTexture.SetPixels(pixels);
            displayTexture.Apply();
        }
        hoveredDoorRegion = -1;
    }

    private void ApplyStaticDoorGlow()
    {
        if (hoveredDoorRegion >= 0 && hoveredDoorRegion < regions.Count)
        {
            StoreOriginalDisplayColors();
            Color[] pixels = displayTexture.GetPixels();

            foreach (Vector2Int pos in regions[hoveredDoorRegion])
            {
                int pixelIndex = pos.y * displayTexture.width + pos.x;
                if (pixelIndex < pixels.Length && pixelIndex < originalDisplayColors.Length)
                {
                    Color originalColor = originalDisplayColors[pixelIndex];
                    Color targetGlowColor = Color.Lerp(originalColor, regionGlowColor, 0.5f);
                    Color staticGlowColor = Color.Lerp(originalColor, targetGlowColor, 0.4f); // Static intensity
                    pixels[pixelIndex] = staticGlowColor;
                }
            }

            displayTexture.SetPixels(pixels);
            displayTexture.Apply();
        }
    }

    [ContextMenu("Test Door Region Glows")]
    public void TriggerDoorGlow()
    {
        for (int i = 0; i < regions.Count; i++)
        {
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, i);
            if (mapping.Type == RegionType.Door)
            {
                ShowRegionGlow(i);
            }
        }
    }

    private void StoreOriginalDisplayColors()
    {
        if (displayTexture != null && originalDisplayColors == null)
        {
            originalDisplayColors = displayTexture.GetPixels();
        }
    }

    private IEnumerator UpdateRegionGlows()
    {
        StoreOriginalDisplayColors();

        while (glowingRegions.Count > 0)
        {
            float pulse = Mathf.Sin(Time.time * 4f) * 0.5f + 0.5f; // 0 to 1 pulse
            float glowIntensity = Mathf.Lerp(0.3f, 0.8f, pulse);

            Color[] pixels = displayTexture.GetPixels();

            foreach (int regionIndex in glowingRegions)
            {
                if (regionIndex < regions.Count)
                {
                    foreach (Vector2Int pos in regions[regionIndex])
                    {
                        int pixelIndex = pos.y * displayTexture.width + pos.x;
                        if (pixelIndex < pixels.Length && pixelIndex < originalDisplayColors.Length)
                        {
                            Color originalColor = originalDisplayColors[pixelIndex];
                            Color targetGlowColor = Color.Lerp(originalColor, regionGlowColor, 0.5f);
                            Color glowedColor = Color.Lerp(originalColor, targetGlowColor, glowIntensity);
                            pixels[pixelIndex] = glowedColor;
                        }
                    }
                }
            }

            displayTexture.SetPixels(pixels);
            displayTexture.Apply();

            yield return null;
        }

        // Restore original colors when no regions are glowing
        if (originalDisplayColors != null)
        {
            displayTexture.SetPixels(originalDisplayColors);
            displayTexture.Apply();
        }
    }

    public IEnumerator FlashTile(int bestX, int bestY)
    {
        focusPos = new Vector2Int(bestX, bestY);
        MoveHighlightTo(focusPos);
        
        float showTime = 2f;
        float fadeTime = 0.5f;
        
        if(IsZoomedIn && Defines.IsMobile())
        {
            cameraController2D.PanTowards(new Vector2(bestX, bestY));
        }
        else
        {
            cameraController2D.PanAndZoomInOn(new Vector2(bestX, bestY));
        }
        
        yield return new WaitForSeconds(0.5f);
        SFX?.PlayHint();

        hintHighlight.transform.position = new Vector3(bestX, bestY, ClickableTile.GetZOffset(new Vector2Int(bestX, bestY))-5f);

        // for (int i = 0; i < 5; i++)
        // {
        //     hintHighlight.gameObject.SetActive(false);
        //     yield return new WaitForSeconds(0.25f);
        //     hintHighlight.gameObject.SetActive(true);
        //     yield return new WaitForSeconds(0.25f);
        // }
        
        hintHighlight.color = Color.white;

        hintHighlight.gameObject.SetActive(true);
        yield return new WaitForSeconds(showTime);
        
        float t = 0;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / fadeTime);
            hintHighlight.color = new Color(1f, 1f, 1f, a);
            yield return new WaitForEndOfFrame();
        }

        hintHighlight.gameObject.SetActive(false);
    }

    private bool IsMistake(int x, int y)
    {
        TileState state = boardStateTexture.GetState(x, y);

        return IsMistake(state, x, y);
    }
    
    private bool IsMistake(TileState state, int x, int y)
    {
        if(state == TileState.White && solution[x, y])
            return true;

        if (state == TileState.Black && !solution[x, y])
            return true;

        return false;
    }

    public void ClearErrors()
    {
        //find all mistakes, then clear them and all the squares adjacent to them
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                // Check if there's an error at this position
                if (!IsMistake(x,y)) 
                    continue;
                
                int mistakeRegion = regionMap[x, y];
                
                // Clear the error and the adjacent squares
                for (int i = -1; i <= 1; i++)
                {
                    for (int j = -1; j <= 1; j++)
                    {
                        int nx = x + i;
                        int ny = y + j;
                        
                        // Check if the position is within the bounds of the board
                        if (nx < 0 || nx >= boardStateTexture.Width || ny < 0 || ny >= boardStateTexture.Height)
                            continue;
                            
                        //don't clear walls
                        int nRegion = regionMap[nx, ny];
                        
                        if(nRegion < 0 || nRegion != mistakeRegion)
                            continue;
                        
                        if(boardStateTexture.IsSolved(nx, ny))
                            continue;

                        FlipTile(new Vector2Int(nx, ny), TileState.Empty, true);
                    }
                }
            }
        }

        // Apply the changes to the textures
        displayTexture.Apply();
        
        // Clear undo history since we've modified the board state
        undoSystem.ClearHistory();
        
        SFX?.PlayClearErrors();
        OnErrorsCleared?.Invoke();
    }

    public int GetRegionAtCenterOfScreen()
    {
        var bounds = cameraController2D.GetCameraBounds();
        Vector2Int center = new Vector2Int(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
        return regionMap[center.x, center.y];
    }

    public int GetRegionAtScreenPosition(Vector2 screenPosition)
    {
        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 0));
        Vector2Int tilePosition = new Vector2Int(Mathf.RoundToInt(worldPosition.x), Mathf.RoundToInt(worldPosition.y));

        if (tilePosition.x < 0 || tilePosition.x >= regionMap.GetLength(0) ||
            tilePosition.y < 0 || tilePosition.y >= regionMap.GetLength(1))
        {
            return -1;
        }

        return regionMap[tilePosition.x, tilePosition.y];
    }

    public void SaveImmediate()
    {
        SaveProgress();
        SaveReplay();
    }
    
    // RecoverLostProgress has been removed from the package.
    // Achievement-based progress recovery is game-specific and belongs in the host project.
    
    public void SetHighlightBrightness(float brightness)
    {
        for (int i = 0; i < tileHighlights.Length; i++)
        {
            //the central tile is always full brightness
            if(i == 4)
                continue;
            
            Transform tileHighlight = tileHighlights[i];
            tileHighlight.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, brightness);
        }
    }

    public void DebugPruneAll()
    {
        StartCoroutine(PruneAllRegions());
    }

    public void DebugClearHighlightedRegion()
    {
        if (HighlightedRegion < 0 || HighlightedRegion >= regions.Count)
        {
            Debug.LogError("No valid region is currently highlighted");
            return;
        }

        Debug.Log($"Clearing highlighted region {HighlightedRegion}");

        // Clear all tiles in the highlighted region to empty state
        foreach (Vector2Int pos in regions[HighlightedRegion])
        {
            boardStateTexture.SetState(pos.x, pos.y, TileState.Empty);
            displayTexture.SetPixel(pos.x, pos.y, GetEmptyColor(pos.x, pos.y));
        }

        displayTexture.Apply();

        // Update any on-screen tiles
        foreach (Vector2Int pos in regions[HighlightedRegion])
        {
            if (onScreenTiles.TryGetValue(pos, out var tile))
            {
                tile.SetStateInstant(TileState.Empty);
            }
        }

        // Mark region as no longer solved
        solvedRegions[HighlightedRegion] = false;

        // Recalculate progress
        CalculateProgress();

        // Save the game state
        SaveProgress();

        SetDifficulty(currentDifficulty);
    }
    
    private IEnumerator PruneAllRegions()
    {
        for (int i = 0; i < regions.Count; i++)
        {
            yield return PruneRegionCoroutine(i, currentDifficulty);
        }
    }

    public void DebugFillTinyAndHugeRegions()
    {
        const int tinyRegionSize = 20;
        const int hugeRegionSize = 1000;
        
        for (int i = 0; i < regions.Count; i++)
        {
            if(regions[i].Count < tinyRegionSize || regions[i].Count > hugeRegionSize)
            {
                foreach (var pos in regions[i])
                {
                    FlipTile(pos, solution[pos.x, pos.y] ? TileState.Black : TileState.White, true);
                }
            }
        }
    }

    public void DebugSolveAllButSmallest()
    {
        if (regions.Count == 0)
        {
            Debug.LogWarning("No regions found");
            return;
        }

        const int minRegionSize = 15;

        // Find the smallest region that meets the minimum size threshold using LINQ
        var validRegions = regions
            .Select((region, index) => new { Region = region, Index = index })
            .Where(x => x.Region.Count >= minRegionSize)
            .OrderBy(x => x.Region.Count).ToArray();

        if (!validRegions.Any())
        {
            Debug.LogWarning($"No regions found with at least {minRegionSize} tiles");
            return;
        }

        int smallestRegionIndex = validRegions.First().Index;
        int smallestRegionSize = regions[smallestRegionIndex].Count;

        Debug.Log($"Solving all regions except smallest valid region {smallestRegionIndex} (size: {smallestRegionSize}, min threshold: {minRegionSize})");

        int replayStep = replayIndex + 1;
        int solvedTiles = 0;
        
        // Solve all regions except the smallest one
        for (int i = 0; i < regions.Count; i++)
        {
            if(regionMappingRepository.GetRegionMapping(SceneId, i).Type != RegionType.Discovery)
                continue;
                
#if DEMO
            if (!DemoRegionIndices.Contains(i))
                continue;
#endif
            
            if (i == smallestRegionIndex)
                continue; // Skip the smallest region
            
            foreach (var pos in regions[i])
            {
                bool black = solution[pos.x, pos.y];
                TileState state = black ? TileState.Black : TileState.White;
                if (boardStateTexture.GetState(pos.x, pos.y) != state)
                {
                    replayData[pos.x, pos.y] = replayStep++;
                    boardStateTexture.SetState(pos.x, pos.y, state);
                    displayTexture.SetPixel(pos.x, pos.y, GetDisplayColor(state));
                    solvedTiles++;
                }
            }
        }

        // Update progress counts
        ProgressCount += solvedTiles;
        SolvedCount += solvedTiles;
        replayIndex = replayStep - 1;
        
        displayTexture.Apply();

        _ = CheckRegionCompletion();
        GrantProgressAchievements();
        GrantRegionAchievements();
        GrantEdgeAchievements();
        
        ForceRefreshBoard();
        SaveProgress();
    }

    public void RevealBoard()
    {
        Ease.EaseTarget(1f, Easing.Quintic.Out, 0f, t =>
        {
            fullIllustration.color = new Color(1f, 1f, 1f, t);
        }, () =>
        {
            fullIllustration.color = new Color(1f, 1f, 1f, 1f);
            foreach (GameObject normalEffect in normalEffects)
            {
                normalEffect.SetActive(true);
            }
        });
    }

    public void DebugRegenFullClueSet()
    {
        EnsureClueDirectoryExists();
        clues = MosaicGenerator.GenerateFullClueSetRegionLocked(solution, regionMap);

        string path = GetAbsolutePathForClueFile(currentDifficulty);
        //save
        File.WriteAllText(path, MosaicSolver.GetCluesAsCSV(clues));
    }
    
    public void MoveHighlightTo(Vector2Int pos)
    {
        Vector2Int[] regionNeighbours = GetRegionNeighbours(pos);

        for (int i = 0; i < tileHighlights.Length; i++)
        {
            if (i >= regionNeighbours.Length)
            {
                tileHighlights[i].position = new Vector3(-3000, -3000, -1f);
            }
            else
            {
                Vector2Int n = regionNeighbours[i];
                tileHighlights[i].position = new Vector3(n.x, n.y, ClickableTile.GetZOffset(n)-5f);
            }
        }
    }
    
    public void ResetGamePadCursor()
    {
        MoveHighlightTo(focusPos);
    }

    // Returns the indices of Discovery regions that are revealed (not fully Hidden) and not yet solved.
    // Used by the hint system instead of calling GameProgressManager.
    private List<int> GetRevealedUnsolvedDiscoveryRegions()
    {
        List<int> result = new List<int>();

        for (int r = 0; r < regions.Count; r++)
        {
            if (solvedRegions[r])
                continue;

            bool isDiscovery = false;
            bool isRevealed = false;

            foreach (Vector2Int pos in regions[r])
            {
                if (regionTypeMap[pos.x, pos.y] == RegionType.Discovery)
                    isDiscovery = true;

                if (boardStateTexture.GetState(pos.x, pos.y) != TileState.Hidden)
                    isRevealed = true;

                if (isDiscovery && isRevealed)
                    break;
            }

            if (isDiscovery && isRevealed)
                result.Add(r);
        }

        return result;
    }

    private void HideTileHighlights()
    {
        foreach (Transform tileHighlight in tileHighlights)
        {
            tileHighlight.position = new Vector3(-3000, -3000, -1f);
        }
    }

    public void EnsureOnscreen(Vector2Int pos)
    {
        if(cameraController2D.IsPanning)
            return;
        
        if (pos.x < 0 || pos.x >= boardStateTexture.Width ||
            pos.y < 0 || pos.y >= boardStateTexture.Height) 
            return;
        
        RectInt bounds = cameraController2D.GetCameraBounds();

        //contract by 1
        bounds.xMin += 1;
        bounds.xMax -= 1;
        bounds.yMin += 1;
        bounds.yMax -= 1;

        Vector2Int offset = bounds.size / 2;
        
        if (!bounds.Contains(pos))
        {
            Vector3Int panTarget = Vector3Int.FloorToInt(cameraController2D.GetCurrentPosition());
            
            if (pos.x >= bounds.xMax || pos.x <= bounds.xMin)
            {
                panTarget.x = pos.x;
            }

            if (pos.y >= bounds.yMax || pos.y <= bounds.yMin)
            {
                panTarget.y = pos.y;
            }

            cameraController2D.PanTowards(new Vector2(panTarget.x, panTarget.y));
        }
    }

    public void OnAnyClick(bool primaryClick)
    {
        if(isPointerOverUIElement)
            return;

        if (ShiftHeld)
            primaryClick = !primaryClick;
        
        if(onScreenTiles.TryGetValue(focusPos, out ClickableTile tile))
        {
            if(tile.Clicked == false)
                OnTileClick(tile, primaryClick);
        }
    }

    public void FrameRegion(int regionID, bool allowZoomIn = false)
    {
        if (regionID < 0 || regionID >= regions.Count)
            return;
            
        var regionTiles = regions[regionID];
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        
        foreach (var pos in regionTiles)
        {
            minX = Mathf.Min(minX, pos.x);
            minY = Mathf.Min(minY, pos.y);
            maxX = Mathf.Max(maxX, pos.x);
            maxY = Mathf.Max(maxY, pos.y);
        }

        cameraController2D.FrameRegion(new Vector2(minX, minY), new Vector2(maxX, maxY), allowZoomIn);
    }

    public void FrameRegions(bool allowZoomIn = false, params int[] regionIDs)
    {
        if (regionIDs == null || regionIDs.Length == 0)
            return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (int regionID in regionIDs)
        {
            if (regionID < 0 || regionID >= regions.Count)
                continue;

            var regionTiles = regions[regionID];
            foreach (var pos in regionTiles)
            {
                minX = Mathf.Min(minX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxX = Mathf.Max(maxX, pos.x);
                maxY = Mathf.Max(maxY, pos.y);
            }
        }

        if (minX != float.MaxValue) // Valid regions were found
        {
            cameraController2D.FrameRegion(new Vector2(minX, minY), new Vector2(maxX, maxY), allowZoomIn);
        }
    }

    public void ZoomToUnsolvedRegion(int regionID)
    {
        if (regionID < 0 || regionID >= regions.Count)
        {
            Debug.LogError($"Invalid region ID {regionID} for zoom. Valid range is 0-{regions.Count - 1}");
            return;
        }

        var regionTiles = regions[regionID];
        Vector2Int regionCenter = GetRegionCenter(regionID);

        // Find the most central empty tile in the region
        Vector2Int bestTile = regionCenter;
        float bestDistance = float.MaxValue;
        bool foundEmptyTile = false;

        foreach (var pos in regionTiles)
        {
            TileState state = boardStateTexture.GetState(pos.x, pos.y);

            // Log error if we find hidden tiles - this shouldn't happen in an unlocked region
            if (state == TileState.Hidden)
            {
                Debug.Log($"Found hidden tile at {pos} in region {regionID}. Not zooming in.");
                return;
            }

            // Only consider empty tiles
            if (state == TileState.Empty)
            {
                float distance = Vector2Int.Distance(pos, regionCenter);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTile = pos;
                    foundEmptyTile = true;
                }
            }
        }

        // If no empty tiles found, use region center anyway
        if (!foundEmptyTile)
        {
            Debug.Log($"No empty tiles found in region {regionID}.");
            return;
        }

        Debug.Log($"Zooming to tile {bestTile} in region {regionID} (center: {regionCenter})");

        // Use the same zoom behavior as the hint system
        if (IsZoomedIn && Defines.IsMobile())
        {
            cameraController2D.PanTowards(new Vector2(bestTile.x, bestTile.y));
        }
        else
        {
            cameraController2D.PanAndZoomInOn(new Vector2(bestTile.x, bestTile.y));
        }
    }

    private Vector2Int GetRegionCenter(int regionID)
    {
        if (regionID < 0 || regionID >= regions.Count)
            return Vector2Int.zero;
            
        var regionTiles = regions[regionID];
        int sumX = 0, sumY = 0;
        
        foreach (var pos in regionTiles)
        {
            sumX += pos.x;
            sumY += pos.y;
        }
        
        return new Vector2Int(sumX / regionTiles.Count, sumY / regionTiles.Count);
    }

    public bool NudgeToNearestRegion(Vector2 direction)
    {
        if (direction == Vector2.zero)
            return false;

        List<int> eligibleRegions = new List<int>();

        var revealedUnsolvedRegions = GetRevealedUnsolvedDiscoveryRegions();
        eligibleRegions.AddRange(revealedUnsolvedRegions);

        int startingRegion = regionMap[focusPos.x, focusPos.y];

        for (int regionIndex = 0; regionIndex < regions.Count; regionIndex++)
        {
            if(regionIndex == startingRegion)
                continue;
            
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, regionIndex);
            if (mapping != null && mapping.Type == RegionType.Door)
            {
                eligibleRegions.Add(regionIndex);
            }
        }

        if (eligibleRegions.Count == 0)
            return false;

        Vector2Int currentFocus = focusPos;
        int closestRegion = -1;
        float closestDistance = float.MaxValue;
        float bestDirectionAlignment = -1f;

        
        foreach (int regionIndex in eligibleRegions)
        {
            Vector2Int regionCenter = GetRegionCenter(regionIndex);
            Vector2 toRegion = ((Vector2)(regionCenter - currentFocus)).normalized;

            float alignment = Vector2.Dot(direction.normalized, toRegion);
            
            if (alignment > 0.3f)
            {
                float distance = Vector2Int.Distance(currentFocus, regionCenter);

                Debug.Log($"Found region {regionIndex} at {regionCenter} with alignment {alignment} and distance {distance}");
                
                if (distance < closestDistance)
                {
                    closestRegion = regionIndex;
                    closestDistance = distance;
                    bestDirectionAlignment = alignment;
                }
            }
        }

        if (closestRegion >= 0)
        {
            Vector2Int regionCenter = GetRegionCenter(closestRegion);
            focusPos = regionCenter;
            return true;
        }

        return false;
    }

    public void CheckForNewlyUnlockedRegions(Action onFinished = null)
    {
        SaveProgress();
        StartCoroutine(CheckAndRevealNewlyUnlockedRegions(onFinished));
    }

    private IEnumerator CheckAndRevealNewlyUnlockedRegions(Action onFinished)
    {
        List<int> regionsToReveal = new List<int>();

        // Check each discovery region to see if it's unlocked but currently hidden
        for (int r = 0; r < regions.Count; r++)
        {
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, r);

            if(Defines.IsDemo() && !DemoRegionIndices.Contains(r))
                continue;
            
            if (mapping.Type == RegionType.Discovery && mapping.IsGated)
            {
                // Check if region is unlocked but has hidden tiles
                bool isUnlocked = IsRegionUnlocked(r);
                bool hasHiddenTiles = false;

                foreach (var pos in regions[r])
                {
                    if (boardStateTexture.GetState(pos.x, pos.y) == TileState.Hidden)
                    {
                        hasHiddenTiles = true;
                        break;
                    }
                }

                if (isUnlocked && hasHiddenTiles)
                {
#if DEMO
                    if (!DemoRegionIndices.Contains(r))
                    {
                        Debug.Log($"Skipping region {r} reveal - not in demo region indices");
                        continue;
                    }
#endif
                    Debug.Log($"Found newly unlocked region: {r}");
                    regionsToReveal.Add(r);
                }
            }
        }

        if (regionsToReveal.Count == 0)
        {
            Debug.Log("No regions to reveal");
            onFinished?.Invoke();
            yield break;
        }

        // Set flag and disable input immediately to prevent evidence button and door clicks during delay
        IsRevealingRegions = true;
        SetInputEnabled(false);

        // Wait for cutscene UI to go away
        yield return new WaitForSeconds(0.5f);

        // Zoom fully out to show the reveal
        cameraController2D.FullyZoomOutSmooth();
        cameraController2D.SetInputEnabled(false);
        // Wait for zoom to complete
        yield return new WaitForSeconds(0.5f);

        int pitchIndex = 0;
        
        // Reveal regions one by one with animation
        foreach (int regionIndex in regionsToReveal)
        {
            Debug.Log($"Revealing newly unlocked region: {regionIndex}");
            float pitch = AudioExtensions.GetSemitonePitch(pitchIndex);
            pitchIndex++;
            SFX?.RegionReveal(pitch);
            yield return RevealUnlockedRegion(regionIndex, 0.6f);
            //yield return new WaitForSeconds(0.5f); // Brief pause between regions
        }
        
        cameraController2D.SetInputEnabled(true);

        // Re-enable input after region reveals complete
        SetInputEnabled(true);
        IsRevealingRegions = false;

        yield return null;

        onFinished?.Invoke();
    }

    public IEnumerator RevealUnlockedRegion(int regionID, float duration)
    {
        var regionTiles = regions[regionID];
        if (regionTiles == null || regionTiles.Count == 0)
        {
            Debug.LogWarning($"RevealUnlockedRegion: No tiles found for region {regionID}");
            yield break;
        }


        // Get center of the region
        Vector2Int center = GetRegionCenter(regionID);

        // Sort tiles by distance from center
        var sortedTiles = new List<(Vector2Int pos, float dist)>();

        foreach (var pos in regionTiles)
        {
            float dist = Vector2.Distance(center, pos);
            sortedTiles.Add((pos, dist));
        }

        sortedTiles.Sort((a, b) => a.dist.CompareTo(b.dist));

        float timeSinceLastTile = 0f;
        int currentTileIndex = 0;

        float delay = duration / sortedTiles.Count;

        int tilesRevealed = 0;
        var firstTile = sortedTiles[0];
        var emptyColor = GetEmptyColor(firstTile.pos.x, firstTile.pos.y);

        while (currentTileIndex < sortedTiles.Count)
        {
            timeSinceLastTile += Time.deltaTime;

            while (timeSinceLastTile > 0)
            {
                var (pos, _) = sortedTiles[currentTileIndex];

                // Change tile from Hidden to Empty
                boardStateTexture.SetState(pos.x, pos.y, TileState.Empty);
                displayTexture.SetPixel(pos.x, pos.y, emptyColor);

                if (onScreenTiles.TryGetValue(pos, out var tile))
                {
                    var baseColor = baseIllustration.GetPixel(pos.x, pos.y);
                    tile.SetColor(baseColor);
                    tile.SetStateInstant(TileState.Empty);
                }

                currentTileIndex++;
                tilesRevealed++;

                if (currentTileIndex >= sortedTiles.Count)
                    break;

                // Subtract the time used for this tile
                timeSinceLastTile -= delay;
            }
            
            displayTexture.Apply();
            yield return new WaitForEndOfFrame();
        }
        
        SetupHintsAndErrors();
    }

    public bool IsRegionRevealed(int regionID)
    {
        foreach (var pos in regions[regionID])
        {
            if (boardStateTexture.GetState(pos) == TileState.Empty)
                return false;
        }
        
        return true;
    }

    private HashSet<int> RevealingRegions = new HashSet<int>();
    
    private IEnumerator RevealRegionTiles(int regionID, float duration, bool linked = false)
    {
        var regionTiles = regions[regionID];
        if (regionTiles == null || regionTiles.Count == 0)
            yield break;
        
        if (RevealingRegions.Add(regionID) == false)
        {
            Debug.LogError("Skipping duplicate reveal anim for region "+regionID);
            yield break;
        }

        yield return new WaitForSeconds(0.1f);

        // Get center of the region
        Vector2Int center = GetRegionCenter(regionID);
        
        // Sort tiles by distance from center
        var sortedTiles = new List<(Vector2Int pos, float dist)>();
        
        foreach (var pos in regionTiles)
        {
            float dist = Vector2.Distance(center, pos);
            sortedTiles.Add((pos, dist));
        }
        
        sortedTiles.Sort((a, b) => a.dist.CompareTo(b.dist));

        float timeSinceLastTile = 0f;
        float timeSinceLastSound = 0f;
        int currentTileIndex = 0;
        
        float delay = duration / sortedTiles.Count;
        
        while (currentTileIndex < sortedTiles.Count)
        {
            timeSinceLastTile += Time.deltaTime;
            timeSinceLastSound += Time.deltaTime;

            while (timeSinceLastTile > 0)
            {
                var (pos, _) = sortedTiles[currentTileIndex];
                var solvedColor = solvedIllustration.GetPixel(pos.x, pos.y);
                
                displayTexture.SetPixel(pos.x, pos.y, solvedColor);

                var state = solution[pos.x, pos.y] ? TileState.SolvedBlack : TileState.SolvedWhite;
                
                if (linked)
                    state = TileState.Linked;
                
                boardStateTexture.SetState(pos.x, pos.y, state);
                
                if (onScreenTiles.TryGetValue(pos, out var tile))
                {
                    tile.SetColor(solvedColor);
                    tile.SetStateInstant(state);
                    tile.SetClueVisible(false);
                }
                
                currentTileIndex++;
                
                if(currentTileIndex >= sortedTiles.Count)
                    break;
                
                // Subtract the time used for this tile
                timeSinceLastTile -= delay;
            }
            
            // Play sound effect at regular intervals
            if (timeSinceLastSound >= RegionRevealClickFrequency)
            {
                float t = (float)currentTileIndex / sortedTiles.Count;
                float pitch = t * 0.25f + 0.75f;
                float volume = t * 0.25f + 0.25f;
                SFX?.TileReveal(volume, pitch);
                timeSinceLastSound = 0f;
            }
        
            RevealingRegions.Remove(regionID);
            displayTexture.Apply();
            yield return new WaitForEndOfFrame();
        }
    }

    public IEnumerator ZoomInAndRevealIllustrationAndThen(int regionID, Action onComplete)
    {
        SetInputEnabled(false);
        
        FrameRegion(regionID, true);
        
        // Wait for camera movement to complete (FrameRegion uses 0.5f duration internally)
        yield return new WaitForSeconds(0.6f);
            
        SolveRegion(regionID);

        // Wait for camera movement to complete (matches CameraController2D.moveDuration)
        yield return new WaitForSeconds(1f);

        // Calculate reveal duration and start animation
        float revealDuration = RegionRevealDuration;
        yield return RevealRegionTiles(regionID, revealDuration);

        SaveImmediate();

        // Small delay before completion
        yield return new WaitForSeconds(0.5f);

        SetupHintsAndErrors();

        SetInputEnabled(true);
        OnRegionCompleted?.Invoke(regionID);
        
        // Call completion callback
        onComplete?.Invoke();
    }
    
    public IEnumerator RevealIllustrationAndThen(int regionID, Action onComplete, bool linked = false)
    {
        SetInputEnabled(false);
        
        // First frame the region
        FrameRegion(regionID);

        // Wait for camera movement to complete (matches CameraController2D.moveDuration)
        yield return new WaitForSeconds(1f);

        // Calculate reveal duration and start animation
        float revealDuration = RegionRevealDuration;
        yield return RevealRegionTiles(regionID, revealDuration, linked);

        GrantProgressAchievements();
        
        // Small delay before completion
        yield return new WaitForSeconds(revealDuration);

        SetupHintsAndErrors();
        
        yield return new WaitForSeconds(0.6f);

        // Fully zoom out before completion
        cameraController2D.FullyZoomOutSmooth();
        
        // Wait for zoom out to complete
        yield return new WaitForSeconds(1f);

        SetInputEnabled(true);
        
        // Call completion callback
        onComplete?.Invoke();
    }

    public void OnClickRelease()
    {
        MakeAllTilesClickable();
        
        drawingRegion = -1;

        bool regionCompletedThisClick = false;
        bool wasBossRegion = false;
            
        foreach (int regionID in CheckRegionCompletion(true))
        {
            regionCompletedThisClick = true;
                
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, regionID);

            if (mapping.IsOpponentRegion)
            {
                wasBossRegion = true;
                
                if(!Defines.IsDevelopment())
                    Debug.LogError("player should not be completing the AI's regions");
            }

            if (mapping.IsLinked)
            {
                CompleteLinkedRegion(mapping.LinkedRegion);
                HideCluesInRegion(mapping.LinkedRegion);
                StartCoroutine(RevealIllustrationAndThen(mapping.LinkedRegion, null, true));
            }
            
            HideCluesInRegion(regionID);
            StartCoroutine(RevealIllustrationAndThen(regionID, ()=>
            {
                SaveProgress();
                GrantProgressAchievements();
                OnRegionCompleted?.Invoke(regionID);
            }));
        }
            
        if (regionCompletedThisClick)
        {
            if (wasBossRegion)
            {
                SFX?.BossRegionComplete();
            }
            else
            {
                SFX?.RegionComplete();
            }
        }
            
        Cursor.SetCursor(cursors[1], cursorHotspot, CursorMode.Auto);
            
        GrantProgressAchievements();
        //GrantEdgeAchievements();

        SaveDirty = true;

        if (isPointerOverUIElement) 
            return;

        if (focusPos.x < 0 || focusPos.x >= boardStateTexture.Width ||
            focusPos.y < 0 || focusPos.y >= boardStateTexture.Height) 
            return;
            
        //check if clicking on a completed region
        int region = regionMap[focusPos.x, focusPos.y];
            
        if (region < 0) 
            return;

        //bool allowClickingOnRegions = !Defines.IsMobile() || !IsZoomedIn;
        
        if (!regionCompletedThisClick && 
            !cameraController2D.IsDragGesture && 
            inputEnabled && 
            !IsZoomedIn)
        {
            bool isSolved = solvedRegions[region];
            OnRegionClicked?.Invoke(region, isSolved);
        }
    }

    public void HandleMousePositionChange(Vector2 mousePos)
    {
        lastMousePos = mousePos;
        
        isPointerOverUIElement = CheckIfOverUIElement(lastMousePos);
        bool mouseIsOnScreen = mousePos.x >= 0 && mousePos.x < Screen.width &&
                               mousePos.y >= 0 && mousePos.y < Screen.height;
        if (isPointerOverUIElement)
        {
            HideTileHighlights();

            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            focusPos = cameraController2D.ScreenToGrid(screenCenter);
        }
        else if(mouseIsOnScreen)
        {
            focusPos = cameraController2D.ScreenToGrid(lastMousePos);
            MoveHighlightTo(focusPos);
        }
    }

    public void CaptureMouseInput()
    {
        //Cursor.lockState = CursorLockMode.Locked;
        isPointerOverUIElement = false;
    }

    public void LockCamera()
    {
        cameraController2D.SetInputEnabled(false);
    }

    public void UnlockCamera()
    {
        cameraController2D.SetInputEnabled(true);
    }

    public void DebugSolveSpiral()
    {
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        int left = 0;
        int right = width - 1;
        int top = height - 1;
        int bottom = 0;
        
        int replayStep = 1;
        
        while (left <= right && bottom <= top)
        {
            // Move right along top
            for (int x = left; x <= right; x++)
            {
                if (regionMap[x, top] >= 0 && !boardStateTexture.IsSolved(x, top))
                {
                    bool black = solution[x, top];
                    replayData[x, top] = replayStep++;
                    FlipTile(new Vector2Int(x, top), black ? TileState.Black : TileState.White, true);
                }
            }
            top--;

            // Move down along right
            for (int y = top; y >= bottom; y--)
            {
                if (regionMap[right, y] >= 0 && !boardStateTexture.IsSolved(right, y))
                {
                    bool black = solution[right, y];
                    replayData[right, y] = replayStep++;
                    FlipTile(new Vector2Int(right, y), black ? TileState.Black : TileState.White, true);
                }
            }
            right--;

            // Move left along bottom
            for (int x = right; x >= left; x--)
            {
                if (regionMap[x, bottom] >= 0 && !boardStateTexture.IsSolved(x, bottom))
                {
                    bool black = solution[x, bottom];
                    replayData[x, bottom] = replayStep++;
                    FlipTile(new Vector2Int(x, bottom), black ? TileState.Black : TileState.White, true);
                }
            }
            bottom++;

            // Move up along left
            for (int y = bottom; y <= top; y++)
            {
                if (regionMap[left, y] >= 0 && !boardStateTexture.IsSolved(left, y))
                {
                    bool black = solution[left, y];
                    replayData[left, y] = replayStep++;
                    FlipTile(new Vector2Int(left, y), black ? TileState.Black : TileState.White, true);
                }
            }
            left++;
        }

        displayTexture.Apply();
        replayIndex = replayStep;
        
        CheckRegionCompletion();
        GrantProgressAchievements();
        GrantRegionAchievements();
        GrantEdgeAchievements();
        
        ForceRefreshBoard();
        SaveProgress();
    }

    public void SetInputEnabled(bool enable)
    {
        Debug.Log($"SetInputEnabled: {enable}");
        
        if(enable)
            UnlockCamera();
        else
            LockCamera();

        inputEnabled = enable;
        GetComponent<TileBoardController>().SetInputEnabled(enable);
    }

    public bool CanUndo => undoSystem.CanUndo;

    public int UndoMoveCount => undoSystem.MoveCount;

    public string GetUndoHistoryDebug(int maxMoves = 5)
    {
        return undoSystem.GetHistoryDebugString(maxMoves);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public void DebugTestUndoSystem()
    {
        Debug.Log($"Undo system test - Current move count: {undoSystem.MoveCount}");
        Debug.Log($"Can undo: {undoSystem.CanUndo}");
        
        if (undoSystem.CanUndo)
        {
            var lastMove = undoSystem.PeekLastMove();
            if (lastMove.HasValue)
            {
                var move = lastMove.Value;
                Debug.Log($"Last move: {move.position} {move.fromState} -> {move.toState}");
            }
        }
        
        Debug.Log(GetUndoHistoryDebug(10));
    }

    public void SetDifficulty(int difficulty = 0)
    {
        currentDifficulty = difficulty;
        
        Debug.Log($"Difficulty changed to {currentDifficulty}");
        
        //load clues from file
        try
        {
            clues = LoadCluesFromDisk(currentDifficulty);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error loading clues: {e.Message}");
            
            if(Application.isEditor)
                DebugRegenFullClueSet();
        }

        List<int> hiddenRegions = new List<int>();
        
        for (int r = 0; r < regions.Count; r++)
        {
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, r);

            if (mapping.Type is RegionType.Empty or RegionType.Door or RegionType.Walkable)
            {
                hiddenRegions.Add(r);
            }
#if DEMO
            if (!DemoRegionIndices.Contains(r))
            {
                hiddenRegions.Add(r);
            }
#endif
        }
        
        HideRegions(hiddenRegions.ToArray());
        
        cluesVisible.Populate(true);

        // Hide clues in solved regions
        for (int r = 0; r < regions.Count; r++)
        {
            if (solvedRegions[r])
            {
                foreach (Vector2Int pos in regions[r])
                {
                    cluesVisible[pos.x, pos.y] = false;
                }
            }
        }

        SetupHintsAndErrors();
        ForceRefreshBoard();
    }

    public void ResumeFromLastPoint()
    {
        if (ProgressCount == 0)
        {
            //FindNearestHint();
        }
        else
        {
            //cameraController2D.ZoomToLastPosition();
            ResetGamePadCursor();
        }
    }

    // AI Opponent Integration Methods
    public BoardState BoardStateTexture => boardStateTexture;
    public int[,] Clues => clues;
    public int[,] RegionMap => regionMap;

    public bool TryFindHintInOpponentRegion(Vector2Int aiCursorPos, out Vector2Int hintPos)
    {
        hintPos = Vector2Int.zero;
        
        if (opponentTiles[aiCursorPos.x, aiCursorPos.y] == false)
        {
            int regionID = regionMap[aiCursorPos.x, aiCursorPos.y];
            Debug.LogError("AI is looking for a clue in a region that doesn't belong to them = "+regionID);
            return false;
        }
        
        HintInfo hintInfo = FindNearestHint(aiCursorPos);

        if (hintInfo.Result != HintResult.NoneFound)
        {
            // For advanced hints, check both tiles and use the first one that's in opponent region
            if (hintInfo.Result == HintResult.FoundAdvanced)
            {
                if (opponentTiles[hintInfo.FirstTile.x, hintInfo.FirstTile.y])
                {
                    hintPos = hintInfo.FirstTile;
                    return true;
                }
                if (opponentTiles[hintInfo.SecondTile.x, hintInfo.SecondTile.y])
                {
                    hintPos = hintInfo.SecondTile;
                    return true;
                }
            }
            else if (hintInfo.Result == HintResult.FoundSimple)
            {
                // Verify the hint is in an opponent region
                if (opponentTiles[hintInfo.FirstTile.x, hintInfo.FirstTile.y])
                {
                    hintPos = hintInfo.FirstTile;
                    return true;
                }
            }
        }

        return false;
    }

    public bool FlipTileForAI(Vector2Int pos, TileState newState, bool instant = false)
    {
        FlipTile(pos, newState, instant);

        bool regionComplete = false;
        
        foreach (int regionID in CheckRegionCompletion(true))
        {
            regionComplete = true;
            
            RegionMapping mapping = regionMappingRepository.GetRegionMapping(SceneId, regionID);

            if (mapping.IsOpponentRegion)
            {
                Debug.Log("AI has solved a region");
            }
            else
            {
                Debug.LogError("AI should not be completing the player's regions");
            }
        
            if (mapping.IsLinked)
            {
                CompleteLinkedRegion(mapping.LinkedRegion);
                HideCluesInRegion(mapping.LinkedRegion);
                StartCoroutine(RevealIllustrationAndThen(mapping.LinkedRegion, null, true));
            }
            
            HideCluesInRegion(regionID);
            StartCoroutine(RevealIllustrationAndThen(regionID, ()=>
            {
                SaveProgress();
                OnAICompletedRegion(regionID);
            }));
        }
        
        if (regionComplete)
        {
            SFX?.BossRegionComplete();
        }

        return regionComplete;
    }
    
    private void OnAICompletedRegion(int region)
    {
        Debug.Log($"AI completed region {region}");
        // Host subscribes to OnRegionCompleted and calls PrepareWaterfall() if needed.
        OnRegionCompleted?.Invoke(region);
    }

    public bool TryFindUnsolvedOpponentTile(out Vector2Int unsolvedTile)
    {
        int width = boardStateTexture.Width;
        int height = boardStateTexture.Height;
        
        unsolvedTile = Vector2Int.zero;
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (boardStateTexture.GetState(x, y) != TileState.Empty)
                    continue;
                
                if (opponentTiles[x, y])
                {
                    unsolvedTile = new Vector2Int(x, y);
                    return true;
                }
            }
        }

        return false;
    }

    public HashSet<int> GetAllRegions()
    {
        HashSet<int> regions = new HashSet<int>();

        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                regions.Add(regionMap[x, y]);
            }
        }

        return regions;
    }

    public RectInt GetRegionBounds(int regionIndex)
    {
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        bool foundRegion = false;

        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if (regionMap[x, y] == regionIndex)
                {
                    foundRegion = true;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }
        }

        if (!foundRegion)
            return new RectInt(0, 0, 1, 1);

        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private void UpdateCountdownForPosition(Vector2Int pos, int change)
    {
        int region = regionMap[pos.x, pos.y];
        // Update countdown for all clue tiles within 1 square of the placed/removed tile
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                int nx = pos.x + i;
                int ny = pos.y + j;

                // Skip if out of bounds
                if (nx < 0 || nx >= boardStateTexture.Width || ny < 0 || ny >= boardStateTexture.Height)
                    continue;

                //don't change countdown in neighbouring regions!
                if(regionMap[nx,ny] != region)
                    continue;
                
                // Only update clue tiles (tiles with a clue number >= 0)
                if (clues[nx, ny] >= 0)
                {
                    // Update the countdown value in our array
                    countdownValues[nx, ny] += change;

                    // Update the display if the tile is on screen
                    Vector2Int cluePos = new Vector2Int(nx, ny);
                    if (onScreenTiles.TryGetValue(cluePos, out var tile))
                    {
                        tile.SetCountdownDisplay(countdownValues[nx, ny]);
                    }
                }
            }
        }
    }

    public void ShowAllDiscoveryRegions()
    {
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                if(regionTypeMap[x,y] == RegionType.Discovery)
                    boardStateTexture.SetState(x,y,TileState.Empty);
            }
        }
        
        cluesVisible.Populate(true);

        SetupHintsAndErrors();
        ForceRefreshBoard();
    }

    private IEnumerator ShimmerAnimationCoroutine()
    {
        while (true)
        {
            // Wait 3 seconds between shimmer animations
            yield return new WaitForSeconds(3f);

            // Only shimmer if globally enabled, locally enabled, and we're not already doing it
            if (MosaicPrivacyAndSettings.GetShimmerEnabled() && inputEnabled && !isShimmering && displayTexture != null)
            {
                yield return StartCoroutine(ShimmerWaveAnimation());
            }
        }
    }

    private IEnumerator ShimmerWaveAnimation()
    {
        if (displayTexture == null || boardStateTexture == null)
            yield break;

        isShimmering = true;

        int width = displayTexture.width;
        int height = displayTexture.height;
        float waveDuration = 2f; // Total time for wave to cross the screen
        float waveWidth = 32f; // Width of the shimmer wave effect

        // Store original colors for empty tiles only
        Color[,] originalColors = new Color[width, height];
        bool[,] isEmptyTile = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                TileState state = boardStateTexture.GetState(x, y);
                isEmptyTile[x, y] = (state == TileState.Empty);
                
                if (isEmptyTile[x, y])
                {
                    originalColors[x, y] = displayTexture.GetPixel(x, y);
                }
            }
        }

        // Randomize start position
        float centerX = UnityEngine.Random.Range(0f, width);
        float centerY = UnityEngine.Random.Range(0f, height);
        float maxDistance = Mathf.Max(
            Mathf.Sqrt(centerX * centerX + centerY * centerY),
            Mathf.Sqrt((width - centerX) * (width - centerX) + centerY * centerY),
            Mathf.Sqrt(centerX * centerX + (height - centerY) * (height - centerY)),
            Mathf.Sqrt((width - centerX) * (width - centerX) + (height - centerY) * (height - centerY))
        ); // Maximum distance to any corner from random center

        for (float t = 0; t < waveDuration; t += Time.deltaTime)
        {
            float progress = t / waveDuration;
            float waveRadius = progress * maxDistance;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!isEmptyTile[x, y])
                        continue;
                    
                    int region = regionMap[x, y];
                    
                    if(solvedRegions[region] || RevealingRegions.Contains(region))
                        continue;

                    // Calculate distance from center (wave radiates outward)
                    float distanceFromCenter = Mathf.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
                    float distanceFromWave = Mathf.Abs(distanceFromCenter - waveRadius);

                    if (distanceFromWave <= waveWidth)
                    {
                        // Calculate shimmer intensity based on distance from wave center
                        float intensity = 1f - (distanceFromWave / waveWidth);
                        intensity = Mathf.Pow(intensity, 2f); // Smooth falloff

                        // Apply shimmer color
                        Color originalColor = originalColors[x, y];
                        Color shimmerColorLerped = Color.Lerp(originalColor, blackTileColor, 0.5f * intensity);

                        displayTexture.SetPixel(x, y, shimmerColorLerped);
                    }
                    else
                    {
                        // Restore original color for tiles outside the wave
                        displayTexture.SetPixel(x, y, originalColors[x, y]);
                    }
                }
            }

            displayTexture.Apply();
            yield return null;
        }

        // Restore all original colors
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (isEmptyTile[x, y])
                {
                    int region = regionMap[x, y];
                    
                    if(solvedRegions[region] || RevealingRegions.Contains(region))
                        continue;
                    
                    displayTexture.SetPixel(x, y, originalColors[x, y]);
                }
            }
        }

        displayTexture.Apply();
        isShimmering = false;
    }

    public void DebugSolveAllRevealedRegions()
    {
        if (boardStateTexture == null || solution == null || regionMap == null)
        {
            Debug.LogError("Board not properly initialized for debug solve");
            return;
        }

        HashSet<int> revealedRegions = new HashSet<int>();

        // Find all currently revealed regions
        for (int x = 0; x < boardStateTexture.Width; x++)
        {
            for (int y = 0; y < boardStateTexture.Height; y++)
            {
                TileState state = boardStateTexture.GetState(x, y);
                if (state == TileState.Empty)
                {
                    int region = regionMap[x, y];
                    if (region >= 0)
                    {
                        revealedRegions.Add(region);
                    }
                }
            }
        }

        Debug.Log($"Debug solving {revealedRegions.Count} revealed regions");

        // Solve all tiles in revealed regions
        foreach (int region in revealedRegions)
        {
            for (int x = 0; x < boardStateTexture.Width; x++)
            {
                for (int y = 0; y < boardStateTexture.Height; y++)
                {
                    if (regionMap[x, y] == region && boardStateTexture.GetState(x, y) == TileState.Empty)
                    {
                        bool shouldBeBlack = solution[x, y];
                        TileState targetState = shouldBeBlack ? TileState.Black : TileState.White;
                        FlipTile(new Vector2Int(x, y), targetState, true);
                    }
                }
            }
        }

        Debug.Log("Debug solve completed");
    }

    public void ShowIllustration(bool show)
    {
        Debug.Log($"Showing full illustration: {show}");
        fullIllustration.enabled = show;
    }
}
