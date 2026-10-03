using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using TMPro;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;
using BDArmory.Extensions;
using KSP.UI;

namespace BDArmory.Utils
{
    #region FloatLogRange
    /// <summary>
    /// Logarithmic FloatRange slider.
    /// Gives ranges with values of the form: 0.01, 0.0316, 0.1, 0.316, 1.
    /// Specify minValue, maxValue and steps. E.g., (0.01, 1, 4) would give the above sequence.
    /// Based on https://github.com/meirumeiru/InfernalRobotics/blob/develop/InfernalRobotics/InfernalRobotics/Gui/UIPartActionFloatEditEx.cs
    /// I'm not entirely sure how much of this is necessary.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_FloatLogRange : UI_FloatRange
    {
        private const string UIControlName = "FloatLogRange";
        public int steps = 10;
        public UI_FloatLogRange() { }

        /// <summary>
        /// Update the limits.
        /// Call this instead of directly setting minValue/maxValue to properly adjust the slider.
        /// </summary>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        public void UpdateLimits(float minValue, float maxValue)
        {
            this.minValue = minValue;
            this.maxValue = maxValue;
            var partActionFieldItem = ((UIPartActionFloatLogRange)this.partActionItem);
            if (partActionFieldItem != null) partActionFieldItem.UpdateLimits();
        }
        public (float, float, float, int) GetLimits() => (minValue, maxValue, 0, steps);

        public static float ToSliderValue(float value, float minValue, float maxValue, int steps)
        {
            float logMinValue = Mathf.Log10(minValue);
            float logMaxValue = Mathf.Log10(maxValue);
            float sliderStepSize = (logMaxValue - logMinValue) / steps;
            return Mathf.Clamp(BDAMath.RoundToUnit(Mathf.Log10(value) - logMinValue, sliderStepSize) + logMinValue, logMinValue, logMaxValue);
        }
        public static float FromSliderValue(float value, float minValue, float maxValue, int steps)
        {
            float logMinValue = Mathf.Log10(minValue);
            float logMaxValue = Mathf.Log10(maxValue);
            float sliderStepSize = (logMaxValue - logMinValue) / steps;
            return Mathf.Pow(10f, Mathf.Clamp(BDAMath.RoundToUnit(value - logMinValue, sliderStepSize) + logMinValue, logMinValue, logMaxValue));
        }
    }

    [UI_FloatLogRange]
    public class UIPartActionFloatLogRange : UIPartActionFieldItem
    {
        protected UI_FloatLogRange logFloatRange { get { return (UI_FloatLogRange)control; } }
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldValue;
        public Slider slider;
        private float sliderStepSize;
        private bool blockSliderUpdate;
        private bool numericSliders = false;
        public GameObject numericContainer;
        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputField;
        private float lastDisplayedValue = 0;

        public static Type VersionTaggedType(Type baseClass)
        {
            var ass = baseClass.Assembly;
            // FIXME The below works to prevent ReflectionTypeLoadException on KSP 1.9, there might be a better way other than OtherUtils.GetLoadableTypes though?
            Type tagged = OtherUtils.GetLoadableTypes(ass).Where(t => t.BaseType == baseClass).Where(t => t.FullName.StartsWith(baseClass.FullName)).FirstOrDefault();
            if (tagged != null)
                return tagged;
            return baseClass;
        }

        internal static T GetTaggedComponent<T>(GameObject gameObject) where T : Component
        {
            return (T)gameObject.GetComponent(VersionTaggedType(typeof(T)));
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static UIPartActionFloatLogRange CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("UIPartActionFloatLogRange", VersionTaggedType(typeof(UIPartActionFloatLogRange)));
            UIPartActionFloatLogRange partActionFloatLogRange = GetTaggedComponent<UIPartActionFloatLogRange>(gameObject);
            gameObject.SetActive(false);

            // Find the template for FloatRange
            UIPartActionFloatRange partActionFloatRange = (UIPartActionFloatRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionFloatRange.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionFloatRange.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionFloatRange.gameObject, gameObject.transform, ref list);
            list.TryGetValue(partActionFloatRange.fieldName.gameObject, out GameObject fieldNameGO);
            partActionFloatLogRange.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.fieldAmount.gameObject, out GameObject fieldValueGO);
            partActionFloatLogRange.fieldValue = fieldValueGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.slider.gameObject, out GameObject sliderGO);
            partActionFloatLogRange.slider = sliderGO.GetComponent<Slider>();
            list.TryGetValue(partActionFloatRange.numericContainer, out partActionFloatLogRange.numericContainer);
            list.TryGetValue(partActionFloatRange.inputField.gameObject, out GameObject inputFieldGO);
            partActionFloatLogRange.inputField = inputFieldGO.GetComponent<TMP_InputField>();
            list.TryGetValue(partActionFloatRange.fieldNameNumeric.gameObject, out GameObject fieldNameNumericGO);
            partActionFloatLogRange.fieldNameNumeric = fieldNameNumericGO.GetComponent<TextMeshProUGUI>();

