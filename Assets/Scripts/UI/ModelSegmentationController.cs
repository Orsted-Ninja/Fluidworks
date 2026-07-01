using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AeroFlow.Core;

namespace AeroFlow.UI
{
    public class ModelSegmentationController
    {
        private VisualElement _root;
        private ListView _partsList;
        private VisualElement _partDetails;
        private VisualElement _noSelectionMsg;
        private Label _selectedPartName;
        
        private DropdownField _motionTypeDropdown;
        private DropdownField _collectionTypeDropdown;
        private VisualElement _rotationSettings;
        private VisualElement _translationSettings;
        
        private Slider _rpmSlider;
        private Vector3Field _rotationAxisField;
        private Toggle _worldAxisToggle;
        
        private Slider _speedSlider;
        private Vector3Field _translationAxisField;
        
        private Vector3Field _pivotOffsetField;
        private Button _resetPivotBtn;
        private Button _aiIdentifyBtn;
        private Button _applyCollectionBtn;
        private Button _setStaticBtn;
        private Button _setRotatingBtn;
        private Button _setTranslatingBtn;
        private Button _clearCollectionBtn;

        private PartRegistry _currentRegistry;
        private List<PartRegistry.PartInfo> _displayedParts = new List<PartRegistry.PartInfo>();
        private PartRegistry.PartInfo _selectedPart;

        public ModelSegmentationController(VisualElement root)
        {
            _root = root;
            BindElements();
            RegisterCallbacks();
            Refresh();
        }

        private void BindElements()
        {
            _partsList = _root.Q<ListView>("parts-list");
            _partDetails = _root.Q<VisualElement>("part-details");
            _noSelectionMsg = _root.Q<VisualElement>("no-selection-msg");
            _selectedPartName = _root.Q<Label>("selected-part-name");
            
            _motionTypeDropdown = _root.Q<DropdownField>("motion-type-dropdown");
            _collectionTypeDropdown = _root.Q<DropdownField>("collection-type-dropdown");
            _rotationSettings = _root.Q<VisualElement>("rotation-settings");
            _translationSettings = _root.Q<VisualElement>("translation-settings");
            
            _rpmSlider = _root.Q<Slider>("rpm-slider");
            _rotationAxisField = _root.Q<Vector3Field>("rotation-axis-field");
            _worldAxisToggle = _root.Q<Toggle>("world-axis-toggle");
            
            _speedSlider = _root.Q<Slider>("speed-slider");
            _translationAxisField = _root.Q<Vector3Field>("translation-axis-field");
            
            _pivotOffsetField = _root.Q<Vector3Field>("pivot-offset-field");
            _resetPivotBtn = _root.Q<Button>("reset-pivot-btn");
            _aiIdentifyBtn = _root.Q<Button>("ai-identify-btn");
            _applyCollectionBtn = _root.Q<Button>("apply-collection-btn");
            _setStaticBtn = _root.Q<Button>("set-static-btn");
            _setRotatingBtn = _root.Q<Button>("set-rotating-btn");
            _setTranslatingBtn = _root.Q<Button>("set-translating-btn");
            _clearCollectionBtn = _root.Q<Button>("clear-collection-btn");
        }

