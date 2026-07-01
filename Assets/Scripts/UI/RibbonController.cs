using System;
using UnityEngine.UIElements;
using AeroFlow.Managers;

public class RibbonController
{
    public event Action OnPlayClicked;
    public event Action OnPauseClicked;
    public event Action<float> OnSpeedChanged;
    public event Action<float> OnCameraSpeedChanged;
    public event Action OnCloseClicked;
    public event Action OnHomeClicked;
    public event Action<string> OnViewChanged;
    public event Action<string> OnRenderModeChanged;
    public event Action OnExportVideoClicked;
    public event Action OnExportScreenshotClicked;
    public event Action OnExportCsvClicked;
    public event Action OnExportJsonClicked;
    public event Action OnExportHtmlReportClicked;
    public event Action<string> OnPanelSelected;
    public event Action OnOutlinePanelToggled;
    public event Action OnConsolePanelToggled;
    public event Action OnPropertiesPanelToggled;
    public event Action OnLoadWindTunnelClicked;
    public event Action OnLoadSPHClicked;
    public event Action OnLoadPipeFlowClicked;
    public event Action OnLoadRotatingMachineryClicked;
    public event Action<string> OnWorkflowTemplateSelected;

    private readonly VisualElement _ribbonRoot;
    private VisualElement _setupTabContent;
    private VisualElement _simulationTabContent;
    private VisualElement _viewTabContent;
    private VisualElement _windowsTabContent;
    private Button _playPauseButton;
    private Button _quickPlayToggleButton;
    private Button _quickSpeedDownButton;
    private Button _quickSpeedUpButton;
    private Button _closeAppButton;
    private Button _exportVideoButton;
    private Button _outlineToggleButton;
    private Button _consoleToggleButton;
    private Button _propertiesToggleButton;
    private Label _speedLabel;
    private Label _cameraSpeedLabel;
    private Slider _cameraSpeedSlider;
    private float _currentSpeed = 1.0f;
    private bool _isPaused = true;

    public RibbonController(VisualElement ribbonRoot)
    {
        _ribbonRoot = ribbonRoot;
        QueryElements();
        RegisterCallbacks();
        SetActiveTab("setup-tab");
        ApplyPlaybackState();
        UpdateSpeedLabel();
    }

    private void QueryElements()
    {
        _setupTabContent = _ribbonRoot.Q<VisualElement>("setup-tab-content");
        _simulationTabContent = _ribbonRoot.Q<VisualElement>("simulation-tab-content");
        _viewTabContent = _ribbonRoot.Q<VisualElement>("view-tab-content");
        _windowsTabContent = _ribbonRoot.Q<VisualElement>("windows-tab-content");
        _playPauseButton = _ribbonRoot.Q<Button>("play-pause-button");
        _quickPlayToggleButton = _ribbonRoot.Q<Button>("quick-play-toggle-button");
        _quickSpeedDownButton = _ribbonRoot.Q<Button>("quick-slow-button");
        _quickSpeedUpButton = _ribbonRoot.Q<Button>("quick-fast-button");
        _closeAppButton = _ribbonRoot.Q<Button>("close-app-button");
        _exportVideoButton = _ribbonRoot.Q<Button>("export-video-button");
        _outlineToggleButton = _ribbonRoot.Q<Button>("windows-outline-toggle-button");
        _consoleToggleButton = _ribbonRoot.Q<Button>("windows-console-toggle-button");
        _propertiesToggleButton = _ribbonRoot.Q<Button>("windows-properties-toggle-button");
        _speedLabel = _ribbonRoot.Q<Label>("speed-label");
        _cameraSpeedLabel = _ribbonRoot.Q<Label>("camera-speed-label");
        _cameraSpeedSlider = _ribbonRoot.Q<Slider>("camera-speed-slider");
    }

