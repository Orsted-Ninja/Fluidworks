using UnityEngine;
using AeroFlow.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AeroFlow.UI
{
    public class CameraController : MonoBehaviour
    {
        public Transform target;
        public float distance = 18.0f; // Default closer framing
        public float orbitSensitivityX = 4.0f;
        public float orbitSensitivityY = 4.0f;
        public float panSpeed = 0.015f;
        public float zoomSpeed = 15f;
        public float zoomSpeedMultiplier = 1f;
        public float flyLookSensitivity = 2.2f;
        public float flyMoveSpeed = 12f;
        public float flyBoostMultiplier = 2.5f;
        public float targetFollowSharpness = 14f;
        public float cameraFollowSharpness = 18f;
        public float minYAngle = -80f;
        public float maxYAngle = 80f;
        public float minDistance = 2f;
        public float maxDistance = 150f;

        private float currentX = 45f;
        private float currentY = 30f;
        private Vector3 targetPos;
        private Vector3 lastMousePosition;
        private bool lastMousePositionValid;
        private float navigationSpeed = 12f;
        private Vector3 targetVelocity;
        private Vector3 cameraVelocity;

        private void Start()
        {
            if (target == null)
            {
                GameObject go = new GameObject("CameraTarget");
                go.transform.position = new Vector3(0, 3, 0); // Focus slightly above ground
                target = go.transform;
            }
            targetPos = target.position;
            
            // Force camera to isometric start
            currentX = 45f;
            currentY = 30f;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            if (UIFocusUtility.IsTextInputFocused())
            {
                lastMousePositionValid = false;
                return;
            }

            Vector2 mouseDelta = GetMouseDelta();
            bool flying = GetMouseButtonState(1) && (GetKeyState(KeyCode.W) || GetKeyState(KeyCode.A) || GetKeyState(KeyCode.S) || GetKeyState(KeyCode.D) || GetKeyState(KeyCode.Q) || GetKeyState(KeyCode.E));
            bool orbiting = !flying && !UIFocusUtility.IsPointerOverUI() && ((GetKeyState(KeyCode.LeftAlt) && GetMouseButtonState(0)) || GetMouseButtonState(1));
            bool panning = GetMouseButtonState(2) && !UIFocusUtility.IsPointerOverUI();
            if (!orbiting && !panning && !flying)
            {
                lastMousePositionValid = false;
            }

            // Orbit: Alt + Left Mouse or Right Mouse drag fallback
            if (orbiting)
            {
                currentX += mouseDelta.x * orbitSensitivityX;
                currentY -= mouseDelta.y * orbitSensitivityY;
                currentY = Mathf.Clamp(currentY, minYAngle, maxYAngle);
            }

            // Pan: Middle Mouse
            if (panning)
            {
                float panX = -mouseDelta.x * panSpeed * distance;
                float panY = -mouseDelta.y * panSpeed * distance;
                Vector3 delta = transform.right * panX + transform.up * panY;
                targetPos += delta;
            }

            // Zoom with Scroll Wheel
            float scroll = GetScrollDelta();
            if (Mathf.Abs(scroll) > 0.0001f && !UIFocusUtility.IsPointerOverUI())
            {
                float zoomFactor = 1f;
                if (GetKeyState(KeyCode.LeftControl) || GetKeyState(KeyCode.RightControl))
                {
                    zoomFactor = 0.22f; // Fine zoom
                }
                else if (GetKeyState(KeyCode.LeftShift) || GetKeyState(KeyCode.RightShift))
                {
                    zoomFactor = 1.8f; // Fast zoom
                }

                distance -= scroll * zoomSpeed * zoomSpeedMultiplier * zoomFactor;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            // Runtime zoom sensitivity keybinds
            if (GetKeyDownState(KeyCode.LeftBracket) || GetKeyDownState(KeyCode.Minus))
            {
                zoomSpeedMultiplier = Mathf.Clamp(zoomSpeedMultiplier * 0.9f, 0.15f, 4.0f);
            }
            if (GetKeyDownState(KeyCode.RightBracket) || GetKeyDownState(KeyCode.Equals))
            {
                zoomSpeedMultiplier = Mathf.Clamp(zoomSpeedMultiplier * 1.1f, 0.15f, 4.0f);
            }

            // Fly Mode: Right Mouse + WASD/QE + mouse look
            if (flying)
            {
                float lookX = mouseDelta.x * flyLookSensitivity;
                float lookY = -mouseDelta.y * flyLookSensitivity;
                currentX += lookX;
                currentY = Mathf.Clamp(currentY + lookY, minYAngle, maxYAngle);

                Quaternion flyRotation = Quaternion.Euler(currentY, currentX, 0f);
                Vector3 flyForward = Vector3.ProjectOnPlane(flyRotation * Vector3.forward, Vector3.up).normalized;
                if (flyForward.sqrMagnitude < 1e-6f)
                {
                    flyForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                }
                Vector3 flyRight = Vector3.ProjectOnPlane(flyRotation * Vector3.right, Vector3.up).normalized;
                if (flyRight.sqrMagnitude < 1e-6f)
                {
                    flyRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
                }

                float speed = flyMoveSpeed * ((GetKeyState(KeyCode.LeftShift) || GetKeyState(KeyCode.RightShift)) ? flyBoostMultiplier : 1f);
                Vector3 move = Vector3.zero;
                if (GetKeyState(KeyCode.W)) move += flyForward;
                if (GetKeyState(KeyCode.S)) move -= flyForward;
                if (GetKeyState(KeyCode.D)) move += flyRight;
                if (GetKeyState(KeyCode.A)) move -= flyRight;
                if (GetKeyState(KeyCode.E)) move += Vector3.up;
                if (GetKeyState(KeyCode.Q)) move += Vector3.down;

                if (move.sqrMagnitude > 0.0001f)
                {
                    Vector3 delta = move.normalized * speed * Time.unscaledDeltaTime;
                    targetPos += delta;
                }
            }

            // Focus loaded model quickly
            if (GetKeyDownState(KeyCode.F))
            {
                FrameLoadedModelOrTarget();
            }

            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.0001f, 0.05f);
            float targetSmoothTime = flying ? 0.025f : Mathf.Max(0.02f, 1f / Mathf.Max(1f, targetFollowSharpness));
            float cameraSmoothTime = flying ? 0.02f : Mathf.Max(0.02f, 1f / Mathf.Max(1f, cameraFollowSharpness));

            // Use damped follow instead of frame-rate-sensitive lerp so movement remains
            // stable when the simulation stalls for a frame.
            targetPos.y = Mathf.Max(targetPos.y, 0.2f);
            target.position = Vector3.SmoothDamp(target.position, targetPos, ref targetVelocity, targetSmoothTime, Mathf.Infinity, dt);
            Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
            Vector3 desiredCameraPos = target.position + rotation * new Vector3(0, 0, -distance);
            desiredCameraPos.y = Mathf.Max(desiredCameraPos.y, 0.4f);
            transform.position = Vector3.SmoothDamp(transform.position, desiredCameraPos, ref cameraVelocity, cameraSmoothTime, Mathf.Infinity, dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 1f - Mathf.Exp(-cameraFollowSharpness * dt));
            transform.LookAt(target.position);
        }

        private Vector2 GetMouseDelta()
        {
            Vector3 current = GetMousePosition();
            if (!lastMousePositionValid)
            {
                lastMousePosition = current;
                lastMousePositionValid = true;
                return Vector2.zero;
            }

            Vector2 delta = (Vector2)(current - lastMousePosition);
            lastMousePosition = current;
            return delta * 0.02f;
        }

        private static float GetScrollDelta()
        {
            float s = Input.mouseScrollDelta.y;
#if ENABLE_INPUT_SYSTEM
            if (Mathf.Abs(s) < 0.0001f && Mouse.current != null)
            {
                s = Mouse.current.scroll.ReadValue().y * 0.05f;
            }
#endif
            if (Mathf.Abs(s) > 0.0001f) return s;
            // Legacy fallback with controlled scaling
            return Input.GetAxis("Mouse ScrollWheel");
        }

        private static Vector3 GetMousePosition()
        {
            Vector3 pos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if ((pos.x == 0f && pos.y == 0f) && Mouse.current != null)
            {
                Vector2 p = Mouse.current.position.ReadValue();
                pos = new Vector3(p.x, p.y, 0f);
            }
#endif
            return pos;
        }

        private static bool GetMouseButtonState(int button)
        {
            bool pressed = Input.GetMouseButton(button);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                switch (button)
                {
                    case 0: pressed |= Mouse.current.leftButton.isPressed; break;
                    case 1: pressed |= Mouse.current.rightButton.isPressed; break;
                    case 2: pressed |= Mouse.current.middleButton.isPressed; break;
                }
            }
#endif
            return pressed;
        }

        private static bool GetKeyState(KeyCode key)
        {
            bool pressed = Input.GetKey(key);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (key)
                {
                    case KeyCode.LeftAlt: pressed |= Keyboard.current.leftAltKey.isPressed; break;
                    case KeyCode.RightControl: pressed |= Keyboard.current.rightCtrlKey.isPressed; break;
                    case KeyCode.LeftControl: pressed |= Keyboard.current.leftCtrlKey.isPressed; break;
                    case KeyCode.RightShift: pressed |= Keyboard.current.rightShiftKey.isPressed; break;
                    case KeyCode.LeftShift: pressed |= Keyboard.current.leftShiftKey.isPressed; break;
                    case KeyCode.W: pressed |= Keyboard.current.wKey.isPressed; break;
                    case KeyCode.A: pressed |= Keyboard.current.aKey.isPressed; break;
                    case KeyCode.S: pressed |= Keyboard.current.sKey.isPressed; break;
                    case KeyCode.D: pressed |= Keyboard.current.dKey.isPressed; break;
                    case KeyCode.Q: pressed |= Keyboard.current.qKey.isPressed; break;
                    case KeyCode.E: pressed |= Keyboard.current.eKey.isPressed; break;
                    case KeyCode.F: pressed |= Keyboard.current.fKey.isPressed; break;
                    case KeyCode.LeftBracket: pressed |= Keyboard.current.leftBracketKey.isPressed; break;
                    case KeyCode.RightBracket: pressed |= Keyboard.current.rightBracketKey.isPressed; break;
                    case KeyCode.Minus: pressed |= Keyboard.current.minusKey.isPressed; break;
                    case KeyCode.Equals: pressed |= Keyboard.current.equalsKey.isPressed; break;
                }
            }
#endif
            return pressed;
        }

        private static bool GetKeyDownState(KeyCode key)
        {
            bool down = Input.GetKeyDown(key);
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                switch (key)
                {
                    case KeyCode.F: down |= Keyboard.current.fKey.wasPressedThisFrame; break;
                    case KeyCode.LeftBracket: down |= Keyboard.current.leftBracketKey.wasPressedThisFrame; break;
                    case KeyCode.RightBracket: down |= Keyboard.current.rightBracketKey.wasPressedThisFrame; break;
                    case KeyCode.Minus: down |= Keyboard.current.minusKey.wasPressedThisFrame; break;
                    case KeyCode.Equals: down |= Keyboard.current.equalsKey.wasPressedThisFrame; break;
                }
            }
