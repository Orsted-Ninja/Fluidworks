using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.Core
{
    public class ModelDragController : MonoBehaviour
    {
        public float dragPlaneHeight = 0f;
        public float dragSmooth = 15f;

        private Camera _cam;
        private bool _dragging;
        private Vector3 _offset;
        private Vector3 _targetPos;
        private UIDocument _uiDocument;
        private Rigidbody _rb;
        private bool _wasKinematic;

        private void Start()
        {
            _cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            _uiDocument = FindAnyObjectByType<UIDocument>();
            _targetPos = transform.position;
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (_cam == null) return;

            if (Input.GetMouseButtonDown(0) && Input.GetKey(KeyCode.LeftControl))
            {
                if (IsPointerOverUI()) return;
                if (TryBeginDrag()) return;
            }

            if (Input.GetMouseButtonUp(0) && _dragging)
            {
                _dragging = false;
                if (_rb != null)
                {
                    _rb.isKinematic = _wasKinematic;
                }
            }

            if (_dragging)
            {
                var plane = new Plane(Vector3.up, new Vector3(0, dragPlaneHeight, 0));
                Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
                if (plane.Raycast(ray, out float enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    _targetPos = hit + _offset;
                }
                
                // Only lerp the position while dragging so it doesn't fight physics when dropped
                transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * dragSmooth);
            }
        }

        private bool TryBeginDrag()
        {
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (UnityEngine.Physics.Raycast(ray, out RaycastHit hit, 500f))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    _dragging = true;
                    // Update drag height to the current height of the object, 
                    // in case it fell or moved due to physics natively 
                    dragPlaneHeight = transform.position.y;
                    
                    _offset = transform.position - hit.point;
                    _targetPos = transform.position;

                    // Temporarily make kinematic to drag cleanly without physics fighting
                    if (_rb != null)
                    {
                        _wasKinematic = _rb.isKinematic;
                        _rb.linearVelocity = Vector3.zero;
                        _rb.angularVelocity = Vector3.zero;
                        _rb.isKinematic = true;
                    }
                    
                    return true;
                }
            }
            return false;
        }

        private bool IsPointerOverUI()
        {
            if (_uiDocument == null || _uiDocument.rootVisualElement == null) return false;
            var panel = _uiDocument.rootVisualElement.panel;
            if (panel == null) return false;
            return panel.Pick(Input.mousePosition) != null;
        }
    }
}