    private void RegisterCallbacks()
    {
        BindButton("home-screen-button", () => OnHomeClicked?.Invoke());
        BindButton("setup-tab-button", () => SetActiveTab("setup-tab"));
        BindButton("simulation-tab-button", () => SetActiveTab("simulation-tab"));
        BindButton("view-tab-button", () => SetActiveTab("view-tab"));
        BindButton("windows-tab-button", () => SetActiveTab("windows-tab"));

        BindButton("template-external-aero-button", () => SelectTemplate(WorkflowTemplateCatalog.ExternalAero));
        BindButton("template-internal-flow-button", () => SelectTemplate(WorkflowTemplateCatalog.InternalFlow));
        BindButton("template-rotating-machinery-button", () => SelectTemplate(WorkflowTemplateCatalog.RotatingMachinery));
        BindButton("template-free-surface-button", () => SelectTemplate(WorkflowTemplateCatalog.FreeSurface));
        BindButton("template-fsi-lite-button", () => SelectTemplate(WorkflowTemplateCatalog.FsiLite));
        BindButton("template-playback-validation-button", () => SelectTemplate(WorkflowTemplateCatalog.PlaybackValidation));

        // Simulation tab: capture & export
        BindButton("export-video-button", () => OnExportVideoClicked?.Invoke());
        BindButton("export-screenshot-button", () => OnExportScreenshotClicked?.Invoke());
        BindButton("export-csv-button", () => OnExportCsvClicked?.Invoke());
        BindButton("export-json-button", () => OnExportJsonClicked?.Invoke());
        BindButton("export-report-button", () => OnExportHtmlReportClicked?.Invoke());

        if (_playPauseButton != null) _playPauseButton.clicked += TogglePlayPause;
        if (_quickPlayToggleButton != null) _quickPlayToggleButton.clicked += TogglePlayPause;
        if (_quickSpeedDownButton != null) _quickSpeedDownButton.clicked += () => ChangeSpeed(-0.2f);
        if (_quickSpeedUpButton != null) _quickSpeedUpButton.clicked += () => ChangeSpeed(0.2f);
        BindButton("rewind-button", () => ChangeSpeed(-0.2f));
        BindButton("fast-forward-button", () => ChangeSpeed(0.2f));
        BindButton("geometry-panel-button", () => OnPanelSelected?.Invoke("geometry"));
        BindButton("fluids-panel-button", () => OnPanelSelected?.Invoke("materials"));
        BindButton("results-panel-button", () => OnPanelSelected?.Invoke("results"));
        if (_outlineToggleButton != null) _outlineToggleButton.clicked += () => OnOutlinePanelToggled?.Invoke();
        if (_consoleToggleButton != null) _consoleToggleButton.clicked += () => OnConsolePanelToggled?.Invoke();
        if (_propertiesToggleButton != null) _propertiesToggleButton.clicked += () => OnPropertiesPanelToggled?.Invoke();

        BindButton("iso-view-button", () => OnViewChanged?.Invoke("iso"));
        BindButton("front-view-button", () => OnViewChanged?.Invoke("front"));
        BindButton("top-view-button", () => OnViewChanged?.Invoke("top"));
        BindButton("side-view-button", () => OnViewChanged?.Invoke("side"));
        BindButton("standard-render-button", () => OnRenderModeChanged?.Invoke("Standard"));
        BindButton("wireframe-render-button", () => OnRenderModeChanged?.Invoke("Wireframe"));

        if (_cameraSpeedSlider != null)
        {
            _cameraSpeedSlider.RegisterValueChangedCallback(evt =>
            {
                float value = UnityEngine.Mathf.Clamp(evt.newValue, 2f, 40f);
                if (_cameraSpeedLabel != null) _cameraSpeedLabel.text = $"Speed: {value:F1}";
                OnCameraSpeedChanged?.Invoke(value);
            });

            float initialValue = UnityEngine.Mathf.Clamp(_cameraSpeedSlider.value, 2f, 40f);
            if (_cameraSpeedLabel != null) _cameraSpeedLabel.text = $"Speed: {initialValue:F1}";
        }

        if (_closeAppButton != null) _closeAppButton.clicked += () => OnCloseClicked?.Invoke();
    }

