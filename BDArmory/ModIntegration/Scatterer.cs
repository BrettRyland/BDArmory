using System;
using System.Linq;

namespace BDArmory.ModIntegration
{
  [KSPAddon(KSPAddon.Startup.Flight, true)]
  public static class Scatterer
  {
    private const string ScattererAssemblyName = "scatterer";
    public static bool IsInstalled
    {
      get
      {
        if (haveChecked) return field;
        using var a = AppDomain.CurrentDomain.GetAssemblies().ToList().GetEnumerator();
        while (a.MoveNext())
        {
          if (a.Current.FullName.Split([','])[0] == ScattererAssemblyName)
          {
            field = true;
            return true;
          }
        }
        return false;
      }
    } = false;
    private static bool haveChecked = false;
  }
}