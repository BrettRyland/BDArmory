using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Competition.RemoteOrchestration
{

    [Serializable]
    public class CompetitionModel
    {
        public int id;
        public string name;
        public int status;
        public int stage;
        public int remaining_heats;
        public string started_at;
        public string ended_at;
        public string created_at;
        public string updated_at;
        public string mode;

        public override string ToString() { return "{id: " + id + ", name: " + name + ", status: " + status + ", stage: " + stage + ", mode: " + mode + ", started_at: " + started_at + ", ended_at: " + ended_at + ", created_at: " + created_at + ", updated_at: " + updated_at + "}"; }

        public bool IsActive()
        {
            return ended_at == null || ended_at == "";
        }
    }

    [Serializable]
    public class CompetitionResponse
    {
        public CompetitionModel competition;
    }

    [Serializable]
    public class PlayerCollection
    {
        public List<PlayerModel> players;
    }

    [Serializable]
    public class PlayerModel
    {
        public int id;
        public string name;
        public bool is_human;
        public override string ToString() { return "{id: " + id + ", name: " + name + ", is_human: " + is_human + "}"; }
        public static List<PlayerModel> FromCsv(string csv)
        {
            List<PlayerModel> results = new List<PlayerModel>();
            string[] lines = csv.Split('\n');
            if (lines.Length > 0)
            {
                for (var k = 1; k < lines.Length; k++)
                {
                    //                    Debug.Log(string.Format("[BDArmory.BDAScoreModels]: PlayerModel.FromCsv line {0}", lines[k]));
                    if (!lines[k].Contains(","))
                    {
                        continue;
                    }
                    try
                    {
                        string[] values = lines[k].Split(',');
                        // AUBRANIUM, please consider encoding the player and vessel names on the API in base64 in the CSV file. This would allow any UTF8 character to be used for the player and vessel names (they would still need to be stripped of leading and trailing whitespace). Similarly for VesselModel. Not required for HeatModel. This uses System.Linq and System.Text.
                        // string[] values = lines[k].Split(',').Select(v => Encoding.UTF8.GetString(Convert.FromBase64String(v))).ToArray();
                        if (values.Length > 0)
                        {
                            PlayerModel model = new PlayerModel();
                            model.id = int.Parse(values[0]);
                            model.name = values[1];
                            model.is_human = bool.Parse(values[2]);
                            results.Add(model);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("[BDArmory.BDAScoreModels]: PlayerModel.FromCsv error: " + e);
                    }
                }
            }
            return results;
        }
    }

    [Serializable]
    public class HeatCollection
    {
        public List<HeatModel> heats;
    }

    [Serializable]
    public class HeatModel
    {
        public int id;
        public int competition_id;
        public int order;
        public int stage;
        public string started_at;
        public string ended_at;

        public bool Started() { return started_at != null && !"".Equals(started_at) && ended_at == null && !"".Equals(ended_at); }
        public bool Available() { return (started_at == null || "".Equals(started_at)) && (ended_at == null || "".Equals(ended_at)); }
        public override string ToString() { return "{id: " + id + ", competition_id: " + competition_id + ", order: " + order + ", stage: " + stage + ", started_at: " + started_at + ", ended_at: " + ended_at + "}"; }
        public static List<HeatModel> FromCsv(string csv)
        {
            List<HeatModel> results = new List<HeatModel>();
            string[] lines = csv.Split('\n');
            if (lines.Length > 0)
            {
                for (var k = 1; k < lines.Length; k++)
                {
                    //                    Debug.Log(string.Format("[BDArmory.BDAScoreModels]: HeatModel.FromCsv line {0}", lines[k]));
                    if (!lines[k].Contains(","))
                    {
                        continue;
                    }
                    try
                    {
                        string[] values = lines[k].Split(',');
                        if (values.Length > 0)
                        {
                            HeatModel model = new HeatModel();
                            model.id = int.Parse(values[0]);
                            model.competition_id = int.Parse(values[1]);
                            model.stage = int.Parse(values[2]);
                            model.order = int.Parse(values[3]);
                            model.started_at = values[4];
                            model.ended_at = values[5];
                            results.Add(model);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("[BDArmory.BDAScoreModels]: HeatModel.FromCsv error: " + e);
                    }
                }
            }
            return results;
        }
    }

    [Serializable]
    public class VesselCollection
    {
        public List<VesselModel> vessels;
    }

    [Serializable]
    public class VesselModel
    {
        public int id;
        public int player_id;
        public string name;
        public string craft_url;
        public override string ToString() { return "{id: " + id + ", player_id: " + player_id + ", name: " + name + ", craft_url: " + craft_url + "}"; }
        public static List<VesselModel> FromCsv(string csv)
        {
            List<VesselModel> results = new List<VesselModel>();
            string[] lines = csv.Split('\n');
            if (lines.Length > 0)
            {
                for (var k = 1; k < lines.Length; k++)
                {
                    //                    Debug.Log(string.Format("[BDArmory.BDAScoreModels]: VesselModel.FromCsv line {0}", lines[k]));
                    if (!lines[k].Contains(","))
                    {
                        continue;
                    }
                    try
                    {
                        string[] values = lines[k].Split(',');
                        if (values.Length > 0)
                        {
                            VesselModel model = new VesselModel();
                            model.id = int.Parse(values[0]);
                            model.player_id = int.Parse(values[1]);
                            model.craft_url = values[2];
                            model.name = values[3];
                            results.Add(model);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Log("[BDArmory.BDAScoreModels]: VesselModel.FromCsv error: " + e);
                    }
                }
            }
            return results;
        }
    }

    [Serializable]
    public class RecordModel
    {
        public int competition_id;
        public int vessel_id;
        public int heat_id;
        public int hits_out;
        public int hits_in;
        public double dmg_out;
        public double dmg_in;
        public int ram_parts_out;
        public int ram_parts_in;
        public int mis_strikes_out;
        public int mis_strikes_in;
        public int mis_parts_out;
        public int mis_parts_in;
        public double mis_dmg_out;
        public double mis_dmg_in;
        public int roc_strikes_out;
        public int roc_strikes_in;
        public int roc_parts_out;
        public int roc_parts_in;
        public double roc_dmg_out;
        public double roc_dmg_in;
        public int ast_parts_in;
        public int assists;
        public int kills;
        public int deaths;
        public double HPremaining;
        public float distance;
        public string weapon;
        public float death_order;
        public float death_time;
        public int wins;
        public int waypoints;
        public float elapsed_time;
        public float deviation;

        public string ToJSON()
        {
            string result = JsonUtility.ToJson(this);
            Debug.Log(string.Format("[BDArmory.BDAScoreModels]: [RecordModel] json: {0}", result));
            return result;
        }
    }
}
