using System;

namespace BDArmory.UI
{
    public enum MissileSettingType { FloatRange, Bool, Action, Choice }

    /// <summary>
    /// Descriptor for one popup row. Adding a parameter = one factory call in
    /// ModularMissilePopupMenu.BuildSettings(); the render loop never changes.
    /// </summary>
    public class MissileSettingItem
    {
        public string name;
        public string labelKey;
        public MissileSettingType type;
        public int tab;
        public Func<float> getFloat;
        public Action<float> setFloat;
        public Func<bool> getBool;
        public Action<bool> setBool;
        public Action onAction;
        public Func<string> getActionStatus;
        public string[] choiceOptions;
        public string[] choiceDisplay;
        public Func<int> getChoice;
        public Action<int> setChoice;
        public Func<float, string> format;
        public float min;
        public float max;
        public float step;
        public Func<bool> visibleCondition;

        protected MissileSettingItem() { }

        public static MissileSettingItem Slider(int tab, string name, string labelKey,
            Func<float> getFloat, Action<float> setFloat,
            float min, float max, float step, Func<float, string> format = null,
            Func<bool> visibleCondition = null)
        {
            return new MissileSettingItem
            {
                name = name, labelKey = labelKey, type = MissileSettingType.FloatRange, tab = tab,
                getFloat = getFloat, setFloat = setFloat, min = min, max = max, step = step,
                format = format ?? (v => v.ToString("F2")),
                visibleCondition = visibleCondition ?? (() => true)
            };
        }

        public static MissileSettingItem Toggle(int tab, string name, string labelKey,
            Func<bool> getBool, Action<bool> setBool, Func<bool> visibleCondition = null)
        {
            return new MissileSettingItem
            {
                name = name, labelKey = labelKey, type = MissileSettingType.Bool, tab = tab,
                getBool = getBool, setBool = setBool,
                visibleCondition = visibleCondition ?? (() => true)
            };
        }

        public static MissileSettingItem Button(int tab, string name, string labelKey,
            Action onAction, Func<string> getActionStatus = null, Func<bool> visibleCondition = null)
        {
            return new MissileSettingItem
            {
                name = name, labelKey = labelKey, type = MissileSettingType.Action, tab = tab,
                onAction = onAction, getActionStatus = getActionStatus,
                visibleCondition = visibleCondition ?? (() => true)
            };
        }

        public static MissileSettingItem Choice(int tab, string name, string labelKey,
            string[] options, string[] display,
            Func<int> getChoice, Action<int> setChoice, Func<bool> visibleCondition = null)
        {
            return new MissileSettingItem
            {
                name = name, labelKey = labelKey, type = MissileSettingType.Choice, tab = tab,
                choiceOptions = options, choiceDisplay = display,
                getChoice = getChoice, setChoice = setChoice,
                visibleCondition = visibleCondition ?? (() => true)
            };
        }
    }

    public class SectionItem : MissileSettingItem
    {
        public SectionItem(int tab, string labelKey, Func<bool> visibleCondition = null)
        {
            type = MissileSettingType.FloatRange; // unused; IsSection flags header rendering
            this.tab = tab;
            name = "section_" + labelKey;
            this.labelKey = labelKey;
            this.visibleCondition = visibleCondition ?? (() => true);
        }

        public bool IsSection => name != null && name.StartsWith("section_");
    }
}
