using System;
using BDArmory.Settings;
using BDArmory.Utils;

namespace BDArmory.Modules
{
  /// <summary>
  /// This allows setting the suit worn by EVA kerbals if spawned via BDArmory or via going EVA from a part.
  /// EVA kerbals can't have their suits changed once spawned.
  /// </summary>
  public class KerbalSuitSelector : BDAPartModule
  {
    /// <summary>
    /// Same as ProtoCrewMember.KerbalSuit, but with an extra "Random" option.
    /// </summary>
    public enum KerbalSuit
    {
      NoChange = -1,
      Default = 0,
      Vintage = 1,
      Future = 2,
      Slim = 3,
      Random = 4
    }

    [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "#LOC_BDArmory_Settings_KerbalSuitType"),
        UI_ChooseOption(options = new string[5] { "Default", "Vintage", "Future", "Slim", "Random" })]
    public string suit = "Default";

    public ProtoCrewMember.KerbalSuit Suit
    {
      set
      {
        field = value;
        foreach (var crew in part.protoModuleCrew)
          crew.suit = value; // Update existing proto-crew on the part.
      }
    }

    void Start()
    {
      if (BDArmorySettings.DISABLE_KERBAL_SUIT_SELECTION || !CheckValidPart(part))
      {
        part.RemoveModule(this);
        return;
      }
      if (HighLogic.LoadedSceneIsFlight)
      {
        Fields[nameof(suit)].guiActive = part.FindModuleImplementing<KerbalSeat>() == null; // Enable the in-flight UI as long as it's not a seat as the seat's suit type can't be changed in flight.
      }
      else
      {
        SetOnSuitChanged();
      }
      OnSuitChanged();
    }

    static bool CheckValidPart(Part part)
    {
      if (part == null) return false;
      if (part.FindModuleImplementing<KerbalSeat>() != null) return true;
      var command = part.FindModuleImplementing<ModuleCommand>();
      if (command != null && command.minimumCrew >= 1) return true;
      return false;
    }

    void SetOnSuitChanged()
    {
      (
        HighLogic.LoadedSceneIsEditor ?
          (UI_ChooseOption)Fields[nameof(suit)].uiControlEditor :
          (UI_ChooseOption)Fields[nameof(suit)].uiControlFlight
      ).onFieldChanged = OnSuitChanged;
    }

    void OnSuitChanged(BaseField field = null, object obj = null)
    {
      var suitType = (KerbalSuit)Enum.Parse(typeof(KerbalSuit), suit);
      Suit = Enum.IsDefined(typeof(ProtoCrewMember.KerbalSuit), (ProtoCrewMember.KerbalSuit)suitType) ?
        (ProtoCrewMember.KerbalSuit)suitType :
        (ProtoCrewMember.KerbalSuit)UnityEngine.Random.Range(0, 4);
      this.UpdateChooseOptionPAW(field, obj);
    }

    /// <summary>
    /// Set the suit type.
    /// Note: this is called from OnLoad prior to Start, which is when Suit gets set.
    /// </summary>
    /// <param name="suitType"></param>
    public void SetSuit(KerbalSuit suitType)
    {
      suit = suitType.ToString();
    }

    public static void EnableKerbalSuitSelection(bool enable)
    {
      if (enable) // Add the KerbalSuitSelector module to any existing parts.
      {
        if (HighLogic.LoadedSceneIsFlight)
        {
          foreach(var vessel in FlightGlobals.Vessels)
          {
            foreach (var part in vessel.Parts)
            {
              if (CheckValidPart(part))
              {
                part.AddModule(nameof(KerbalSuitSelector));
              }
            }
          }
        }
        else if (HighLogic.LoadedSceneIsEditor)
        {
          var ship = EditorLogic.fetch.ship;
          if (ship == null) return;
          foreach (var part in ship.Parts)
          {
            if (CheckValidPart(part))
            {
              part.AddModule(nameof(KerbalSuitSelector));
            }
          }
        }
      }
      else // Remove the KerbalSuitSelector module from any existing parts.
      {
        if (HighLogic.LoadedSceneIsFlight)
        {
          foreach (var vessel in FlightGlobals.Vessels)
          {
            foreach (var part in vessel.Parts)
            {
              var kss = part.FindModuleImplementing<KerbalSuitSelector>();
              if (kss != null)
              {
                part.RemoveModule(kss);
              }
            }
          }
        }
        else if (HighLogic.LoadedSceneIsEditor)
        {
          var ship = EditorLogic.fetch.ship;
          if (ship == null) return;
          foreach (var part in ship.Parts)
          {
            var kss = part.FindModuleImplementing<KerbalSuitSelector>();
            if (kss != null)
            {
              part.RemoveModule(kss);
            }
          }
        }
      }
    }
  }
}
