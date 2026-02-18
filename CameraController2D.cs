using System;
using System.Collections.Generic;
using Board;
using Framework;
using Framework.Input;
using Helpers;
using InputHelpers;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public class CameraController2D : MonoBehaviour, GlobalControls.ICameraActions
{
        [SerializeField] private float maxZoom = 50;
        [SerializeField] private float minZoom = 4;
        [SerializeField] private float zoomSpeedMin = 2;
        [SerializeField] private float zoomSpeedMax = 8;
        
        [SerializeField] private Vector2 dragSpeed = new Vector2(2, 2);

        private GlobalControls controls;
        private Camera m_camera;
        private Vector3 dragOrigin;
        private Vector3 cameraOrigin;

        private Vector2 bounds;

        private float startZoom;
        private float endZoom;
        private Vector3 startPos;
        private Vector3 movingTowards;
        private float moveTimer;
        private const float moveDuration = 0.5f;

        private Vector2 lastPosition;
        private float lastZoom;
        private bool InputEnabled = false;
        
        private Vector2 edgeScrollBounds = new Vector2(0.1f, 0.1f);
        private bool windowFocused = true;
        public bool IsPanning => moveTimer < moveDuration;
        public bool IsClickDragging => middleClickPhase == InputActionPhase.Performed;

        private Vector2 lastTouchPosition;
        private Vector2 touchVelocity;
        public bool IsTouchDragging {get; private set;} = false;
        public bool IsDragGesture {get; private set;} = false;
        private const float MIN_DRAG_DISTANCE = 10f; // Distance in pixels to count as a drag
        
        [SerializeField] private float inertiaDecay = 0.93f;
        [SerializeField] private float minVelocityThreshold = 0.1f;
        
        private const int VELOCITY_SAMPLE_COUNT = 5;
        private Queue<Vector2> recentMovements = new Queue<Vector2>();
        private Vector2 lastCameraPosition;
        
        private float accumulatedZoomInput = 0f;
        private const float ZOOM_THRESHOLD = 1f;


        private void Awake()
        {
            m_camera = GetComponent<Camera>();
            moveTimer = moveDuration;
            lastCameraPosition = m_camera.transform.position;
            
            controls = new GlobalControls();
            controls.Camera.Enable();
            controls.Camera.AddCallbacks(this);
            
            ControlSchemeSwapper.OnChanged += OnControlSchemeChanged;
        }

        private void OnDestroy()
        {
            controls.Camera.Disable();
            controls.Camera.RemoveCallbacks(this);
            
            ControlSchemeSwapper.OnChanged -= OnControlSchemeChanged;
        }
        
        private void OnControlSchemeChanged(ControlScheme controlScheme)
        {
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

        private void Update()
        {
            if(moveTimer < moveDuration)
            {
                moveTimer = Math.Min(moveTimer + Time.deltaTime, moveDuration);
                float moveScaled = Easing.Quintic.InOut(moveTimer / moveDuration);
                m_camera.transform.position = Vector3.Lerp( startPos, movingTowards, moveScaled);
                
                //zoom in
                m_camera.orthographicSize = Mathf.Lerp(startZoom, endZoom, moveScaled);
                return;
            }
            
            if(!InputEnabled)
                return;
            
            if (startDrag)
            {
                startDrag = false;
                dragOrigin = new Vector3(mousePosition.x, mousePosition.y, 0);
                cameraOrigin = m_camera.transform.position;
                return;
            }

            bool allowDragInput = true;

            Vector3 dragDelta = new Vector3(m_camera.orthographicSize * dragSpeed.x * m_camera.aspect,
                m_camera.orthographicSize * dragSpeed.y, 0);
            
            if (allowDragInput)
            {
                if (middleClickPhase == InputActionPhase.Performed)
                {
                    Vector3 delta = dragOrigin - new Vector3(mousePosition.x, mousePosition.y, 0);
                    Vector3 pos = m_camera.ScreenToViewportPoint(delta);
                    Vector3 move = new Vector3(pos.x * dragDelta.x, pos.y * dragDelta.y, 0);
                    m_camera.transform.position = cameraOrigin + move;
                }
                else if(touch0Pressed)
                {
                    if (!IsTouchDragging)
                    {
                        IsTouchDragging = true;
                        IsDragGesture = false;
                        recentMovements.Clear();
                        lastCameraPosition = m_camera.transform.position;
                        lastTouchPosition = touch0.position;
                    }

                    Vector3 delta = new Vector3(touch0.startPosition.x, touch0.startPosition.y, 0) - new Vector3(touch0.position.x, touch0.position.y, 0);
                    
                    // Check if we've moved far enough to count as a drag
                    if (!IsDragGesture)
                    {
                        float dragDistance = Vector2.Distance(lastTouchPosition, touch0.position);
                        if (dragDistance > MIN_DRAG_DISTANCE)
                        {
                            IsDragGesture = true;
                        }
                    }

                    Vector3 pos = m_camera.ScreenToViewportPoint(delta);
                    Vector3 move = new Vector3(pos.x * dragDelta.x, pos.y * dragDelta.y, 0);
                    m_camera.transform.position = cameraOrigin + move;

                    // Track camera movement
                    Vector2 currentMovement = (Vector2)m_camera.transform.position - (Vector2)lastCameraPosition;
                    recentMovements.Enqueue(currentMovement);
                    if (recentMovements.Count > VELOCITY_SAMPLE_COUNT)
                        recentMovements.Dequeue();
                    lastCameraPosition = m_camera.transform.position;
                }
            }

            // Calculate average velocity when touch ends
            if (!touch0Pressed && IsTouchDragging)
            {
                IsTouchDragging = false;
                IsDragGesture = false;
                if (recentMovements.Count > 0)
                {
                    Vector2 totalMovement = Vector2.zero;
                    foreach (var movement in recentMovements)
                    {
                        totalMovement += movement;
                    }
                    touchVelocity = totalMovement / recentMovements.Count / Time.deltaTime;
                }
            }

            // Apply ongoing inertia movement
            if (!IsTouchDragging && touchVelocity.magnitude > minVelocityThreshold)
            {
                m_camera.transform.position += (Vector3)(touchVelocity * Time.deltaTime);
                touchVelocity *= inertiaDecay;
            }

            //gamepad and wasd controls
            Vector3 inputDelta = new Vector3(input.x, input.y, 0);

            if (MosaicPrivacyAndSettings.GetEdgeScrollEnabled() && windowFocused &&
                ControlSchemeSwapper.currentControlScheme == ControlScheme.KeyboardAndMouse)
            {
                int margin = 1;
                //check if mouse outside of screen
                
                if (mousePosition.x < margin)
                {
                    inputDelta.x = -1;
                }
                else if (mousePosition.x >= Screen.width - margin)
                {
                    inputDelta.x = 1;
                }

                if (mousePosition.y < margin)
                {
                    inputDelta.y = -1;
                }
                else if (mousePosition.y >= Screen.height - margin)
                {
                    inputDelta.y = 1;
                }
            }

            var position = m_camera.transform.position;
            position += Vector3.Scale(inputDelta, dragDelta * Time.deltaTime);
            
            //apply bounds
            position = new Vector3(
                Mathf.Clamp(position.x, 0, bounds.x),
                Mathf.Clamp(position.y, 0, bounds.y),
                position.z);
            
            m_camera.transform.position = position;

            float scrollInput = zoomInput;

            bool isOutside = mousePosition.x < 0 || mousePosition.x > Screen.width || mousePosition.y < 0 || mousePosition.y > Screen.height;

            if (isOutside)
            {
                scrollInput = 0;
            }
            
            float zoom = m_camera.orthographicSize;

            // Handle button-based zoom input
            if (zoomOutPressed)
            {
                scrollInput = 1;
                zoomOutPressed = false;
            }
            else if (zoomInPressed)
            {
                scrollInput = -1;
                zoomInPressed = false;
            }

            // Apply zoom behavior with fixed levels and sensitivity
            float[] zoomLevels = new[] { 4f, 6f, 8.5f, 12f, 16f, 22f, 30f, 50f, 80f, 115f, 150f };

            if (ControlSchemeSwapper.currentControlScheme == ControlScheme.Gamepad ||
                ControlSchemeSwapper.currentControlScheme == ControlScheme.SteamDeck)
            {
                zoomLevels = new[] { 4f, 6f, 12f, 24f, 50f, 120f, 200f };
            }
            
            // Map normalized sensitivity (0-1) to actual range with split scaling
            float normalizedSensitivity = MosaicPrivacyAndSettings.GetZoomSensitivity();
            float sensitivity;
            
            if (normalizedSensitivity <= 0.5f)
            {
                // Map 0-0.5 to Mac range (0.01-0.1)
                float t = normalizedSensitivity * 2f; // Scale to 0-1
                sensitivity = Mathf.Lerp(0.01f, 0.1f, t);
            }
            else
            {
                // Map 0.5-1 to higher range (0.1-3.0)
                float t = (normalizedSensitivity - 0.5f) * 2f; // Scale to 0-1
                sensitivity = Mathf.Lerp(0.1f, 3.0f, t);
            }
            
            // Apply zoom behavior based on sensitivity level
            if (scrollInput != 0)
            {
                if (normalizedSensitivity <= 0.5f)
                {
                    // Smooth zoom for low sensitivity (no level snapping)
                    float zoomDelta = -scrollInput * sensitivity * 20f; // Scale for smooth zoom
                    zoom = Mathf.Clamp(zoom + zoomDelta, minZoom, maxZoom);
                }
                else
                {
                    if(accumulatedZoomInput > 0 && scrollInput < 0 || accumulatedZoomInput < 0 && scrollInput > 0)
                        accumulatedZoomInput = 0;
                    
                    // Level-based zoom for high sensitivity
                    accumulatedZoomInput += scrollInput * sensitivity;
                    
                    if (Mathf.Abs(accumulatedZoomInput) >= ZOOM_THRESHOLD)
                    {
                        // Find the closest zoom level to our current zoom
                        int closestIndex = 0;
                        float closestDiff = Mathf.Abs(zoomLevels[0] - zoom);

                        for (int i = 1; i < zoomLevels.Length; i++)
                        {
                            float diff = Mathf.Abs(zoomLevels[i] - zoom);
                            if (diff < closestDiff)
                            {
                                closestDiff = diff;
                                closestIndex = i;
                            }
                        }

                        // Move to next/previous zoom level based on accumulated input
                        if (accumulatedZoomInput > 0 && closestIndex > 0)
                        {
                            zoom = zoomLevels[closestIndex - 1];
                            accumulatedZoomInput -= ZOOM_THRESHOLD;
                        }
                        else if (accumulatedZoomInput < 0 && closestIndex < zoomLevels.Length - 1)
                        {
                            zoom = zoomLevels[closestIndex + 1];
                            accumulatedZoomInput += ZOOM_THRESHOLD;
                        }
                        else
                        {
                            // Reset if we can't zoom further in that direction
                            accumulatedZoomInput = 0f;
                        }
                    }
                }
            }

            // Pinch zoom
            if (ControlSchemeSwapper.currentControlScheme == ControlScheme.Touch)
            {
                if(touch0.phase == UnityEngine.InputSystem.TouchPhase.Began || touch1.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    prevDistance = Vector2.Distance(touch0.position, touch1.position);
                }
                
                // Calculate the midpoint between the two touch points before zooming
                Vector2 touchMidpointBeforeZoom = (touch0.position + touch1.position) / 2;
                Vector3 worldMidpointBeforeZoom = m_camera.ScreenToWorldPoint(new Vector3(touchMidpointBeforeZoom.x, touchMidpointBeforeZoom.y, m_camera.nearClipPlane));

                // Calculate the current distance between the two touch points
                float currentDistance = Vector2.Distance(touch0.position, touch1.position);

                // Calculate the zoom delta based on the difference between the current and previous distances
                float pinchAmount = prevDistance / currentDistance;
               
                Debug.Log(pinchAmount);
                
                zoom = Mathf.Clamp(zoom * pinchAmount, minZoom, maxZoom);

                // Apply the new zoom level
                m_camera.orthographicSize = zoom;

                // Calculate the midpoint between the two touch points after zooming
                Vector3 worldMidpointAfterZoom = m_camera.ScreenToWorldPoint(new Vector3(touchMidpointBeforeZoom.x, touchMidpointBeforeZoom.y, m_camera.nearClipPlane));

                // Adjust the camera position based on the difference
                Vector3 adjustment = worldMidpointBeforeZoom - worldMidpointAfterZoom;
                m_camera.transform.position += adjustment;

                // Update the previous distance
                prevDistance = currentDistance;
            }
            else if(ControlSchemeSwapper.currentControlScheme == ControlScheme.KeyboardAndMouse)
            {
                // Calculate the world position of the mouse before zooming
                Vector3 mouseWorldPosBeforeZoom =
                    m_camera.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, m_camera.nearClipPlane));

                // Apply the new zoom level
                m_camera.orthographicSize = zoom;

                // Calculate the world position of the mouse after zooming
                Vector3 mouseWorldPosAfterZoom =
                    m_camera.ScreenToWorldPoint(new Vector3(mousePosition.x, mousePosition.y, m_camera.nearClipPlane));

                // Adjust the camera position based on the difference
                Vector3 adjustment = mouseWorldPosBeforeZoom - mouseWorldPosAfterZoom;

                //if (scrollInput > 0) //only when zooming in?
                {
                    m_camera.transform.position += adjustment;
                }
            }
            else //gamepads just zoom into center
            {
                m_camera.orthographicSize = zoom;
            }
        }

        public void SetBounds(Vector2 vector2)
        {
            bounds = vector2;
        }

        public Vector3 GetCurrentPosition()
        {
            return new Vector3(m_camera.transform.position.x, m_camera.transform.position.y, m_camera.orthographicSize);
        }
        
        public void SetLastPosition(Vector2 position, float zoom)
        {
            lastPosition = position;
            lastZoom = zoom;
        }
        
        public void SetPositionInstant(Vector3 position)
        {
            m_camera.transform.position = new Vector3(position.x, position.y, m_camera.transform.position.z);
            m_camera.orthographicSize = position.z;
        }

        public Vector2Int ScreenToGrid(Vector3 screenPos)
        {
            Vector3 worldPos = m_camera.ScreenToWorldPoint(screenPos);
            return new Vector2Int(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y));
        }
        
        public Vector2 GridToScreen(Vector2Int gridPos)
        {
            Vector3 worldPos = new Vector3(gridPos.x, gridPos.y, 0);
            return m_camera.WorldToScreenPoint(worldPos);
        }

        public void PanAndZoomInOn(Vector3 target)
        {
            startZoom = m_camera.orthographicSize;
            endZoom = minZoom;
            startPos = m_camera.transform.position;
            movingTowards = new Vector3(target.x, target.y, m_camera.transform.position.z);
            moveTimer = 0;
        }
        
        public void PanTowards(Vector3 target)
        {
            startZoom = m_camera.orthographicSize;
            endZoom = m_camera.orthographicSize;
            startPos = m_camera.transform.position;
            movingTowards = new Vector3(target.x, target.y, m_camera.transform.position.z);
            moveTimer = 0;
        }

        public void FullyZoomOut()
        {
            m_camera.orthographicSize = maxZoom;
            m_camera.transform.position = new Vector3(bounds.x/2, bounds.y/2, m_camera.transform.position.z);
        }

        public void FullyZoomOutSmooth()
        {
            startZoom = m_camera.orthographicSize;
            endZoom = maxZoom;
            startPos = m_camera.transform.position;
            movingTowards = new Vector3(bounds.x/2, bounds.y/2, m_camera.transform.position.z);
            moveTimer = 0;
        }

        public void FullyZoomOutSmooth(Vector2Int position)
        {
            startZoom = m_camera.orthographicSize;
            endZoom = maxZoom;
            startPos = m_camera.transform.position;
            movingTowards = new Vector3(position.x, position.y, m_camera.transform.position.z);
            moveTimer = 0;
        }

        public void FrameRegion(Vector2 min, Vector2 max, bool allowZoomIn = false)
        {
            // Calculate the center of the region
            Vector2 center = (min + max) * 0.5f;
            
            // Calculate required orthographic size to fit the region
            float width = max.x - min.x;
            float height = max.y - min.y;
            float aspectRatio = Screen.width / (float)Screen.height;
            
            // Add padding with extra space for UI elements
            float horizontalPadding = 4f;
            float verticalPadding = 4f;
            width += horizontalPadding * 2;  // Equal padding on left and right
            height += verticalPadding * 2;   // Equal padding on top and bottom
            
            // Shift center down slightly to account for top UI being larger than bottom UI
            center.y += verticalPadding * 0.2f;
            
            // Calculate the required orthographic size based on both dimensions
            float orthoSize = Mathf.Max(height * 0.5f, width * 0.5f / aspectRatio);
            
            // Clamp to camera limits
            orthoSize = Mathf.Clamp(orthoSize, minZoom, maxZoom);
            
            //do not zoom in as the player will have to zoom out again
            if (orthoSize > m_camera.orthographicSize || allowZoomIn)
            {
                startZoom = m_camera.orthographicSize;
                endZoom = orthoSize;
            }
            else
            {
                startZoom = m_camera.orthographicSize;
                endZoom = m_camera.orthographicSize;
            }

            startPos = m_camera.transform.position;
            movingTowards = new Vector3(center.x, center.y, m_camera.transform.position.z);
            moveTimer = 0;
        }

        public void ZoomToLastPosition()
        {
            PanAndZoomInOn(lastPosition);
        }

        public void SetInputEnabled(bool enabled)
        {
            Debug.Log("Camera input: "+enabled);
            InputEnabled = enabled;

            if (enabled)
            {
                controls.Camera.Enable();
            }
            else
            {   
                controls.Camera.Disable();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            windowFocused = hasFocus;
        }

        private Vector2 input = Vector2.zero;
        private Vector2 mousePosition = Vector2.zero;
        private InputActionPhase middleClickPhase;
        private float zoomInput;
        private bool zoomInPressed;
        private bool zoomOutPressed;
        private bool controlHeld;
        private bool startDrag = false;
        
        public void OnMove(InputAction.CallbackContext context)
        {
            //Debug.Log("move "+context.phase);
            //if (context.phase == InputActionPhase.Performed)
            {
                input = context.ReadValue<Vector2>();
            }
        }

        public void OnZoom(InputAction.CallbackContext context)
        {
            //is this right? This is to stop the the zoom from being to fast on continuous scrollwheels
            //but perhaps it needs to be continuous input at a slower rate?
            if (context.performed)
            {
                zoomInput = context.ReadValue<float>();
            }
            else
            {
                zoomInput = 0;
            }
        }

        public void OnControl(InputAction.CallbackContext context)
        {
            controlHeld = context.ReadValueAsButton();
        }

        public void OnZoomOut(InputAction.CallbackContext context)
        {
            if(!controlHeld)
                zoomOutPressed = context.performed;
        }

        public void OnZoomIn(InputAction.CallbackContext context)
        {
            zoomInPressed = context.performed;
        }

        public void OnMousePosition(InputAction.CallbackContext context)
        {
            mousePosition = context.ReadValue<Vector2>();
            //Debug.Log(context.phase+ " "+mousePosition);
        }

        public void OnMiddleClick(InputAction.CallbackContext context)
        {
            middleClickPhase = context.phase;
            
            if (middleClickPhase == InputActionPhase.Started)
            {
                startDrag = true;
            }
            
        }

        private float prevDistance;
        private TouchState touch0;
        private TouchState touch1;
        private bool touch0Pressed = false;
        private bool touch1Pressed = false;

        public void OnTouch0(InputAction.CallbackContext context)
        {
            touch0 = context.ReadValue<TouchState>();

            if (touch0.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                touch0Pressed = true;
            }
            
            if(touch0.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                touch0Pressed = false;
            }
        }

        public void OnTouch1(InputAction.CallbackContext context)
        {
            touch1 = context.ReadValue<TouchState>();

            if (touch1.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                touch1Pressed = true;
            }
            
            if(touch1.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                touch1Pressed = false;
            }
        }

        //this method fires for every finger
        public void OnMultiTouch(InputAction.CallbackContext context)
        {
            if (context.started)
            {
                cameraOrigin = m_camera.transform.position;

                //set drag origin to midpoint of all touches
                // Vector2 touchMidpoint = touch0.position;
                //
                // if(touch1Pressed)
                //     touchMidpoint = (touchMidpoint + touch1.position) / 2f;
                //
                // dragOrigin = new Vector3(touchMidpoint.x, touchMidpoint.y, 0);
            }
        }

        public void PanAndZoomToPosition(Vector2Int position, float zoomLevel)
        {
            startZoom = m_camera.orthographicSize;
            endZoom = Mathf.Clamp(zoomLevel, minZoom, maxZoom);
            startPos = m_camera.transform.position;
            movingTowards = new Vector3(position.x, position.y, m_camera.transform.position.z);
            moveTimer = 0;
        }

        public RectInt GetCameraBounds()
        {
            Rect rect = GetCameraBoundsRect();
        
            int xMin = Mathf.RoundToInt(rect.xMin);
            int xMax = xMin + Mathf.CeilToInt(rect.width)+1;
            int yMin = Mathf.RoundToInt(rect.yMin);
            int yMax = yMin + Mathf.CeilToInt(rect.height)+1;
        
            var newScreenBounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
            return newScreenBounds;
        }
        
        public Rect GetCameraBoundsRect()
        {
            //get camera bounds and convert to grid space
            Vector3 bottomLeft = m_camera.ScreenToWorldPoint(Vector3.zero);
            Vector3 topRight = m_camera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height));
            
            return new Rect(bottomLeft.x, bottomLeft.y, topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
        }
    }