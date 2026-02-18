using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Framework.Input;
using InputHelpers;

public class TileBoardController: MonoBehaviour, GlobalControls.ITileboardControlsActions
{
    private TileBoard tileBoard;
    private GlobalControls controls;

    private const float NudgeThreshold = 0.7f;
    private Vector2 gamepadCursorPos;
    private InputActionPhase leftClickPhase;
    private InputActionPhase rightClickPhase;
    private Vector2 cursorInput;
    private Vector2 dpadInput;
    private bool rightStickClick = false;
    private float NudgeTimer;
    private float NudgeHoldInterval = 0.15f;
    private float NudgeHoldIntervalZoomedOut = 0.5f;
    
    private Vector2 longPressStartPos;
    private Vector2 tapStartPos;
    private Vector2 lastMousePos;
    private bool isFingerPainting = false;
    
    private void Awake()
    {
        tileBoard = GetComponent<TileBoard>();
        
        controls = new GlobalControls();
        controls.TileboardControls.Enable();
        controls.TileboardControls.AddCallbacks(this);
        
        ControlSchemeSwapper.OnChanged += OnControlSchemeChanged;
    }
    
    private void OnDestroy()
    {
        controls.TileboardControls.Disable();
        controls.TileboardControls.RemoveCallbacks(this);

        ControlSchemeSwapper.OnChanged -= OnControlSchemeChanged;
    }
    
    private void OnControlSchemeChanged(ControlScheme controlScheme)
    {
        Cursor.visible = controlScheme is ControlScheme.KeyboardAndMouse or ControlScheme.SteamDeck;
        
        switch (controlScheme)
        {
            case ControlScheme.KeyboardAndMouse:
                controls.bindingMask = InputBinding.MaskByGroup("Mouse+Keyboard");
                break;
            case ControlScheme.Gamepad:
                controls.bindingMask = InputBinding.MaskByGroup("Gamepad");
                break;
            case ControlScheme.Touch:
                controls.bindingMask = InputBinding.MaskByGroup("Touchscreen");
                break;
            case ControlScheme.SteamDeck:
                controls.bindingMask = InputBinding.MaskByGroups("Gamepad", "Mouse+Keyboard");
                break;
        }
    }

    public void OnLeftClick(InputAction.CallbackContext context)
    {
        //Debug.Log("Left click: " + context.phase);
        
        leftClickPhase = context.phase;

        if (leftClickPhase == InputActionPhase.Canceled)
        {
            tileBoard.OnClickRelease();
        }
    }

    public void OnRightClick(InputAction.CallbackContext context)
    {
        //Debug.Log("Right click: " + context.phase);
        
        rightClickPhase = context.phase;

        if (rightClickPhase == InputActionPhase.Canceled)
        {
            tileBoard.OnClickRelease();
        }
    }

    public void OnUndo(InputAction.CallbackContext context)
    {
        if (context.performed) tileBoard.Undo();
    }

    public void OnToggleEditMode(InputAction.CallbackContext context)
    {
        if (!Application.isEditor)
            return;

        if (context.performed)
        {
            tileBoard.SwitchMode();
        }
    }

    public void OnCursorMove(InputAction.CallbackContext context)
    {
        cursorInput = context.ReadValue<Vector2>();
    }

    public void OnNudgeUp(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            dpadInput.y = 1;
            //tileBoard.focusPos += new Vector2Int(0,1);
            //NudgeTo(tileBoard.focusPos);
        }
        
