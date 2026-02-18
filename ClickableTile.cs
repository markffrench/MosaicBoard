using System;
using Framework;
using MosaicPuzzle;
using Framework.Input;
using Helpers;
using InputHelpers;
using MoreMountains.Feedbacks;
using UnityEngine;

public class ClickableTile : MonoBehaviour
{
    [SerializeField] private Sprite[]      tileSprites;

    private static Sprite[] emptyBorderSprites;
    private static Sprite[] numberSprites; // Normal sprites: 0-9 light, 10-19 dark
    private static Sprite[] crypticNumberSprites; // Cryptic sprites: 0-9 light, 10-19 dark

    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SpriteRenderer numberSprite;
    [SerializeField] private TileState      currentState;

    [SerializeField] private ParticleSystem effectPrefab;
    [SerializeField] private ParticleSystem effectDarkPrefab;
    [SerializeField] private ParticleSystem effectEmptyPrefab;
    
    private TileState pendingState;
    
    private bool hint = false;
    private bool error = false;
    private bool nextToWall = false;
    private bool solved = false;
    private bool isOpponentRegion = false;
    private bool isCrypticRegion = false;
    private int Count = -1;
    private bool usingCountdownDisplay = false;

    public Color PixelColor { get; private set; }
    public Color RegionColor { get; private set; }
    public Vector2Int Position { get; private set; }
    public int Region { get; private set; }
    private byte Walls;
    
    private Color blackTileColor = Color.white;
    private Color whiteTileColor = Color.white;
    
    public Action<ClickableTile> OnClickTile;

    private const float BASE_FLIP_DURATION = 0.30f;
    private float flipDuration => Defines.IsMobile() ? BASE_FLIP_DURATION * 0.5f : BASE_FLIP_DURATION;
    private float flipTime;

    public bool Clicked = false;
    
    public static void SetupNumberSprites()
    {
        numberSprites = new Sprite[20];
        crypticNumberSprites = new Sprite[20];

        // Load normal light sprites (0-9) from numbers folder
        for (int i = 0; i < 10; i++)
        {
            string spritePath = "numbers/light_" + i;
            Sprite s = Resources.Load<Sprite>(spritePath);
            numberSprites[i] = s;
        }

        // Load normal dark sprites (10-19) from numbers folder
        for (int i = 0; i < 10; i++)
        {
            string spritePath = "numbers/dark_" + i;
            Sprite s = Resources.Load<Sprite>(spritePath);
            numberSprites[10 + i] = s;
        }

        // Load cryptic light sprites (0-9) from cryptic_numbers folder
        for (int i = 0; i < 10; i++)
        {
            string spritePath = "cryptic_numbers/light_" + i;
            Sprite s = Resources.Load<Sprite>(spritePath);
            crypticNumberSprites[i] = s;
        }

        // Load cryptic dark sprites (10-19) from cryptic_numbers folder
        for (int i = 0; i < 10; i++)
        {
            string spritePath = "cryptic_numbers/dark_" + i;
            Sprite s = Resources.Load<Sprite>(spritePath);
            crypticNumberSprites[10 + i] = s;
        }
    }
    
    public static void SetupWallSprites()
    {
        emptyBorderSprites = new Sprite[16];
        
        string[] spriteNames = {
            "_", 
            "_n", 
            "_e", 
            "_ne", 
            "_s",
            "_ns",
            "_es",
            "_nes",
            "_w",
            "_nw",
            "_ew",
            "_new",
            "_sw",
            "_nsw",
            "_esw",
            "_nesw"
        };

        //0000 -
        //0001 n
        //0010 e
        //0011 ne
        //0100 s
        //0101 ns
        //0110 es
        //0111 nes
        //1000 w
        //1001 nw
        //1010 ew
        //1011 new
        //1100 sw
        //1101 nsw
        //1110 esw
        //1111 nesw

        for (int i = 0; i < spriteNames.Length; i++)
        {
            string spritePath = "border_tiles/empty" + spriteNames[i];
            Sprite s = Resources.Load<Sprite>(spritePath);
            emptyBorderSprites[i] = s;
        }
    }
    
