using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace AeroFlow.UI
{
    public static class SliderInputBinder
    {
        private const string SliderTextFieldClassName = "unity-base-slider__text-field";
        private const string InnerTextInputClassName = "unity-text-input";

        public static void BindFloat(Slider slider, float initialValue, int decimals, System.Action<float> apply)
        {
            if (slider == null) return;
            slider.showInputField = true;
            slider.SetValueWithoutNotify(initialValue);

            RemoveLegacyNumericRow(slider);
            if (TryConfigureInlineFloatField(slider, decimals, apply))
            {
                return;
            }

            var row = EnsureNumericRow<FloatField>(slider, out Label valueLabel, out FloatField inputField);
            if (row == null || valueLabel == null || inputField == null) return;

            ConfigureNumericField(inputField, 84);
            inputField.formatString = "F" + Mathf.Clamp(decimals, 0, 6);
            inputField.isDelayed = true;

            bool suppress = false;
            void SyncUi(float v)
            {
                string fmt = "F" + Mathf.Clamp(decimals, 0, 6);
                string txt = v.ToString(fmt, CultureInfo.InvariantCulture);
                valueLabel.text = txt;
                if (!suppress) inputField.SetValueWithoutNotify(v);
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                SyncUi(evt.newValue);
                apply?.Invoke(evt.newValue);
            });

            void ApplyFromInput()
            {
                float parsed = Mathf.Clamp(inputField.value, slider.lowValue, slider.highValue);
                suppress = true;
                slider.value = parsed;
                inputField.SetValueWithoutNotify(parsed);
                suppress = false;
            }

            inputField.RegisterCallback<FocusOutEvent>(_ => ApplyFromInput());
            inputField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ApplyFromInput();
                }
            });

            SyncUi(slider.value);
        }

        public static void BindInt(SliderInt slider, int initialValue, System.Action<int> apply)
        {
            if (slider == null) return;
            slider.showInputField = true;
            slider.SetValueWithoutNotify(initialValue);

            RemoveLegacyNumericRow(slider);
            if (TryConfigureInlineIntegerField(slider, apply))
            {
                return;
            }

            var row = EnsureNumericRow<IntegerField>(slider, out Label valueLabel, out IntegerField inputField);
            if (row == null || valueLabel == null || inputField == null) return;

            ConfigureNumericField(inputField, 84);
            inputField.isDelayed = true;

            bool suppress = false;
            void SyncUi(int v)
            {
                string txt = v.ToString(CultureInfo.InvariantCulture);
                valueLabel.text = txt;
                if (!suppress) inputField.SetValueWithoutNotify(v);
            }

            slider.RegisterValueChangedCallback(evt =>
            {
                SyncUi(evt.newValue);
                apply?.Invoke(evt.newValue);
            });

            void ApplyFromInput()
            {
                int parsed = Mathf.Clamp(inputField.value, slider.lowValue, slider.highValue);
                suppress = true;
                slider.value = parsed;
                inputField.SetValueWithoutNotify(parsed);
                suppress = false;
            }

            inputField.RegisterCallback<FocusOutEvent>(_ => ApplyFromInput());
            inputField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    ApplyFromInput();
                }
            });

            SyncUi(slider.value);
        }

        private static bool TryConfigureInlineFloatField(Slider slider, int decimals, System.Action<float> apply)
        {
            FloatField inputField = slider.Q<FloatField>(className: SliderTextFieldClassName);
            if (inputField == null)
            {
                return false;
            }

            ConfigureNumericField(inputField, 84);
            inputField.formatString = "F" + Mathf.Clamp(decimals, 0, 6);
            inputField.isDelayed = true;
            EnsureFieldCanTakeFocus(inputField);

            bool suppress = false;
            slider.RegisterValueChangedCallback(evt =>
            {
                suppress = true;
                inputField.SetValueWithoutNotify(evt.newValue);
                suppress = false;
                apply?.Invoke(evt.newValue);
            });

            inputField.RegisterValueChangedCallback(evt =>
            {
                if (suppress) return;

                float parsed = Mathf.Clamp(evt.newValue, slider.lowValue, slider.highValue);
                suppress = true;
                slider.value = parsed;
                inputField.SetValueWithoutNotify(parsed);
                suppress = false;
                apply?.Invoke(parsed);
            });

            inputField.SetValueWithoutNotify(slider.value);
            return true;
        }

        private static bool TryConfigureInlineIntegerField(SliderInt slider, System.Action<int> apply)
        {
            IntegerField inputField = slider.Q<IntegerField>(className: SliderTextFieldClassName);
            if (inputField == null)
            {
                return false;
            }

            ConfigureNumericField(inputField, 84);
            inputField.isDelayed = true;
            EnsureFieldCanTakeFocus(inputField);

            bool suppress = false;
            slider.RegisterValueChangedCallback(evt =>
            {
                suppress = true;
                inputField.SetValueWithoutNotify(evt.newValue);
                suppress = false;
                apply?.Invoke(evt.newValue);
            });

            inputField.RegisterValueChangedCallback(evt =>
            {
                if (suppress) return;

                int parsed = Mathf.Clamp(evt.newValue, slider.lowValue, slider.highValue);
                suppress = true;
                slider.value = parsed;
                inputField.SetValueWithoutNotify(parsed);
                suppress = false;
                apply?.Invoke(parsed);
            });

            inputField.SetValueWithoutNotify(slider.value);
            return true;
        }

        private static void ConfigureNumericField<TValue>(TextValueField<TValue> inputField, float width)
        {
            inputField.label = string.Empty;
            inputField.isReadOnly = false;
            inputField.focusable = true;
            inputField.delegatesFocus = true;
            inputField.selectAllOnFocus = true;
            inputField.selectAllOnMouseUp = true;
            inputField.style.width = width;
            inputField.style.height = 20;
            inputField.style.fontSize = 11;
            inputField.style.flexShrink = 0;
            if (inputField.labelElement != null)
            {
                inputField.labelElement.style.display = DisplayStyle.None;
            }
        }

        private static void EnsureFieldCanTakeFocus<TValue>(TextValueField<TValue> inputField)
        {
            if (inputField == null)
            {
                return;
            }

            inputField.pickingMode = PickingMode.Position;
            inputField.RegisterCallback<PointerDownEvent>(evt => 
            { 
                inputField.Focus(); 
                evt.StopPropagation(); 
            });

            VisualElement innerInput = inputField.Q(className: InnerTextInputClassName);
            if (innerInput != null)
            {
                innerInput.pickingMode = PickingMode.Position;
                innerInput.RegisterCallback<PointerDownEvent>(evt => 
                { 
                    inputField.Focus(); 
                    evt.StopPropagation(); 
                });
            }
        }

        private static void RemoveLegacyNumericRow(BindableElement slider)
        {
            if (slider?.parent == null)
            {
                return;
            }

            string rowName = slider.name + "-numeric-row";
            VisualElement row = slider.parent.Q<VisualElement>(rowName);
            if (row != null)
            {
                row.RemoveFromHierarchy();
            }
        }

        private static VisualElement EnsureNumericRow<TField>(BindableElement slider, out Label valueLabel, out TField inputField)
            where TField : VisualElement, new()
        {
            valueLabel = null;
            inputField = null;
            if (slider?.parent == null) return null;

            string rowName = slider.name + "-numeric-row";
            VisualElement row = slider.parent.Q<VisualElement>(rowName);
            if (row == null)
            {
                row = new VisualElement { name = rowName };
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.FlexEnd;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 2;
                row.style.marginBottom = 6;

                valueLabel = new Label { name = slider.name + "-value-label" };
                valueLabel.style.minWidth = 60;
                valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                valueLabel.style.fontSize = 11;
                valueLabel.style.color = new Color(0.80f, 0.94f, 1.0f, 1f);
                valueLabel.style.marginRight = 6;

                inputField = new TField();
                inputField.name = slider.name + "-input-field";

                row.Add(valueLabel);
                row.Add(inputField);

                int sliderIndex = slider.parent.IndexOf(slider);
                slider.parent.Insert(Mathf.Min(sliderIndex + 1, slider.parent.childCount), row);
            }
            else
            {
                valueLabel = row.Q<Label>(slider.name + "-value-label");
                inputField = row.Q<TField>(slider.name + "-input-field");
                if (inputField == null)
                {
                    row.Clear();

                    valueLabel = new Label { name = slider.name + "-value-label" };
                    valueLabel.style.minWidth = 60;
                    valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                    valueLabel.style.fontSize = 11;
                    valueLabel.style.color = new Color(0.80f, 0.94f, 1.0f, 1f);
                    valueLabel.style.marginRight = 6;

                    inputField = new TField();
                    inputField.name = slider.name + "-input-field";

                    row.Add(valueLabel);
                    row.Add(inputField);
                }
            }

            return row;
        }
    }
}
