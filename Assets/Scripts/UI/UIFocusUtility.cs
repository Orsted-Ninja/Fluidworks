using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;

namespace AeroFlow.UI
{
    public static class UIFocusUtility
    {
        private static UIDocument _cachedDocument;

        public static bool IsTextInputFocused()
        {
            if (_cachedDocument == null || _cachedDocument.rootVisualElement == null)
            {
                _cachedDocument = Object.FindFirstObjectByType<UIDocument>();
            }

            var panel = _cachedDocument?.rootVisualElement?.panel;
            if (panel?.focusController == null)
            {
                return false;
            }

            var focusedElement = panel.focusController.focusedElement as VisualElement;
            return IsTextInputElement(focusedElement);
        }

        private static bool IsTextInputElement(VisualElement element)
        {
            while (element != null)
            {
                if (element is TextField || element is FloatField || element is IntegerField)
                {
                    return true;
                }

                if (element.ClassListContains("unity-text-input"))
                {
                    return true;
                }

                element = element.parent;
            }

            return false;
        }

        public static bool IsPointerOverUI()
        {
            // 1. Check legacy EventSystem (UGUI/Editor fallback)
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return true;
            }

            // 2. Check UI Toolkit Panels
            if (_cachedDocument == null || _cachedDocument.rootVisualElement == null)
            {
                _cachedDocument = Object.FindFirstObjectByType<UIDocument>();
            }

            if (_cachedDocument != null && _cachedDocument.rootVisualElement != null)
            {
                Vector2 mousePos = Input.mousePosition;
                // UI Toolkit uses top-left origin, Unity Input uses bottom-left
                mousePos.y = (float)Screen.height - mousePos.y;

                // Pick the leaf element under the pointer
                VisualElement picked = _cachedDocument.rootVisualElement.panel.Pick(mousePos);
                
                // Climb the hierarchy to see if we belong to a blocking container
                while (picked != null)
                {
                    string n = picked.name;
                    
                    // Known blocking containers defined in MainLayout.uxml
                    if (n == "left-sidebar" || n == "right-sidebar" || n == "console" || 
                        n == "ribbon-container" || n == "status-bar" || n == "load-prompt" ||
                        n == "left-resizer" || n == "right-resizer" || n == "console-resizer")
                    {
                        return true;
                    }

                    // Interactive elements always block
                    if (picked is Button || picked is TextField || picked is Slider || picked is ScrollView)
                    {
                        return true;
                    }

                    // Special case: if we hit the viewport specifically, we definitely want camera interaction
                    if (n == "viewport")
                    {
                        return false;
                    }

                    picked = picked.parent;
                }
            }

            return false;
        }
    }
}
