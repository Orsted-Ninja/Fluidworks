using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Sim3D.PipeFlow;

namespace AeroFlow.UI
{
    public class PipeBoundaryConditionsController
    {
        private readonly VisualElement _root;
        private BoundaryConditionManager _manager;
        private ScrollView _openingsList;
        private Label _statusLabel;
        private Label _pickStatus;
        private Label _totalCount;
        private Label _inletCount;
        private Label _outletCount;
        private Button _cancelPickBtn;

        public PipeBoundaryConditionsController(VisualElement root)
        {
            _root = root;
            BindControls();
        }

        private void BindControls()
        {
            _manager = Object.FindFirstObjectByType<BoundaryConditionManager>();

            _openingsList = _root.Q<ScrollView>("bc-openings-list");
            _statusLabel = _root.Q<Label>("bc-status-label");
            _pickStatus = _root.Q<Label>("bc-pick-status");
            _totalCount = _root.Q<Label>("bc-total-count");
            _inletCount = _root.Q<Label>("bc-inlet-count");
            _outletCount = _root.Q<Label>("bc-outlet-count");
            _cancelPickBtn = _root.Q<Button>("bc-cancel-pick-button");

            var detectBtn = _root.Q<Button>("bc-detect-button");
            if (detectBtn != null)
                detectBtn.clicked += OnDetectClicked;

            var assignInletBtn = _root.Q<Button>("bc-assign-inlet-button");
            if (assignInletBtn != null)
                assignInletBtn.clicked += () => StartPicking(AssignmentMode.PickingInlet);

            var assignOutletBtn = _root.Q<Button>("bc-assign-outlet-button");
            if (assignOutletBtn != null)
                assignOutletBtn.clicked += () => StartPicking(AssignmentMode.PickingOutlet);

            if (_cancelPickBtn != null)
                _cancelPickBtn.clicked += OnCancelPick;

            if (_manager != null)
            {
                _manager.OnAssignmentsChanged += OnChanged;
                _manager.OnAssignmentModeChanged += OnModeChanged;
                if (_manager.OpeningCount > 0)
                {
                    RebuildOpeningsList();
                    UpdateSummary();
                }
                UpdateWorkflowReadiness();
            }
        }

        private void StartPicking(AssignmentMode mode)
        {
            EnsureManager();
            if (_manager == null || _manager.OpeningCount == 0)
            {
                if (_statusLabel != null)
                    _statusLabel.text = "Detect openings first!";
                return;
            }
            _manager.BeginPicking(mode);
        }

        private void OnCancelPick()
        {
            if (_manager != null) _manager.CancelPicking();
        }