    private void SelectTemplate(string templateId)
    {
        OnWorkflowTemplateSelected?.Invoke(templateId);
        switch (templateId)
        {
            case WorkflowTemplateCatalog.ExternalAero:
                OnLoadWindTunnelClicked?.Invoke();
                break;
            case WorkflowTemplateCatalog.FreeSurface:
                OnLoadSPHClicked?.Invoke();
                break;
            case WorkflowTemplateCatalog.InternalFlow:
                OnLoadPipeFlowClicked?.Invoke();
                break;
            case WorkflowTemplateCatalog.RotatingMachinery:
                OnLoadRotatingMachineryClicked?.Invoke();
                break;
            default:
                OnLoadWindTunnelClicked?.Invoke();
                break;
        }
    }

    private void BindButton(string name, Action onClicked)
    {
        if (onClicked == null) return;

        var button = _ribbonRoot.Q<Button>(name);
        if (button != null)
        {
            button.clicked += () => onClicked();
        }
    }

    private void SetActiveTab(string tabName)
    {
        if (_setupTabContent != null) _setupTabContent.style.display = tabName == "setup-tab" ? DisplayStyle.Flex : DisplayStyle.None;
        if (_simulationTabContent != null) _simulationTabContent.style.display = tabName == "simulation-tab" ? DisplayStyle.Flex : DisplayStyle.None;
        if (_viewTabContent != null) _viewTabContent.style.display = tabName == "view-tab" ? DisplayStyle.Flex : DisplayStyle.None;
        if (_windowsTabContent != null) _windowsTabContent.style.display = tabName == "windows-tab" ? DisplayStyle.Flex : DisplayStyle.None;

        _ribbonRoot.Q<Button>("setup-tab-button")?.EnableInClassList("ribbon-tab-button--active", tabName == "setup-tab");
        _ribbonRoot.Q<Button>("simulation-tab-button")?.EnableInClassList("ribbon-tab-button--active", tabName == "simulation-tab");
        _ribbonRoot.Q<Button>("view-tab-button")?.EnableInClassList("ribbon-tab-button--active", tabName == "view-tab");
        _ribbonRoot.Q<Button>("windows-tab-button")?.EnableInClassList("ribbon-tab-button--active", tabName == "windows-tab");
    }

    public void SetWindowPanelStates(bool outlineVisible, bool consoleVisible, bool propertiesVisible)
    {
        SetToggleButtonState(_outlineToggleButton, "Outline", outlineVisible);
        SetToggleButtonState(_consoleToggleButton, "Console", consoleVisible);
        SetToggleButtonState(_propertiesToggleButton, "Properties", propertiesVisible);
    }

    private static void SetToggleButtonState(Button button, string label, bool isVisible)
    {
        if (button == null) return;
        button.text = $"{label}: {(isVisible ? "On" : "Off")}";
        button.EnableInClassList("ribbon-toggle-button--active", isVisible);
    }

    private void TogglePlayPause()
    {
        _isPaused = !_isPaused;
        ApplyPlaybackState();

        if (_isPaused)
        {
            OnPauseClicked?.Invoke();
        }
        else
        {
            OnPlayClicked?.Invoke();
        }
    }

    private void ChangeSpeed(float delta)
    {
        _currentSpeed = UnityEngine.Mathf.Clamp(_currentSpeed + delta, 0.2f, 3.0f);
        UpdateSpeedLabel();
        OnSpeedChanged?.Invoke(_currentSpeed);
    }

    private void ApplyPlaybackState()
    {
        if (_playPauseButton != null) _playPauseButton.text = _isPaused ? "Play" : "Pause";
        if (_quickPlayToggleButton != null) _quickPlayToggleButton.text = _isPaused ? ">" : "||";
    }

    private void UpdateSpeedLabel()
    {
        if (_speedLabel != null) _speedLabel.text = $"({_currentSpeed:F1}x)";
    }

    public void SetPlaybackState(bool isPaused)
    {
        _isPaused = isPaused;
        ApplyPlaybackState();
    }

    public void SetPlaybackSpeed(float speed)
    {
        _currentSpeed = UnityEngine.Mathf.Clamp(speed, 0.2f, 3.0f);
        UpdateSpeedLabel();
    }

    public void SetRecordingState(bool isRecording)
    {
        if (_exportVideoButton == null) return;
        _exportVideoButton.text = isRecording ? "Stop Recording" : "Record Video";
    }
}
