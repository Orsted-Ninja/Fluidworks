using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using FluidWorks.Marketplace;

namespace FluidWorks.UI.Marketplace
{
    public class MarketplaceUIController : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _overlay;
        private VisualElement _grid;
        private VisualElement _loadingOverlay;
        private List<TemplateMetadata> _templates = new List<TemplateMetadata>();

        public void Initialize(VisualElement root)
        {
            _root = root;

            var template = Resources.Load<VisualTreeAsset>("UI/UXML/Marketplace");
            if (template == null)
            {
                Debug.LogError("[MarketplaceUI] Could not find Marketplace.uxml in Resources/UI/UXML/");
                return;
            }

            var window = template.Instantiate();
            window.style.position = Position.Absolute;
            window.style.left = 0;
            window.style.top = 0;
            window.style.right = 0;
            window.style.bottom = 0;
            window.pickingMode = PickingMode.Ignore;

            _overlay = window.Q<VisualElement>("marketplace-overlay");
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }
            
            _root.Add(window);

            _grid = window.Q<VisualElement>("market-grid");
            _loadingOverlay = window.Q<VisualElement>("market-loading");

            // Bind Buttons
            window.Q<Button>("market-close-btn").clicked += Hide;
            
            // Filter buttons
            BindFilter(window, "filter-featured", false);
            BindFilter(window, "filter-installed", true);

            // Search
            var searchField = window.Q<TextField>("market-search");
            searchField?.RegisterValueChangedCallback(evt => RefreshGrid(false, evt.newValue));
        }

        private void BindFilter(VisualElement window, string btnName, bool installedOnly)
        {
            var btn = window.Q<Button>(btnName);
            if (btn != null)
            {
                btn.clicked += () => 
                {
                    // Update active state
                    var buttons = window.Query<Button>(className: "sidebar-btn").ToList();
                    foreach (var b in buttons) b.RemoveFromClassList("sidebar-btn--active");
                    btn.AddToClassList("sidebar-btn--active");

                    RefreshGrid(installedOnly);
                };
            }
        }

        public void Show()
        {
            if (_overlay == null) return;
            _overlay.parent?.BringToFront();
            _overlay.style.display = DisplayStyle.Flex;
            FetchData();
        }

        public void Hide()
        {
            if (_overlay == null) return;
            _overlay.style.display = DisplayStyle.None;
        }

        private void FetchData()
        {
            _loadingOverlay.style.display = DisplayStyle.Flex;
            StartCoroutine(MarketplaceAPI.FetchTemplates(
                templates => 
                {
                    _templates = templates;
                    _loadingOverlay.style.display = DisplayStyle.None;
                    RefreshGrid(false);
                },
                error => 
                {
                    Debug.LogWarning("[MarketplaceUI] API Fetch failed: " + error);
                    _loadingOverlay.style.display = DisplayStyle.None;
                    
                    // Offline Mock Data
                    if (_templates.Count == 0)
                    {
                        _templates = GetMockTemplates();
                    }
                    RefreshGrid(false);
                }
            ));
        }

        private List<TemplateMetadata> GetMockTemplates()
        {
            return new List<TemplateMetadata>
            {
                new TemplateMetadata { id = "f1_2024", name = "F1 2024 Chassis", author = "AeroTeam", category = "Automotive", difficulty = "Expert", solver = "Navier-Stokes (Air)" },
                new TemplateMetadata { id = "wing_naca", name = "NACA 2412 Airfoil", author = "FluidWorks", category = "Aviation", difficulty = "Beginner", solver = "Navier-Stokes (Air)" },
                new TemplateMetadata { id = "pipe_bend", name = "Turbulent Pipe Bend", author = "IndustrialSim", category = "Industrial", difficulty = "Intermediate", solver = "PipeFlow (Liquid)" },
                new TemplateMetadata { id = "dam_break_prop", name = "Coastal Barrier Test", author = "NatureSim", category = "Environmental", difficulty = "Expert", solver = "SPH (Liquid)" }
            };
        }

        private void RefreshGrid(bool installedOnly, string searchText = "")
        {
            _grid.Clear();
            
            var list = _templates;
            if (installedOnly)
            {
                list = list.Where(t => TemplateDownloadManager.Instance.IsDownloaded(t.id)).ToList();
            }

            if (!string.IsNullOrEmpty(searchText))
            {
                list = list.Where(t => t.name.ToLower().Contains(searchText.ToLower()) || t.author.ToLower().Contains(searchText.ToLower())).ToList();
            }

            foreach (var template in list)
            {
                CreateCard(template);
            }
        }

        private void CreateCard(TemplateMetadata t)
        {
            var cardTemplate = Resources.Load<VisualTreeAsset>("UI/UXML/TemplateCard");
            if (cardTemplate == null) return;

            var card = cardTemplate.Instantiate();
            card.Q<Label>("card-title").text = t.name;
            card.Q<Label>("card-author").text = "by " + t.author;
            card.Q<Label>("card-type").text = t.category;
            card.Q<Label>("card-difficulty").text = t.difficulty;
            card.Q<Label>("card-badge-text").text = t.solver.Contains("SPH") ? "Fluid" : "Air";

            var btnDownload = card.Q<Button>("card-download-btn");
            var btnImport = card.Q<Button>("card-import-btn");
            var progressContainer = card.Q<VisualElement>("card-progress");
            var progressBarFill = card.Q<VisualElement>("progress-bar");

            bool isDownloaded = TemplateDownloadManager.Instance != null && TemplateDownloadManager.Instance.IsDownloaded(t.id);
            btnDownload.style.display = isDownloaded ? DisplayStyle.None : DisplayStyle.Flex;
            btnImport.style.display = isDownloaded ? DisplayStyle.Flex : DisplayStyle.None;

            btnDownload.clicked += () => StartCoroutine(HandleDownload(t, btnDownload, btnImport, progressContainer, progressBarFill));
            btnImport.clicked += () => 
            {
                TemplateImporter.Instance?.ImportTemplate(TemplateDownloadManager.Instance.GetTemplateArchivePath(t.id));
                // Hide(); // Optional: Close marketplace on import
            };

            _grid.Add(card);
        }

        private IEnumerator HandleDownload(TemplateMetadata t, Button dlBtn, Button imBtn, VisualElement progressCont, VisualElement barFill)
        {
            dlBtn.style.display = DisplayStyle.None;
            progressCont.style.display = DisplayStyle.Flex;

            // Start download
            bool success = false;
            StartCoroutine(TemplateDownloadManager.Instance.DownloadTemplate(t, s => success = s));

            // Wait and update progress
            while (TemplateDownloadManager.Instance.GetProgress(t.id) < 1.0f && TemplateDownloadManager.Instance.GetProgress(t.id) >= 0f)
            {
                float p = TemplateDownloadManager.Instance.GetProgress(t.id);
                barFill.style.width = Length.Percent(p * 100f);
                yield return null;
            }

            progressCont.style.display = DisplayStyle.None;
            if (success || TemplateDownloadManager.Instance.IsDownloaded(t.id))
            {
                imBtn.style.display = DisplayStyle.Flex;
            }
            else
            {
                dlBtn.style.display = DisplayStyle.Flex;
                Debug.LogError("[MarketplaceUI] Download failed for " + t.name);
            }
        }
    }
}