        private void RegisterCallbacks()
        {
            _partsList.makeItem = () => new Label();
            _partsList.bindItem = (element, index) => {
                var label = element as Label;
                var part = _displayedParts[index];
                if (label == null || part == null) return;
                label.text = $"{part.partId}  [{PartRegistry.GetCollectionLabel(part.segmentationCollection)}]";
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.color = GetCollectionColor(part.segmentationCollection);
            };
            _partsList.selectionChanged += OnPartSelectionChanged;

            _motionTypeDropdown.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) {
                    _selectedPart.motionSettings.motionType = (PartMotionType)System.Enum.Parse(typeof(PartMotionType), evt.newValue);
                    SyncCollectionFromMotion(_selectedPart);
                    _currentRegistry?.ApplySegmentationVisuals();
                    UpdateSettingsVisibility();
                }
            });

            _rpmSlider.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.intensity = evt.newValue;
            });
            _rotationAxisField.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.axis = evt.newValue;
            });
            _worldAxisToggle.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.useWorldAxis = evt.newValue;
            });

            _speedSlider.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.intensity = evt.newValue;
            });
            _translationAxisField.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.axis = evt.newValue;
            });

            _pivotOffsetField.RegisterValueChangedCallback(evt => {
                if (_selectedPart?.motionSettings != null) _selectedPart.motionSettings.pivotOffset = evt.newValue;
            });

            _resetPivotBtn.clicked += () => {
                if (_selectedPart?.motionSettings != null) {
                    _selectedPart.motionSettings.pivotOffset = Vector3.zero;
                    _pivotOffsetField.value = Vector3.zero;
                }
            };

            _aiIdentifyBtn.clicked += () => {
                if (_currentRegistry != null) {
                    var model = RuntimeModelLookup.GetLoadedModel();
                    if (model != null && _currentRegistry.GetParts().Count <= 1)
                    {
                        if (MeshSegmentationUtility.TryAutoSegmentSingleMesh(model, out int createdParts) && createdParts > 0)
                        {
                            _currentRegistry.Rebuild(model.transform, true);
                            _currentRegistry.AutoIdentifyParts();
                            _currentRegistry.ApplySegmentationVisuals();
                            Debug.Log($"[Segmentation] Auto-split single mesh into {createdParts} part(s).");
                        }
                        else
                        {
                            Debug.LogWarning("[Segmentation] Auto Segment Model could not split the current mesh. The file may already be multi-part or the geometry may be too simple for automatic separation.");
                        }
                    }
                    _currentRegistry.AutoIdentifyParts();
                    _currentRegistry.ApplySegmentationVisuals();
                    Refresh();
                }
            };

            if (_applyCollectionBtn != null)
            {
                _applyCollectionBtn.clicked += ApplySelectedCollection;
            }

            if (_setStaticBtn != null)
            {
                _setStaticBtn.clicked += () => SetSelectedCollection(PartSegmentationCollection.StaticStructure);
            }

            if (_setRotatingBtn != null)
            {
                _setRotatingBtn.clicked += () => SetSelectedCollection(PartSegmentationCollection.RotatingBlade);
            }

            if (_setTranslatingBtn != null)
            {
                _setTranslatingBtn.clicked += () => SetSelectedCollection(PartSegmentationCollection.TranslatingPart);
            }

            if (_clearCollectionBtn != null)
            {
                _clearCollectionBtn.clicked += () => SetSelectedCollection(PartSegmentationCollection.Unclassified);
            }
        }

        public void Refresh()
        {
            var model = RuntimeModelLookup.GetLoadedModel();
            _currentRegistry = model != null ? model.GetComponent<PartRegistry>() : null;
            
            if (_currentRegistry != null) {
                _displayedParts = _currentRegistry.GetParts();
                _partsList.itemsSource = _displayedParts;
                _partsList.Rebuild();
                _currentRegistry.ApplySegmentationVisuals();
            } else {
                _displayedParts.Clear();
                _partsList.itemsSource = _displayedParts;
                _partsList.Rebuild();
            }

            UpdateSelectionUI();
        }

        private void OnPartSelectionChanged(IEnumerable<object> selected)
        {
            _selectedPart = null;
            foreach (var item in selected) {
                _selectedPart = item as PartRegistry.PartInfo;
                break;
            }
            UpdateSelectionUI();
        }

        private void UpdateSelectionUI()
        {
            if (_selectedPart == null || _selectedPart.motionSettings == null) {
                _partDetails.style.display = DisplayStyle.None;
                _noSelectionMsg.style.display = DisplayStyle.Flex;
                return;
            }

            _partDetails.style.display = DisplayStyle.Flex;
            _noSelectionMsg.style.display = DisplayStyle.None;
            _selectedPartName.text = $"Part: {_selectedPart.partId}";

            var m = _selectedPart.motionSettings;
            if (_collectionTypeDropdown != null)
            {
                _collectionTypeDropdown.choices = new List<string>
                {
                    PartRegistry.GetCollectionLabel(PartSegmentationCollection.Unclassified),
                    PartRegistry.GetCollectionLabel(PartSegmentationCollection.StaticStructure),
                    PartRegistry.GetCollectionLabel(PartSegmentationCollection.RotatingBlade),
                    PartRegistry.GetCollectionLabel(PartSegmentationCollection.TranslatingPart),
                    PartRegistry.GetCollectionLabel(PartSegmentationCollection.PhysicsDriven)
                };
                _collectionTypeDropdown.SetValueWithoutNotify(PartRegistry.GetCollectionLabel(_selectedPart.segmentationCollection));
            }
            _motionTypeDropdown.value = m.motionType.ToString();
            _rpmSlider.value = m.intensity;
            _rotationAxisField.value = m.axis;
            _worldAxisToggle.value = m.useWorldAxis;
            _speedSlider.value = m.intensity;
            _translationAxisField.value = m.axis;
            _pivotOffsetField.value = m.pivotOffset;

            UpdateSettingsVisibility();
        }

        private void UpdateSettingsVisibility()
        {
            if (_selectedPart == null || _selectedPart.motionSettings == null) return;

            var type = _selectedPart.motionSettings.motionType;
            _rotationSettings.style.display = (type == PartMotionType.ConstantRotation) ? DisplayStyle.Flex : DisplayStyle.None;
            _translationSettings.style.display = (type == PartMotionType.ConstantTranslation) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void ApplySelectedCollection()
        {
            if (_selectedPart == null || _currentRegistry == null || _collectionTypeDropdown == null) return;
            SetSelectedCollection(PartRegistry.ParseCollectionLabel(_collectionTypeDropdown.value));
        }

        private void SetSelectedCollection(PartSegmentationCollection collection)
        {
            if (_selectedPart == null || _currentRegistry == null) return;

            _currentRegistry.SetPartCollection(_selectedPart, collection, applyVisuals: true);
            Refresh();
        }

        private static void SyncCollectionFromMotion(PartRegistry.PartInfo part)
        {
            if (part == null || part.motionSettings == null) return;

            switch (part.motionSettings.motionType)
            {
                case PartMotionType.ConstantRotation:
                    part.segmentationCollection = PartSegmentationCollection.RotatingBlade;
                    break;
                case PartMotionType.ConstantTranslation:
                    part.segmentationCollection = PartSegmentationCollection.TranslatingPart;
                    break;
                case PartMotionType.PhysicsDriven:
                    part.segmentationCollection = PartSegmentationCollection.PhysicsDriven;
                    break;
                default:
                    part.segmentationCollection = PartSegmentationCollection.StaticStructure;
                    break;
            }
        }

        private static Color GetCollectionColor(PartSegmentationCollection collection)
        {
            switch (collection)
            {
                case PartSegmentationCollection.StaticStructure:
                    return new Color(0.24f, 0.58f, 1.00f, 1f);
                case PartSegmentationCollection.RotatingBlade:
                    return new Color(0.10f, 0.95f, 0.85f, 1f);
                case PartSegmentationCollection.TranslatingPart:
                    return new Color(1.00f, 0.74f, 0.18f, 1f);
                case PartSegmentationCollection.PhysicsDriven:
                    return new Color(0.84f, 0.40f, 1.00f, 1f);
                default:
                    return new Color(0.80f, 0.85f, 0.92f, 1f);
            }
        }
    }
}
