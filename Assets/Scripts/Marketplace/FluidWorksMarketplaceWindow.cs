#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Video;
using FluidWorks.Marketplace;

public class FluidWorksMarketplaceWindow : EditorWindow
{
    private VisualElement _root;
    private ScrollView _grid;
    private VisualTreeAsset _cardTemplate;
    private List<TemplateMetadata> _templates = new List<TemplateMetadata>();
    private Dictionary<string, VideoPlayer> _activePlayers = new Dictionary<string, VideoPlayer>();

    [MenuItem("FluidWorks/Marketplace")]
    public static void ShowWindow()
    {
        var window = GetWindow<FluidWorksMarketplaceWindow>("FluidWorks Marketplace");
        window.minSize = new Vector2(940, 600);
    }

    private void CreateGUI()
    {
        _root = rootVisualElement;

        // Load Styles
        var mainStyle = Resources.Load<StyleSheet>("UI/USS/MainStyle");
        var marketStyle = Resources.Load<StyleSheet>("UI/USS/Marketplace");
        if (mainStyle != null) _root.styleSheets.Add(mainStyle);
        if (marketStyle != null) _root.styleSheets.Add(marketStyle);

        _root.AddToClassList("root");
        _root.style.backgroundColor = new StyleColor(new Color(0.03f, 0.05f, 0.07f, 1f));

        // Header
        var header = new VisualElement { style = { height = 60, paddingLeft = 20, justifyContent = Justify.Center, borderBottomWidth = 1, borderBottomColor = new Color(0.3f, 0.6f, 0.8f, 0.2f) } };
        var title = new Label("FLUIDWORKS MARKETPLACE") { style = { fontSize = 20, color = new Color(0.15f, 0.76f, 0.94f), letterSpacing = 2 } };
        header.Add(title);
        _root.Add(header);

        // Tabs
        var tabContainer = new VisualElement { style = { flexDirection = FlexDirection.Row, paddingLeft = 10, height = 30, backgroundColor = new Color(0.04f, 0.08f, 0.12f) } };
        var btnAll = new Button { text = "Marketplace" };
        var btnInstalled = new Button { text = "Installed" };
        btnAll.AddToClassList("ribbon-tab-button");
        btnInstalled.AddToClassList("ribbon-tab-button");
        tabContainer.Add(btnAll);
        tabContainer.Add(btnInstalled);
        _root.Add(tabContainer);

        btnAll.clicked += () => RefreshGrid(false);
        btnInstalled.clicked += () => RefreshGrid(true);

        // Content
        _grid = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        _grid.contentContainer.AddToClassList("marketplace-grid");
        _root.Add(_grid);

        _cardTemplate = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/UI/UXML/Marketplace/MarketplaceCard.uxml");

        RefreshMarketplace();
    }

    private void RefreshGrid(bool installedOnly)
    {
        _grid.Clear();
        var list = installedOnly
            ? _templates.Where(t => TemplateDownloadManager.Instance != null && TemplateDownloadManager.Instance.IsDownloaded(t.id)).ToList()
            : _templates;

        foreach (var t in list)
        {
            var card = _cardTemplate.Instantiate();
            BindCard(card, t);
            _grid.Add(card);
        }
    }

    private void RefreshMarketplace()
    {
        _grid.Clear();
        _templates.Clear();

        // Fetch from API
        MarketplaceAPI.FetchTemplates(
            templates =>
            {
                _templates = templates;
                RefreshGrid(false);
            },
            error =>
            {
                Debug.LogWarning("[Marketplace] Offline mode: Loading local metadata...");
                // In a real scenario, we'd load a local templates_cache.json
                RefreshGrid(true);
            }
        ).RunCoroutine();
    }

    private void PopulateGrid()
    {
        foreach (var t in _templates)
        {
            var card = _cardTemplate.Instantiate();
            BindCard(card, t);
            _grid.Add(card);
        }
    }

    private void BindCard(VisualElement card, TemplateMetadata template)
    {
        card.Q<Label>("template-title").text = template.name;
        card.Q<Label>("template-author").text = "by " + template.author;
        card.Q<Label>("stat-difficulty").text = template.difficulty;
        card.Q<Label>("stat-solver").text = template.solver;

        var btnDownload = card.Q<Button>("btn-download");
        var btnImport = card.Q<Button>("btn-import");

        bool isDownloaded = TemplateDownloadManager.Instance != null && TemplateDownloadManager.Instance.IsDownloaded(template.id);
        btnDownload.style.display = isDownloaded ? DisplayStyle.None : DisplayStyle.Flex;
        btnImport.style.display = isDownloaded ? DisplayStyle.Flex : DisplayStyle.None;

        btnDownload.clicked += () => StartDownload(template, btnDownload, btnImport, card);
        btnImport.clicked += () =>
        {
            TemplateImporter.Instance?.ImportTemplate(TemplateDownloadManager.Instance.GetTemplateArchivePath(template.id));
        };

        // Video Setup
        SetupPreview(card.Q("video-render-target"), template);
    }

    private void StartDownload(TemplateMetadata t, Button downloadBtn, Button importBtn, VisualElement card)
    {
        if (TemplateDownloadManager.Instance == null) return;

        var progressLabel = card.Q<Label>("download-progress-overlay");
        progressLabel.style.display = DisplayStyle.Flex;

        TemplateDownloadManager.Instance.DownloadTemplate(t, success =>
        {
            if (success)
            {
                downloadBtn.style.display = DisplayStyle.None;
                importBtn.style.display = DisplayStyle.Flex;
                progressLabel.style.display = DisplayStyle.None;
            }
        }).RunCoroutine();
    }

    private void SetupPreview(VisualElement target, TemplateMetadata template)
    {
        // In a real editor script, we'd use a custom render texture and a hidden VideoPlayer or similar.
        // For this implementation, we'll placeholder the visual.
        target.style.backgroundColor = Color.black;
    }
}

// Simple editor-compatible coroutine runner
public static class CoroutineRunner
{
    public static void RunCoroutine(this System.Collections.IEnumerator enumerator)
    {
        EditorApplication.update += () =>
        {
            if (!enumerator.MoveNext())
            {
                // Finished
            }
        };
    }
}
#endif
