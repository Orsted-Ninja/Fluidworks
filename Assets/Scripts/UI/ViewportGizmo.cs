using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AeroFlow.UI
{
    /// <summary>
    /// Blender-style 3D orientation gizmo drawn in the top-right viewport corner.
    /// Rotates with the camera and allows clicking axis labels to snap views.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ViewportGizmo : MonoBehaviour
    {
        [Header("Layout")]
        [Range(60, 180)] public int gizmoSize = 110;
        [Range(4, 40)] public int marginRight = 16;
        [Range(4, 40)] public int marginTop = 16;

        [Header("Style")]
        public Color xColor = new Color(0.95f, 0.25f, 0.28f, 1f);
        public Color yColor = new Color(0.40f, 0.85f, 0.25f, 1f);
        public Color zColor = new Color(0.30f, 0.55f, 1.00f, 1f);
        public Color bgColor = new Color(0.04f, 0.08f, 0.12f, 0.55f);
        public Color hoverColor = new Color(1f, 1f, 1f, 0.18f);
        [Range(0.25f, 0.9f)] public float axisLength = 0.62f;
        [Range(4, 20)] public int labelFontSize = 12;

        private Camera _cam;
        private CameraController _camController;
        private Material _glMaterial;
        private int _hoveredAxis = -1; // 0=X, 1=Y, 2=Z, 3=-X, 4=-Y, 5=-Z
        private GUIStyle _labelStyle;
        private GUIStyle _labelHoverStyle;
        private GUIStyle _labelDimStyle;

        private bool _isDragging;
        private Vector2 _dragStartMouse;
        private Vector2 _dragStartAngles;

        // Axis endpoints in screen space (for hit testing)
        private readonly Vector2[] _axisScreenPos = new Vector2[6];
        private readonly string[] _axisLabels = { "X", "Y", "Z", "-X", "-Y", "-Z" };

        private void Start()
        {
            _cam = GetComponent<Camera>();
            _camController = GetComponent<CameraController>();
            EnsureMaterial();
        }

        private void EnsureMaterial()
        {
            if (_glMaterial != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("GUI/Text Shader") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("[ViewportGizmo] Gizmo shader not found.");
                shader = Shader.Find("Hidden/InternalErrorShader");
            }
            if (shader != null)
            {
                _glMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _glMaterial.SetInt("_ZWrite", 0);
                _glMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
        }

        private void Update()
        {
            UpdateHover();
            HandleDrag();
            HandleClick();
        }

        private void HandleDrag()
        {
            Vector2 mousePos = GetMouseScreenPos();
            bool mouseDown = GetMouseButton(0);

            if (!_isDragging)
            {
                if (mouseDown && IsMouseOverGizmo())
                {
                    _isDragging = true;
                    _dragStartMouse = mousePos;
                    _dragStartAngles = GetCameraAngles();
                }
            }
            else
            {
                if (!mouseDown)
                {
                    _isDragging = false;
                }
                else
                {
                    Vector2 delta = mousePos - _dragStartMouse;
                    // Orbit sensitivity factor matches CameraController logic roughly
                    float yaw = _dragStartAngles.x + delta.x * 0.6f;
                    float pitch = _dragStartAngles.y + delta.y * 0.6f;
                    SetCameraAngles(yaw, pitch);
                }
            }
        }

        private bool IsMouseOverGizmo()
        {
            return GetGizmoRect().Contains(GetMouseScreenPos());
        }

        private Vector2 GetCameraAngles()
        {
            if (_camController == null) _camController = GetComponent<CameraController>();
            if (_camController == null) return Vector2.zero;

            var type = typeof(CameraController);
            var xField = type.GetField("currentX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var yField = type.GetField("currentY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            float x = xField != null ? (float)xField.GetValue(_camController) : 0f;
            float y = yField != null ? (float)yField.GetValue(_camController) : 0f;
            return new Vector2(x, y);
        }

        private void SetCameraAngles(float yaw, float pitch)
        {
            if (_camController == null) _camController = GetComponent<CameraController>();
            if (_camController == null) return;

            pitch = Mathf.Clamp(pitch, -89f, 89f);

            var type = typeof(CameraController);
            var xField = type.GetField("currentX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var yField = type.GetField("currentY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (xField != null) xField.SetValue(_camController, yaw);
            if (yField != null) yField.SetValue(_camController, pitch);
        }

        private void OnPostRender()
        {
            EnsureMaterial();
            DrawGizmo();
        }

        private Rect GetGizmoRect()
        {
            float x = Screen.width - gizmoSize - marginRight;
            float y = marginTop;
            return new Rect(x, y, gizmoSize, gizmoSize);
        }

        private Vector2 GetGizmoCenter()
        {
            Rect r = GetGizmoRect();
            return new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);
        }

        private void DrawGizmo()
        {
            if (_glMaterial == null) return;

            Rect rect = GetGizmoRect();
            Vector2 center = new Vector2(rect.x + rect.width * 0.5f, rect.y + rect.height * 0.5f);
            float radius = gizmoSize * 0.5f * axisLength;

            // Camera rotation - we invert to show world axes from camera's perspective
            Quaternion camRot = _cam.transform.rotation;
            Matrix4x4 viewMat = Matrix4x4.Rotate(Quaternion.Inverse(camRot));

            // Compute axis endpoints in screen coords
            Vector3[] worldAxes = { Vector3.right, Vector3.up, Vector3.forward };
            Color[] colors = { xColor, yColor, zColor };

            // Transform axes to view space
            Vector3[] viewAxes = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                viewAxes[i] = viewMat.MultiplyVector(worldAxes[i]);
                // Screen coords: x = right, y = up
                float sx = center.x + viewAxes[i].x * radius;
                float sy = center.y - viewAxes[i].y * radius; // flip Y for screen space
                _axisScreenPos[i] = new Vector2(sx, sy);
                // Negative axes
                _axisScreenPos[i + 3] = new Vector2(center.x - viewAxes[i].x * radius, center.y + viewAxes[i].y * radius);
            }

            // Sort by depth (z in view space) so back axes draw first
            int[] drawOrder = { 0, 1, 2 };
            // Sort by z-depth (more negative = further back = draw first)
            for (int i = 0; i < 2; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    if (viewAxes[drawOrder[i]].z > viewAxes[drawOrder[j]].z)
                    {
                        (drawOrder[i], drawOrder[j]) = (drawOrder[j], drawOrder[i]);
                    }
                }
            }

            // Set up GL for screen-space drawing
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            _glMaterial.SetPass(0);

            // Draw background circle
            DrawCircle(center, gizmoSize * 0.48f, bgColor, 32);

            // Draw hover highlight
            if (_hoveredAxis >= 0)
            {
                Vector2 hoverPos = _axisScreenPos[_hoveredAxis];
                DrawCircle(hoverPos, 11f, hoverColor, 16);
            }

            // Draw axes: back halves first (dimmed), then front halves
            for (int pass = 0; pass < 2; pass++)
            {
                for (int orderIdx = 0; orderIdx < 3; orderIdx++)
                {
                    int i = drawOrder[orderIdx];
                    float depth = viewAxes[i].z;
                    bool isFront = depth >= 0;

                    if (pass == 0 && isFront) continue;  // skip front axes in back pass
                    if (pass == 1 && !isFront) continue;  // skip back axes in front pass

                    Color c = colors[i];
                    float alpha = isFront ? 1f : 0.3f;
                    Color lineCol = new Color(c.r, c.g, c.b, alpha);
                    Color dimCol = new Color(c.r * 0.4f, c.g * 0.4f, c.b * 0.4f, alpha * 0.45f);

                    if (isFront)
                    {
                        // Draw positive axis line
                        DrawLine(center, _axisScreenPos[i], lineCol, 2.5f);
                        // Draw endpoint dot
                        DrawCircle(_axisScreenPos[i], 6f, lineCol, 12);
                    }
                    else
                    {
                        // Draw negative axis (back side)
                        DrawLine(center, _axisScreenPos[i + 3], dimCol, 1.5f);
                        DrawCircle(_axisScreenPos[i + 3], 4f, dimCol, 10);
                    }
                }
            }

            // Draw center dot
            DrawCircle(center, 3.5f, new Color(0.8f, 0.9f, 1f, 0.7f), 10);

            GL.PopMatrix();
        }

        private void OnGUI()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = labelFontSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                };
                _labelHoverStyle = new GUIStyle(_labelStyle)
                {
                    normal = { textColor = new Color(1f, 1f, 0.6f) }
                };
                _labelDimStyle = new GUIStyle(_labelStyle)
                {
                    fontSize = labelFontSize - 2,
                    normal = { textColor = new Color(0.5f, 0.6f, 0.7f, 0.6f) }
                };
            }

            Quaternion camRot = _cam.transform.rotation;
            Matrix4x4 viewMat = Matrix4x4.Rotate(Quaternion.Inverse(camRot));
            Vector3[] worldAxes = { Vector3.right, Vector3.up, Vector3.forward };
            Color[] colors = { xColor, yColor, zColor };

            // Draw labels for all 6 axis endpoints
            for (int i = 0; i < 6; i++)
            {
                int axisIdx = i < 3 ? i : i - 3;
                Vector3 viewAxis = viewMat.MultiplyVector(worldAxes[axisIdx]);
                float depth = i < 3 ? viewAxis.z : -viewAxis.z;
                bool isFront = depth >= 0;

                Vector2 pos = _axisScreenPos[i];
                Color c = colors[axisIdx];

                GUIStyle style;
                if (_hoveredAxis == i)
                    style = _labelHoverStyle;
                else if (!isFront)
                    style = _labelDimStyle;
                else
                    style = _labelStyle;

                if (isFront || _hoveredAxis == i)
                {
                    style.normal.textColor = _hoveredAxis == i
                        ? new Color(1f, 1f, 0.6f)
                        : new Color(c.r, c.g, c.b);
                }

                // Offset label from the dot
                Vector2 dir = (pos - GetGizmoCenter()).normalized;
                Vector2 labelPos = pos + dir * 10f;

                Rect labelRect = new Rect(labelPos.x - 14f, labelPos.y - 10f, 28f, 20f);
                GUI.Label(labelRect, _axisLabels[i], style);
            }
        }

        private void UpdateHover()
        {
            _hoveredAxis = -1;
            Vector2 mousePos = GetMouseScreenPos();
            float bestDist = 18f; // hit radius in pixels

            for (int i = 0; i < 6; i++)
            {
                float dist = Vector2.Distance(mousePos, _axisScreenPos[i]);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    _hoveredAxis = i;
                }
            }
        }

        private void HandleClick()
        {
            if (!GetMouseButtonDown(0)) return;
            if (_hoveredAxis < 0) return;

            if (_camController == null)
                _camController = GetComponent<CameraController>();

            if (_camController == null) return;

            switch (_hoveredAxis)
            {
                case 0: // +X (front)
                    _camController.SnapToView("front");
                    break;
                case 1: // +Y (top)
                    _camController.SnapToView("top");
                    break;
                case 2: // +Z (side)
                    _camController.SnapToView("side");
                    break;
                case 3: // -X (back)
                    SnapToCustomView(180f, 0f);
                    break;
                case 4: // -Y (bottom)
                    SnapToCustomView(0f, -89f);
                    break;
                case 5: // -Z (opposite side)
                    SnapToCustomView(-90f, 0f);
                    break;
            }
        }

        private void SnapToCustomView(float yaw, float pitch)
        {
            // Access the CameraController's private angle fields via reflection-free approach:
            // CameraController.SnapToView sets currentX and currentY, so we use a similar pattern
            if (_camController == null) return;

            // Use reflection to set the private fields since SnapToView only handles 4 views
            var type = typeof(CameraController);
            var xField = type.GetField("currentX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var yField = type.GetField("currentY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (xField != null) xField.SetValue(_camController, yaw);
            if (yField != null) yField.SetValue(_camController, pitch);
        }

        // GL drawing helpers
        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            if (width <= 1.5f)
            {
                GL.Begin(GL.LINES);
                GL.Color(color);
                GL.Vertex3(a.x, a.y, 0);
                GL.Vertex3(b.x, b.y, 0);
                GL.End();
                return;
            }

            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * (width * 0.5f);

            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(a.x + perp.x, a.y + perp.y, 0);
            GL.Vertex3(a.x - perp.x, a.y - perp.y, 0);
            GL.Vertex3(b.x - perp.x, b.y - perp.y, 0);
            GL.Vertex3(b.x + perp.x, b.y + perp.y, 0);
            GL.End();
        }

        private static void DrawCircle(Vector2 center, float radius, Color color, int segments)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            float step = 2f * Mathf.PI / segments;
            for (int i = 0; i < segments; i++)
            {
                float a1 = i * step;
                float a2 = (i + 1) * step;
                GL.Vertex3(center.x, center.y, 0);
                GL.Vertex3(center.x + Mathf.Cos(a1) * radius, center.y + Mathf.Sin(a1) * radius, 0);
                GL.Vertex3(center.x + Mathf.Cos(a2) * radius, center.y + Mathf.Sin(a2) * radius, 0);
            }
            GL.End();
        }

        private static Vector2 GetMouseScreenPos()
        {
            Vector3 pos = Input.mousePosition;
#if ENABLE_INPUT_SYSTEM
            if ((pos.x == 0f && pos.y == 0f) && Mouse.current != null)
            {
                Vector2 p = Mouse.current.position.ReadValue();
                pos = new Vector3(p.x, p.y, 0f);
            }
#endif
            // Convert from bottom-left to top-left origin (for GUI/GL coords)
            return new Vector2(pos.x, Screen.height - pos.y);
        }

        private static bool GetMouseButtonDown(int button)
        {
            bool down = Input.GetMouseButtonDown(button);
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                switch (button)
                {
                    case 0: down |= Mouse.current.leftButton.wasPressedThisFrame; break;
                    case 1: down |= Mouse.current.rightButton.wasPressedThisFrame; break;
                }
            }
#endif
            return down;
        }

        private static bool GetMouseButton(int button)
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

        private void OnDestroy()
        {
            if (_glMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(_glMaterial);
                else
                    DestroyImmediate(_glMaterial);
            }
        }
    }
}
