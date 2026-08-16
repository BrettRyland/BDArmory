using System;
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

    [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#LOC_BDArmory_Settings_KerbalSuitType"),
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

    public void Start()
    {
      if (!CheckValidPart())
      {
        part.RemoveModule(this);
        return;
      }
      if (HighLogic.LoadedSceneIsFlight && part.FindModuleImplementing<KerbalSeat>() != null)
      {
        Fields[nameof(suit)].guiActive = false; // The seat's suit type can't be changed in flight.
      }
      else
      {
        SetOnSuitChanged();
      }
      OnSuitChanged();
    }

    bool CheckValidPart()
    {
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
  }
}
