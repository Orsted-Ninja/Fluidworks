using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using AeroFlow.Core;
using AeroFlow.Managers;
using AeroFlow.Rendering;
using AeroFlow.UI;
using AeroFlow.Sim3D.PipeFlow;
using AeroFlow.Sim3D.RotatingMachinery;
using AeroFlow.Visualization;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using SFB;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Video;

namespace AeroFlow.UI
{
    public class MainScreenController : MonoBehaviour
    {
        private ProjectTreeController _projectTreeController;
        private PropertiesPanelController _propertiesPanelController;
        private RibbonController _ribbonController;
        private FluidWorks.Settings.SettingsUIController _settingsUI;
        private FluidWorks.UI.Marketplace.MarketplaceUIController _marketplaceUI;
        private FluidWorks.UI.AboutUIController _aboutUI;
        private FluidWorks.UI.HelpUIController _helpUI;

        // Project System Managers
        private FluidWorks.ProjectSystem.ProjectSaveManager _saveManager;
        private FluidWorks.ProjectSystem.ProjectLoadManager _loadManager;
        private string _activeProjectName = "Untitled_Project";

        private Camera _activeCamera;
        private CameraController _cameraController;
        
        private VisualElement _loadPrompt;
        private Button _loadGeometryBtn;
        private string _currentTemplateId = WorkflowTemplateCatalog.ExternalAero;
        private bool _hudHidden;
        private VisualElement _rootElement;
        private VisualElement _ribbonContainer;
        private VisualElement _mainContent;
        private VisualElement _leftSidebar;
        private VisualElement _rightSidebar;
        private VisualElement _leftResizer;
        private VisualElement _rightResizer;
        private VisualElement _consoleResizer;
        private VisualElement _statusBar;
        private VisualElement _viewportOverlayPrompt;
        private VisualElement _hudButton;
        private bool _loadPromptVisible = true;
        private bool _recordingIndicatorVisible;
        private VisualElement _homeScreen;
        private VisualElement _homeGroupProjectActions;
        private VisualElement _homeGroupSimulationTemplates;
        private Button _btnHomeWT, _btnHomePF, _btnHomeRM, _btnHomeDB, _btnConfirmTemplate;
        private string _pendingTemplateSelection = "";
        
        // Splash screen properties
        private VisualElement _splashScreen;
        private Label _splashStatusLabel;
        private VisualElement _splashProgressFill;

        // Home Trailer Video
        private VideoPlayer _homeVideoPlayer;
        private RenderTexture _homeVideoRT;

        // Console collapse state
        private VisualElement _console;
        private VisualElement _consoleScrollArea;
        private TextField _consoleInput;
        private Button _consoleSubmitButton;
        private bool _consoleExpanded = false;
        private bool _outlinePanelVisible = true;
        private bool _consolePanelVisible = true;
        private bool _propertiesPanelVisible = true;
        private const float ConsoleExpandedHeight = 140f;
        private const float ConsoleCollapsedHeight = 28f;

        // Recording indicator
        private VisualElement _recordingIndicator;

        // Status bar labels
        private Label _statusModeLabel;
        private Label _statusFpsLabel;
        private string _activePanelKey = "boundary";

        // Cached metric labels (avoid per-frame Q<Label> queries)
        private Label _dragLabel, _liftLabel, _sideForceLabel, _reynoldsLabel, _pressureLabel;
        private Label _referenceAreaLabel, _dragForceLabel, _verticalForceLabel, _downforceLabel;
        private Label _copLabel, _pitchMomentLabel, _yawMomentLabel, _rollMomentLabel;
        private Label _axleLoadLabel, _topSpeedLabel, _regimeLabel, _assessmentLabel;
        private Label _qualityLabel, _scoreLabel, _tipsLabel;
        private Label _navierState, _navierMean, _navierMax, _navierDeltaP, _navierTau, _navierDiv;
        private Label _modelScoreLabel, _modelGradeLabel, _modelCdLabel, _modelSepRiskLabel;
        private Label _modelDownforceLabel, _modelEfficiencyLabel, _modelFeaturesLabel, _modelImprovementsLabel;
        private bool _metricLabelsCached;
        
        private void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _rootElement = root;
            var modelLoader = FindAnyObjectByType<RuntimeModelLoader>();

            _saveManager = gameObject.AddComponent<FluidWorks.ProjectSystem.ProjectSaveManager>();
            _loadManager = gameObject.AddComponent<FluidWorks.ProjectSystem.ProjectLoadManager>();

            // Initialize Settings System
            if (FluidWorks.Settings.SettingsManager.Instance == null)
            {
                gameObject.AddComponent<FluidWorks.Settings.SettingsManager>();
            }
            _settingsUI = gameObject.AddComponent<FluidWorks.Settings.SettingsUIController>();
            _settingsUI.Initialize(root);

            _marketplaceUI = gameObject.AddComponent<FluidWorks.UI.Marketplace.MarketplaceUIController>();
            _marketplaceUI.Initialize(root);

            _aboutUI = gameObject.AddComponent<FluidWorks.UI.AboutUIController>();
            _aboutUI.Initialize(root);

            _helpUI = gameObject.AddComponent<FluidWorks.UI.HelpUIController>();
            _helpUI.Initialize(root);

            _projectTreeController = new ProjectTreeController(root);
            _propertiesPanelController = new PropertiesPanelController(root);
            _mainContent = root.Q<VisualElement>("main-content");
            
            // Look for the "ribbon" element which is the root of Ribbon.uxml inside the instance
            var ribbonElement = root.Q<VisualElement>("ribbon");
            if (ribbonElement != null) {
                _ribbonController = new RibbonController(ribbonElement);
            } else {
                Debug.LogError("Could not find 'ribbon' element in the UI hierarchy.");
            }
            
            _loadPrompt = root.Q<VisualElement>("load-prompt");
            _loadGeometryBtn = root.Q<Button>("load-geometry-btn");
            _ribbonContainer = root.Q<VisualElement>("ribbon-container");
            _leftSidebar = root.Q<VisualElement>("left-sidebar");
            _rightSidebar = root.Q<VisualElement>("right-sidebar");
            _statusBar = root.Q<VisualElement>("status-bar");
            _viewportOverlayPrompt = _loadPrompt;
            _hudButton = root.Q<Button>("hud-visibility-button");
            if (_hudButton is Button hudButton)
            {
                hudButton.clicked += ToggleHudVisibility;
            }

            // Homepage hooks
            _homeScreen = root.Q<VisualElement>("home-screen");
            if (_homeScreen != null)
            {
                var gpuLabel = _homeScreen.Q<Label>("home-gpu-hardware-label");
                if (gpuLabel != null) gpuLabel.text = UnityEngine.SystemInfo.graphicsDeviceName;

                _btnHomeWT = root.Q<Button>("home-btn-wind-tunnel");
                _btnHomePF = root.Q<Button>("home-btn-pipe-flow");
                _btnHomeRM = root.Q<Button>("home-btn-machinery");
                _btnHomeDB = root.Q<Button>("home-btn-dambreak");
                _btnConfirmTemplate = root.Q<Button>("home-btn-confirm-template");

                _btnHomeWT?.RegisterCallback<ClickEvent>(evt => SelectTemplate("windtunnel"));
                _btnHomePF?.RegisterCallback<ClickEvent>(evt => SelectTemplate("pipeflow"));
                _btnHomeRM?.RegisterCallback<ClickEvent>(evt => SelectTemplate("machinery"));
                _btnHomeDB?.RegisterCallback<ClickEvent>(evt => SelectTemplate("dambreak"));
                
                _btnConfirmTemplate?.RegisterCallback<ClickEvent>(evt => ConfirmTemplateSelection());

                // New Project Actions
                _homeGroupProjectActions = root.Q<VisualElement>("home-group-project-actions");
                _homeGroupSimulationTemplates = root.Q<VisualElement>("home-group-simulation-templates");
                
                var btnBackToActions = root.Q<Button>("home-btn-back-actions");
                btnBackToActions?.RegisterCallback<ClickEvent>(evt => ShowProjectActions());

                var btnNewProj = root.Q<Button>("home-btn-new-project");
                var btnOpenProj = root.Q<Button>("home-btn-open-project");
                var btnImportFile = root.Q<Button>("home-btn-import-file");
                var btnRecent = root.Q<Button>("home-btn-recent-projects");

                btnNewProj?.RegisterCallback<ClickEvent>(evt => CreateNewProject());
                btnOpenProj?.RegisterCallback<ClickEvent>(evt => OpenSystemProject());
                btnImportFile?.RegisterCallback<ClickEvent>(evt => {
                    DismissHomePage();
                    if (modelLoader != null) modelLoader.OpenFilePicker();
                });
                btnRecent?.RegisterCallback<ClickEvent>(evt => Debug.Log("Recent Projects Selected"));

                // System Options
                var btnMarketplace = root.Q<Button>("home-btn-marketplace");
                var btnSettings = root.Q<Button>("home-btn-settings");
                var btnHelp = root.Q<Button>("home-btn-help");
                var btnDocs = root.Q<Button>("home-btn-documentation");
                var btnAbout = root.Q<Button>("home-btn-about");

                btnMarketplace?.RegisterCallback<ClickEvent>(evt => _marketplaceUI.Show());
                btnSettings?.RegisterCallback<ClickEvent>(evt => _settingsUI.Show());
                btnHelp?.RegisterCallback<ClickEvent>(evt => _helpUI.Show());
                btnDocs?.RegisterCallback<ClickEvent>(evt => Application.OpenURL("https://github.com/abhinavmanoj05/Aeroflow-final/wiki"));
                btnAbout?.RegisterCallback<ClickEvent>(evt => _aboutUI.Show());
                // Exit Logic with Dialog
                var btnHomeExit = root.Q<Button>("home-btn-exit");
                var exitDialog = root.Q<VisualElement>("home-exit-dialog");
                var btnExitCancel = root.Q<Button>("home-btn-exit-cancel");
                var btnExitConfirm = root.Q<Button>("home-btn-exit-confirm");

                btnHomeExit?.RegisterCallback<ClickEvent>(evt => { if (exitDialog != null) exitDialog.style.display = DisplayStyle.Flex; });
                btnExitCancel?.RegisterCallback<ClickEvent>(evt => { if (exitDialog != null) exitDialog.style.display = DisplayStyle.None; });
                btnExitConfirm?.RegisterCallback<ClickEvent>(evt => HandleCloseApp());
                
                SetupHomeTrailer(root);
            }

            // Splash Screen initialization
            _splashScreen = root.Q<VisualElement>("splash-screen");
            if (_splashScreen != null)
            {
                _splashStatusLabel = root.Q<Label>("splash-status-label");
                _splashProgressFill = root.Q<VisualElement>("splash-progress-fill");

                // Disable home screen interaction while splash is running
                if (_homeScreen != null) _homeScreen.style.display = DisplayStyle.None;
                
                StartCoroutine(RunSplashScreenSequence());
            }