        if (context.canceled)
        {
            dpadInput.y = 0;
        }
    }

    public void OnNudgeDown(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            dpadInput.y = -1;
            //tileBoard.focusPos -= new Vector2Int(0,1);
            //NudgeTo(tileBoard.focusPos);
        }
        
        if (context.canceled)
        {
            dpadInput.y = 0;
        }
    }

    public void OnNudgeRight(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            dpadInput.x = 1;
            //tileBoard.focusPos += new Vector2Int(1,0);
            //NudgeTo(tileBoard.focusPos);
        }

        if (context.canceled)
        {
            dpadInput.x = 0;
        }
    }

    public void OnNudgeLeft(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            dpadInput.x = -1;
            //tileBoard.focusPos -= new Vector2Int(1,0);
            //NudgeTo(tileBoard.focusPos);
        }
        
        if (context.canceled)
        {
            dpadInput.x = 0;
        }
    }

    public void OnHint(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            tileBoard.RaiseHintRequested();
        }
    }

    public void OnTap(InputAction.CallbackContext context)
    {
        //Debug.Log("Tap: " + context.phase);

        if(context.started)
        {
            tapStartPos = lastMousePos;
        }
        
        if (context.performed)
        {
            if (!IsDraggingGesture(tapStartPos, lastMousePos))
            {
                tileBoard.OnAnyClick(true);
                tileBoard.OnClickRelease();
            }
        }
    }

    private bool IsDraggingGesture(Vector2 startPos, Vector2 endPos)
    {
        //example dpis
        //pixel 6a = 429
        //ipad pro = 264
        //iphonex = 458
        float distance = (longPressStartPos - lastMousePos).magnitude;

        //scale threshold based on screen dpi
        float thresholdInCm = 0.5f;
        float dpi = Screen.dpi;
        float thresholdInInches = thresholdInCm / 2.54f;
            
        float pixelThreshold = dpi * thresholdInInches;
            
        // float distanceInCm = distance / dpi * 2.54f;
        // Debug.Log("Tap and hold performed: " + distanceInCm + "cm");

        return distance > pixelThreshold;
    }
    
    public void OnTapAndHold(InputAction.CallbackContext context)
    {
        //Debug.Log("Tap and hold: " + context.phase);
        
        //get mouse pos
        if (context.started)
        {
            longPressStartPos = lastMousePos;
        }

        //start drawing, but only if the touch has been pressed in one place for a while
        if (context.performed)
        {
            if (!IsDraggingGesture(longPressStartPos, lastMousePos))
            {
                tileBoard.OnAnyClick(true);
                tileBoard.LockCamera();
                isFingerPainting = true;
                Debug.Log("Clicked");
            }
        }

        if (context.canceled)
        {
            isFingerPainting = false;
            tileBoard.UnlockCamera();
            tileBoard.OnClickRelease();
        }
    }

    public void OnClearErrors(InputAction.CallbackContext context)
    {
        tileBoard.ClearErrors();
    }

    public void OnMousePosition(InputAction.CallbackContext context)
    {
        Vector2 mousePos = context.ReadValue<Vector2>();
        //Debug.Log("Mouse position: " + mousePos + " " + context.phase);
        lastMousePos = mousePos;
        tileBoard.HandleMousePositionChange(mousePos);
    }

    public void OnControl(InputAction.CallbackContext context)
    {
        tileBoard.ControlHeld = context.ReadValueAsButton();
    }

    public void OnAlt(InputAction.CallbackContext context)
    {
        tileBoard.AltHeld = context.ReadValueAsButton();
    }
    
    public void OnShift(InputAction.CallbackContext context)
    {
        tileBoard.ShiftHeld = context.ReadValueAsButton();
    }

    private void NudgeTo(Vector2Int pos)
    {
        tileBoard.CaptureMouseInput();
        tileBoard.MoveHighlightTo(pos);
        tileBoard.EnsureOnscreen(pos);
    }

    private void Update()
    {
        if (NudgeTimer > 0)
        {
            NudgeTimer -= Time.deltaTime;
            if (NudgeTimer <= 0 || (cursorInput.magnitude < 0.5f && dpadInput == Vector2.zero))
            {
                NudgeTimer = 0;
                rightStickClick = false;
            }
        }
        else 
        {
            if (tileBoard.IsZoomedIn) //nudge one tile at a time
            {
                if (cursorInput.x > NudgeThreshold || dpadInput.x > NudgeThreshold)
                {
                    rightStickClick = true;
                    tileBoard.focusPos += new Vector2Int(1,0);
                }

                if (cursorInput.x < -NudgeThreshold || dpadInput.x < -NudgeThreshold)
                {
                    rightStickClick = true;
                    tileBoard.focusPos -= new Vector2Int(1,0);
                }

                if (cursorInput.y > NudgeThreshold || dpadInput.y > NudgeThreshold)
                {
                    rightStickClick = true;
                    tileBoard.focusPos += new Vector2Int(0,1);
                }

                if (cursorInput.y < -NudgeThreshold || dpadInput.y < -NudgeThreshold)
                {
                    rightStickClick = true;
                    tileBoard.focusPos -= new Vector2Int(0,1);
                }
            }
            else //nudge over to the next region
            {
                Vector2 direction = Vector2.zero;

                if (cursorInput.x > NudgeThreshold || dpadInput.x > NudgeThreshold)
                    direction.x = 1f;
                else if (cursorInput.x < -NudgeThreshold || dpadInput.x < -NudgeThreshold)
                    direction.x = -1f;

                if (cursorInput.y > NudgeThreshold || dpadInput.y > NudgeThreshold)
                    direction.y = 1f;
                else if (cursorInput.y < -NudgeThreshold || dpadInput.y < -NudgeThreshold)
                    direction.y = -1f;

                if (direction != Vector2.zero && tileBoard.NudgeToNearestRegion(direction))
                {
                    rightStickClick = true;
                }
            }

            if (rightStickClick)
            {
                NudgeTimer = tileBoard.IsZoomedIn ? NudgeHoldInterval : NudgeHoldIntervalZoomedOut;
                NudgeTo(tileBoard.focusPos);
            }
        }
        
        if (leftClickPhase == InputActionPhase.Performed || isFingerPainting)
        {
            tileBoard.OnAnyClick(true);
        }
        else if (rightClickPhase == InputActionPhase.Performed)
        {
            tileBoard.OnAnyClick(false);
        }
    }
    
    public void SetInputEnabled(bool enabled)
    {
        if (enabled)
        {
            controls.TileboardControls.Enable();
        }
        else
        {   
            controls.TileboardControls.Disable();
        }
    }
}