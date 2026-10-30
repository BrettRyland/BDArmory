using BDArmory.Extensions;
using BDArmory.Services;
using BDArmory.Utils;
using UnityEngine;

namespace BDArmory.Weapons
{
    public abstract class EngageableWeapon : BDAPartModule, IEngageService
    {
        [KSPField(isPersistant = true)]
        public bool engageEnabled = true;

        // Weapon usage settings
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageRangeMin"),//Engage Range Min
         UI_FloatPowerRange(minValue = 0f, maxValue = 5000f, power = 2, sigFig = 2, scene = UI_Scene.Editor)]
        public float engageRangeMin;

        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageRangeMax"),//Engage Range Max
         UI_FloatPowerRange(minValue = 0f, maxValue = 5000f, power = 2, sigFig = 2, scene = UI_Scene.Editor)]
        public float engageRangeMax;

        [KSPField]
        public bool limitEngageOptions = false;

        public bool canEngageAir = true;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageAir"),//Engage Air
         UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool engageAir = true;

        public bool canEngageMissile = true;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageMissile"),//Engage Missile
         UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool engageMissile = true;

        public bool canEngageGround = true;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageSurface"),//Engage Surface
         UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool engageGround = true;

        public bool canEngageSLW = true;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_EngageSLW"),//Engage SLW
        UI_Toggle(disabledText = "#LOC_BDArmory_false", enabledText = "#LOC_BDArmory_true")]//false--true
        public bool engageSLW = true;

        [KSPField(advancedTweakable = true, isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "#LOC_BDArmory_weaponChannel"),
            UI_FloatRange(minValue = 0, maxValue = 10, stepIncrement = 1, scene = UI_Scene.All, affectSymCounterparts = UI_Scene.All)]
        public float weaponChannel = 0; // weaponChannel telling a weaponManager which weapons it may use

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_DisableEngageOptions", active = true)]//Disable Engage Options
        public void ToggleEngageOptions()
        {
            ToggleEngageOptionsImpl(!engageEnabled);
        }
        public void ToggleEngageOptionsImpl(bool setEnabled, bool applyToSymmetric = true)
        {
            engageEnabled = setEnabled;

            if (engageEnabled == false)
            {
                Events[nameof(ToggleEngageOptions)].guiName = StringUtils.Localize("#LOC_BDArmory_EnableEngageOptions");//"Enable Engage Options"
            }
            else
            {
                Events[nameof(ToggleEngageOptions)].guiName = StringUtils.Localize("#LOC_BDArmory_DisableEngageOptions");//"Disable Engage Options"
            }

            Fields[nameof(engageRangeMin)].guiActive = engageEnabled;
            Fields[nameof(engageRangeMin)].guiActiveEditor = engageEnabled;
            Fields[nameof(engageRangeMax)].guiActive = engageEnabled;
            Fields[nameof(engageRangeMax)].guiActiveEditor = engageEnabled;

            EnforceEngageLimits();

            if (applyToSymmetric)
            {
                foreach (var sympart in part.symmetryCounterparts)
                    sympart.GetComponent<EngageableWeapon>().ToggleEngageOptionsImpl(setEnabled, false);
            }
        }

        public void EnforceEngageLimits()
        {
            // Not strictly necessary since we guard this at the various weapon modules, but hey, it's safer this way
            canEngageAir = (!limitEngageOptions || canEngageAir);
            canEngageMissile = (!limitEngageOptions || canEngageMissile);
            canEngageGround = (!limitEngageOptions || canEngageGround);
            canEngageSLW = (!limitEngageOptions || canEngageSLW);

            engageAir &= canEngageAir;
            
            engageMissile &= canEngageMissile;

            engageGround &= canEngageGround;

            engageSLW &= canEngageSLW;

            Fields[nameof(engageAir)].guiActive = engageEnabled && canEngageAir;
            Fields[nameof(engageAir)].guiActiveEditor = engageEnabled && canEngageAir;

            Fields[nameof(engageMissile)].guiActive = engageEnabled && canEngageMissile;
            Fields[nameof(engageMissile)].guiActiveEditor = engageEnabled && canEngageMissile;

            Fields[nameof(engageGround)].guiActive = engageEnabled && canEngageGround;
            Fields[nameof(engageGround)].guiActiveEditor = engageEnabled && canEngageGround;

            Fields[nameof(engageSLW)].guiActive = engageEnabled && canEngageSLW;
            Fields[nameof(engageSLW)].guiActiveEditor = engageEnabled && canEngageSLW;
        }

        public void HideEngageOptions()
        {
            Events[nameof(ToggleEngageOptions)].guiActive = false;
            Events[nameof(ToggleEngageOptions)].guiActiveEditor = false;
            Fields[nameof(engageRangeMin)].guiActive = true;
            Fields[nameof(engageRangeMin)].guiActiveEditor = true;
            Fields[nameof(engageRangeMax)].guiActive = true;
            Fields[nameof(engageRangeMax)].guiActiveEditor = true;
            Fields[nameof(engageAir)].guiActive = false;
            Fields[nameof(engageAir)].guiActiveEditor = false;
            Fields[nameof(engageMissile)].guiActive = false;
            Fields[nameof(engageMissile)].guiActiveEditor = false;
            Fields[nameof(engageGround)].guiActive = false;
            Fields[nameof(engageGround)].guiActiveEditor = false;
            Fields[nameof(engageSLW)].guiActive = false;
            Fields[nameof(engageSLW)].guiActiveEditor = false;

        }
        public void OnRangeUpdated(BaseField field, object obj)
        {
            // ensure max >= min
            if (engageRangeMax < engageRangeMin)
                engageRangeMax = engageRangeMin;
        }

        void OnEngageOptionsChanged(BaseField field, object obj)
        {
            var value = (bool)field.GetValue(this);
            foreach (var sympart in part.symmetryCounterparts)
            {
                var engageableWeapon = sympart.GetComponent<EngageableWeapon>();
                if (engageableWeapon is not null)
                {
                    // field.SetValue(value, engageableWeapon); // This doesn't work across hosts for some reason!
                    field.FieldInfo.SetValue(engageableWeapon, value); // But this does, despite doing what the above is essentially doing‽
                }
            }

            var wm = vessel.ActiveController().WM;
            if (wm is not null) wm.weaponsListNeedsUpdating = true;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            BaseShortName = shortName;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;

            var engageAirField = (UI_Toggle)Fields[nameof(engageAir)].uiControlFlight;
            engageAirField.onFieldChanged = OnEngageOptionsChanged;
            var engageMissileField = (UI_Toggle)Fields[nameof(engageMissile)].uiControlFlight;
            engageMissileField.onFieldChanged = OnEngageOptionsChanged;
            var engageGroundField = (UI_Toggle)Fields[nameof(engageGround)].uiControlFlight;
            engageGroundField.onFieldChanged = OnEngageOptionsChanged;
            var engageSLWField = (UI_Toggle)Fields[nameof(engageSLW)].uiControlFlight;
            engageSLWField.onFieldChanged = OnEngageOptionsChanged;
        }

        protected void InitializeEngagementRange(float min, float max)
        {
            min = Mathf.Max(min, 1f); // Avoid 0 min range for now. FIXME Remove these if the special value of 0 gets added to UI_FloatSemiLogRange.
            max = Mathf.Max(max, 1f); // Avoid 0 max range for now.

            var rangeMin = (UI_FloatPowerRange)Fields[nameof(engageRangeMin)].uiControlEditor;
            rangeMin.UpdateLimits(min, max);
            rangeMin.onFieldChanged = OnRangeUpdated;

            var rangeMax = (UI_FloatPowerRange)Fields[nameof(engageRangeMax)].uiControlEditor;
            rangeMax.UpdateLimits(min, max);
            rangeMax.onFieldChanged = OnRangeUpdated;

            if ((engageRangeMin == 0) && (engageRangeMax == 0))
            {
                // no sensible settings yet, set to default
                engageRangeMin = min;
                engageRangeMax = max;
            }
        }

        //implementations from Interface
        public float GetEngagementRangeMin()
        {
            return engageRangeMin;
        }

        public float GetEngagementRangeMax()
        {
            return engageRangeMax;
        }

        public bool GetEngageAirTargets()
        {
            return engageAir;
        }

        public bool GetEngageMissileTargets()
        {
            return engageMissile;
        }

        public bool GetEngageGroundTargets()
        {
            return engageGround;
        }

        public bool GetEngageSLWTargets()
        {
            return engageSLW;
        }

        [KSPField(isPersistant = true)]
        public string shortName = string.Empty;
        public string BaseShortName { get; private set; } = string.Empty;

        public string GetShortName()
        {
            return shortName;
        }

        public float GetWeaponChannel()
        {
            return weaponChannel;
        }
    }
}