            // Console collapse
            _console = root.Q<VisualElement>("console");
            _consoleScrollArea = root.Q<VisualElement>("console-scroll");
            _consoleInput = root.Q<TextField>("console-input");
            _consoleSubmitButton = root.Q<Button>("console-submit-button");
            _leftResizer = root.Q<VisualElement>("left-resizer");
            _rightResizer = root.Q<VisualElement>("right-resizer");
            _consoleResizer = root.Q<VisualElement>("console-resizer");
            
            if (_consoleInput != null)
            {
                _consoleInput.RegisterCallback<NavigationSubmitEvent>(evt => ExecuteConsoleCommand());
            }

            if (_consoleSubmitButton != null)
            {
                _consoleSubmitButton.clicked += ExecuteConsoleCommand;
            }

            var consoleToggleBtn = root.Q<Button>("console-toggle-btn");
            if (consoleToggleBtn != null)
            {
                consoleToggleBtn.clicked += ToggleConsole;
            }
            if (_console != null) _console.style.height = ConsoleCollapsedHeight;

            // Initialize Panel Resizers
            var leftSidebar = root.Q<VisualElement>("left-sidebar");
            var rightSidebar = root.Q<VisualElement>("right-sidebar");
            var leftResizer = root.Q<VisualElement>("left-resizer");
            var rightResizer = root.Q<VisualElement>("right-resizer");
            var consoleResizer = root.Q<VisualElement>("console-resizer");

            if (leftResizer != null && leftSidebar != null)
                new PanelResizer(leftResizer, leftSidebar, PanelResizer.Direction.Vertical);
                
            if (rightResizer != null && rightSidebar != null)
                new PanelResizer(rightResizer, rightSidebar, PanelResizer.Direction.Vertical, fromRightOrBottom: true);
                
            if (consoleResizer != null && _console != null)
                new PanelResizer(consoleResizer, _console, PanelResizer.Direction.Horizontal, fromRightOrBottom: true);

            // Recording indicator
            _recordingIndicator = root.Q<VisualElement>("recording-indicator");

            // Status bar
            _statusModeLabel = root.Q<Label>("status-mode-label");
            _statusFpsLabel = root.Q<Label>("status-fps-label");

            if (_loadGeometryBtn != null && modelLoader != null)
            {
                _loadGeometryBtn.clicked += modelLoader.OpenFilePicker;
            }

            if (_projectTreeController != null && _propertiesPanelController != null)
            {
                _projectTreeController.OnSelectionChanged += (viewKey) =>
                {
                    string resolved = ResolveViewKey(viewKey);
                    _activePanelKey = resolved;
                    if (resolved != "results")
                    {
                        var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
                        sim?.SetLiveResultsComputationEnabled(false);
                    }
                    ShowAndBindPropertiesView(resolved);
                };
            }

            _propertiesPanelController.SetActiveView("windtunnel");
            _activePanelKey = "windtunnel";
            _propertiesPanelController.BindWindTunnelControls();
            // Home button is now handled via RibbonController.OnHomeClicked

            if (_ribbonController != null)
            {
                _ribbonController.OnLoadSPHClicked += LoadDamBreak;
                _ribbonController.OnLoadWindTunnelClicked += LoadWindTunnel;
                _ribbonController.OnLoadPipeFlowClicked += LoadPipeFlow;
                _ribbonController.OnLoadRotatingMachineryClicked += LoadRotatingMachinery;
                _ribbonController.OnWorkflowTemplateSelected += HandleWorkflowTemplateSelected;
                _ribbonController.OnPlayClicked += PlaySimulation;
                _ribbonController.OnPauseClicked += PauseSimulation;
                _ribbonController.OnSpeedChanged += HandleSimulationSpeedChanged;
                _ribbonController.OnViewChanged += HandleViewChanged;
                _ribbonController.OnRenderModeChanged += HandleRenderModeChanged;
                _ribbonController.OnCameraSpeedChanged += HandleCameraSpeedChanged;
                _ribbonController.OnPanelSelected += HandlePanelSelected;
                _ribbonController.OnExportVideoClicked += HandleExportVideo;
                _ribbonController.OnExportScreenshotClicked += HandleExportScreenshot;
                _ribbonController.OnExportCsvClicked += HandleExportResultsCsv;
                _ribbonController.OnExportJsonClicked += HandleExportSnapshotJson;
                _ribbonController.OnExportHtmlReportClicked += HandleExportHtmlReport;
                _ribbonController.OnCloseClicked += HandleCloseApp;
                _ribbonController.OnHomeClicked += ShowHomePage;
                _ribbonController.OnOutlinePanelToggled += ToggleOutlinePanel;
                _ribbonController.OnConsolePanelToggled += ToggleConsolePanel;
                _ribbonController.OnPropertiesPanelToggled += TogglePropertiesPanel;
            }
            HandleWorkflowTemplateSelected(_currentTemplateId);

            // Attach Camera Controller dynamically
            EnsureCameraController();

            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyWindowPanelVisibility();
            ApplyHudVisibility();
        }

