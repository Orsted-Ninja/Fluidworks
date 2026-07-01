using UnityEngine;
using UnityEngine.UIElements;

namespace FluidWorks.UI
{
    public class HelpUIController : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _helpOverlay;

        public void Initialize(VisualElement root)
        {
            _root = root;
            
            var template = Resources.Load<VisualTreeAsset>("UI/UXML/HelpWindow");
            if (template == null)
            {
                Debug.LogError("[HelpUI] Could not find HelpWindow.uxml in Resources/UI/UXML/");
                return;
            }

            var window = template.Instantiate();
            window.style.position = Position.Absolute;
            window.style.left = 0;
            window.style.top = 0;
            window.style.right = 0;
            window.style.bottom = 0;
            window.pickingMode = PickingMode.Ignore;
            
            _helpOverlay = window.Q<VisualElement>("help-overlay");
            if (_helpOverlay != null)
            {
                _helpOverlay.style.display = DisplayStyle.None;
            }
            
            _root.Add(window);

            // Bind Buttons
            window.Q<Button>("help-close-x").clicked += Hide;
            window.Q<Button>("help-close-btn").clicked += Hide;
        }

        public void Show()
        {
            if (_helpOverlay == null) return;
            _helpOverlay.parent?.BringToFront();
            _helpOverlay.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (_helpOverlay == null) return;
            _helpOverlay.style.display = DisplayStyle.None;
        }
    }
}