    public void SetPosition(Vector2Int pos)
    {
        Position = pos;

        float zOffset = GetZOffset(pos);
        transform.position = new Vector3(pos.x, pos.y, zOffset);
    }
    
    public static float GetZOffset(Vector2Int pos)
    {
        return -(0.1f * pos.y + 0.001f * pos.x);
    }
    
    private void Update()
    {
        if (flipTime > 0)
        {
            flipTime = Math.Max(flipTime - Time.deltaTime, 0f);
        }
        else
        {
            return;
        }
        
        float t = 1f - Easing.Quartic.Out(flipTime / flipDuration);
        
        t *= 2;
        t -= 1f;
        
        if(t < 0)
            t = -t;

        if (pendingState != currentState && flipTime < flipDuration * 0.5f)
        {
            currentState = pendingState;
            DisplayState();
            SpawnEffect();
        }
        
        transform.localScale = new Vector3(t, 1f, 1f);
    }

    private void SpawnEffect()
    {
        ParticleSystem prefab;
        
        if(currentState == TileState.Empty)
        {
            prefab = effectEmptyPrefab;
        }
        else if (currentState == TileState.White)
        {
            prefab = effectDarkPrefab;
        }
        else 
        {
            prefab = effectPrefab;
        }
        
        ParticleSystem instance = Instantiate(prefab, transform.position, Quaternion.identity);
        
        if (PrivacyAndSettings.GetVibrationSetting() && ControlSchemeSwapper.currentControlScheme == ControlScheme.Touch)
        {
            MMF_Player player = instance.GetComponent<MMF_Player>();
            
            //player.FeedbacksIntensity = ControlSchemeSwapper.currentControlScheme == ControlScheme.Gamepad ? 0f : 1f;
            player?.PlayFeedbacks();
        }

        Destroy(instance.gameObject, 1f);
    }
    
    public void SetStateInstant(TileState state)
    {
        currentState = state;
        DisplayState();
    }
    
    public TileState CurrentState
    {
        get => currentState;
    }

    private void DisplayState()
    {
//        Color inv = new Color(1f - PixelColor.r, 1f - PixelColor.g, 1f - PixelColor.b);
        // var blendTextColor = new Color(PixelColor.r + inv.r * 0.9f,
        //     PixelColor.g + inv.g * 0.9f,
        //     PixelColor.b + inv.b * 0.9f);

        var state = currentState;

#if MEGA_MOSAIC
        //always show state of border cells
        if (nextToWall || PixelColor == Color.black)
        {
            if(state == TileState.SolvedWhite)
                state = TileState.White;
            
            if(state == TileState.SolvedBlack)
                state = TileState.Black;
        }
#endif
        
        var displayState = state;
        
        if (ProjectPrivacyAndSettings.GetInvertedColors())
        {
            if (state == TileState.White)
            {
                displayState = TileState.Black;
            }
            else if (state == TileState.Black)
            {
                displayState = TileState.White;
            }
        }

        switch (displayState)
        {
            case TileState.Empty:
                spriteRenderer.sprite = emptyBorderSprites[Walls];
                spriteRenderer.color = RegionColor;
                numberSprite.color = Color.white;
                break;
            case TileState.White://actually black
                spriteRenderer.sprite = emptyBorderSprites[Walls];
                spriteRenderer.color = whiteTileColor;
                numberSprite.color = Color.white;
                break;
            case TileState.Black://actually white
                spriteRenderer.sprite = emptyBorderSprites[Walls];
                spriteRenderer.color = blackTileColor;
                numberSprite.color = Color.black;
                break;
            case TileState.SolvedWhite:
                spriteRenderer.sprite = emptyBorderSprites[Walls];
                spriteRenderer.color = PixelColor;
                numberSprite.color = Color.white;
                break;
            case TileState.SolvedBlack:
                spriteRenderer.sprite = emptyBorderSprites[Walls];
                spriteRenderer.color = PixelColor;
                numberSprite.color = Color.white;
                break;
            case TileState.Hidden:
                spriteRenderer.sprite = emptyBorderSprites[0];
                spriteRenderer.color = PixelColor;
                numberSprite.color = Color.white;
                break;
        }

        if (state == TileState.Hidden)
        {
            numberSprite.color = Color.clear;
        }
        else if (Count < 0 && !usingCountdownDisplay)
        {
            numberSprite.color = Color.clear;
        }
        else if (error)
        {
            numberSprite.color = Color.red;
        }
        else if (hint)
        {
            numberSprite.color = Color.yellow;
        }
        else if (solved || isOpponentRegion)
        {
            numberSprite.color = numberSprite.color.WithAlpha(0.2f);
        }
    }