        private void Update()
        {
            if (!UIFocusUtility.IsTextInputFocused())
            {
                if (Input.GetKeyDown(KeyCode.Z)) ToggleHudVisibility();
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) // Handled by TextField usually, but if we want a global key to focus:
                if (Input.GetKeyDown(KeyCode.BackQuote)) FocusConsole();
                
                // Ctrl+S to Save Project
                if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S))
                {
                    SaveSystemProject();
                }
            }
            else
            {
                if (Input.GetKeyDown(KeyCode.Escape)) _consoleInput?.Blur();
            }
        }

        private void HandleViewChanged(string viewName)
        {
            EnsureCameraController(forceRebind: true);
            ApplyViewToAllEnabledGameCameras(viewName);
        }

        private void HandleRenderModeChanged(string renderMode)
        {
            string actualMode = renderMode;
            if (string.Equals(renderMode, "Wireframe", System.StringComparison.OrdinalIgnoreCase) && 
                _currentTemplateId == WorkflowTemplateCatalog.InternalFlow)
            {
                actualMode = "Ghost";
            }

            EnsureCameraController(forceRebind: true);
            if (_activeCamera != null)
            {
                ApplyRenderMode(actualMode);
            }
        }

        private void HandleSimulationSpeedChanged(float speed)
        {
            var simMgr = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
            if (simMgr != null)
            {
                simMgr.SetSimulationTimeScale(speed);
            }
        }

        internal void ApplyVisualizationMode(string visualizationMode)
        {
            bool useSurfaceHeatmap =
                string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationPressure, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationSurfacePressure, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visualizationMode, WindTunnelSimulation3D.VisualizationSurfaceFriction, System.StringComparison.OrdinalIgnoreCase);

            ApplyRenderMode(useSurfaceHeatmap ? "Pressure" : "Standard");
        }

        public string GetCurrentTemplateId()
        {
            return _currentTemplateId;
        }

        public bool IsEnvironmentLoading()
        {
            return _isLoadingEnv;
        }

        public void EnsureActiveWorkflowSceneLoaded()
        {
            var def = WorkflowTemplateCatalog.GetOrDefault(_currentTemplateId);
            EnsureRecommendedSceneLoaded(def.recommendedScene);
        }

        public void OnRuntimeModelLoaded()
        {
            PauseSimulation();
            _ribbonController?.SetPlaybackState(true);

            // For pipe flow and machinery modes, bind their own controls
            if (_currentTemplateId == WorkflowTemplateCatalog.InternalFlow)
            {
                _propertiesPanelController?.SetActiveView("pipeflow");
                _propertiesPanelController?.BindPipeFlowControls(true);
                _activePanelKey = "pipeflow";
                return;
            }

            if (_currentTemplateId == WorkflowTemplateCatalog.RotatingMachinery)
            {
                _propertiesPanelController?.SetActiveView("machinery");
                _propertiesPanelController?.BindRotatingMachineryControls();
                _activePanelKey = "machinery";
                
                var loader = FindAnyObjectByType<RuntimeModelLoader>();
                if (loader != null && loader.CurrentPartRegistry != null)
                {
                    loader.CurrentPartRegistry.ApplySegmentationVisuals();
                }
                
                return;
            }

            var wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind == null)
            {
                return;
            }

            switch (_currentTemplateId)
            {
                case WorkflowTemplateCatalog.ExternalAero:
                    wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationStreamlines);
                    break;
                case WorkflowTemplateCatalog.FsiLite:
                case WorkflowTemplateCatalog.PlaybackValidation:
                    wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationPressure);
                    break;
                default:
                    break;
            }

            ApplyVisualizationMode(wind.settings.visualizationMode);
            _propertiesPanelController?.BindWindTunnelControls();
        }

        private void ApplyRenderMode(string renderMode)
        {
            var bootstrapper = FindAnyObjectByType<AeroFlow.Display.VisualsBootstrapper>();
            if (bootstrapper != null)
            {
                bootstrapper.SetRenderMode(renderMode);
            }
            else
            {
                Debug.Log($"[Mock] Switched Render Mode to: {renderMode}");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureCameraController(forceRebind: true, preferredScene: scene);
            
            // Rebind boundary UI to newly loaded simulation scenes.
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root != null)
            {
                BindBoundaryControls(root);
            }

            var loader = FindAnyObjectByType<RuntimeModelLoader>();
            if (loader != null && loader.HasLoadedModel())
            {
                loader.AlignToSimulationContext();
            }
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLogMessage;
            VerifyBuildIntegrity();
        }

        private void VerifyBuildIntegrity()
        {
            Debug.Log("<color=#6cf5ff>[Build Integrity] Starting startup verification...</color>");
            
            // Check Scenes
            string[] criticalScenes = { "WindTunnelSample", "Wind Tunnel (3D)", "Test C (3D)" };
            foreach (var scene in criticalScenes)
            {
                int index = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath(scene);
                if (index == -1) 
                    Debug.LogError($"<color=red>[Build Integrity] CRITICAL: Scene '{scene}' is missing from Build Settings!</color>");
                else 
                    Debug.Log($"[Build Integrity] Scene '{scene}' found at index {index}.");
            }

            // Check the active runtime shader paths instead of stale hardcoded names.
            VerifyResolvedShader("Lit", RuntimeShaderResolver.FindLitShader);
            VerifyResolvedShader("Pressure", RuntimeShaderResolver.FindPressureShader);
            VerifyResolvedShader("Sectionable", RuntimeShaderResolver.FindSectionableShader);
            VerifyResolvedShader("Slice", RuntimeShaderResolver.FindSliceShader);
            VerifyResolvedShader("Streamline", RuntimeShaderResolver.FindLineShader);
            VerifyResolvedShader("SimpleUnlit", RuntimeShaderResolver.FindSimpleUnlitShader);

            // Check Compute Shaders
            string[] criticalCompute = { "Compute/WindTunnel/NavierStokes3D", "Compute/DamBreak/FluidSim3D" };
            foreach (var cPath in criticalCompute)
            {
                var c = Resources.Load<ComputeShader>(cPath);
                if (c == null) 
                    Debug.LogError($"<color=red>[Build Integrity] CRITICAL: Compute Shader at '{cPath}' failed to load from Resources!</color>");
                else 
                    Debug.Log($"[Build Integrity] Compute Shader '{cPath}' loaded (Name: {c.name}).");
            }
        }

        private static void VerifyResolvedShader(string label, System.Func<Shader> resolver)
        {
            Shader shader = null;
            try
            {
                shader = resolver?.Invoke();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"<color=orange>[Build Integrity] WARNING: Shader resolver '{label}' threw an exception: {ex.Message}</color>");
                return;
            }

            if (shader == null)
            {
                Debug.LogWarning($"<color=orange>[Build Integrity] WARNING: No supported shader resolved for '{label}'. Visuals using this path may be broken.</color>");
                return;
            }

            Debug.Log($"[Build Integrity] {label} shader resolved successfully: '{shader.name}'.");
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLogMessage;
        }

        private void HandleLogMessage(string logString, string stackTrace, LogType type)
        {
            if (_consoleScrollArea == null) return;
            var label = new Label(logString);
            label.style.whiteSpace = WhiteSpace.Normal;
            if (type == LogType.Error || type == LogType.Exception) label.style.color = new Color(0.9f, 0.3f, 0.3f);
            else if (type == LogType.Warning) label.style.color = new Color(0.9f, 0.7f, 0.2f);
            else label.style.color = new Color(0.81f, 0.92f, 1f); // Soft cyan for normal logs
            label.style.fontSize = 11;
            label.style.paddingBottom = 2;
            _consoleScrollArea.Add(label);
            
            // Auto-scroll to bottom
            _consoleScrollArea.RegisterCallback<GeometryChangedEvent>(evt => {
                var scroller = _consoleScrollArea as ScrollView;
                if (scroller != null) scroller.scrollOffset = new Vector2(0, scroller.contentContainer.layout.height);
            });

            while (_consoleScrollArea.childCount > 100) _consoleScrollArea.RemoveAt(0);
        }

        private void FocusConsole()
        {
            if (_consoleInput == null) return;
            _consoleExpanded = true;
            if (_console != null) _console.style.height = ConsoleExpandedHeight;
            _consoleInput.Focus();
            _consoleInput.value = "";
        }

        private void ExecuteConsoleCommand()
        {
            if (_consoleInput == null) return;
            string raw = _consoleInput.value;
            _consoleInput.value = "";
            if (string.IsNullOrWhiteSpace(raw)) return;

            string cmd = raw.Trim().ToLower();
            Debug.Log($"> {raw}"); // Echo command

            if (cmd == "start sim" || cmd == "play" || cmd == "run")
            {
                PlaySimulation();
                Debug.Log("<color=#4bd28c>Simulation started.</color>");
            }
            else if (cmd == "stop sim" || cmd == "pause" || cmd == "stop")
            {
                PauseSimulation();
                Debug.Log("<color=#f0be46>Simulation paused.</color>");
            }
            else if (cmd == "load model" || cmd == "import" || cmd == "load")
            {
                var loader = FindAnyObjectByType<RuntimeModelLoader>();
                if (loader != null) loader.OpenFilePicker();
                else Debug.LogError("RuntimeModelLoader not found in scene.");
            }
            else if (cmd == "clear" || cmd == "cls")
            {
                _consoleScrollArea?.Clear();
                Debug.Log("> Console cleared.");
            }
            else if (cmd == "help" || cmd == "?")
            {
                Debug.Log("<b>Available Commands:</b>");
                Debug.Log("- <b>start sim / play</b>: Begin simulation");
                Debug.Log("- <b>stop sim / pause</b>: Pause simulation");
                Debug.Log("- <b>load model / import</b>: Open model file picker");
                Debug.Log("- <b>clear / cls</b>: Clear console output");
                Debug.Log("- <b>help</b>: Show this list");
            }
            else
            {
                Debug.LogWarning($"Unknown command: '{cmd}'. Type 'help' for available commands.");
            }
            
            _consoleInput.Focus(); // Keep focus for next command
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= HandleLogMessage;
            CleanupHomeTrailer();
        }

        private void SetupHomeTrailer(VisualElement root)
        {
            var showcaseImage = root.Q<VisualElement>(className: "home-showcase-image");
            if (showcaseImage == null) return;

            // Check both root StreamingAssets and Videos/ subfolder for robustness
            string videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, "home-page-trailer.mp4");
            if (!System.IO.File.Exists(videoPath)) videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Videos", "home-page-trailer.mp4");
            if (!System.IO.File.Exists(videoPath)) return;

            _homeVideoRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            _homeVideoRT.Create();

            var vpGo = new GameObject("HomeTrailerVideoPlayer");
            vpGo.transform.SetParent(this.transform);
            _homeVideoPlayer = vpGo.AddComponent<VideoPlayer>();
            _homeVideoPlayer.url = videoPath;
            _homeVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _homeVideoPlayer.targetTexture = _homeVideoRT;
            _homeVideoPlayer.isLooping = true;
            _homeVideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
            _homeVideoPlayer.Play();

            showcaseImage.style.backgroundImage = Background.FromRenderTexture(_homeVideoRT);

            _homeScreen?.RegisterCallback<GeometryChangedEvent>(evt => {
                if (_homeVideoPlayer != null) {
                    if (_homeScreen.style.display == DisplayStyle.None) _homeVideoPlayer.Pause();
                    else if (!_homeVideoPlayer.isPlaying) _homeVideoPlayer.Play();
                }
            });
        }

        private void CleanupHomeTrailer()
        {
            if (_homeVideoPlayer != null)
            {
                _homeVideoPlayer.Stop();
                Destroy(_homeVideoPlayer.gameObject);
                _homeVideoPlayer = null;
            }
            if (_homeVideoRT != null)
            {
                _homeVideoRT.Release();
                Destroy(_homeVideoRT);
                _homeVideoRT = null;
            }
        }

        private void ToggleHudVisibility()
        {
            _hudHidden = !_hudHidden;
            ApplyHudVisibility();
        }

        public bool IsHudHidden()
        {
            return _hudHidden;
        }

        public void SetHudHidden(bool hidden)
        {
            if (_hudHidden == hidden)
            {
                return;
            }

            _hudHidden = hidden;
            ApplyHudVisibility();
        }

        private void ApplyHudVisibility()
        {
            bool showHud = !_hudHidden;
            if (_ribbonContainer != null) _ribbonContainer.style.display = showHud ? DisplayStyle.Flex : DisplayStyle.None;
            if (_statusBar != null) _statusBar.style.display = showHud ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyWindowPanelVisibility();
            bool isHomeVisible = _homeScreen != null && _homeScreen.style.display == DisplayStyle.Flex;
            if (_viewportOverlayPrompt != null) 
            {
                _viewportOverlayPrompt.style.display = showHud && _loadPromptVisible && !isHomeVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_recordingIndicator != null) _recordingIndicator.style.display = showHud && _recordingIndicatorVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (_hudButton is Button hudButton)
            {
                hudButton.text = showHud ? "Hide HUD [Z]" : "Show HUD [Z]";
                hudButton.tooltip = showHud ? "Hide interface and keep only the simulation view" : "Restore the interface";
            }
        }

        private void ApplyWindowPanelVisibility()
        {
            bool showHud = !_hudHidden;
            bool showOutline = showHud && _outlinePanelVisible;
            bool showConsole = showHud && _consolePanelVisible;
            bool showProperties = showHud && _propertiesPanelVisible;

            if (_leftSidebar != null) _leftSidebar.style.display = showOutline ? DisplayStyle.Flex : DisplayStyle.None;
            if (_leftResizer != null) _leftResizer.style.display = showOutline ? DisplayStyle.Flex : DisplayStyle.None;

            if (_console != null)
            {
                _console.style.display = showConsole ? DisplayStyle.Flex : DisplayStyle.None;
                if (showConsole)
                {
                    _console.style.height = _consoleExpanded ? ConsoleExpandedHeight : ConsoleCollapsedHeight;
                    var toggleBtn = _console.Q<Button>("console-toggle-btn");
                    if (toggleBtn != null) toggleBtn.text = _consoleExpanded ? "-" : "+";
                }
            }
            if (_consoleResizer != null) _consoleResizer.style.display = showConsole ? DisplayStyle.Flex : DisplayStyle.None;
            if (_consoleScrollArea != null) _consoleScrollArea.style.display = showConsole && _consoleExpanded ? DisplayStyle.Flex : DisplayStyle.None;

            if (_rightSidebar != null) _rightSidebar.style.display = showProperties ? DisplayStyle.Flex : DisplayStyle.None;
            if (_rightResizer != null) _rightResizer.style.display = showProperties ? DisplayStyle.Flex : DisplayStyle.None;

            _ribbonController?.SetWindowPanelStates(_outlinePanelVisible, _consolePanelVisible, _propertiesPanelVisible);
        }

        private void CreateNewProject()
        {
            if (_homeGroupProjectActions != null) _homeGroupProjectActions.style.display = DisplayStyle.None;
            if (_homeGroupSimulationTemplates != null) _homeGroupSimulationTemplates.style.display = DisplayStyle.Flex;
        }

        private void PlayHomeVideo(string filename)
        {
            if (_homeVideoPlayer == null) return;
            string videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, filename);
            if (!System.IO.File.Exists(videoPath)) videoPath = System.IO.Path.Combine(Application.streamingAssetsPath, "Videos", filename);
            
            string fileUri = "file://" + videoPath; // VideoPlayer URLs generally use URIs or absolute paths. Unity accepts raw paths as well, but we should make sure the equality check against .url matches. Usually .url returns the exact assigned string.
            // But let's just assign the raw path for robustness since it was done in SetupHomeTrailer using exactly videoPath.
            if (System.IO.File.Exists(videoPath) && _homeVideoPlayer.url != videoPath)
            {
                _homeVideoPlayer.url = videoPath;
                _homeVideoPlayer.Play();
            }
            else if (!_homeVideoPlayer.isPlaying)
            {
                _homeVideoPlayer.Play();
            }
        }

        private void ShowProjectActions()
        {
            if (_homeGroupProjectActions != null) _homeGroupProjectActions.style.display = DisplayStyle.Flex;
            if (_homeGroupSimulationTemplates != null) _homeGroupSimulationTemplates.style.display = DisplayStyle.None;
            
            _pendingTemplateSelection = "";
            _btnHomeWT?.RemoveFromClassList("home-action-btn--selected");
            _btnHomePF?.RemoveFromClassList("home-action-btn--selected");
            _btnHomeRM?.RemoveFromClassList("home-action-btn--selected");
            _btnHomeDB?.RemoveFromClassList("home-action-btn--selected");
            if (_btnConfirmTemplate != null) _btnConfirmTemplate.style.display = DisplayStyle.None;

            PlayHomeVideo("home-page-trailer.mp4");
        }

        private void SelectTemplate(string templateKey)
        {
            _pendingTemplateSelection = templateKey;
            
            _btnHomeWT?.RemoveFromClassList("home-action-btn--selected");
            _btnHomePF?.RemoveFromClassList("home-action-btn--selected");
            _btnHomeRM?.RemoveFromClassList("home-action-btn--selected");
            _btnHomeDB?.RemoveFromClassList("home-action-btn--selected");

            string videoFile = "home-page-trailer.mp4";

            if (templateKey == "windtunnel") { _btnHomeWT?.AddToClassList("home-action-btn--selected"); videoFile = "windtunnel.mp4"; }
            else if (templateKey == "pipeflow") { _btnHomePF?.AddToClassList("home-action-btn--selected"); videoFile = "pipe.mp4"; }
            else if (templateKey == "machinery") { _btnHomeRM?.AddToClassList("home-action-btn--selected"); videoFile = "rotatory.mp4"; }
            else if (templateKey == "dambreak") { _btnHomeDB?.AddToClassList("home-action-btn--selected"); videoFile = "dambreak.mp4"; }

            if (_btnConfirmTemplate != null) _btnConfirmTemplate.style.display = DisplayStyle.Flex;
            PlayHomeVideo(videoFile);
        }

        private void ConfirmTemplateSelection()
        {
            if (string.IsNullOrEmpty(_pendingTemplateSelection)) return;
            
            if (_pendingTemplateSelection == "windtunnel") { DismissHomePage(); LoadWindTunnel(); }
            else if (_pendingTemplateSelection == "pipeflow") { DismissHomePage(); LoadPipeFlow(); }
            else if (_pendingTemplateSelection == "machinery") { DismissHomePage(); LoadRotatingMachinery(); }
            else if (_pendingTemplateSelection == "dambreak") { DismissHomePage(); LoadDamBreak(); }
        }

        private void OpenSystemProject()
        {
#if UNITY_EDITOR
            string selectedPath = UnityEditor.EditorUtility.OpenFilePanel("Open FluidWorks Project", Application.persistentDataPath, "fluidworks");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                DismissHomePage();
                _loadManager.LoadProject(selectedPath);
                _activeProjectName = System.IO.Path.GetFileNameWithoutExtension(selectedPath);
            }