        private void OnModeChanged(AssignmentMode mode)
        {
            bool picking = mode != AssignmentMode.None;

            if (_pickStatus != null)
            {
                _pickStatus.style.display = picking ? DisplayStyle.Flex : DisplayStyle.None;
                if (picking)
                {
                    string what = mode == AssignmentMode.PickingInlet ? "INLET" : "OUTLET";
                    _pickStatus.text = $"Click an opening to assign as {what}…";
                }
            }

            if (_cancelPickBtn != null)
                _cancelPickBtn.style.display = picking ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnDetectClicked()
        {
            EnsureManager();
            if (_manager == null) return;
            if (_statusLabel != null) _statusLabel.text = "Detecting…";
            _manager.DetectOpenings();
            var pipeSim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            if (pipeSim != null)
            {
                pipeSim.boundaryManager = _manager;
                pipeSim.InvalidateBoundaryState();
            }
            RebuildOpeningsList();
            UpdateSummary();
            UpdateWorkflowReadiness();
        }

        private void EnsureManager()
        {
            if (_manager != null) return;
            _manager = Object.FindFirstObjectByType<BoundaryConditionManager>();
            var pipeSim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            if (_manager == null)
            {
                if (pipeSim != null)
                    _manager = pipeSim.gameObject.AddComponent<BoundaryConditionManager>();
                else
                {
                    var go = new GameObject("BoundaryConditionManager");
                    _manager = go.AddComponent<BoundaryConditionManager>();
                }
                _manager.OnAssignmentsChanged += OnChanged;
                _manager.OnAssignmentModeChanged += OnModeChanged;
            }

            if (pipeSim != null)
            {
                pipeSim.boundaryManager = _manager;
            }
        }

        private void OnChanged()
        {
            var pipeSim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            if (pipeSim != null)
            {
                pipeSim.boundaryManager = _manager;
                pipeSim.InvalidateBoundaryState();
            }

            RebuildOpeningsList();
            UpdateSummary();
            UpdateWorkflowReadiness();
        }

        private void UpdateWorkflowReadiness()
        {
            EnsureManager();
            if (_manager == null)
                return;

            var pipeSim = Object.FindFirstObjectByType<PipeFlowSimulation3D>();
            bool ready = _manager.HasValidFlowAssignments();

            if (_statusLabel != null)
            {
                if (_manager.OpeningCount == 0)
                {
                    _statusLabel.text = "Click Detect to find openings";
                }
                else if (!ready)
                {
                    _statusLabel.text = "Assign at least one inlet and one outlet to enable flow results.";
                }
                else
                {
                    _statusLabel.text = "Ready. Press Play to compute surface results.";
                }
            }

            if (ready && pipeSim != null)
            {
                pipeSim.InitializeIfNeeded();
                pipeSim.SetVisualizationMode(PipeFlowSimulation3D.NormalizeVisualizationMode(pipeSim.settings.visualizationMode));
            }
        }

        private void RebuildOpeningsList()
        {
            if (_openingsList == null) return;
            _openingsList.Clear();

            if (_manager == null || _manager.OpeningCount == 0)
            {
                if (_statusLabel != null)
                    _statusLabel.text = "No openings detected.";
                return;
            }

            if (_statusLabel != null)
                _statusLabel.text = $"{_manager.OpeningCount} opening(s) detected";

            for (int i = 0; i < _manager.Assignments.Count; i++)
            {
                var a = _manager.Assignments[i];
                int idx = i;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 3;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.paddingLeft = 6;
                row.style.paddingRight = 6;
                row.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);
                row.style.borderTopLeftRadius = 4;
                row.style.borderTopRightRadius = 4;
                row.style.borderBottomLeftRadius = 4;
                row.style.borderBottomRightRadius = 4;

                var dot = new VisualElement();
                dot.style.width = 10;
                dot.style.height = 10;
                dot.style.borderTopLeftRadius = 5;
                dot.style.borderTopRightRadius = 5;
                dot.style.borderBottomLeftRadius = 5;
                dot.style.borderBottomRightRadius = 5;
                dot.style.marginRight = 6;
                dot.style.backgroundColor = DotColor(a.type);
                row.Add(dot);

                var info = new VisualElement();
                info.style.flexGrow = 1;

                var title = new Label($"Opening {i + 1}");
                title.style.fontSize = 11;
                title.style.color = new Color(0.88f, 0.93f, 1f);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                info.Add(title);

                string detail = a.opening != null
                    ? $"R = {a.opening.radius:F3}m  •  {a.opening.vertexCount} pts"
                    : "—";
                var sub = new Label(detail);
                sub.style.fontSize = 9;
                sub.style.color = new Color(0.55f, 0.65f, 0.75f, 0.8f);
                info.Add(sub);

                if (a.type != BoundaryType.Unassigned)
                {
                    var badge = new Label(a.type == BoundaryType.Inlet ? "INLET" : "OUTLET");
                    badge.style.fontSize = 8;
                    badge.style.paddingLeft = 4;
                    badge.style.paddingRight = 4;
                    badge.style.paddingTop = 1;
                    badge.style.paddingBottom = 1;
                    badge.style.borderTopLeftRadius = 2;
                    badge.style.borderTopRightRadius = 2;
                    badge.style.borderBottomLeftRadius = 2;
                    badge.style.borderBottomRightRadius = 2;
                    badge.style.unityFontStyleAndWeight = FontStyle.Bold;
                    badge.style.color = Color.white;
                    badge.style.backgroundColor = a.type == BoundaryType.Inlet
                        ? new Color(0.15f, 0.7f, 0.3f, 0.7f)
                        : new Color(0.85f, 0.2f, 0.15f, 0.7f);
                    badge.style.marginTop = 1;
                    info.Add(badge);
                }
                row.Add(info);

                var flashBtn = new Button { text = "👁" };
                flashBtn.style.width = 24;
                flashBtn.style.height = 24;
                flashBtn.style.fontSize = 12;
                flashBtn.style.marginLeft = 4;
                flashBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                flashBtn.style.backgroundColor = new Color(0.12f, 0.2f, 0.35f, 0.5f);
                flashBtn.style.borderTopWidth = 0;
                flashBtn.style.borderBottomWidth = 0;
                flashBtn.style.borderLeftWidth = 0;
                flashBtn.style.borderRightWidth = 0;
                flashBtn.style.borderTopLeftRadius = 3;
                flashBtn.style.borderTopRightRadius = 3;
                flashBtn.style.borderBottomLeftRadius = 3;
                flashBtn.style.borderBottomRightRadius = 3;
                flashBtn.tooltip = "Flash this opening";
                flashBtn.clicked += () => _manager?.FlashOpening(idx);
                row.Add(flashBtn);

                if (a.type != BoundaryType.Unassigned)
                {
                    var clearBtn = new Button { text = "✕" };
                    clearBtn.style.width = 22;
                    clearBtn.style.height = 22;
                    clearBtn.style.fontSize = 11;
                    clearBtn.style.marginLeft = 2;
                    clearBtn.style.unityTextAlign = TextAnchor.MiddleCenter;
                    clearBtn.style.backgroundColor = new Color(0.4f, 0.1f, 0.1f, 0.4f);
                    clearBtn.style.borderTopWidth = 0;
                    clearBtn.style.borderBottomWidth = 0;
                    clearBtn.style.borderLeftWidth = 0;
                    clearBtn.style.borderRightWidth = 0;
                    clearBtn.style.borderTopLeftRadius = 3;
                    clearBtn.style.borderTopRightRadius = 3;
                    clearBtn.style.borderBottomLeftRadius = 3;
                    clearBtn.style.borderBottomRightRadius = 3;
                    clearBtn.tooltip = "Clear assignment";
                    clearBtn.clicked += () => _manager?.SetBoundaryType(idx, BoundaryType.Unassigned);
                    row.Add(clearBtn);
                }

                _openingsList.Add(row);
            }
        }

        private Color DotColor(BoundaryType t) => t switch
        {
            BoundaryType.Inlet => new Color(0.2f, 0.85f, 0.4f, 1f),
            BoundaryType.Outlet => new Color(0.95f, 0.25f, 0.2f, 1f),
            _ => new Color(0.35f, 0.6f, 1f, 0.7f)
        };

        private void UpdateSummary()
        {
            if (_manager == null) return;
            int total = _manager.OpeningCount, inlets = 0, outlets = 0;
            foreach (var a in _manager.Assignments)
            {
                if (a.type == BoundaryType.Inlet) inlets++;
                else if (a.type == BoundaryType.Outlet) outlets++;
            }
            if (_totalCount != null) _totalCount.text = total.ToString();
            if (_inletCount != null) _inletCount.text = inlets.ToString();
            if (_outletCount != null) _outletCount.text = outlets.ToString();
        }
    }
}