    public void FlipTile(TileState newState)
    {
        if (flipTime > 0)
        {
            SetStateInstant(pendingState);
        }
        
        pendingState = newState;
        flipTime = flipDuration;
    }

    public void SetCount(int count)
    {
        this.Count = count;
        usingCountdownDisplay = false;

        if (Count < 0)
        {
            numberSprite.color = Color.clear;
        }
        else
        {
            numberSprite.color = Color.white;
            Sprite[] spriteArray = isCrypticRegion ? crypticNumberSprites : numberSprites;
            
            numberSprite.sprite = spriteArray[count];
        }
    }

    public void SetCountdownDisplay(int countdownValue)
    {
        usingCountdownDisplay = true;

        Sprite[] spriteArray = isCrypticRegion ? crypticNumberSprites : numberSprites;

        if (countdownValue < 0)
        {
            // For negative numbers, show absolute value in red since we don't have negative number sprites
            numberSprite.color = Color.red;
            numberSprite.sprite = spriteArray[Mathf.Abs(countdownValue)];
        }
        else
        {
            // Regular countdown display (including 0)
            numberSprite.color = Color.white;
            numberSprite.sprite = spriteArray[countdownValue];
        }
    }
    
    public void SetColor(Color color)
    {
        PixelColor = color;
    }
    
    public void SetTileColors(Color blackColor, Color whiteColor)
    {
        blackTileColor = blackColor;
        whiteTileColor = whiteColor;
    }
    
    public void SetClueVisible(bool visible)
    {
        numberSprite.enabled = visible;
    }

    public void SetOpponentRegion(bool isOpponent)
    {
        isOpponentRegion = isOpponent;
    }

    public void SetCrypticRegion(bool isCryptic)
    {
        isCrypticRegion = isCryptic;
    }

    // public void OnPointerDown(PointerEventData eventData)
    // {
    //     if(Input.GetMouseButton(0) || Input.GetMouseButton(1))
    //         OnClickTile?.Invoke(this);
    // }
    //
    // public void OnPointerEnter(PointerEventData eventData)
    // {
    //     if(Input.GetMouseButton(0) || Input.GetMouseButton(1))
    //         OnClickTile?.Invoke(this);
    // }

    public void ShowClueState(bool error, bool hint, bool nextToWall, bool solved)
    {
        this.error = error;
        this.hint = hint;
        this.nextToWall = nextToWall;
        this.solved = solved;

        DisplayState();
    }
    
    public void SetRegion(int region, Color regionColor)
    {
        Region = region;
        RegionColor = regionColor;
    }
    
    public void SetWalls(bool n, bool e, bool s, bool w)
    {
        Walls = Walls.WithBit(0, n)
                    .WithBit(1, e)
                    .WithBit(2, s)
                    .WithBit(3, w);
    }

    public void SetWalls(byte borderFlag)
    {
        Walls = borderFlag;
    }
}