#endif
            return down;
        }

        public void SnapToView(string viewName)
        {
            // Keep current target when possible; fallback to model center.
            if (targetPos == Vector3.zero)
            {
                TrySetTargetToLoadedModelCenter();
                if (targetPos == Vector3.zero)
                {
                    targetPos = new Vector3(0, 3, 0);
                }
            }
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            switch (viewName.ToLower())
            {
                case "iso":
                    currentX = 45f; currentY = 30f;
                    break;
                case "top":
                    currentX = 0f; currentY = 89f; // Slightly off 90 to prevent Gimbal lock
                    break;
                case "front":
                    currentX = 0f; currentY = 0f;
                    break;
                case "side":
                    currentX = 90f; currentY = 0f;
                    break;
            }

            if (target != null)
            {
                target.position = targetPos;
            }
        }

        public void FrameBounds(Bounds bounds, float padding = 1.2f)
        {
            targetPos = bounds.center;
            if (target != null)
            {
                target.position = targetPos;
            }

            targetVelocity = Vector3.zero;
            cameraVelocity = Vector3.zero;
            float radius = bounds.extents.magnitude;
            Camera cam = GetComponent<Camera>();
            if (cam == null) cam = Camera.main;
            float fov = cam != null ? cam.fieldOfView : 60f;
            float dist = radius / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            distance = Mathf.Clamp(dist * padding, minDistance, maxDistance);
        }

        public static void SnapCameraToView(Camera cam, string viewName)
        {
            if (cam == null) return;
            Vector3 target = new Vector3(0, 3, 0);
            float distance = 18f;
            float x = 45f;
            float y = 30f;

            switch (viewName.ToLower())
            {
                case "top":
                    x = 0f; y = 89f;
                    break;
                case "front":
                    x = 0f; y = 0f;
                    break;
                case "side":
                    x = 90f; y = 0f;
                    break;
                case "iso":
                default:
                    x = 45f; y = 30f;
                    break;
            }

            Quaternion rotation = Quaternion.Euler(y, x, 0);
            cam.transform.position = target + rotation * new Vector3(0, 0, -distance);
            cam.transform.LookAt(target);
        }

        private void FrameLoadedModelOrTarget()
        {
            if (RuntimeModelLookup.TryGetRenderableBounds(out Bounds b))
            {
                FrameBounds(b, 1.2f);
                return;
            }

            FrameBounds(new Bounds(targetPos, Vector3.one * 6f), 1.0f);
        }

        private void TrySetTargetToLoadedModelCenter()
        {
            if (RuntimeModelLookup.TryGetRenderableBounds(out Bounds b))
            {
                targetPos = b.center;
            }
        }

        public void SetNavigationSpeed(float speed)
        {
            float s = Mathf.Clamp(speed, 2f, 40f);
            navigationSpeed = s;
            flyMoveSpeed = s;
            zoomSpeed = Mathf.Lerp(1.8f, 22f, (s - 2f) / 38f);
            orbitSensitivityX = Mathf.Lerp(1.8f, 9.5f, (s - 2f) / 38f);
            orbitSensitivityY = orbitSensitivityX;
            panSpeed = Mathf.Lerp(0.004f, 0.03f, (s - 2f) / 38f);
        }

    }
}