#elif UNITY_STANDALONE_WIN
            var extensions = new[] { new SFB.ExtensionFilter("FluidWorks Project", "fluidworks") };
            var paths = SFB.StandaloneFileBrowser.OpenFilePanel("Open Project", "", extensions, false);
            if (paths.Length > 0 && !string.IsNullOrEmpty(paths[0]))
            {
                DismissHomePage();
                _loadManager.LoadProject(paths[0]);
                _activeProjectName = System.IO.Path.GetFileNameWithoutExtension(paths[0]);
            }
#else
            Debug.LogWarning("File dialog not supported securely on this platform.");
#endif
        }

        private void SaveSystemProject()
        {
#if UNITY_EDITOR
            string selectedPath = UnityEditor.EditorUtility.SaveFilePanel("Save FluidWorks Project", Application.persistentDataPath, _activeProjectName, "fluidworks");
            if (!string.IsNullOrEmpty(selectedPath)) ExecuteSave(selectedPath);
#elif UNITY_STANDALONE_WIN
            var extensionList = new [] { new SFB.ExtensionFilter("FluidWorks Project", "fluidworks") };
            string selectedPath = SFB.StandaloneFileBrowser.SaveFilePanel("Save Project", "", _activeProjectName, extensionList);
            if (!string.IsNullOrEmpty(selectedPath)) ExecuteSave(selectedPath);
#else
            Debug.LogWarning("File dialog not supported securely on this platform.");
#endif
        }

        private void ExecuteSave(string path)
        {
            _activeProjectName = System.IO.Path.GetFileNameWithoutExtension(path);
            var data = new FluidWorks.ProjectSystem.ProjectData();
            data.metadata.projectName = _activeProjectName;
            data.metadata.creationDate = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            data.metadata.softwareVersion = Application.version;
            
            if (Camera.main != null)
            {
                data.visualization.cameraPosition = Camera.main.transform.position;
                data.visualization.cameraRotation = Camera.main.transform.rotation;
            }
            
            string activeModelPath = "";
            var loader = FindAnyObjectByType<RuntimeModelLoader>();
            if (loader != null && loader.CurrentDescriptor != null)
            {
                activeModelPath = loader.CurrentDescriptor.visualModelPath;
            }
            _saveManager.SaveProject(path, data, activeModelPath);
        }

        private void PlaySimulation()
        {
            var sim3D = FindAnyObjectByType<Simulation3D>();
            if (sim3D != null) sim3D.Play();

            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windSim != null) windSim.Play();

            var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (pipeSim != null) pipeSim.Play();

            var machinerySim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            if (machinerySim != null) machinerySim.Play();

            _ribbonController?.SetPlaybackState(false);
        }

        private void PauseSimulation()
        {
            var sim3D = FindAnyObjectByType<Simulation3D>();
            if (sim3D != null) sim3D.Pause();

            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windSim != null) windSim.Pause();

            var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (pipeSim != null) pipeSim.Pause();

            var machinerySim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            if (machinerySim != null) machinerySim.Pause();

            _ribbonController?.SetPlaybackState(true);
        }

        private string _currentEnvScene = "";
        private bool _isLoadingEnv = false;

        public void LoadDamBreak()
        {
            if (_isLoadingEnv) return;
            _currentTemplateId = WorkflowTemplateCatalog.FreeSurface;
            Debug.Log("Loading Dam Break Simulation Environment...");
            _ribbonController?.SetPlaybackState(true);
            StartCoroutine(LoadEnvironmentAsync("Test C (3D)"));
        }

        public void LoadWindTunnel()
        {
            if (_isLoadingEnv) return;
            if (_currentTemplateId == WorkflowTemplateCatalog.FreeSurface)
            {
                _currentTemplateId = WorkflowTemplateCatalog.ExternalAero;
            }
            Debug.Log("Loading Wind Tunnel Simulation Environment...");
            _ribbonController?.SetPlaybackState(true);
            StartCoroutine(LoadEnvironmentAsync("WindTunnelSample"));
        }

        public void LoadPipeFlow()
        {
            if (_isLoadingEnv) return;
            _currentTemplateId = WorkflowTemplateCatalog.InternalFlow;
            Debug.Log("Loading Pipe Flow Simulation Environment...");
            _ribbonController?.SetPlaybackState(true);
            StartCoroutine(LoadEnvironmentAsync("WindTunnelSample"));
        }

        public void LoadRotatingMachinery()
        {
            if (_isLoadingEnv) return;
            _currentTemplateId = WorkflowTemplateCatalog.RotatoryMode;
            Debug.Log("Loading Rotatory Mode Simulation Environment...");
            _ribbonController?.SetPlaybackState(true);
            StartCoroutine(LoadEnvironmentAsync("WindTunnelSample"));
        }

        private System.Collections.IEnumerator LoadEnvironmentAsync(string targetSceneName)
        {
            _isLoadingEnv = true;

            // Unload the old scene if one exists
            if (!string.IsNullOrEmpty(_currentEnvScene))
            {
                var asyncUnload = SceneManager.UnloadSceneAsync(_currentEnvScene);
                if (asyncUnload != null)
                {
                    yield return asyncUnload;
                }
            }

            // Load the new simulation scene (with fallback)
            _currentEnvScene = targetSceneName;
            Debug.Log($"[SceneLoad] Attempting to load environment scene: {targetSceneName}");
            var loadOp = SceneManager.LoadSceneAsync(targetSceneName, LoadSceneMode.Additive);
            
            if (loadOp == null)
            {
                Debug.LogError($"[SceneLoad] FAILED to initiate load for scene '{targetSceneName}'. It might be missing from Build Settings.");
                
                // Fallback for Wind Tunnel if WindTunnelSample is missing
                if (targetSceneName == "WindTunnelSample")
                {
                    Debug.LogWarning("[SceneLoad] Attempting fallback to 'Wind Tunnel (3D)'...");
                    loadOp = SceneManager.LoadSceneAsync("Wind Tunnel (3D)", LoadSceneMode.Additive);
                    if (loadOp != null) _currentEnvScene = "Wind Tunnel (3D)";
                }
            }

            if (loadOp != null)
            {
                while (!loadOp.isDone)
                {
                    Debug.Log($"[SceneLoad] Progress: {loadOp.progress:P0}");
                    yield return null;
                }
                Debug.Log($"[SceneLoad] Scene '{_currentEnvScene}' loaded successfully.");
            }
            else
            {
                Debug.LogError($"[SceneLoad] CRITICAL: Both '{targetSceneName}' and fallback failed. Scene is likely missing from build.");
                _isLoadingEnv = false;
                yield break;
            }

            // Post-load UI adjustments based on environment
            if (targetSceneName == "Test C (3D)")
            {
                _propertiesPanelController?.SetActiveView("dambreak");
                _propertiesPanelController?.BindDamBreakControls();
                yield return null;
                var loader = FindAnyObjectByType<RuntimeModelLoader>();
                if (loader != null && loader.HasLoadedModel())
                {
                    loader.AlignToSimulationContext();
                }
            }
            else if (targetSceneName == "Wind Tunnel (3D)" || targetSceneName == "WindTunnelSample")
            {
                yield return null;

                // Clean up simulation components from other modes to avoid priority conflicts
                CleanupUnusedSimulationComponents();

                // Determine which simulation mode to set up based on template
                if (_currentTemplateId == WorkflowTemplateCatalog.InternalFlow)
                {
                    // Pipe flow mode: add PipeFlowSimulation3D if needed
                    var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
                    if (pipeSim == null)
                    {
                        var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
                        GameObject host = windSim != null ? windSim.gameObject : new GameObject("PipeFlowSimulation");
                        pipeSim = host.AddComponent<PipeFlowSimulation3D>();
                    }
                    EnsureInternalFlowRenderer(pipeSim.gameObject);
                    pipeSim.InitializeIfNeeded();
                    pipeSim.Pause();

                    _propertiesPanelController?.SetActiveView("pipeflow");
                    _propertiesPanelController?.BindPipeFlowControls(true);
                    _activePanelKey = "pipeflow";
                }
                else if (_currentTemplateId == WorkflowTemplateCatalog.RotatingMachinery)
                {
                    // Rotating machinery mode: add RotatingMachinerySimulation3D if needed
                    var machSim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
                    if (machSim == null)
                    {
                        var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
                        GameObject host = windSim != null ? windSim.gameObject : new GameObject("RotatingMachinerySimulation");
                        machSim = host.AddComponent<RotatingMachinerySimulation3D>();
                    }
                    EnsureInternalFlowRenderer(machSim.gameObject);
                    machSim.InitializeIfNeeded();
                    machSim.Pause();

                    _propertiesPanelController?.SetActiveView("machinery");
                    _propertiesPanelController?.BindRotatingMachineryControls();
                    _activePanelKey = "machinery";
                }
                else
                {
                    // Default wind tunnel mode
                    _propertiesPanelController?.SetActiveView("windtunnel");
                    _propertiesPanelController?.BindWindTunnelControls();
                    _activePanelKey = "windtunnel";

                    var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
                    if (windSim != null)
                    {
                        windSim.InitializeIfNeeded();
                        windSim.Pause();
                    }
                }

                ConfigureSceneSimulationComponentsForTemplate();

                _ribbonController?.SetPlaybackState(true);
                _ribbonController?.SetPlaybackSpeed(1f);

                var loader = FindAnyObjectByType<RuntimeModelLoader>();
                if (loader != null && loader.HasLoadedModel())
                {
                    loader.AlignToSimulationContext();
                    OnRuntimeModelLoaded();
                }

                // Frame camera closer to model or tunnel volume.
                var camController = FindAnyObjectByType<CameraController>();
                if (camController != null)
                {
                    Bounds b = new Bounds(Vector3.zero, Vector3.one);
                    if (RuntimeModelLookup.TryGetRenderableBounds(out var loadedBounds))
                    {
                        b = loadedBounds;
                        camController.FrameBounds(b, 1.15f);
                    }
                    else
                    {
                        var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
                        if (windSim != null)
                        {
                            b = new Bounds(windSim.transform.position, windSim.transform.localScale);
                            camController.FrameBounds(b, 1.15f);
                        }
                    }
                }
            }
            else
            {
                _ribbonController?.SetPlaybackState(true);
                _ribbonController?.SetPlaybackSpeed(1f);
            }
            ApplyTemplatePostLoad(targetSceneName);

            _isLoadingEnv = false;
        }

        private void CleanupUnusedSimulationComponents()
        {
            // Destroy dynamically added simulation components that don't match current template
            if (_currentTemplateId != WorkflowTemplateCatalog.InternalFlow)
            {
                var pipe = FindAnyObjectByType<PipeFlowSimulation3D>();
                if (pipe != null) Destroy(pipe);
            }

            if (_currentTemplateId != WorkflowTemplateCatalog.RotatingMachinery)
            {
                var mach = FindAnyObjectByType<RotatingMachinerySimulation3D>();
                if (mach != null) Destroy(mach);
            }
        }

        private void ConfigureSceneSimulationComponentsForTemplate()
        {
            bool windMode = _currentTemplateId != WorkflowTemplateCatalog.InternalFlow
                         && _currentTemplateId != WorkflowTemplateCatalog.RotatingMachinery
                         && _currentTemplateId != WorkflowTemplateCatalog.FreeSurface;

            var windSim = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (windSim != null)
            {
                if (!windMode)
                {
                    windSim.Pause();
                }

                windSim.enabled = windMode;

                var streamlineField = windSim.GetComponent<StreamlineFieldRenderer>();
                if (streamlineField != null) streamlineField.enabled = windMode;

                var sliceRenderer = windSim.GetComponent<FlowFieldSliceRenderer>();
                if (sliceRenderer != null) sliceRenderer.enabled = windMode;

                var legacyStreamlines = windSim.GetComponent<WindTunnelStreamlineRenderer>();
                if (legacyStreamlines != null) legacyStreamlines.enabled = false;

                var particles = windSim.GetComponentsInChildren<FlowParticleSystem>(true);
                for (int i = 0; i < particles.Length; i++)
                {
                    if (particles[i] == null) continue;
                    particles[i].enabled = windMode;
                    if (!windMode)
                    {
                        particles[i].gameObject.SetActive(false);
                    }
                }

                // Hide/show wind tunnel enclosure (glass box, floor, guides)
                var enclosure = windSim.GetComponent<WindTunnelEnclosure>();
                if (enclosure != null)
                {
                    enclosure.enabled = windMode;
                    // Force the enclosure's child objects to hide
                    var enclosurePresentation = enclosure.transform.Find("WindTunnelPresentation");
                    if (enclosurePresentation != null)
                    {
                        enclosurePresentation.gameObject.SetActive(windMode);
                    }
                }
            }

            bool showSceneFloor = _currentTemplateId != WorkflowTemplateCatalog.RotatingMachinery;
            SetSharedSceneFloorVisible(showSceneFloor);

            var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
            if (pipeSim != null)
            {
                pipeSim.enabled = _currentTemplateId == WorkflowTemplateCatalog.InternalFlow;
            }

            var machSim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
            if (machSim != null)
            {
                machSim.enabled = _currentTemplateId == WorkflowTemplateCatalog.RotatingMachinery;
            }

            var internalFlowRenderer = FindAnyObjectByType<InternalAeroRevamp>();
            if (internalFlowRenderer != null)
            {
                internalFlowRenderer.enabled = !windMode;
            }

            var legacyInternalFlowRenderer = FindAnyObjectByType<InternalFlowFieldRenderer>();
            if (legacyInternalFlowRenderer != null)
            {
                legacyInternalFlowRenderer.enabled = !windMode;
            }
        }

        private static void SetSharedSceneFloorVisible(bool visible)
        {
            string[] floorNames = { "Floor Display", "Floor Display " };
            for (int i = 0; i < floorNames.Length; i++)
            {
                GameObject floor = GameObject.Find(floorNames[i]);
                if (floor != null)
                {
                    floor.SetActive(visible);
                }
            }
        }

        private static void EnsureInternalFlowRenderer(GameObject host)
        {
            if (host == null)
                return;

            if (host.GetComponent<InternalAeroRevamp>() == null)
            {
                host.AddComponent<InternalAeroRevamp>();
            }

            if (host.GetComponent<InternalFlowFieldRenderer>() == null)
            {
                host.AddComponent<InternalFlowFieldRenderer>();
            }
        }

        private void HandleWorkflowTemplateSelected(string templateId)
        {
            _currentTemplateId = string.IsNullOrEmpty(templateId) ? WorkflowTemplateCatalog.ExternalAero : templateId;
            var def = WorkflowTemplateCatalog.GetOrDefault(_currentTemplateId);
            var loader = FindAnyObjectByType<RuntimeModelLoader>();

            _propertiesPanelController?.SetActiveView("workflow");
            _propertiesPanelController?.SetWorkflowTemplateInfo(def.title, def.summary, def.outputs);
            UpdateLoadPromptForTemplate(_currentTemplateId, def.title);
            ConfigureLoaderForTemplate(_currentTemplateId);
            EnsureRecommendedSceneLoaded(def.recommendedScene);
            ConfigureSceneSimulationComponentsForTemplate();

            var wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind != null && _currentTemplateId == WorkflowTemplateCatalog.ExternalAero)
            {
                bool hasLoadedModel = loader != null && loader.HasLoadedModel();
                wind.settings.inletSource = "Auto";
                wind.settings.useCustomInletDirection = false;
                wind.settings.inletDirection = Vector3.right;
                wind.settings.angleOfAttack = Mathf.Clamp(wind.settings.angleOfAttack, -15f, 15f);
                wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationStreamlines);
                ApplyVisualizationMode(wind.settings.visualizationMode);
            }
        }

        private void UpdateLoadPromptForTemplate(string templateId, string templateTitle)
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) return;

            var title = root.Q<Label>("load-prompt-title-label");
            if (title != null) title.text = $"Load Geometry for {templateTitle}";

            var subtitle = root.Q<Label>("load-prompt-subtitle-label");
            if (subtitle != null)
            {
                subtitle.text = "Load a visual model (GLB/GLTF/OBJ/STL).";
            }
        }

        private void ConfigureLoaderForTemplate(string templateId)
        {
            var loader = FindAnyObjectByType<RuntimeModelLoader>();
            if (loader == null) return;

            loader.autoRegisterParts = templateId != WorkflowTemplateCatalog.FreeSurface;
            loader.autoAddMovablePartComponents = templateId == WorkflowTemplateCatalog.FsiLite || templateId == WorkflowTemplateCatalog.RotatingMachinery;
            loader.autoConfigurePartMotion = templateId == WorkflowTemplateCatalog.RotatingMachinery;
            loader.autoSegmentSingleMeshModels = templateId == WorkflowTemplateCatalog.RotatoryMode;
            loader.promptForSimulationModel = false;
        }

        private void EnsureRecommendedSceneLoaded(string recommendedScene)
        {
            if (_isLoadingEnv || string.IsNullOrWhiteSpace(recommendedScene) || _currentEnvScene == recommendedScene)
            {
                return;
            }

            _ribbonController?.SetPlaybackState(true);
            StartCoroutine(LoadEnvironmentAsync(recommendedScene));
        }

        private void ApplyTemplatePostLoad(string sceneName)
        {
            if (sceneName == "Test C (3D)")
            {
                return;
            }

            // For pipe flow and rotating machinery, configure their own solvers
            if (_currentTemplateId == WorkflowTemplateCatalog.InternalFlow)
            {
                var pipeSim = FindAnyObjectByType<PipeFlowSimulation3D>();
                if (pipeSim != null)
                {
                    pipeSim.settings.inletVelocity = Mathf.Clamp(pipeSim.settings.inletVelocity, 0.1f, 50f);
                }
                return;
            }

            if (_currentTemplateId == WorkflowTemplateCatalog.RotatingMachinery)
            {
                var machSim = FindAnyObjectByType<RotatingMachinerySimulation3D>();
                if (machSim != null)
                {
                    machSim.settings.inletVelocity = Mathf.Clamp(machSim.settings.inletVelocity, 0.1f, 80f);
                    machSim.settings.angularVelocityRPM = Mathf.Clamp(machSim.settings.angularVelocityRPM, 10f, 10000f);
                }
                return;
            }

            var wind = FindAnyObjectByType<WindTunnelSimulation3D>();
            if (wind == null) return;

            switch (_currentTemplateId)
            {
                case WorkflowTemplateCatalog.FsiLite:
                    wind.settings.inletVelocity = Mathf.Clamp(wind.settings.inletVelocity, 10f, 45f);
                    wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationPressure);
                    ApplyVisualizationMode(wind.settings.visualizationMode);
                    break;
                case WorkflowTemplateCatalog.PlaybackValidation:
                    wind.settings.iterationsPerFrame = Mathf.Max(wind.settings.iterationsPerFrame, 6);
                    wind.settings.turbulenceIntensity = Mathf.Min(wind.settings.turbulenceIntensity, 3f);
                    wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationPressure);
                    ApplyVisualizationMode(wind.settings.visualizationMode);
                    break;
                default:
                    var loader = FindAnyObjectByType<RuntimeModelLoader>();
                    wind.settings.inletSource = "Auto";
                    wind.settings.useCustomInletDirection = false;
                    wind.settings.inletDirection = Vector3.right;
                    wind.settings.angleOfAttack = Mathf.Clamp(wind.settings.angleOfAttack, -15f, 15f);
                    wind.settings.inletVelocity = Mathf.Clamp(wind.settings.inletVelocity, 10f, 55f);
                    wind.settings.turbulenceIntensity = Mathf.Clamp(wind.settings.turbulenceIntensity, 0f, 8f);
                    wind.SetVisualizationMode(WindTunnelSimulation3D.VisualizationStreamlines);
                    ApplyVisualizationMode(wind.settings.visualizationMode);
                    break;
            }
        }

        private void HandleCameraSpeedChanged(float speed)
        {
            EnsureCameraController(forceRebind: true);
            if (_cameraController != null)
            {
                _cameraController.SetNavigationSpeed(speed);
            }
        }

        private void BindDynamicControls(VisualElement root)
        {
            if (SimulationManager.Instance == null) return;

            var speedSlider = root.Q<Slider>("speed-slider");
            if (speedSlider != null)
            {
                speedSlider.SetValueWithoutNotify(SimulationManager.Instance.airSpeed);
                speedSlider.RegisterValueChangedCallback(evt => SimulationManager.Instance.SetAirSpeed(evt.newValue));
            }

            var densitySlider = root.Q<Slider>("density-slider");
            if (densitySlider != null)
            {
                densitySlider.SetValueWithoutNotify(SimulationManager.Instance.fluidDensity);
                densitySlider.RegisterValueChangedCallback(evt => SimulationManager.Instance.SetDensity(evt.newValue));
            }
            
            var viscositySlider = root.Q<Slider>("viscosity-slider");
            if (viscositySlider != null)
            {
                viscositySlider.SetValueWithoutNotify(SimulationManager.Instance.viscosity);
                viscositySlider.RegisterValueChangedCallback(evt => SimulationManager.Instance.SetViscosity(evt.newValue));
            }

            var turbulenceSlider = root.Q<Slider>("turbulence-slider");
            if (turbulenceSlider != null)
            {
                turbulenceSlider.SetValueWithoutNotify(SimulationManager.Instance.turbulence);
                turbulenceSlider.RegisterValueChangedCallback(evt => SimulationManager.Instance.SetTurbulence(evt.newValue));
            }

            var angleSlider = root.Q<Slider>("angle-slider");
            if (angleSlider != null)
            {
                angleSlider.SetValueWithoutNotify(SimulationManager.Instance.angleOfAttack);
                angleSlider.RegisterValueChangedCallback(evt => SimulationManager.Instance.SetAngle(evt.newValue));
            }
        }

        private void BindBoundaryControls(VisualElement root)
        {
            // Detect if we are in pipe flow mode
            var pipeSim = FindAnyObjectByType<AeroFlow.Sim3D.PipeFlow.PipeFlowSimulation3D>();
            if (pipeSim != null && pipeSim.isActiveAndEnabled)
            {
                // Show pipe-specific boundary conditions panel
                _propertiesPanelController.SetActiveView("pipeboundary");
                _propertiesPanelController.BindPipeBoundaryConditionsControls();
                return;
            }

            BindDynamicControls(root);
            new BoundaryConditionsController(root);
        }

        private void BindResultsControls(VisualElement root)
        {
            // Invalidate cached labels so they get re-queried from the newly
            // cloned AnalysisProperties.uxml on the next UpdateMetrics call.
            _metricLabelsCached = false;

            var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
            var computeToggleBtn = root.Q<Button>("results-compute-toggle-button");
            var computeStatusLabel = root.Q<Label>("results-compute-status-label");
            if (computeToggleBtn != null && computeToggleBtn.userData == null)
            {
                computeToggleBtn.userData = true;
                computeToggleBtn.clicked += () =>
                {
                    var manager = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
                    if (manager == null)
                    {
                        return;
                    }

                    manager.SetLiveResultsComputationEnabled(!manager.IsLiveResultsComputationEnabled());
                    RefreshResultsComputationUi(root, manager);
                };
            }

            RefreshResultsComputationUi(root, sim);

            var exportCsvBtn = root.Q<Button>("export-results-button");
            if (exportCsvBtn != null && exportCsvBtn.userData == null)
            {
                exportCsvBtn.userData = true;
                exportCsvBtn.clicked += HandleExportResultsCsv;
            }

            var exportJsonBtn = root.Q<Button>("export-snapshot-button");
            if (exportJsonBtn != null && exportJsonBtn.userData == null)
            {
                exportJsonBtn.userData = true;
                exportJsonBtn.clicked += HandleExportSnapshotJson;
            }

            var exportReportPanelBtn = root.Q<Button>("export-report-button-panel");
            if (exportReportPanelBtn != null && exportReportPanelBtn.userData == null)
            {
                exportReportPanelBtn.userData = true;
                exportReportPanelBtn.clicked += HandleExportHtmlReport;
            }

            var exportPdfReportPanelBtn = root.Q<Button>("export-pdf-report-button-panel");
            if (exportPdfReportPanelBtn != null && exportPdfReportPanelBtn.userData == null)
            {
                exportPdfReportPanelBtn.userData = true;
                exportPdfReportPanelBtn.clicked += HandleExportPdfReport;
            }
        }

        private void RefreshResultsComputationUi(VisualElement root, SimulationManager sim)
        {
            var computeToggleBtn = root.Q<Button>("results-compute-toggle-button");
            var computeStatusLabel = root.Q<Label>("results-compute-status-label");
            bool enabled = sim != null && sim.IsLiveResultsComputationEnabled();

            if (computeToggleBtn != null)
            {
                computeToggleBtn.text = enabled ? "Stop Results Computation" : "Start Results Computation";
            }

            if (computeStatusLabel != null)
            {
                computeStatusLabel.text = enabled
                    ? "Results computation is active while this tab stays open."
                    : "Results computation is paused. Visualization playback continues without live metrics.";
            }
        }

        private void HandleExportPdfReport()
        {
            var generator = FindAnyObjectByType<FluidWorks.Reporting.ReportGenerator>();
            if (generator == null)
            {
                Debug.Log("[UI] ReportGenerator not found in scene, creating one...");
                var go = new GameObject("ReportSystem");
                go.AddComponent<FluidWorks.Reporting.ScreenshotCapture>();
                generator = go.AddComponent<FluidWorks.Reporting.ReportGenerator>();
            }

            generator?.PromptAndGenerateReport();
        }

        private void EnsureCameraController(bool forceRebind = false, Scene preferredScene = default)
        {
            if (!forceRebind && _cameraController != null && _activeCamera != null && _activeCamera.enabled)
            {
                return;
            }

            var cam = FindPrimaryCamera(preferredScene);
            if (cam == null) return;

            SetExclusiveGameCamera(cam);

            if (_activeCamera != cam || forceRebind)
            {
                _activeCamera = cam;
                if (_activeCamera.tag != "MainCamera")
                {
                    _activeCamera.tag = "MainCamera";
                }

                _cameraController = _activeCamera.GetComponent<CameraController>();
                if (_cameraController == null)
                {
                    _cameraController = _activeCamera.gameObject.AddComponent<CameraController>();
                }
            }
        }

        private Camera FindPrimaryCamera(Scene preferredScene = default)
        {
            Camera preferred = FindBestSceneCamera(preferredScene);
            if (preferred != null)
            {
                return preferred;
            }

            if (!string.IsNullOrEmpty(_currentEnvScene))
            {
                Scene envScene = SceneManager.GetSceneByName(_currentEnvScene);
                preferred = FindBestSceneCamera(envScene);
                if (preferred != null)
                {
                    return preferred;
                }
            }

            Camera best = null;
            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var c = cameras[i];
                if (!c.enabled || c.cameraType != CameraType.Game) continue;
                if (best == null || c.depth > best.depth)
                {
                    best = c;
                }
            }
            return best;
        }

        private static Camera FindBestSceneCamera(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            Camera best = null;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var cameras = roots[i].GetComponentsInChildren<Camera>(true);
                for (int j = 0; j < cameras.Length; j++)
                {
                    Camera camera = cameras[j];
                    if (camera == null || !camera.enabled || camera.cameraType != CameraType.Game) continue;
                    if (best == null || camera.depth > best.depth)
                    {
                        best = camera;
                    }
                }
            }

            return best;
        }

        private static void SetExclusiveGameCamera(Camera activeCamera)
        {
            if (activeCamera == null)
            {
                return;
            }

            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera camera = cameras[i];
                if (camera == null || camera.cameraType != CameraType.Game) continue;

                bool makeActive = camera == activeCamera;
                camera.enabled = makeActive;

                var listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = makeActive;
                }

                if (makeActive)
                {
                    if (!camera.CompareTag("MainCamera"))
                    {
                        camera.tag = "MainCamera";
                    }
                }
                else if (camera.CompareTag("MainCamera"))
                {
                    camera.tag = "Untagged";
                }
            }
        }

        private void ApplyViewToAllEnabledGameCameras(string viewName)
        {
            var cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                var c = cameras[i];
                if (!c.enabled || c.cameraType != CameraType.Game) continue;

                var controller = c.GetComponent<CameraController>();
                if (controller != null)
                {
                    controller.SnapToView(viewName);
                }
                else
                {
                    CameraController.SnapCameraToView(c, viewName);
                }
            }
        }

        private string ResolveViewKey(string viewKey)
        {
            bool isInternal = _currentTemplateId == WorkflowTemplateCatalog.InternalFlow;
            bool isMachinery = _currentTemplateId == WorkflowTemplateCatalog.RotatingMachinery;
            bool isDamBreak = _currentTemplateId == WorkflowTemplateCatalog.FreeSurface;

            switch (viewKey)
            {
                case "geometry":
                    if (isMachinery) return "machinery";
                    if (isInternal) return "pipeflow";
                    if (isDamBreak) return "dambreak";
                    return "windtunnel";
                case "materials":
                    if (isMachinery) return "machinery";
                    if (isInternal) return "fluid";
                    if (isDamBreak) return "dambreak";
                    return "fluid";
                case "boundary":
                    if (isMachinery) return "machinery";
                    if (isInternal) return "pipeboundary";
                    return "boundary";
                case "monitors":
                    return "results";
                default:
                    return viewKey;
            }
        }

        private void HandlePanelSelected(string viewKey)
        {
            if (_propertiesPanelController == null) return;
            string resolved = ResolveViewKey(viewKey);
            _activePanelKey = resolved;
            _propertiesPanelVisible = true;
            ApplyWindowPanelVisibility();
            if (resolved != "results")
            {
                var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
                sim?.SetLiveResultsComputationEnabled(false);
            }
            ShowAndBindPropertiesView(resolved);
        }

        private void ShowAndBindPropertiesView(string resolved)
        {
            if (_propertiesPanelController == null) return;

            _propertiesPanelVisible = true;

            _propertiesPanelController.SetActiveView(resolved);
            var container = _propertiesPanelController.GetContainer();
            if (container == null) return;

            switch (resolved)
            {
                case "boundary":
                    BindBoundaryControls(container);
                    break;
                case "pipeboundary":
                    _propertiesPanelController.BindPipeBoundaryConditionsControls();
                    break;
                case "windtunnel":
                    _propertiesPanelController.BindWindTunnelControls();
                    break;
                case "fluid":
                    _propertiesPanelController.BindFluidPropertiesControls();
                    break;
                case "dambreak":
                    _propertiesPanelController.BindDamBreakControls();
                    break;
                case "results":
                    BindResultsControls(container);
                    break;
                case "segmentation":
                    _propertiesPanelController.BindModelSegmentationControls();
                    break;
                case "machinery":
                    _propertiesPanelController.BindRotatingMachineryControls();
                    break;
                case "pipeflow":
                    _propertiesPanelController.BindPipeFlowControls(true);
                    break;
                case "pipeflowmodel":
                    _propertiesPanelController.BindPipeFlowControls(false);
                    break;
            }
        }

        private void HandleExportVideo()
        {
            var recorder = FindAnyObjectByType<AeroFlow.Managers.VideoCaptureManager>();
            if (recorder == null)
            {
                Debug.LogWarning("[UI] VideoCaptureManager not found in scene.");
                return;
            }
            if (recorder.IsRecording)
            {
                recorder.StopRecording();
            }
            else
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                var path = SFB.StandaloneFileBrowser.SaveFilePanel(
                    "Save MP4",
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\Downloads",
                    "AeroFlow_Capture",
                    "mp4"
                );
                if (!string.IsNullOrEmpty(path))
                {
                    recorder.StartRecordingWithSavePath(path);
                }
#else
                recorder.StartRecording();
#endif
            }
            _recordingIndicatorVisible = recorder.IsRecording;
            if (_ribbonController != null) _ribbonController.SetRecordingState(recorder.IsRecording);
            if (_recordingIndicator != null) _recordingIndicator.style.display = !_hudHidden && _recordingIndicatorVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void HandleExportScreenshot()
        {
            var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
            if (sim == null) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var path = SFB.StandaloneFileBrowser.SaveFilePanel(
                "Save Viewport Screenshot",
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\Downloads",
                "AeroFlow_Screenshot",
                "png"
            );
            if (!string.IsNullOrEmpty(path))
            {
                sim.CaptureViewportScreenshot(path);
            }
#else
            string fallback = System.IO.Path.Combine(Application.persistentDataPath, "AeroFlow_Screenshot.png");
            sim.CaptureViewportScreenshot(fallback);
#endif
        }

        private void HandleExportHtmlReport()
        {
            var generator = FluidWorks.Reporting.ReportGenerator.Instance != null
                ? FluidWorks.Reporting.ReportGenerator.Instance
                : FindAnyObjectByType<FluidWorks.Reporting.ReportGenerator>();
            if (generator == null)
            {
                Debug.LogWarning("[UI] ReportGenerator not found in scene.");
                return;
            }
            generator.PromptAndGenerateReport();
        }

        private void ToggleConsole()
        {
            _consoleExpanded = !_consoleExpanded;
            ApplyWindowPanelVisibility();
        }

        private void ToggleOutlinePanel()
        {
            _outlinePanelVisible = !_outlinePanelVisible;
            ApplyWindowPanelVisibility();
        }

        private void ToggleConsolePanel()
        {
            _consolePanelVisible = !_consolePanelVisible;
            ApplyWindowPanelVisibility();
        }

        private void TogglePropertiesPanel()
        {
            _propertiesPanelVisible = !_propertiesPanelVisible;
            ApplyWindowPanelVisibility();
        }

        private void HandleExportResultsCsv()
        {
            var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
            if (sim == null) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var path = SFB.StandaloneFileBrowser.SaveFilePanel(
                "Save CFD Results (CSV)",
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\Downloads",
                "AeroFlow_Results",
                "csv"
            );
            if (string.IsNullOrEmpty(path)) return;
            sim.ExportResultsToCsv(path);
#else
            string fallback = System.IO.Path.Combine(Application.persistentDataPath, "AeroFlow_Results.csv");
            sim.ExportResultsToCsv(fallback);
#endif
        }

        private void HandleExportSnapshotJson()
        {
            var sim = SimulationManager.Instance != null ? SimulationManager.Instance : FindAnyObjectByType<SimulationManager>();
            if (sim == null) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var path = SFB.StandaloneFileBrowser.SaveFilePanel(
                "Save CFD Snapshot (JSON)",
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile) + "\\Downloads",
                "AeroFlow_Snapshot",
                "json"
            );
            if (string.IsNullOrEmpty(path)) return;
            sim.ExportLatestSnapshotToJson(path);