            return partActionFloatLogRange;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            UpdateLimits();
            fieldName.text = field.guiName;
            fieldNameNumeric.text = field.guiName;
            float value = GetFieldValue();
            SetFieldValue(value);
            UpdateDisplay(value);
            // Debug.Log($"DEBUG value is {value} with limits {logFloatRange.minValue}—{logFloatRange.maxValue}");
            // Debug.Log($"DEBUG slider has value {slider.value} with limits {slider.minValue}—{slider.maxValue}");
            slider.onValueChanged.AddListener(OnValueChanged);
            inputField.onValueChanged.AddListener(OnNumericValueChanged);
            inputField.onSubmit.AddListener(OnNumericSubmitted);
            inputField.onSelect.AddListener(OnNumericSelected);
            inputField.onDeselect.AddListener(OnNumericDeselected);
        }

        private float GetFieldValue()
        {
            float value = field.GetValue<float>(field.host);
            return value;
        }
        private float UpdateSlider(float value)
        {
            // Note: We use Log10 here as it has better human-centric rounding properties (i.e., 0.001 instead of 0.000999999999).
            value = Mathf.Pow(10f, Mathf.Clamp(BDAMath.RoundToUnit(Mathf.Log10(value) - slider.minValue, sliderStepSize) + slider.minValue, slider.minValue, slider.maxValue));
            // Debug.Log($"DEBUG Slider updated to {value}");
            return value;
        }
        private void UpdateDisplay(float value)
        {
            if (numericSliders != Window.NumericSliders)
            {
                numericSliders = Window.NumericSliders;
                slider.gameObject.SetActive(!Window.NumericSliders);
                numericContainer.SetActive(Window.NumericSliders);
            }
            blockSliderUpdate = true;
            lastDisplayedValue = value;
            fieldValue.text = value.ToString("G3");
            if (numericSliders)
            { inputField.text = fieldValue.text; }
            else
            { slider.value = Mathf.Log10(value); }
            blockSliderUpdate = false;
        }
        private void OnValueChanged(float obj)
        {
            if (blockSliderUpdate) return;
            if (control is not null && control.requireFullControl)
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY)) return; }
            else
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return; }
            float value = Mathf.Pow(10f, slider.value);
            value = UpdateSlider(value);
            SetFieldValue(value);
            UpdateDisplay(value);
        }
        private void OnNumericSubmitted(string str)
        {
            if (float.TryParse(str, out float value))
            {
                value = Mathf.Clamp(value, logFloatRange.minValue, logFloatRange.maxValue); // Clamp, but don't round the value when in numeric mode.
                SetFieldValue(value);
                UpdateDisplay(value);
            }
        }
        void OnNumericValueChanged(string str)
        {
            if (inputField.wasCanceled) OnNumericSubmitted(str);
        }
        void OnNumericSelected(string str)
        {
            AddInputFieldLock(str);
        }
        void OnNumericDeselected(string str)
        {
            OnNumericSubmitted(str);
            RemoveInputfieldLock();
        }

        public override void UpdateItem()
        {
            float value = GetFieldValue();
            if (value == lastDisplayedValue && numericSliders == Window.NumericSliders) return; // Do nothing if the value hasn't changed or the # hasn't been toggled.
            // fieldName.text = field.guiName; // Label doesn't update.
            UpdateDisplay(value);
        }

        /// <summary>
        /// Update the limits of the slider.
        /// Call this whenever the min/max values of the underlying field are changed.
        /// </summary>
        public void UpdateLimits()
        {
            var value = GetFieldValue(); // Store the current value so it doesn't get clamped.
            blockSliderUpdate = true; // Block the slider from updating while we reset the value.
            slider.minValue = Mathf.Log10(logFloatRange.minValue);
            slider.maxValue = Mathf.Log10(logFloatRange.maxValue);
            sliderStepSize = (slider.maxValue - slider.minValue) / logFloatRange.steps;
            logFloatRange.stepIncrement = sliderStepSize;
            SetFieldValue(value); // Restore the unclamped value.
            UpdateDisplay(value);
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionFloatLogRangeRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;
        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning) StopCoroutine(register);
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionFloatLogRange.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_FloatLogRange));

            isRunning = false;
        }
    }
    #endregion

    #region FloatSemiLogRange
    /// <summary>
    /// Semi-Logarithmic FloatRange slider.
    /// Gives ranges where the values are of the form: 0.9, 1, 2, ..., 9, 10, 20, ..., 90, 100, 200, ..., 900, 1000, 2000.
    /// Specify minValue, maxValue and sigFig. The stepIncrement is automatically calculated.
    /// Based on the Logarithmic FloatRange slider above.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_FloatSemiLogRange : UI_FloatRange
    {
        private const string UIControlName = "FloatSemiLogRange";
        public float sigFig = 2; // 2 sig.fig. gives: ..., 9.8, 9.9, 10, 11, 12, ...; the fractional component (if non-zero) determines the rounding amount.
        public bool withZero = false; // Include a special 0 value and sets reducedPrecisionAtMin to true.
        public bool reducedPrecisionAtMin = false; // Lower the sigFig for the lowest values.
        public UI_FloatSemiLogRange() { }

        /// <summary>
        /// Update the limits.
        /// Call this instead of directly setting min/max value or sigFig to properly update the slider.
        /// </summary>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <param name="sigFig"></param>
        /// <param name="withZero"></param>
        /// <param name="reducedPrecisionAtMin">(Automatically set to true if withZero is true and sigFig>1.)</param>
        public void UpdateLimits(float minValue, float maxValue, float sigFig = 0, Toggle withZero = Toggle.NoChange, Toggle reducedPrecisionAtMin = Toggle.NoChange)
        {
            // Sanitise input.
            this.minValue = Mathf.Min(minValue, maxValue);
            this.maxValue = Mathf.Max(minValue, maxValue);
            if (sigFig > 0) this.sigFig = sigFig;
            this.withZero = withZero switch { Toggle.On => true, Toggle.Off => false, Toggle.Toggle => !this.withZero, _ => this.withZero };
            this.reducedPrecisionAtMin = this.sigFig > 1 && (this.withZero || reducedPrecisionAtMin switch { Toggle.On => true, Toggle.Off => false, Toggle.Toggle => !this.reducedPrecisionAtMin, _ => this.reducedPrecisionAtMin });
            var partActionFieldItem = (UIPartActionFloatSemiLogRange)partActionItem;
            if (partActionFieldItem != null) partActionFieldItem.UpdateLimits();
        }

        public (float, float, float, float, bool, bool) GetLimits() => (minValue, maxValue, Mathf.Max(10f * (sigFig % 1f), 1f), sigFig, withZero, reducedPrecisionAtMin);

        /// <summary>
        /// Static function for converting linear values to semi-log values.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="minValue">The minimum value of the slider.</param>
        /// <param name="sigFig">The number of significant figures (for integer rounding). Default=2.</param>
        /// <returns>The semi-log value.</returns>
        public static float FromSliderValue(float value, float minValue, float sigFig = 2, bool withZero = false, bool reducedPrecisionAtMin = false)
        {
            reducedPrecisionAtMin = sigFig > 1 && (reducedPrecisionAtMin || withZero);
            int sigfig = Mathf.CeilToInt(sigFig);
            float rounding = Mathf.Max(10f * (sigFig % 1f), 1f);
            float minStepSize = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(minValue)) + (reducedPrecisionAtMin && rounding == 1 ? 1 : 0));
            float sliderStepSize = Mathf.Pow(10f, 1 - sigfig) * rounding;
            float sliderMinValue = BDAMath.RoundToUnit(reducedPrecisionAtMin ? 1 - (11 - 10 * minValue / minStepSize) * sliderStepSize : minValue / minStepSize - (withZero ? sliderStepSize : 0), sliderStepSize);

            value = BDAMath.RoundToUnit(value, sliderStepSize);
            if (withZero && value < sliderMinValue + sliderStepSize / 2f) return 0;
            else if (reducedPrecisionAtMin && value <= 1f + sliderStepSize / 2f) value *= minStepSize;
            else value = Mathf.Pow(10f, Mathf.Floor((value - 1f) / 9f)) * (1f + (value - 1f) % 9f) * minStepSize;

            value = BDAMath.RoundToUnit(value, rounding * Mathf.Pow(10, Mathf.CeilToInt(Mathf.Log10(value)) - sigfig)); // Round to the rounding units.
            if (Mathf.Log10(value) - (sigfig - 1) > 0) value = Mathf.Round(value); // Round whole numbers properly.
            return value;
        }
        /// <summary>
        /// Static function for converting semi-log values to linear values.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="minValue">The minimum value of the slider.</param>
        /// <returns>The linear value.</returns>
        public static float ToSliderValue(float value, float minValue, float sigFig = 2, bool withZero = false, bool reducedPrecisionAtMin = false)
        {
            reducedPrecisionAtMin = sigFig > 1 && (reducedPrecisionAtMin || withZero);
            int sigfig = Mathf.CeilToInt(sigFig);
            float rounding = Mathf.Max(10f * (sigFig % 1f), 1f);
            float minStepSize = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(minValue)) + (reducedPrecisionAtMin && rounding == 1 && sigfig > 1 ? 1 : 0));
            float sliderStepSize = Mathf.Pow(10f, 1 - sigfig) * rounding;
            if (withZero || reducedPrecisionAtMin)
            {
                float sliderMinValue = BDAMath.RoundToUnit(sigfig > 1 ? 1 - (11 - 10 * minValue / minStepSize) * sliderStepSize : minValue / minStepSize - sliderStepSize, sliderStepSize);
                if (value < minValue) return sliderMinValue;
                if (value < minStepSize) return BDAMath.RoundToUnit(value / minStepSize, sliderStepSize);
            }
            value /= minStepSize;
            float factor = Mathf.Floor(Mathf.Log10(value));
            return BDAMath.RoundToUnit(factor * 9f + value / Mathf.Pow(10f, factor), sliderStepSize);
        }
    }

    [UI_FloatSemiLogRange]
    public class UIPartActionFloatSemiLogRange : UIPartActionFieldItem
    {
        protected UI_FloatSemiLogRange semiLogFloatRange { get { return (UI_FloatSemiLogRange)control; } }
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldValue;
        public Slider slider;
        private float sliderStepSize;
        private float minStepSize;
        private float maxStepSize;
        private bool blockSliderUpdate;
        private bool numericSliders = false;
        private string fieldFormatString = "G3";
        public GameObject numericContainer;
        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputField;
        private float lastDisplayedValue = 0;
        private bool withZero = false;
        private bool reducedPrecisionAtMin = false;
        private int sigFig;
        private float rounding;

        public static Type VersionTaggedType(Type baseClass)
        {
            var ass = baseClass.Assembly;
            // FIXME The below works to prevent ReflectionTypeLoadException on KSP 1.9, there might be a better way other than OtherUtils.GetLoadableTypes though?
            Type tagged = OtherUtils.GetLoadableTypes(ass).Where(t => t.BaseType == baseClass).Where(t => t.FullName.StartsWith(baseClass.FullName)).FirstOrDefault();
            if (tagged != null)
                return tagged;
            return baseClass;
        }

        internal static T GetTaggedComponent<T>(GameObject gameObject) where T : Component
        {
            return (T)gameObject.GetComponent(VersionTaggedType(typeof(T)));
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static UIPartActionFloatSemiLogRange CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("UIPartActionFloatSemiLogRange", VersionTaggedType(typeof(UIPartActionFloatSemiLogRange)));
            UIPartActionFloatSemiLogRange partActionFloatSemiLogRange = GetTaggedComponent<UIPartActionFloatSemiLogRange>(gameObject);
            gameObject.SetActive(false);

            // Find the template for FloatRange
            UIPartActionFloatRange partActionFloatRange = (UIPartActionFloatRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionFloatRange.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionFloatRange.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionFloatRange.gameObject, gameObject.transform, ref list);
            list.TryGetValue(partActionFloatRange.fieldName.gameObject, out GameObject fieldNameGO);
            partActionFloatSemiLogRange.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.fieldAmount.gameObject, out GameObject fieldValueGO);
            partActionFloatSemiLogRange.fieldValue = fieldValueGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.slider.gameObject, out GameObject sliderGO);
            partActionFloatSemiLogRange.slider = sliderGO.GetComponent<Slider>();
            list.TryGetValue(partActionFloatRange.numericContainer, out partActionFloatSemiLogRange.numericContainer);
            list.TryGetValue(partActionFloatRange.inputField.gameObject, out GameObject inputFieldGO);
            partActionFloatSemiLogRange.inputField = inputFieldGO.GetComponent<TMP_InputField>();
            list.TryGetValue(partActionFloatRange.fieldNameNumeric.gameObject, out GameObject fieldNameNumericGO);
            partActionFloatSemiLogRange.fieldNameNumeric = fieldNameNumericGO.GetComponent<TextMeshProUGUI>();

            return partActionFloatSemiLogRange;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            UpdateLimits();
            fieldName.text = field.guiName;
            fieldNameNumeric.text = field.guiName;
            fieldFormatString = $"G{Mathf.Max(Mathf.CeilToInt(semiLogFloatRange.sigFig) + 2, Mathf.CeilToInt(Mathf.Log10(semiLogFloatRange.maxValue)) + 1)}"; // Show at most 2 digits beyond the requested sig. fig. or enough for the largest number.
            float value = GetFieldValue();
            SetFieldValue(value);
            UpdateDisplay(value);
            slider.onValueChanged.AddListener(OnValueChanged);
            inputField.onValueChanged.AddListener(OnNumericValueChanged);
            inputField.onSubmit.AddListener(OnNumericSubmitted);
            inputField.onSelect.AddListener(OnNumericSelected);
            inputField.onDeselect.AddListener(OnNumericDeselected);
        }

        private float GetFieldValue()
        {
            float value = field.GetValue<float>(field.host);
            return value;
        }
        private void CheckSlider(float value)
        {
            var toValue = ToSliderValue(value);
            var fromValue = FromSliderValue(toValue);
            // Debug.Log($"DEBUG SemiLog: value {value} -> {toValue} -> {fromValue}, static ToSlider: {UI_FloatSemiLogRange.ToSliderValue(value, semiLogFloatRange.minValue, semiLogFloatRange.sigFig, semiLogFloatRange.withZero, semiLogFloatRange.reducedPrecisionAtMin)}, FromSlider: {UI_FloatSemiLogRange.FromSliderValue(toValue, semiLogFloatRange.minValue, semiLogFloatRange.sigFig, semiLogFloatRange.withZero, semiLogFloatRange.reducedPrecisionAtMin)}");
        }
        float FromSliderValue(float value)
        {
            value = BDAMath.RoundToUnit(value, sliderStepSize);
            if (withZero && value < slider.minValue + sliderStepSize / 2f) return 0;
            else if (reducedPrecisionAtMin && value <= 1f + sliderStepSize / 2f) value *= minStepSize;
            else value = Mathf.Pow(10f, Mathf.Floor((value - 1f) / 9f)) * (1f + (value - 1f) % 9f) * minStepSize;

            value = BDAMath.RoundToUnit(value, rounding * Mathf.Pow(10, Mathf.CeilToInt(Mathf.Log10(value)) - sigFig)); // Round to the rounding units.
            if (Mathf.Log10(value) - (sigFig - 1) > 0) value = Mathf.Round(value); // Round whole numbers properly.
            return value;
        }
        float ToSliderValue(float value)
        {
            if (withZero || reducedPrecisionAtMin)
            {
                if (value < semiLogFloatRange.minValue) return slider.minValue;
                if (value < minStepSize) return BDAMath.RoundToUnit(value / minStepSize, sliderStepSize);
            }
            value /= minStepSize;
            float factor = Mathf.Floor(Mathf.Log10(value));
            return BDAMath.RoundToUnit(factor * 9f + value / Mathf.Pow(10f, factor), sliderStepSize);
        }
        private void UpdateDisplay(float value)
        {
            if (numericSliders != Window.NumericSliders)
            {
                numericSliders = Window.NumericSliders;
                slider.gameObject.SetActive(!numericSliders);
                numericContainer.SetActive(numericSliders);
            }
            blockSliderUpdate = true;
            lastDisplayedValue = value;
            fieldValue.text = value.ToString(fieldFormatString);
            if (numericSliders) { inputField.text = fieldValue.text; }
            else { slider.value = ToSliderValue(value); }
            blockSliderUpdate = false;
        }
        private void OnValueChanged(float obj)
        {
            if (blockSliderUpdate) return;
            if (control is not null && control.requireFullControl)
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY)) return; }
            else
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return; }
            float value = FromSliderValue(slider.value);
            // CheckSlider(value);
            SetFieldValue(value);
            UpdateDisplay(value);
        }
        private void OnNumericSubmitted(string str)
        {
            if (float.TryParse(str, out float value))
            {
                value = Mathf.Clamp(value, withZero ? 0 : semiLogFloatRange.minValue, semiLogFloatRange.maxValue); // Clamp, but don't round the value when in numeric mode.
                SetFieldValue(value);
                UpdateDisplay(value);
            }
        }
        void OnNumericValueChanged(string str)
        {
            if (inputField.wasCanceled) OnNumericSubmitted(str);
        }
        void OnNumericSelected(string str)
        {
            AddInputFieldLock(str);
        }
        void OnNumericDeselected(string str)
        {
            OnNumericSubmitted(str);
            RemoveInputfieldLock();
        }

        public override void UpdateItem()
        {
            float value = GetFieldValue();
            if (value == lastDisplayedValue && numericSliders == Window.NumericSliders) return; // Do nothing if the value hasn't changed or the # hasn't been toggled.
            // fieldName.text = field.guiName; // Label doesn't update.
            UpdateDisplay(value);
        }

        public void UpdateLimits()
        {
            var value = GetFieldValue(); // Store the current value so it doesn't get clamped.
            sigFig = Mathf.CeilToInt(semiLogFloatRange.sigFig);
            rounding = Mathf.Max(10f * (semiLogFloatRange.sigFig % 1f), 1f);
            withZero = semiLogFloatRange.withZero;
            reducedPrecisionAtMin = sigFig > 1 && (semiLogFloatRange.reducedPrecisionAtMin || withZero);
            var minStepSizePower = Mathf.Floor(Mathf.Log10(semiLogFloatRange.minValue)) + (reducedPrecisionAtMin && rounding == 1 ? 1 : 0);
            var maxStepSizePower = Mathf.Floor(Mathf.Log10(semiLogFloatRange.maxValue));
            minStepSize = Mathf.Pow(10, minStepSizePower);
            maxStepSize = Mathf.Pow(10, maxStepSizePower);
            blockSliderUpdate = true; // Block the slider from updating while we adjust things (unblocks in UpdateDisplay).
            sliderStepSize = Mathf.Pow(10, 1 - sigFig);
            slider.minValue = BDAMath.RoundToUnit(reducedPrecisionAtMin ? 1 - (10 + (withZero ? 1 : 0) - 10 * semiLogFloatRange.minValue / minStepSize) * sliderStepSize : semiLogFloatRange.minValue / minStepSize - (withZero ? sliderStepSize : 0), sliderStepSize);
            slider.maxValue = BDAMath.RoundToUnit(9f * (maxStepSizePower - minStepSizePower) + semiLogFloatRange.maxValue / maxStepSize, sliderStepSize);
            semiLogFloatRange.stepIncrement = sliderStepSize;
            fieldFormatString = $"G{Mathf.Max(sigFig + 2, Mathf.CeilToInt(Mathf.Log10(semiLogFloatRange.maxValue)) + 1)}"; // Show at most 2 digits beyond the requested sig. fig. or enough for the largest number.
            SetFieldValue(value); // Restore the unclamped value.
            UpdateDisplay(value);
            // Debug.Log($"DEBUG value is {value} with limits {semiLogFloatRange.minValue}—{semiLogFloatRange.maxValue}, with zero: {withZero}, sigFig: {sigFig}, rounding: {rounding}, reducedPrecisionAtMin: {reducedPrecisionAtMin}");
            // Debug.Log($"DEBUG slider has value {slider.value} with limits {slider.minValue}—{slider.maxValue}");
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionFloatSemiLogRangeRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;
        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning) StopCoroutine(register);
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionFloatSemiLogRange.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_FloatSemiLogRange));

            isRunning = false;
        }
    }
    #endregion

    #region FloatPowerRange
    /// <summary>
    /// Power-scaling FloatRange slider.
    /// Specify minValue, maxValue, power and sigFig. The stepIncrement is automatically calculated.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_FloatPowerRange : UI_FloatRange
    {
        private const string UIControlName = "FloatPowerRange";
        public float power;
        public int sigFig = 2;
        public UI_FloatPowerRange() { }

        /// <summary>
        /// Update the limits.
        /// Call this instead of directly setting min/max value or sigFig to properly update the slider.
        /// </summary>
        /// <param name="minValue"></param>
        /// <param name="maxValue"></param>
        /// <param name="sigFig"></param>
        public void UpdateLimits(float minValue, float maxValue, float power = 0, int sigFig = 0)
        {
            // Sanitise input.
            minValue = Mathf.Max(0, minValue); // Values can't be negative.
            maxValue = Mathf.Max(0, maxValue);
            this.minValue = Mathf.Min(minValue, maxValue);
            this.maxValue = Mathf.Max(minValue, maxValue);
            if (power > 0) this.power = power;
            if (sigFig > 0) this.sigFig = sigFig;
            var partActionFieldItem = (UIPartActionFloatPowerRange)partActionItem;
            if (partActionFieldItem != null) partActionFieldItem.UpdateLimits();
        }

        public (float, float, float, int) GetLimits() => (minValue, maxValue, Mathf.Pow(10f, Mathf.CeilToInt(Mathf.Log10(maxValue)) - sigFig - 2), sigFig);

        /// <summary>
        /// Static function for converting linear values to power values.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="power"></param>
        /// <returns></returns>
        public static float FromSliderValue(float value, float power, int sigFig, float maxValue)
        {
            if (value > 0)
            {
                value = Mathf.Pow(value, power);
                var rounding = Mathf.Max(Mathf.Pow(10f, Mathf.CeilToInt(Mathf.Log10(value)) - sigFig), Mathf.Pow(10f, Mathf.CeilToInt(Mathf.Log10(maxValue)) - sigFig - 2));
                return BDAMath.RoundToUnit(value, rounding);
            }
            else return 0;
        }
        /// <summary>
        /// Static function for converting power values to linear values.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="power"></param>
        /// <param name="sigFig"></param>
        /// <returns></returns>
        public static float ToSliderValue(float value, float power)
        {
            if (value > 0) return Mathf.Pow(value, 1 / power);
            else return 0;
        }
    }

    [UI_FloatPowerRange]
    public class UIPartActionFloatPowerRange : UIPartActionFieldItem
    {
        protected UI_FloatPowerRange powerFloatRange { get { return (UI_FloatPowerRange)control; } }
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldValue;
        public Slider slider;
        private float minValue, maxValue, power, sigFig, roundingLimit;
        private bool blockSliderUpdate;
        private bool numericSliders = false;
        private string fieldFormatString = "G4";
        public GameObject numericContainer;
        public TextMeshProUGUI fieldNameNumeric;
        public TMP_InputField inputField;
        private float lastDisplayedValue = 0;

        public static Type VersionTaggedType(Type baseClass)
        {
            var ass = baseClass.Assembly;
            // FIXME The below works to prevent ReflectionTypeLoadException on KSP 1.9, there might be a better way other than OtherUtils.GetLoadableTypes though?
            Type tagged = OtherUtils.GetLoadableTypes(ass).Where(t => t.BaseType == baseClass).Where(t => t.FullName.StartsWith(baseClass.FullName)).FirstOrDefault();
            if (tagged != null)
                return tagged;
            return baseClass;
        }

        internal static T GetTaggedComponent<T>(GameObject gameObject) where T : Component
        {
            return (T)gameObject.GetComponent(VersionTaggedType(typeof(T)));
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static UIPartActionFloatPowerRange CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("UIPartActionFloatPowerRange", VersionTaggedType(typeof(UIPartActionFloatPowerRange)));
            UIPartActionFloatPowerRange partActionFloatPowerRange = GetTaggedComponent<UIPartActionFloatPowerRange>(gameObject);
            gameObject.SetActive(false);

            // Find the template for FloatRange
            UIPartActionFloatRange partActionFloatRange = (UIPartActionFloatRange)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionFloatRange.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionFloatRange.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionFloatRange.gameObject, gameObject.transform, ref list);
            list.TryGetValue(partActionFloatRange.fieldName.gameObject, out GameObject fieldNameGO);
            partActionFloatPowerRange.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.fieldAmount.gameObject, out GameObject fieldValueGO);
            partActionFloatPowerRange.fieldValue = fieldValueGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionFloatRange.slider.gameObject, out GameObject sliderGO);
            partActionFloatPowerRange.slider = sliderGO.GetComponent<Slider>();
            list.TryGetValue(partActionFloatRange.numericContainer, out partActionFloatPowerRange.numericContainer);
            list.TryGetValue(partActionFloatRange.inputField.gameObject, out GameObject inputFieldGO);
            partActionFloatPowerRange.inputField = inputFieldGO.GetComponent<TMP_InputField>();
            list.TryGetValue(partActionFloatRange.fieldNameNumeric.gameObject, out GameObject fieldNameNumericGO);
            partActionFloatPowerRange.fieldNameNumeric = fieldNameNumericGO.GetComponent<TextMeshProUGUI>();

            return partActionFloatPowerRange;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            UpdateLimits();
            fieldName.text = field.guiName;
            fieldNameNumeric.text = field.guiName;
            fieldFormatString = $"G{Mathf.Max(powerFloatRange.sigFig + 2, Mathf.CeilToInt(Mathf.Log10(powerFloatRange.maxValue)) + 1)}"; // Show at most 2 digits beyond the requested sig. fig. or enough for the largest number.
            float value = GetFieldValue();
            SetFieldValue(value);
            UpdateDisplay(value);
            slider.onValueChanged.AddListener(OnValueChanged);
            inputField.onValueChanged.AddListener(OnNumericValueChanged);
            inputField.onSubmit.AddListener(OnNumericSubmitted);
            inputField.onSelect.AddListener(OnNumericSelected);
            inputField.onDeselect.AddListener(OnNumericDeselected);
        }

        private float GetFieldValue()
        {
            float value = field.GetValue<float>(field.host);
            return value;
        }
        float FromSliderValue(float value)
        {
            if (value > 0)
            {
                value = Mathf.Pow(value, power);
                var rounding = Mathf.Max(Mathf.Pow(10f, Mathf.CeilToInt(Mathf.Log10(value)) - sigFig), roundingLimit);
                return BDAMath.RoundToUnit(value, rounding);
            }
            else return 0;
        }
        float ToSliderValue(float value)
        {
            if (value > 0) return Mathf.Pow(value, 1 / power);
            else return 0;
        }
        private void UpdateDisplay(float value)
        {
            if (numericSliders != Window.NumericSliders)
            {
                numericSliders = Window.NumericSliders;
                slider.gameObject.SetActive(!numericSliders);
                numericContainer.SetActive(numericSliders);
            }
            blockSliderUpdate = true;
            lastDisplayedValue = value;
            fieldValue.text = value.ToString(fieldFormatString);
            if (numericSliders) { inputField.text = fieldValue.text; }
            else { slider.value = ToSliderValue(value); }
            blockSliderUpdate = false;
        }
        private void OnValueChanged(float obj)
        {
            if (blockSliderUpdate) return;
            if (control is not null && control.requireFullControl)
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY)) return; }
            else
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return; }
            float value = FromSliderValue(slider.value);
            SetFieldValue(value);
            UpdateDisplay(value);
        }
        private void OnNumericSubmitted(string str)
        {
            if (float.TryParse(str, out float value))
            {
                value = Mathf.Clamp(value, minValue, maxValue); // Clamp, but don't round the value when in numeric mode.
                SetFieldValue(value);
                UpdateDisplay(value);
            }
        }
        void OnNumericValueChanged(string str)
        {
            if (inputField.wasCanceled) OnNumericSubmitted(str);
        }
        void OnNumericSelected(string str)
        {
            AddInputFieldLock(str);
        }
        void OnNumericDeselected(string str)
        {
            OnNumericSubmitted(str);
            RemoveInputfieldLock();
        }

        public override void UpdateItem()
        {
            float value = GetFieldValue();
            if (value == lastDisplayedValue && numericSliders == Window.NumericSliders) return; // Do nothing if the value hasn't changed or the # hasn't been toggled.
            // fieldName.text = field.guiName; // Label doesn't update.
            UpdateDisplay(value);
        }

        public void UpdateLimits()
        {
            var value = GetFieldValue(); // Store the current value so it doesn't get clamped.
            minValue = powerFloatRange.minValue;
            maxValue = powerFloatRange.maxValue;
            power = powerFloatRange.power;
            sigFig = powerFloatRange.sigFig;
            roundingLimit = Mathf.Pow(10f, Mathf.CeilToInt(Mathf.Log10(maxValue)) - sigFig - 2);
            blockSliderUpdate = true; // Block the slider from updating while we adjust things (unblocks in UpdateDisplay).
            slider.minValue = Mathf.Pow(minValue, 1 / power);
            slider.maxValue = Mathf.Pow(maxValue, 1 / power);
            fieldFormatString = $"G{Mathf.Max(sigFig + 2, Mathf.CeilToInt(Mathf.Log10(maxValue)) + 1)}"; // Show at most 2 digits beyond the requested sig. fig. or enough for the largest number.
            SetFieldValue(value); // Restore the unclamped value.
            UpdateDisplay(value);
            // Debug.Log($"DEBUG value is {value} with limits {minValue}—{maxValue}");
            // Debug.Log($"DEBUG slider has value {slider.value} with limits {slider.minValue}—{slider.maxValue}");
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionFloatPowerRangeRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;
        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning) StopCoroutine(register);
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionFloatRange)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionFloatPowerRange.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_FloatPowerRange));

            isRunning = false;
        }
    }
    #endregion

    #region ActionGroup
    /// <summary>
    /// Action group slider.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field)]
    public class UI_ActionGroup : UI_ChooseOption
    {
        private const string UIControlName = "ActionGroup";
        public UI_ActionGroup()
        {
            options = [.. ActionGroups.Values.Select(ag => ag.ToString()).Select(ag => ag.StartsWith("Custom") ? $"AG{ag.Substring(6)}" : ag)];
        }
        public static readonly Dictionary<int, KSPActionGroup> ActionGroups = new() {
            { 0,  KSPActionGroup.None },
            { 1,  KSPActionGroup.Custom01 },
            { 2,  KSPActionGroup.Custom02 },
            { 3,  KSPActionGroup.Custom03 },
            { 4,  KSPActionGroup.Custom04 },
            { 5,  KSPActionGroup.Custom05 },
            { 6,  KSPActionGroup.Custom06 },
            { 7,  KSPActionGroup.Custom07 },
            { 8,  KSPActionGroup.Custom08 },
            { 9,  KSPActionGroup.Custom09 },
            { 10, KSPActionGroup.Custom10 },
            { 11, KSPActionGroup.Gear },
            { 12, KSPActionGroup.Light },
            { 13, KSPActionGroup.RCS },
            { 14, KSPActionGroup.SAS },
            { 15, KSPActionGroup.Brakes },
            { 16, KSPActionGroup.Abort }
        };
    }

    [UI_ActionGroup]
    public class UIPartActionActionGroup : UIPartActionFieldItem
    {
        protected UI_ActionGroup actionGroup { get { return (UI_ActionGroup)control; } }
        public TextMeshProUGUI fieldName;
        public TextMeshProUGUI fieldValue;
        public Slider slider;
        public UIButtonToggle inc;
        public UIButtonToggle dec;
        private bool blockSliderUpdate;

        public static Type VersionTaggedType(Type baseClass)
        {
            var ass = baseClass.Assembly;
            // FIXME The below works to prevent ReflectionTypeLoadException on KSP 1.9, there might be a better way other than OtherUtils.GetLoadableTypes though?
            Type tagged = OtherUtils.GetLoadableTypes(ass).Where(t => t.BaseType == baseClass).Where(t => t.FullName.StartsWith(baseClass.FullName)).FirstOrDefault();
            if (tagged != null)
                return tagged;
            return baseClass;
        }

        internal static T GetTaggedComponent<T>(GameObject gameObject) where T : Component
        {
            return (T)gameObject.GetComponent(VersionTaggedType(typeof(T)));
        }

        public static void InstantiateRecursive2(GameObject go, GameObject goc, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                list.Add(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc.transform.GetChild(i).gameObject, ref list);
            }
        }

        public static void InstantiateRecursive(GameObject go, Transform trfp, ref Dictionary<GameObject, GameObject> list)
        {
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject goc = Instantiate(go.transform.GetChild(i).gameObject);
                goc.transform.parent = trfp;
                goc.transform.localPosition = go.transform.GetChild(i).localPosition;
                if ((goc.transform is RectTransform) && (go.transform.GetChild(i) is RectTransform))
                {
                    RectTransform rtc = goc.transform as RectTransform;
                    RectTransform rt = go.transform.GetChild(i) as RectTransform;

                    rtc.offsetMax = rt.offsetMax;
                    rtc.offsetMin = rt.offsetMin;
                }
                list.Add(go.transform.GetChild(i).gameObject, goc);
                InstantiateRecursive2(go.transform.GetChild(i).gameObject, goc, ref list);
            }
        }

        public static UIPartActionActionGroup CreateTemplate()
        {
            // Create the control
            GameObject gameObject = new GameObject("UIPartActionActionGroup", VersionTaggedType(typeof(UIPartActionActionGroup)));
            UIPartActionActionGroup partActionActionGroup = GetTaggedComponent<UIPartActionActionGroup>(gameObject);
            gameObject.SetActive(false);

            // Find the template for FloatRange
            UIPartActionChooseOption partActionChooseOption = (UIPartActionChooseOption)UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionChooseOption));

            // Copy UI elements
            RectTransform rtc = gameObject.AddComponent<RectTransform>();
            RectTransform rt = partActionChooseOption.transform as RectTransform;
            rtc.offsetMin = rt.offsetMin;
            rtc.offsetMax = rt.offsetMax;
            rtc.anchorMin = rt.anchorMin;
            rtc.anchorMax = rt.anchorMax;
            LayoutElement lec = gameObject.AddComponent<LayoutElement>();
            LayoutElement le = partActionChooseOption.GetComponent<LayoutElement>();
            lec.flexibleHeight = le.flexibleHeight;
            lec.flexibleWidth = le.flexibleWidth;
            lec.minHeight = le.minHeight;
            lec.minWidth = le.minWidth;
            lec.preferredHeight = le.preferredHeight;
            lec.preferredWidth = le.preferredWidth;
            lec.layoutPriority = le.layoutPriority;

            // Copy control elements
            Dictionary<GameObject, GameObject> list = new Dictionary<GameObject, GameObject>();
            InstantiateRecursive(partActionChooseOption.gameObject, gameObject.transform, ref list);
            list.TryGetValue(partActionChooseOption.fieldName.gameObject, out GameObject fieldNameGO);
            partActionActionGroup.fieldName = fieldNameGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionChooseOption.fieldValue.gameObject, out GameObject fieldValueGO);
            partActionActionGroup.fieldValue = fieldValueGO.GetComponent<TextMeshProUGUI>();
            list.TryGetValue(partActionChooseOption.slider.gameObject, out GameObject sliderGO);
            partActionActionGroup.slider = sliderGO.GetComponent<Slider>();
            list.TryGetValue(partActionChooseOption.inc.gameObject, out GameObject incGO);
            partActionActionGroup.inc = incGO.GetComponent<UIButtonToggle>();
            list.TryGetValue(partActionChooseOption.dec.gameObject, out GameObject decGO);
            partActionActionGroup.dec = decGO.GetComponent<UIButtonToggle>();

            return partActionActionGroup;
        }

        public override void Setup(UIPartActionWindow window, Part part, PartModule partModule, UI_Scene scene, UI_Control control, BaseField field)
        {
            base.Setup(window, part, partModule, scene, control, field);
            slider.minValue = UI_ActionGroup.ActionGroups.Keys.Min();
            slider.maxValue = UI_ActionGroup.ActionGroups.Keys.Max();
            fieldName.text = field.guiName;
            var (index, value) = GetValueFromField();
            SetFieldValue(value);
            UpdateDisplay(index);
            slider.onValueChanged.AddListener(OnValueChanged);
            inc.onToggle.AddListener(OnTap_inc);
            dec.onToggle.AddListener(OnTap_dec);
        }

        private (int, int) GetValueFromField()
        {
            var value = (KSPActionGroup)field.GetValue<int>(field.host);
            var index = UI_ActionGroup.ActionGroups.ToDictionary(kvp => kvp.Value, kvp => kvp.Key).GetValueOrDefault(value, 0); // Filter out bitwise combinations, e.g., Gear | Brakes.
            return (index, (int)UI_ActionGroup.ActionGroups.GetValueOrDefault(index, KSPActionGroup.None));
        }
        private void UpdateDisplay(int index)
        {
            blockSliderUpdate = true;
            fieldValue.text = actionGroup.options[index];
            slider.value = index;
            blockSliderUpdate = false;
        }
        private void OnValueChanged(float obj)
        {
            if (blockSliderUpdate) return;
            if (control is not null && control.requireFullControl)
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_FULLONLY)) return; }
            else
            { if (!InputLockManager.IsUnlocked(ControlTypes.TWEAKABLES_ANYCONTROL)) return; }
            int index = Mathf.RoundToInt(slider.value);
            int value = (int)UI_ActionGroup.ActionGroups.GetValueOrDefault(index, KSPActionGroup.None);
            SetFieldValue(value);
            UpdateDisplay(index);
        }
        private void OnTap_dec()
        {
            var index = Mathf.RoundToInt(slider.value);
            if (index <= slider.minValue) return;
            --index;
            int value = (int)UI_ActionGroup.ActionGroups.GetValueOrDefault(index, KSPActionGroup.None);
            SetFieldValue(value);
            UpdateDisplay(index);
        }
        private void OnTap_inc()
        {
            var index = Mathf.RoundToInt(slider.value);
            if (index >= slider.maxValue) return;
            ++index;
            int value = (int)UI_ActionGroup.ActionGroups.GetValueOrDefault(index, KSPActionGroup.None);
            SetFieldValue(value);
            UpdateDisplay(index);
        }
    }

    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class UIPartActionActionGroupRegistration : MonoBehaviour
    {
        private static bool loaded = false;
        private static bool isRunning = false;
        private Coroutine register = null;
        public void Start()
        {
            if (loaded)
            {
                Destroy(gameObject);
                return;
            }
            loaded = true;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnLevelFinishedLoading;
        }

        public void OnLevelFinishedLoading(Scene scene, LoadSceneMode mode)
        {
            if (isRunning) StopCoroutine(register);
            if (!(HighLogic.LoadedSceneIsEditor || HighLogic.LoadedSceneIsFlight)) return;
            isRunning = true;
            register = StartCoroutine(Register());
        }

        internal IEnumerator Register()
        {
            UIPartActionController controller;
            while ((controller = UIPartActionController.Instance) is null) yield return null;

            FieldInfo typesField = (from fld in controller.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                    where fld.FieldType == typeof(List<Type>)
                                    select fld).First();

            List<Type> fieldPrefabTypes;
            while ((fieldPrefabTypes = (List<Type>)typesField.GetValue(controller)) == null
                || fieldPrefabTypes.Count == 0
                || !UIPartActionController.Instance.fieldPrefabs.Find(cls => cls.GetType() == typeof(UIPartActionChooseOption)))
                yield return false;

            // Register prefabs
            controller.fieldPrefabs.Add(UIPartActionActionGroup.CreateTemplate());
            fieldPrefabTypes.Add(typeof(UI_ActionGroup));

            isRunning = false;
        }
    }
    #endregion
}
