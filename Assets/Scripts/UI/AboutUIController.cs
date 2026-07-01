using UnityEngine;
using UnityEngine.UIElements;

namespace FluidWorks.UI
{
    public class AboutUIController : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _aboutOverlay;

        public void Initialize(VisualElement root)
        {
            _root = root;
            
            var template = Resources.Load<VisualTreeAsset>("UI/UXML/AboutWindow");
            if (template == null)
            {
                Debug.LogError("[AboutUI] Could not find AboutWindow.uxml in Resources/UI/UXML/");
                return;
            }

            var window = template.Instantiate();
            window.style.position = Position.Absolute;
            window.style.left = 0;
            window.style.top = 0;
            window.style.right = 0;
            window.style.bottom = 0;
            window.pickingMode = PickingMode.Ignore;
            
            _aboutOverlay = window.Q<VisualElement>("about-overlay");
            if (_aboutOverlay != null)
            {
                _aboutOverlay.style.display = DisplayStyle.None;
            }
            
            _root.Add(window);

            // Bind Buttons
            window.Q<Button>("about-close-x").clicked += Hide;
            window.Q<Button>("about-close-btn").clicked += Hide;
            window.Q<Button>("about-docs-btn").clicked += () => Application.OpenURL("https://github.com/abhinavmanoj05/Aeroflow-final/wiki");
        }

        public void Show()
        {
            if (_aboutOverlay == null) return;
            _aboutOverlay.parent?.BringToFront();
            _aboutOverlay.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (_aboutOverlay == null) return;
            _aboutOverlay.style.display = DisplayStyle.None;
        }
    }
}