#else
            string fallback = System.IO.Path.Combine(Application.persistentDataPath, "AeroFlow_Snapshot.json");
            sim.ExportLatestSnapshotToJson(fallback);
#endif
        }

        private void HandleCloseApp()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void HideLoadPrompt()
        {
            _loadPromptVisible = false;
            if (_loadPrompt != null)
            {
                _loadPrompt.style.display = DisplayStyle.None;
            }
        }

        public void ShowHomePage()
        {
            ShowProjectActions();
            if (_mainContent != null)
            {
                _mainContent.style.display = DisplayStyle.None;
            }
            if (_homeScreen != null)
            {
                _homeScreen.style.display = DisplayStyle.Flex;
            }
            if (_ribbonContainer != null)
            {
                _ribbonContainer.style.display = DisplayStyle.None;
            }
            if (_statusBar != null)
            {
                _statusBar.style.display = DisplayStyle.None;
            }
            if (_homeVideoPlayer != null && !_homeVideoPlayer.isPlaying)
            {
                _homeVideoPlayer.Play();
            }
        }

        public void UpdateMetrics(AeroFlow.Managers.SimulationMetrics m)
        {
            if (!_metricLabelsCached)
            {
                CacheMetricLabels();
            }

            bool isDamBreak = m.simulationMode == "DamBreak";
            bool isPipeFlow = m.simulationMode == "PipeFlow";
            bool isMachinery = m.simulationMode == "RotatingMachinery";

            if (isPipeFlow)
            {
                SetLabel(_dragLabel, $"Friction Factor (f): {m.pipeFrictionFactor:F4}");
                SetLabel(_liftLabel, $"Head Loss (m): {m.pipeHeadLoss:F3}");
                SetLabel(_sideForceLabel, $"Flow Rate (m3/s): {m.pipeFlowRate:F4}");
                SetLabel(_reynoldsLabel, $"Pipe Reynolds (Re): {m.pipeReynolds:E2}");
                SetLabel(_pressureLabel, $"Pressure Gradient (Pa/m): {m.pipePressureGradient:F2}");
                SetLabel(_referenceAreaLabel, "-");
                SetLabel(_dragForceLabel, "-");
                SetLabel(_verticalForceLabel, "-");
                SetLabel(_downforceLabel, "-");
                SetLabel(_copLabel, "-");
                SetLabel(_pitchMomentLabel, "-");
                SetLabel(_yawMomentLabel, "-");
                SetLabel(_rollMomentLabel, "-");
                SetLabel(_axleLoadLabel, "-");
                SetLabel(_topSpeedLabel, "-");

                SetLabel(_regimeLabel, $"Flow Regime: {m.flowRegime}");
                SetLabel(_assessmentLabel, $"Assessment: {m.assessment}");
                SetLabel(_qualityLabel, $"Overall Rating: {m.qualityRating}");
                SetLabel(_scoreLabel, $"Quality Score: {m.qualityScore:P0}");
                SetLabel(_tipsLabel, $"Suggestions: {m.qualityTips}");

                SetLabel(_navierState, $"Pipe Flow Solver: {(m.navierValid ? "Active" : "Waiting")}");
                SetLabel(_navierMean, $"Mean Velocity (m/s): {m.navierMeanVelocity:F3}");
                SetLabel(_navierMax, $"Max Velocity (m/s): {m.navierMaxVelocity:F3}");
                SetLabel(_navierDeltaP, $"Pressure Drop dP (Pa): {m.navierPressureDrop:F3}");
                SetLabel(_navierTau, $"Wall Shear tau_w (Pa): {m.navierWallShear:F5}");
                SetLabel(_navierDiv, $"Divergence L1: {m.navierDivergenceL1:E2}");
            }
            else if (isMachinery)
            {
                SetLabel(_dragLabel, $"Torque (Nm): {m.machineryTorque:F2}");
                SetLabel(_liftLabel, $"Power (W): {m.machineryPower:F1}");
                SetLabel(_sideForceLabel, $"Efficiency: {m.machineryEfficiency:P1}");
                SetLabel(_reynoldsLabel, $"Machine Re: {m.reynolds:E2}");
                SetLabel(_pressureLabel, $"Angular Velocity (rad/s): {m.machineryAngularVelocity:F2}");
                SetLabel(_referenceAreaLabel, $"Tip-Speed Ratio: {m.machineryTipSpeedRatio:F2}");
                SetLabel(_dragForceLabel, $"Wake Deficit: {m.machineryWakeDeficit:P1}");
                SetLabel(_verticalForceLabel, $"Energy: {m.machineryEnergyDirection ?? "-"}");
                SetLabel(_downforceLabel, $"Application: {m.machineryApplicationLabel ?? "-"}");
                SetLabel(_copLabel, "-");
                SetLabel(_pitchMomentLabel, "-");
                SetLabel(_yawMomentLabel, "-");
                SetLabel(_rollMomentLabel, "-");
                SetLabel(_axleLoadLabel, "-");
                SetLabel(_topSpeedLabel, "-");

                SetLabel(_regimeLabel, $"Flow Regime: {m.flowRegime}");
                SetLabel(_assessmentLabel, $"Assessment: {m.assessment}");
                SetLabel(_qualityLabel, $"Overall Rating: {m.qualityRating}");
                SetLabel(_scoreLabel, $"Quality Score: {m.qualityScore:P0}");
                SetLabel(_tipsLabel, $"Suggestions: {m.qualityTips}");

                SetLabel(_navierState, $"MRF Solver: {(m.navierValid ? "Active" : "Waiting")}");
                SetLabel(_navierMean, $"Mean Velocity (m/s): {m.navierMeanVelocity:F3}");
                SetLabel(_navierMax, $"Max Velocity (m/s): {m.navierMaxVelocity:F3}");
                SetLabel(_navierDeltaP, $"Pressure Drop dP (Pa): {m.navierPressureDrop:F3}");
                SetLabel(_navierTau, $"Wall Shear tau_w (Pa): {m.navierWallShear:F5}");
                SetLabel(_navierDiv, $"Divergence L1: {m.navierDivergenceL1:E2}");
            }
            else if (!isDamBreak)
            {
                SetLabel(_dragLabel, $"Cd (Drag):  {m.drag:F4}");
                SetLabel(_liftLabel, $"Cl (Lift):  {m.lift:F4}");
                SetLabel(_sideForceLabel, $"Cs (Side):  {m.sideForceCoeff:F4}");
                SetLabel(_reynoldsLabel, $"Reynolds:  {m.reynolds:E2}");
                SetLabel(_pressureLabel, $"Dynamic Pressure:  {m.pressure:F0} Pa");
                SetLabel(_referenceAreaLabel, $"Frontal Area:  {m.referenceArea:F2} m\u00b2");
                SetLabel(_dragForceLabel, $"Drag Force:  {m.dragForce:F0} N");
                SetLabel(_verticalForceLabel, $"Lift Force:  {m.verticalAeroForce:F0} N");
                SetLabel(_downforceLabel, $"Downforce:  {m.downforce:F0} N");
                SetLabel(_copLabel, $"CoP Offset:  {m.centerOfPressureLongitudinal:F2} m");
                SetLabel(_pitchMomentLabel, $"Pitch Moment:  {m.pitchMoment:F0} Nm");
                SetLabel(_yawMomentLabel, $"Yaw Moment:  {m.yawMoment:F0} Nm");
                SetLabel(_rollMomentLabel, $"Roll Moment:  {m.rollMoment:F0} Nm");
                SetLabel(_axleLoadLabel, $"Axle Loads (F / R):  {m.frontAxleLoad:F0} / {m.rearAxleLoad:F0} N");
                SetLabel(_topSpeedLabel, $"Est. Top Speed:  {m.estimatedTopSpeed * 3.6f:F1} km/h");

                SetLabel(_regimeLabel, $"Regime: {m.flowRegime}");
                SetLabel(_assessmentLabel, $"Assessment: {m.assessment}");
                SetLabel(_qualityLabel, $"Rating: {m.qualityRating}");
                SetLabel(_scoreLabel, $"Score: {m.qualityScore:P0}");
                SetLabel(_tipsLabel, $"Tips: {m.qualityTips}");

                SetLabel(_navierState, $"Navier-Stokes Grid: {(m.navierValid ? "Active" : "Waiting")}");
                SetLabel(_navierMean, $"Mean Velocity: {m.navierMeanVelocity:F3} m/s");
                SetLabel(_navierMax, $"Peak Velocity: {m.navierMaxVelocity:F3} m/s");
                SetLabel(_navierDeltaP, $"Pressure Drop: {m.navierPressureDrop:F3} Pa");
                SetLabel(_navierTau, $"Wall Shear: {m.navierWallShear:F5} Pa");
                SetLabel(_navierDiv, $"Divergence: {m.navierDivergenceL1:E2}");
            }
            else
            {
                SetLabel(_dragLabel, $"Kinetic Energy (J): {m.liquidKineticEnergy:F2}");
                SetLabel(_liftLabel, $"RMS Velocity (m/s): {m.liquidVelocityRms:F3}");
                SetLabel(_sideForceLabel, "-");
                SetLabel(_reynoldsLabel, $"Splash Height (m): {m.liquidSplashHeight:F3}");
                SetLabel(_pressureLabel, $"Impact Pressure Proxy (Pa): {m.liquidImpactPressure:F1}");
                SetLabel(_referenceAreaLabel, "-");
                SetLabel(_dragForceLabel, "-");
                SetLabel(_verticalForceLabel, "-");
                SetLabel(_downforceLabel, "-");
                SetLabel(_copLabel, "-");
                SetLabel(_pitchMomentLabel, "-");
                SetLabel(_yawMomentLabel, "-");
                SetLabel(_rollMomentLabel, "-");
                SetLabel(_axleLoadLabel, "-");
                SetLabel(_topSpeedLabel, "-");

                SetLabel(_regimeLabel, $"Liquid State: {m.flowRegime}");
                SetLabel(_assessmentLabel, $"Assessment: {m.assessment}");
                SetLabel(_qualityLabel, $"Overall Rating: {m.qualityRating}");
                SetLabel(_scoreLabel, $"Stability Score: {m.qualityScore:P0}");
                SetLabel(_tipsLabel, $"Suggestions: {m.qualityTips}");

                SetLabel(_navierState, "Liquid Diagnostics: Active");
                SetLabel(_navierMean, $"Containment: {m.liquidContainment:P1}");
                SetLabel(_navierMax, $"Velocity RMS (m/s): {m.liquidVelocityRms:F3}");
                SetLabel(_navierDeltaP, $"Impact Pressure (Pa): {m.liquidImpactPressure:F2}");
                SetLabel(_navierTau, $"Kinetic Energy (J): {m.liquidKineticEnergy:F2}");
                SetLabel(_navierDiv, $"Stability Index: {m.liquidStability:P1}");
            }

            // ML Model Quality labels (mode-independent)
            bool hasModelQuality = m.modelQualityScore > 0f || (m.modelQualityGrade != null && m.modelQualityGrade != "-");
            if (hasModelQuality)
            {
                SetLabel(_modelScoreLabel, $"Overall Score: {m.modelQualityScore:F0} / 100");
                SetLabel(_modelGradeLabel, $"Grade: {m.modelQualityGrade}");
                SetLabel(_modelCdLabel, $"Predicted Cd Range: {m.modelPredictedCdLow:F3} – {m.modelPredictedCdHigh:F3}");
                SetLabel(_modelSepRiskLabel, $"Separation Risk: {m.modelSeparationRisk:P0}");
                SetLabel(_modelDownforceLabel, $"Downforce Potential: {m.modelDownforcePotential:P0}");
                SetLabel(_modelEfficiencyLabel, $"ML Efficiency Score: {m.modelEfficiencyScore:P0}");
                SetLabel(_modelFeaturesLabel, string.IsNullOrEmpty(m.modelFeatureBreakdown) ? "Analyzing model..." : m.modelFeatureBreakdown);
                SetLabel(_modelImprovementsLabel, string.IsNullOrEmpty(m.modelImprovements) ? "Analyzing model..." : m.modelImprovements);
            }
            else if (m.simulationMode == "WindTunnel")
            {
                // Model loaded but analysis not yet complete — show intermediate state
                SetLabel(_modelFeaturesLabel, "Analyzing model...");
                SetLabel(_modelImprovementsLabel, "Analyzing model...");
            }

            // Update status bar
            if (_statusModeLabel != null)
            {
                string modeName = m.simulationMode ?? "Ready";
                _statusModeLabel.text = $"{modeName} | t = {m.timestamp:F1}s";
            }
            if (_statusFpsLabel != null)
            {
                _statusFpsLabel.text = $"{1f / Time.unscaledDeltaTime:F0} FPS";
            }
        }

        private void CacheMetricLabels()
        {
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null) return;

            _dragLabel = root.Q<Label>("drag-value");
            _liftLabel = root.Q<Label>("lift-value");
            _sideForceLabel = root.Q<Label>("side-force-value");
            _reynoldsLabel = root.Q<Label>("reynolds-value");
            _pressureLabel = root.Q<Label>("pressure-value");
            _referenceAreaLabel = root.Q<Label>("reference-area-value");
            _dragForceLabel = root.Q<Label>("drag-force-value");
            _verticalForceLabel = root.Q<Label>("vertical-force-value");
            _downforceLabel = root.Q<Label>("downforce-value");
            _copLabel = root.Q<Label>("cop-value");
            _pitchMomentLabel = root.Q<Label>("pitch-moment-value");
            _yawMomentLabel = root.Q<Label>("yaw-moment-value");
            _rollMomentLabel = root.Q<Label>("roll-moment-value");
            _axleLoadLabel = root.Q<Label>("axle-load-value");
            _topSpeedLabel = root.Q<Label>("top-speed-value");
            _regimeLabel = root.Q<Label>("flow-regime-label");
            _assessmentLabel = root.Q<Label>("drag-comment-label");
            _qualityLabel = root.Q<Label>("quality-rating-label");
            _scoreLabel = root.Q<Label>("quality-score-label");
            _tipsLabel = root.Q<Label>("quality-tips-label");
            _navierState = root.Q<Label>("navier-state-label");
            _navierMean = root.Q<Label>("navier-mean-velocity-label");
            _navierMax = root.Q<Label>("navier-max-velocity-label");
            _navierDeltaP = root.Q<Label>("navier-pressure-drop-label");
            _navierTau = root.Q<Label>("navier-wall-shear-label");
            _navierDiv = root.Q<Label>("navier-divergence-label");
            _modelScoreLabel = root.Q<Label>("model-quality-score-label");
            _modelGradeLabel = root.Q<Label>("model-quality-grade-label");
            _modelCdLabel = root.Q<Label>("model-predicted-cd-label");
            _modelSepRiskLabel = root.Q<Label>("model-separation-risk-label");
            _modelDownforceLabel = root.Q<Label>("model-downforce-label");
            _modelEfficiencyLabel = root.Q<Label>("model-efficiency-label");
            _modelFeaturesLabel = root.Q<Label>("model-features-label");
            _modelImprovementsLabel = root.Q<Label>("model-improvements-label");

            // Only mark cached if at least one label was found — the Results
            // UXML may not be loaded yet, so we need to retry next frame.
            _metricLabelsCached = _dragLabel != null || _navierState != null || _modelScoreLabel != null;
        }

        private static void SetLabel(Label label, string text)
        {
            if (label != null) label.text = text;
        }

        private void DismissHomePage()
        {
            if (_homeScreen != null)
            {
                _homeScreen.style.display = DisplayStyle.None;
            }
            if (_mainContent != null)
            {
                _mainContent.style.display = DisplayStyle.Flex;
            }
            ApplyHudVisibility();
        }

        private System.Collections.IEnumerator RunSplashScreenSequence()
        {
            float duration = 3.5f; // Total time for splash
            float elapsed = 0f;

            if (_splashProgressFill != null) _splashProgressFill.style.width = Length.Percent(0);

            // Hide the HUD completely during splash screen
            _hudHidden = true;
            ApplyHudVisibility();

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float pct = Mathf.Clamp01(elapsed / duration);

                if (_splashProgressFill != null)
                {
                    _splashProgressFill.style.width = Length.Percent(pct * 100f);
                }

                if (_splashStatusLabel != null)
                {
                    if (pct < 0.2f) _splashStatusLabel.text = "Initializing Graphics Subsystem...";
                    else if (pct < 0.45f) _splashStatusLabel.text = "Heating up the Wind Tunnel...";
                    else if (pct < 0.70f) _splashStatusLabel.text = "Loading Fluid Mechanics Kernels...";
                    else if (pct < 0.90f) _splashStatusLabel.text = "Compiling CFD Shaders...";
                    else _splashStatusLabel.text = "Starting AeroFlow Engine...";
                }

                yield return null;
            }

            // Splash is done, remove it
            if (_splashScreen != null)
            {
                _splashScreen.style.display = DisplayStyle.None;
            }

            // Show the Homepage properly
            ShowHomePage();
            
            // Re-enable HUD in the background (though ShowHomePage will hide it, this keeps the state consistent)
            _hudHidden = false;
        }
    }
}
