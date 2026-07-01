using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace AeroFlow.UI
{
    public class PropertiesPanelController
    {
        private VisualElement _root;
        private VisualElement _container;
        private string _activeViewKey;

        // Cache for the loaded VisualTreeAssets
        private Dictionary<string, VisualTreeAsset> _viewTemplates = new Dictionary<string, VisualTreeAsset>();

        public PropertiesPanelController(VisualElement root)
        {
            _root = root;
            _container = _root.Q<VisualElement>("properties-container"); // Assuming a container exists in MainLayout.uxml

            // Preload all the different property views
            PreloadView("boundary", "UI/UXML/BoundaryConditions");
            PreloadView("fluid", "UI/UXML/FluidProperties");
            PreloadView("results", "UI/UXML/AnalysisProperties");
            PreloadView("dambreak", "UI/UXML/DamBreak/DamBreakProperties");
            PreloadView("windtunnel", "UI/UXML/WindTunnel/WindTunnelProperties");
            PreloadView("workflow", "UI/UXML/WorkflowTemplateProperties");
            PreloadView("pipeflow", "UI/UXML/PipeFlow/PipeFlowProperties");
            PreloadView("pipeflowmodel", "UI/UXML/PipeFlow/PipeFlowProperties");
            PreloadView("pipeboundary", "UI/UXML/PipeFlow/BoundaryConditionsPanel");
            PreloadView("machinery", "UI/UXML/RotatingMachinery/RotatingMachineryProperties");
            PreloadView("segmentation", "UI/UXML/ModelSegmentation");
        }
        
        private void PreloadView(string key, string path)
        {
            var template = Resources.Load<VisualTreeAsset>(path);
            if(template != null)
            {
                _viewTemplates[key] = template;
            }
            else
            {
                Debug.LogError($"[PropertiesPanelController] Failed to load view template at path: {path}");
            }
        }

        public void SetActiveView(string viewKey)
        {
            if (_container == null) return;
            viewKey = NormalizeViewKey(viewKey);
            
            if (_activeViewKey == viewKey && _container.childCount > 0) return;

            // Clear the previous view
            _container.Clear();
            _activeViewKey = viewKey;

            // Instantiate and add the new view if it exists
            if (_viewTemplates.TryGetValue(viewKey, out VisualTreeAsset template))
            {
                template.CloneTree(_container);
            }
            else
            {
                // Fallback for views not yet created
                _container.Add(new Label($"View '{viewKey}' not implemented."));
            }
        }

        public VisualElement GetContainer()
        {
            return _container;
        }

        private string NormalizeViewKey(string viewKey)
        {
            // If the key is already a specific view we have a template for, return it directly
            if (_viewTemplates.ContainsKey(viewKey)) return viewKey;

            switch (viewKey)
            {
                case "geometry":
                    return "boundary";
                case "materials":
                    return "fluid";
                case "monitors":
                    return "results";
                default:
                    return viewKey;
            }
        }

        public void BindFluidPropertiesControls()
        {
            if (_container != null)
            {
                new FluidPropertiesController(_container);
            }
        }

        public void BindDamBreakControls()
        {
            if (_container != null)
            {
                new DamBreakPropertiesController(_container);
            }
        }

        public void BindWindTunnelControls()
        {
            if (_container != null)
            {
                new WindTunnelPropertiesController(_container);
            }
        }

        public void BindPipeFlowControls(bool showSettingsOnOpen = true)
        {
            if (_container != null)
            {
                new PipeFlowPropertiesController(_container, showSettingsOnOpen);
            }
        }

        public void BindRotatingMachineryControls()
        {
            if (_container != null)
            {
                new RotatingMachineryPropertiesController(_container);
            }
        }

        public void BindPipeBoundaryConditionsControls()
        {
            if (_container != null)
            {
                new PipeBoundaryConditionsController(_container);
            }
        }

        public void SetWorkflowTemplateInfo(string title, string summary, string outputs)
        {
            if (_container == null) return;

            var titleLabel = _container.Q<Label>("workflow-template-title");
            if (titleLabel != null) titleLabel.text = title;

            var summaryLabel = _container.Q<Label>("workflow-template-summary");
            if (summaryLabel != null) summaryLabel.text = summary;

            var outputsLabel = _container.Q<Label>("workflow-template-outputs");
            if (outputsLabel != null) outputsLabel.text = outputs;
        }

        public void BindModelSegmentationControls()
        {
            if (_container != null)
            {
                new ModelSegmentationController(_container);
            }
        }
    }
}
