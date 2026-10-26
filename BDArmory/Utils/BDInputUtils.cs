using UnityEngine;
using System;
using System.Collections;

using BDArmory.Settings;

namespace BDArmory.Utils
{
    public class BDInputUtils
    {
        public static string GetInputString()
        {
            //keyCodes
            string[] names = System.Enum.GetNames(typeof(KeyCode));
            int numberOfKeycodes = names.Length;

            for (int i = 0; i < numberOfKeycodes; i++)
            {
                string output = names[i];
                if (output.ToLower().StartsWith("mouse") || output.ToLower().StartsWith("joystick")) continue; // Handle mouse and joystick separately.

                if (output.Contains("Keypad"))
                {
                    output = "[" + output.Substring(6).ToLower() + "]";
                }
                else if (output.Contains("Alpha"))
                {
                    output = output.Substring(5);
                }
                else //lower case key
                {
                    output = output.ToLower();
                }

                //modifiers
                if (output.Contains("control"))
                {
                    output = output.Split('c')[0] + " ctrl";
                }
                else if (output.Contains("alt"))
                {
                    output = output.Split('a')[0] + " alt";
                }
                else if (output.Contains("shift"))
                {
                    output = output.Split('s')[0] + " shift";
                }
                else if (output.Contains("command"))
                {
                    output = output.Split('c')[0] + " cmd";
                }

                //special keys
                else if (output == "backslash")
                {
                    output = @"\";
                }
                else if (output == "backquote")
                {
                    output = "`";
                }
                else if (output == "[period]")
                {
                    output = "[.]";
                }
                else if (output == "[plus]")
                {
                    output = "[+]";
                }
                else if (output == "[multiply]")
                {
                    output = "[*]";
                }
                else if (output == "[divide]")
                {
                    output = "[/]";
                }
                else if (output == "[minus]")
                {
                    output = "[-]";
                }
                else if (output == "[enter]")
                {
                    output = "enter";
                }
                else if (output.Contains("page"))
                {
                    output = output.Insert(4, " ");
                }
                else if (output.Contains("arrow"))
                {
                    output = output.Split('a')[0];
                }
                else if (output == "capslock")
                {
                    output = "caps lock";
                }
                else if (output == "minus")
                {
                    output = "-";
                }

                //test if input is valid
                try
                {
                    if (Input.GetKey(output))
                    {
                        return output;
                    }
                }
                catch (System.Exception e)
                {
                    if (!e.Message.EndsWith("is unknown")) // Ignore unknown keys
                        Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                }
            }

            //mouse
            for (int m = 0; m < 6; m++)
            {
                string inputString = "mouse " + m;
                try
                {
                    if (Input.GetKey(inputString))
                    {
                        return inputString;
                    }
                }
                catch (UnityException e)
                {
                    Debug.Log("[BDArmory.BDInputUtils]: Invalid mouse: " + inputString);
                    Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                }
            }

            //joysticks
            for (int j = 1; j < 12; j++)
            {
                for (int b = 0; b < 20; b++)
                {
                    string inputString = "joystick " + j + " button " + b;
                    try
                    {
                        if (Input.GetKey(inputString))
                        {
                            return inputString;
                        }
                    }
                    catch (UnityException e)
                    {
                        Debug.LogWarning("[BDArmory.BDInputUtils]: Exception thrown in GetInputString: " + e.Message + "\n" + e.StackTrace);
                        return string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        public static bool GetKey(BDInputInfo input)
        {
            return !string.IsNullOrEmpty(input.inputString) && Input.GetKey(input.inputString);
        }

        public static bool GetKeyDown(BDInputInfo input)
        {
            return !string.IsNullOrEmpty(input.inputString) && Input.GetKeyDown(input.inputString);
        }
    }

    /// <summary>
    /// A class for more easily inputting numeric values in TextFields.
    /// There's a configurable delay after the last keystroke before attempting to interpret the string as a double. Default: 0.5s.
    /// Explicit cast to lower precision types may be needed when assigning the current value.
    /// </summary>
    public class NumericInputField : MonoBehaviour
    {
        public static GUIStyle InputFieldStyle;
        public static GUIStyle InputFieldBadStyle;
        static void ConfigureStyles()
        {
            InputFieldStyle = new GUIStyle(GUI.skin.textField)
            { alignment = TextAnchor.MiddleRight };
            InputFieldBadStyle = new GUIStyle(InputFieldStyle);
            InputFieldBadStyle.normal.textColor = Color.red;
            InputFieldBadStyle.focused.textColor = Color.red;
        }

        public NumericInputField Initialise(double lastUpdated, double currentValue, double minValue = double.MinValue, double maxValue = double.MaxValue, (float, float, bool, bool) meta = default)
        {
            this.lastUpdated = lastUpdated; this.CurrentValue = currentValue; this.minValue = minValue; this.maxValue = maxValue;
            (rounding, sigFig, withZero, reducedPrecisionAtMin) = meta;
            return this;
        }
        public double lastUpdated;
        public string possibleValue = string.Empty;
        public double CurrentValue // Note: setting the current value doesn't necessarily update the displayed string. Use SetCurrentValue to force updating the displayed value.
        {
            get { return field; }
            private set
            {
                field = value;
                if (string.IsNullOrEmpty(possibleValue))
                {
                    possibleValue = field.ToString("G6");
                    valid = true;
                }
            }
        }
        public double minValue;
        public double maxValue;

        bool coroutineRunning = false;
        Coroutine coroutine;
        bool valid = true;

        #region Metadata (not used internally, but provided for reference for automation)
        public float rounding;
        public float sigFig;
        public bool withZero;
        public bool reducedPrecisionAtMin;
        #endregion

        // Set the current value and force the display to update.
        public void SetCurrentValue(double value)
        {
            possibleValue = null; // Clear the possibleValue first so that it gets updated.
            CurrentValue = value;
            lastUpdated = Time.time;
        }

        public void TryParseValue(string v)
        {
            if (v != possibleValue)
            {
                lastUpdated = !string.IsNullOrEmpty(v) ? Time.time : Time.time + BDArmorySettings.NUMERIC_INPUT_DELAY; // Give the empty string an extra delay.
                possibleValue = v;
                if (!coroutineRunning)
                {
                    coroutine = StartCoroutine(UpdateValueCoroutine());
                }
            }
        }

        IEnumerator UpdateValueCoroutine()
        {
            var wait = new WaitForFixedUpdate();
            coroutineRunning = true;
            valid = true; // Flag the value as valid until we've tried parsing it.
            while (Time.time - lastUpdated < BDArmorySettings.NUMERIC_INPUT_DELAY)
                yield return wait;
            TryParseCurrentValue(BDArmorySettings.NUMERIC_INPUT_SELF_UPDATE);
            coroutineRunning = false;
            yield return wait;
        }

        void TryParseCurrentValue(bool updatePossible = false)
        {
            double newValue;
            if (double.TryParse(possibleValue, out newValue))
            {
                CurrentValue = Math.Min(Math.Max(newValue, minValue), Math.Max(maxValue, CurrentValue)); // Clamp the new value between the min and max, but not if it's been set higher with the unclamped tuning option. This still allows reducing the value while still above the clamp limit.
                if (newValue != CurrentValue) // The value got clamped.
                    possibleValue = CurrentValue.ToString("G6");
                lastUpdated = Time.time;
                valid = true;
            }
            else
            {
                valid = false;
            }
            if (updatePossible)
            {
                possibleValue = CurrentValue.ToString("G6");
                valid = true;
            }
        }

        // Parse the current possible value immediately.
        public void tryParseValueNow()
        {
            TryParseCurrentValue(true);
            if (coroutineRunning)
            {
                StopCoroutine(coroutine);
                coroutineRunning = false;
            }
        }

        public GUIStyle style
        {
            get
            {
                if (InputFieldStyle == null || InputFieldBadStyle == null) ConfigureStyles();
                return valid ? InputFieldStyle : InputFieldBadStyle;
            }
        }
    }

    /// <summary>
    /// A Vector3 version of NumericInputField.
    /// This has reduced options compared to NumericInputField.
    /// </summary>
    public class NumericInputFieldVector3 : MonoBehaviour
    {
        /// <summary>
        /// Initialise the important fields.
        /// </summary>
        /// <param name="lastUpdated"></param>
        /// <param name="currentValue"></param>
        /// <returns></returns>
        public NumericInputFieldVector3 Initialise(double lastUpdated, in Vector3 currentValue)
        {
            LastUpdated = lastUpdated; CurrentValue = currentValue;
            return this;
        }

        public double LastUpdated { get; private set; }
        public string PossibleValue { get; private set; } = string.Empty;
        public Vector3 CurrentValue
        {
            get { return field; }
            set
            {
                field = value;
                if (string.IsNullOrEmpty(PossibleValue))
                {
                    PossibleValue = field.ToString("0");
                    valid = true;
                }
            }
        }

        bool coroutineRunning = false;
        Coroutine coroutine;
        bool valid = true;

        // Set the current value and force the display to update.
        public void SetCurrentValue(in Vector3 value)
        {
            PossibleValue = null; // Clear the possibleValue first so that it gets updated.
            CurrentValue = value;
            LastUpdated = Time.time;
        }

        public void TryParseValue(string v)
        {
            if (v != PossibleValue)
            {
                LastUpdated = !string.IsNullOrEmpty(v) ? Time.time : Time.time + BDArmorySettings.NUMERIC_INPUT_DELAY; // Give the empty string an extra delay.
                PossibleValue = v;
                if (!coroutineRunning)
                {
                    coroutine = StartCoroutine(UpdateValueCoroutine());
                }
            }
        }

        IEnumerator UpdateValueCoroutine()
        {
            var wait = new WaitForFixedUpdate();
            coroutineRunning = true;
            valid = true; // Flag the value as valid (for the style) until we've tried parsing it.
            while (Time.time - LastUpdated < BDArmorySettings.NUMERIC_INPUT_DELAY)
                yield return wait;
            TryParseCurrentValue(BDArmorySettings.NUMERIC_INPUT_SELF_UPDATE);
            coroutineRunning = false;
            yield return wait;
        }

        // Parse the current possible value immediately.
        public void TryParseValueNow()
        {
            TryParseCurrentValue(true);
            if (coroutineRunning)
            {
                StopCoroutine(coroutine);
                coroutineRunning = false;
            }
        }

        void TryParseCurrentValue(bool updatePossible = false)
        {
            var (success, newValue) = TryParseVector3(PossibleValue);
            if (success)
            {
                CurrentValue = newValue;
                LastUpdated = Time.time;
                valid = true;
            }
            else
            {
                valid = false;
            }
            if (updatePossible)
            {
                PossibleValue = CurrentValue.ToString("0");
                valid = true;
            }
        }

        /// <summary>
        /// Parse a Vector3 in the same format that's used in BDAPersistentSettingsField.
        /// </summary>
        /// <param name="possibleValue"></param>
        /// <returns>(success, value)</returns>
        (bool, Vector3) TryParseVector3(string possibleValue)
        {
            char[] charsToTrim = ['(', ')', ' '];
            string[] strings = possibleValue.Trim(charsToTrim).Split(',');
            if (strings.Length != 3) return (false, default);
            if (!float.TryParse(strings[0], out float x)) return (false, default);
            if (!float.TryParse(strings[1], out float y)) return (false, default);
            if (!float.TryParse(strings[2], out float z)) return (false, default);
            return (true, new Vector3(x, y, z));
        }

        #region Style
        public static GUIStyle InputFieldStyle;
        public static GUIStyle InputFieldBadStyle;
        static void ConfigureStyles()
        {
            InputFieldStyle = new GUIStyle(GUI.skin.textField)
            { alignment = TextAnchor.LowerCenter, wordWrap = false, clipping = TextClipping.Overflow };
            InputFieldBadStyle = new GUIStyle(InputFieldStyle);
            InputFieldBadStyle.normal.textColor = Color.red;
            InputFieldBadStyle.focused.textColor = Color.red;
        }

        public GUIStyle Style
        {
            get
            {
                if (InputFieldStyle == null || InputFieldBadStyle == null) ConfigureStyles();
                return valid ? InputFieldStyle : InputFieldBadStyle;
            }
        }
        #endregion
    }
}
