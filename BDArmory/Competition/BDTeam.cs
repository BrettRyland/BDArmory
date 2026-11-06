using System;
using System.Collections.Generic;
using UnityEngine;

using BDArmory.UI;
using BDArmory.Utils;

namespace BDArmory.Competition
{
    [Serializable]
    public class BDTeam
    {
        // No warranty is provided for changing the Name, but this makes serialization easier. :)
        public string Name;

        public bool Neutral;


        public List<string> Allies = new List<string>();

        public BDTeam(string name, List<string> allies = null, bool neutral = false)
        {
            Name = name;
            Neutral = neutral;
            Allies = allies ?? new List<string>();
        }

        public static BDTeam Get(string name)
        {
            if (!BDArmorySetup.Instance.Teams.ContainsKey(name))
                BDArmorySetup.Instance.Teams.Add(name, new BDTeam(name));
            return BDArmorySetup.Instance.Teams[name];
        }

        public bool IsEnemy(BDTeam other)
        {
            if (Neutral || other == null || other.Neutral || other.Name == Name || Allies.Contains(other.Name))
                return false;
            return true;
        }

        public bool IsFriendly(BDTeam other)
        {
            if (Neutral || other == null || other.Neutral)
                return false;
            return (other.Name == Name || Allies.Contains(other.Name));
        }

        public override string ToString() => Name;

        public static BDTeam Deserialize(string teamString)
        {
            // Backward compatibility
            if (string.IsNullOrEmpty(teamString) || teamString == "False")
                return BDTeam.Get("A");
            else if (teamString == "True")
                return BDTeam.Get("B");
            try
            {
                BDTeam team = UnityEngine.JsonUtility.FromJson<BDTeam>(OtherUtils.JsonDecompat(teamString));
                if (!BDArmorySetup.Instance.Teams.ContainsKey(team.Name))
                {
                    BDArmorySetup.Instance.Teams.Add(team.Name, team);
                }
                return BDArmorySetup.Instance.Teams[team.Name];
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BDArmory.BDTeam]: Exception thrown in Deserialize: " + e.Message + "\n" + e.StackTrace);
                return BDTeam.Get("A");
            }
        }

        public string Serialize()
        {
            return OtherUtils.JsonCompat(UnityEngine.JsonUtility.ToJson(this));
        }

        public override int GetHashCode() => Name.GetHashCode();

        public bool Equals(BDTeam other) => Name == other?.Name;

        public override bool Equals(object obj) => Equals(obj as BDTeam);

        public static bool operator ==(BDTeam left, BDTeam right) => Equals(left, right);

        public static bool operator !=(BDTeam left, BDTeam right) => !Equals(left, right);
    }
}
