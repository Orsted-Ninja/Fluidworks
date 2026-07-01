using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.UI
{
    public class PanelResizer
    {
        public enum Direction { Horizontal, Vertical }

        private readonly VisualElement _resizerElement;
        private readonly VisualElement _targetPanel;
        private readonly Direction _direction;
        private readonly bool _fromRightOrBottom;
        
        private bool _isDragging;
        private Vector2 _startMousePos;
        private float _startSize;

        public PanelResizer(VisualElement resizer, VisualElement target, Direction dir, bool fromRightOrBottom = false)
        {
            _resizerElement = resizer;
            _targetPanel = target;
            _direction = dir;
            _fromRightOrBottom = fromRightOrBottom;

            _resizerElement.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _resizerElement.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _resizerElement.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _resizerElement.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            _isDragging = true;
            _startMousePos = evt.position;
            _startSize = (_direction == Direction.Vertical) ? _targetPanel.resolvedStyle.width : _targetPanel.resolvedStyle.height;
            
            _resizerElement.CapturePointer(evt.pointerId);
            _resizerElement.AddToClassList("resizer--active");
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_isDragging) return;

            Vector2 diff = (Vector2)evt.position - _startMousePos;
            
            if (_direction == Direction.Vertical)
            {
                float newWidth = _fromRightOrBottom ? _startSize - diff.x : _startSize + diff.x;
                newWidth = Mathf.Clamp(newWidth, 100f, 600f);
                _targetPanel.style.width = newWidth;
            }
            else
            {
                float newHeight = _fromRightOrBottom ? _startSize - diff.y : _startSize + diff.y;
                newHeight = Mathf.Clamp(newHeight, 80f, 500f);
                _targetPanel.style.height = newHeight;
                
                // If console is collapsed, don't allow resizing? 
                // Actually, resizing should probably expand it.
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            CleanUp(evt.pointerId);
        }

        private void OnPointerCaptureOut(PointerCaptureOutEvent evt)
        {
            CleanUp(evt.pointerId);
        }

        private void CleanUp(int pointerId)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            _resizerElement.ReleasePointer(pointerId);
            _resizerElement.RemoveFromClassList("resizer--active");
        }
    }
}
